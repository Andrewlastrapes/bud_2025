using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BudgetApp.Api.Migrations
{
    /// <summary>
    /// Cleans up duplicate Plaid transactions and adds a unique constraint on
    /// (user_id, plaid_transaction_id) to prevent future duplicates.
    ///
    /// Root cause of duplicates: the application-level "check then insert" guard in
    /// TransactionService had a race condition when two Plaid webhooks arrived simultaneously.
    /// Both threads passed the AnyAsync check before either committed, producing two rows
    /// with the same plaid_transaction_id — each with its own budget_applied_amount, causing
    /// the dynamic balance to be subtracted twice per transaction.
    ///
    /// BEFORE RUNNING IN PRODUCTION — diagnostic SELECT:
    ///
    ///   SELECT
    ///       user_id, plaid_transaction_id,
    ///       COUNT(*) AS dup_count,
    ///       MAX(CASE WHEN user_decision != 0 THEN 1 ELSE 0 END) AS conflict_decision,
    ///       MAX(CASE WHEN counted_as_income THEN 1 ELSE 0 END) AS has_income,
    ///       MAX(CASE WHEN "LargeExpenseHandled" THEN 1 ELSE 0 END) AS has_le_handled,
    ///       MAX(CASE WHEN hold_reviewed THEN 1 ELSE 0 END) AS has_hold_reviewed,
    ///       MAX(CASE WHEN hold_override_amount IS NOT NULL THEN 1 ELSE 0 END) AS has_override,
    ///       COUNT(DISTINCT COALESCE(budget_applied_amount::text,'N')) AS distinct_applied_amts,
    ///       COUNT(DISTINCT COALESCE("IsLargeExpenseCandidate"::text,'N')) AS distinct_le_flags,
    ///       COUNT(DISTINCT COALESCE(suggested_kind::text,'0')) AS distinct_kinds,
    ///       ARRAY_AGG(id ORDER BY id) AS all_ids,
    ///       ARRAY_AGG(user_decision ORDER BY id) AS decisions,
    ///       ARRAY_AGG(pending ORDER BY id) AS pending_flags,
    ///       ARRAY_AGG(budget_applied_amount ORDER BY id) AS applied_amts
    ///   FROM "Transactions"
    ///   WHERE plaid_transaction_id IS NOT NULL AND plaid_transaction_id != ''
    ///   GROUP BY user_id, plaid_transaction_id
    ///   HAVING COUNT(*) > 1
    ///   ORDER BY COUNT(*) DESC, user_id;
    ///
    /// Column naming reference (confirmed from ApiDbContextModelSnapshot):
    ///   Transactions: user_id, plaid_transaction_id, user_decision, counted_as_income,
    ///                 "LargeExpenseHandled", hold_reviewed, hold_override_amount,
    ///                 budget_applied_amount, "IsLargeExpenseCandidate", suggested_kind, pending
    ///   Balances:     "userId"  (camelCase — must be quoted),  balance
    /// </summary>
    public partial class AddUniqueTransactionConstraintAndCleanupDuplicates : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── PHASE 1: Conflict checks ─────────────────────────────────────────────
            // Abort the migration if any duplicate group contains ambiguous user state
            // that cannot be safely merged. Each check raises an exception if conflicts
            // are found; the migration transaction rolls back cleanly.

            migrationBuilder.Sql(@"
DO $$
DECLARE
    v_count INTEGER;
BEGIN
    -- Check 1: Multiple distinct non-zero user_decision values in the same duplicate group.
    -- If two rows in the same group both have different user decisions, we cannot safely
    -- merge them — require manual resolution.
    SELECT COUNT(*) INTO v_count FROM (
        SELECT user_id, plaid_transaction_id
        FROM ""Transactions""
        WHERE plaid_transaction_id IS NOT NULL AND plaid_transaction_id != ''
        GROUP BY user_id, plaid_transaction_id
        HAVING COUNT(*) > 1
          AND COUNT(DISTINCT CASE WHEN user_decision != 0 THEN user_decision ELSE NULL END) > 1
    ) c;
    IF v_count > 0 THEN
        RAISE EXCEPTION
            'MIGRATION ABORTED: % duplicate group(s) have conflicting user_decision values '
            'that cannot be safely auto-merged. Run the diagnostic SELECT at the top of '
            'the migration file to identify them. Resolve manually, then retry.',
            v_count;
    END IF;

    -- Check 2: Multiple distinct non-null hold_override_amount values.
    -- A hold override is set by a specific user action; two different values mean
    -- the user would have had to override the same transaction twice, which is not possible
    -- via normal flows. Abort if we see this.
    SELECT COUNT(*) INTO v_count FROM (
        SELECT user_id, plaid_transaction_id
        FROM ""Transactions""
        WHERE plaid_transaction_id IS NOT NULL AND plaid_transaction_id != ''
        GROUP BY user_id, plaid_transaction_id
        HAVING COUNT(*) > 1
          AND COUNT(DISTINCT hold_override_amount) FILTER (WHERE hold_override_amount IS NOT NULL) > 1
    ) c;
    IF v_count > 0 THEN
        RAISE EXCEPTION
            'MIGRATION ABORTED: % duplicate group(s) have conflicting hold_override_amount values. '
            'Run the diagnostic SELECT and resolve manually.',
            v_count;
    END IF;

    -- Check 3: Multiple distinct non-zero suggested_kind values.
    -- suggested_kind is assigned at insert time based on transaction classification.
    -- Two different non-zero kinds for the same plaid_transaction_id is unexpected and
    -- may indicate a data corruption issue.
    SELECT COUNT(*) INTO v_count FROM (
        SELECT user_id, plaid_transaction_id
        FROM ""Transactions""
        WHERE plaid_transaction_id IS NOT NULL AND plaid_transaction_id != ''
        GROUP BY user_id, plaid_transaction_id
        HAVING COUNT(*) > 1
          AND COUNT(DISTINCT CASE WHEN suggested_kind != 0 THEN suggested_kind ELSE NULL END) > 1
    ) c;
    IF v_count > 0 THEN
        RAISE EXCEPTION
            'MIGRATION ABORTED: % duplicate group(s) have conflicting suggested_kind values. '
            'Run the diagnostic SELECT and resolve manually.',
            v_count;
    END IF;

    -- Check 4: Multiple rows with counted_as_income = true in the same duplicate group.
    -- Deposits use counted_as_income rather than budget_applied_amount for balance impact.
    -- If more than one duplicate deposit row was counted as income, the balance may have
    -- been credited multiple times. This cannot be automatically corrected without knowing
    -- which credits were actually applied to the balance. Require manual review.
    SELECT COUNT(*) INTO v_count FROM (
        SELECT user_id, plaid_transaction_id
        FROM ""Transactions""
        WHERE plaid_transaction_id IS NOT NULL AND plaid_transaction_id != ''
        GROUP BY user_id, plaid_transaction_id
        HAVING COUNT(*) > 1
          AND SUM(CASE WHEN counted_as_income = true THEN 1 ELSE 0 END) > 1
    ) c;
    IF v_count > 0 THEN
        RAISE EXCEPTION
            'MIGRATION ABORTED: % duplicate deposit group(s) have more than one row with '
            'counted_as_income = true. The balance may have been credited multiple times for '
            'the same deposit. This requires manual review and correction before the unique '
            'constraint can be safely applied. Run the diagnostic SELECT to identify them.',
            v_count;
    END IF;
END;
$$;
");

            // ── PHASE 2: Merge user state into canonical row ─────────────────────────
            // For each duplicate group, the canonical row is chosen by:
            //   1. Prefer rows with a non-zero user_decision (user has already acted)
            //   2. Prefer posted (pending = false) over pending
            //   3. Prefer highest id (most recently inserted)
            // Mergeable boolean flags (hold_reviewed, counted_as_income, LargeExpenseHandled,
            // IsLargeExpenseCandidate) are OR'd across all rows in the group so no true
            // value is lost. Non-null scalar fields (hold_override_amount, suggested_kind,
            // user_decision) use MAX / COALESCE to keep the most meaningful value.

            migrationBuilder.Sql(@"
WITH ranked AS (
    SELECT
        id,
        user_id,
        plaid_transaction_id,
        user_decision,
        counted_as_income,
        ""LargeExpenseHandled"",
        hold_reviewed,
        hold_override_amount,
        ""IsLargeExpenseCandidate"",
        suggested_kind,
        pending,
        ROW_NUMBER() OVER (
            PARTITION BY user_id, plaid_transaction_id
            ORDER BY
                -- Rows with a real user decision rank first
                CASE WHEN user_decision != 0 THEN 0 ELSE 1 END,
                -- Posted rows rank above pending rows
                CASE WHEN pending = false THEN 0 ELSE 1 END,
                -- Newest row (highest id) wins ties
                id DESC
        ) AS rn
    FROM ""Transactions""
    WHERE plaid_transaction_id IS NOT NULL AND plaid_transaction_id != ''
),
group_state AS (
    -- Aggregate the best state across every row in each duplicate group.
    -- Only groups with more than 1 row are included.
    SELECT
        user_id,
        plaid_transaction_id,
        MAX(CASE WHEN rn = 1 THEN id END)                          AS canonical_id,
        GREATEST(MAX(user_decision), 0)                            AS merged_user_decision,
        BOOL_OR(counted_as_income)                                 AS merged_counted_as_income,
        BOOL_OR(""LargeExpenseHandled"")                           AS merged_le_handled,
        BOOL_OR(hold_reviewed)                                     AS merged_hold_reviewed,
        MAX(hold_override_amount)                                  AS merged_hold_override,
        BOOL_OR(""IsLargeExpenseCandidate"")                       AS merged_is_le_candidate,
        MAX(CASE WHEN suggested_kind != 0 THEN suggested_kind END) AS merged_kind
    FROM ranked
    GROUP BY user_id, plaid_transaction_id
    HAVING COUNT(*) > 1
)
UPDATE ""Transactions"" t
SET
    user_decision           = GREATEST(t.user_decision, gs.merged_user_decision),
    counted_as_income       = gs.merged_counted_as_income,
    ""LargeExpenseHandled"" = gs.merged_le_handled,
    hold_reviewed           = gs.merged_hold_reviewed,
    hold_override_amount    = COALESCE(gs.merged_hold_override, t.hold_override_amount),
    ""IsLargeExpenseCandidate"" = gs.merged_is_le_candidate,
    suggested_kind          = COALESCE(gs.merged_kind, NULLIF(t.suggested_kind, 0), 0)
FROM group_state gs
WHERE t.id = gs.canonical_id;
");

            // ── PHASE 3: Restore balance for deleted spend duplicate rows ────────────
            // Each duplicate spend row had budget_applied_amount set when it was inserted
            // and subtracted from the user's balance. We add those amounts back before
            // deleting the rows so the balance is correct after cleanup.
            //
            // Deposit duplicate rows (budget_applied_amount IS NULL) are intentionally
            // skipped: they do not directly affect the balance via budget_applied_amount.
            // Phase 1 (Check 4) already aborted if any deposit group had multiple
            // counted_as_income = true rows — so at most one deposit duplicate was credited.
            // Merging counted_as_income via BOOL_OR in Phase 2 ensures the canonical row
            // retains the flag. No additional balance correction is needed for deposits here.
            //
            // Verified column names from ApiDbContextModelSnapshot:
            //   Balances."userId"  (camelCase — must be quoted in Postgres raw SQL)
            //   Balances.balance   (lowercase — no quote required)

            migrationBuilder.Sql(@"
WITH ranked AS (
    SELECT
        id,
        user_id,
        budget_applied_amount,
        pending,
        user_decision,
        ROW_NUMBER() OVER (
            PARTITION BY user_id, plaid_transaction_id
            ORDER BY
                CASE WHEN user_decision != 0 THEN 0 ELSE 1 END,
                CASE WHEN pending = false THEN 0 ELSE 1 END,
                id DESC
        ) AS rn
    FROM ""Transactions""
    WHERE plaid_transaction_id IS NOT NULL AND plaid_transaction_id != ''
),
to_delete AS (
    -- Rows that will be deleted: non-canonical rows with a non-zero budget_applied_amount.
    -- These rows each subtracted from the balance when inserted; reversing that overcounting.
    SELECT user_id, budget_applied_amount
    FROM ranked
    WHERE rn > 1
      AND budget_applied_amount IS NOT NULL
      AND budget_applied_amount != 0
)
UPDATE ""Balances"" b
SET balance = b.balance + d.total_to_restore
FROM (
    SELECT user_id, SUM(budget_applied_amount) AS total_to_restore
    FROM to_delete
    GROUP BY user_id
) d
WHERE b.""userId"" = d.user_id;
");

            // ── PHASE 4: Delete duplicate rows ───────────────────────────────────────
            // Uses the identical ranking logic as Phases 2 and 3.
            // All non-canonical rows (rn > 1) are deleted.
            // The canonical row (rn = 1) — now containing merged state from Phase 2 —
            // is preserved.

            migrationBuilder.Sql(@"
DELETE FROM ""Transactions""
WHERE id IN (
    SELECT id FROM (
        SELECT
            id,
            ROW_NUMBER() OVER (
                PARTITION BY user_id, plaid_transaction_id
                ORDER BY
                    CASE WHEN user_decision != 0 THEN 0 ELSE 1 END,
                    CASE WHEN pending = false THEN 0 ELSE 1 END,
                    id DESC
            ) AS rn
        FROM ""Transactions""
        WHERE plaid_transaction_id IS NOT NULL AND plaid_transaction_id != ''
    ) ranked
    WHERE rn > 1
);
");

            // ── PHASE 5: Add unique index ─────────────────────────────────────────────
            // Now that duplicates are removed, the unique constraint can be applied safely.
            // Scoped to (user_id, plaid_transaction_id) — not just plaid_transaction_id —
            // for architectural clarity, even though Plaid IDs are globally unique in practice.

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_UserId_PlaidTransactionId",
                table: "Transactions",
                columns: new[] { "user_id", "plaid_transaction_id" },
                unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Transactions_UserId_PlaidTransactionId",
                table: "Transactions");

            // Note: the data cleanup and balance corrections from Up() are not reversed.
            // Restoring deleted duplicate rows is not safe or meaningful in a rollback.
        }
    }
}
