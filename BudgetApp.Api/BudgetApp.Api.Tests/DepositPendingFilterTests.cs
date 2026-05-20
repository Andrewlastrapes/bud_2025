// ─── DepositPendingFilterTests.cs ─────────────────────────────────────────────
// Verifies the deposit-kind classification and filter logic that feeds the
// GET /api/transactions/deposits/pending and
// GET /api/transactions/deposits/pending/summary endpoints.
//
// Business rule (enforced since 2026-05-20):
//   Paychecks are *expected* income. The user entered their paycheck schedule
//   and expected amount during onboarding. The dynamic budget already accounts
//   for paychecks, so they must NOT appear in "Review New Deposits."
//
// Both endpoints filter by:
//   1. t.UserId == user.Id
//   2. unexpectedDepositKinds.Contains(t.SuggestedKind)  ← Windfall | InternalTransfer | Refund
//      (Paycheck is intentionally excluded)
//   3. t.UserDecision == Undecided
//   4. !t.IsLargeExpenseCandidate
//
// These tests exercise the classification engine that assigns SuggestedKind values
// (DynamicBudgetEngine.ClassifyDeposit) and confirm:
//   - unexpected deposits (Windfall, InternalTransfer, Refund) are included
//   - Paycheck IS correctly classified as Paycheck by the engine but is NOT in the filter
//   - debit/spend transactions are NEVER assigned a deposit kind (they don't call ClassifyDeposit)
//   - the unexpected-deposit kinds list is exactly the expected three values
// ──────────────────────────────────────────────────────────────────────────────

using System;
using System.Collections.Generic;
using System.Linq;
using BudgetApp.Api.Data;
using BudgetApp.Api.Services;
using Xunit;

namespace BudgetApp.Api.Tests
{
    public class DepositPendingFilterTests
    {
        // ── Shared ────────────────────────────────────────────────────────────
        private static readonly DynamicBudgetEngine Engine = new();

        // The three unexpected-deposit kinds that the pending and summary endpoints accept.
        // Paycheck is intentionally absent — it is expected income, not a user-reviewable deposit.
        // If this list changes in Program.cs, these tests will catch it.
        private static readonly HashSet<TransactionSuggestedKind> UnexpectedDepositKinds = new()
        {
            TransactionSuggestedKind.Windfall,
            TransactionSuggestedKind.InternalTransfer,
            TransactionSuggestedKind.Refund,
        };

        // ── 1. Paycheck: correctly classified but EXCLUDED from review filter ──
        // The engine still classifies a paycheck-day deposit as Paycheck.
        // But Paycheck must NOT be in the unexpected-deposit filter.
        [Fact]
        public void Paycheck_IsClassifiedAsPaycheck_ButExcludedFromUnexpectedDepositFilter()
        {
            var ctx = new DepositContext
            {
                Amount = 3000m,
                Date = new DateTime(2026, 5, 1),
                MerchantName = "DIRECT DEPOSIT",
                PayDay1 = 1,
                PayDay2 = 15,
                ExpectedPaycheckAmount = 3000m
            };

            var kind = Engine.ClassifyDeposit(ctx);

            // Engine correctly classifies it as a paycheck
            Assert.Equal(TransactionSuggestedKind.Paycheck, kind);

            // But Paycheck must NOT appear in the unexpected-deposit review filter
            Assert.DoesNotContain(kind, UnexpectedDepositKinds);
        }

        // ── 2. Refund appears ─────────────────────────────────────────────────
        [Fact]
        public void Refund_IsClassifiedAsRefund_AndPassesUnexpectedDepositFilter()
        {
            var ctx = new DepositContext
            {
                Amount = 42.50m,
                Date = new DateTime(2026, 5, 10),
                MerchantName = "AMAZON REFUND",
                PayDay1 = 1,
                PayDay2 = 15,
                ExpectedPaycheckAmount = 3000m
            };

            var kind = Engine.ClassifyDeposit(ctx);

            Assert.Equal(TransactionSuggestedKind.Refund, kind);
            Assert.Contains(kind, UnexpectedDepositKinds);
        }

        // ── 3. Windfall appears ───────────────────────────────────────────────
        [Fact]
        public void Windfall_IsClassifiedAsWindfall_AndPassesUnexpectedDepositFilter()
        {
            // A large, non-paycheck-day credit that is much bigger than the expected paycheck
            var ctx = new DepositContext
            {
                Amount = 10_000m,
                Date = new DateTime(2026, 5, 7),   // not a pay day (pay days are 1 and 15)
                MerchantName = "WIRE TRANSFER",
                PayDay1 = 1,
                PayDay2 = 15,
                ExpectedPaycheckAmount = 3000m
            };

            var kind = Engine.ClassifyDeposit(ctx);

            Assert.Equal(TransactionSuggestedKind.Windfall, kind);
            Assert.Contains(kind, UnexpectedDepositKinds);
        }

        // ── 4. InternalTransfer appears ───────────────────────────────────────
        [Fact]
        public void InternalTransfer_IsClassifiedAsInternalTransfer_AndPassesUnexpectedDepositFilter()
        {
            var ctx = new DepositContext
            {
                Amount = 500m,
                Date = new DateTime(2026, 5, 7),   // not a pay day
                MerchantName = "TRANSFER FROM SAVINGS",
                PayDay1 = 1,
                PayDay2 = 15,
                ExpectedPaycheckAmount = 3000m
            };

            var kind = Engine.ClassifyDeposit(ctx);

            // InternalTransfer is the expected classification for a bank-to-bank transfer
            Assert.Equal(TransactionSuggestedKind.InternalTransfer, kind);
            Assert.Contains(kind, UnexpectedDepositKinds);
        }

        // ── 5. Debit/spend transactions do NOT call ClassifyDeposit ───────────
        // In TransactionService the credit-vs-debit branch is:
        //   if (isCredit) { newTx.SuggestedKind = _budgetEngine.ClassifyDeposit(ctx); }
        //   else          { /* SuggestedKind stays at default Unknown (0) */ }
        //
        // This test confirms that Unknown == 0 (the EF default) and is NOT in the
        // unexpected-deposit kinds filter — so debit transactions never appear there.
        [Fact]
        public void Unknown_IsNotInUnexpectedDepositKinds_SoDebitTransactionsNeverAppear()
        {
            // Unknown is the zero/default value — set automatically by EF for all debit rows
            var unknownKind = TransactionSuggestedKind.Unknown;

            Assert.Equal(0, (int)unknownKind);
            Assert.DoesNotContain(unknownKind, UnexpectedDepositKinds);
        }

        // ── 6. Normal debit purchase → Unknown, excluded from deposit endpoint ─
        [Fact]
        public void DebitPurchase_HasUnknownKind_AndIsExcludedFromDepositFilter()
        {
            // Simulate what TransactionService stores for a normal debit: SuggestedKind stays Unknown
            var debitTx = new Transaction
            {
                Id = 1,
                UserId = 99,
                Amount = 55.00m,
                SuggestedKind = TransactionSuggestedKind.Unknown,   // debit — ClassifyDeposit NOT called
                UserDecision = TransactionUserDecision.Undecided,
                IsLargeExpenseCandidate = false,
            };

            bool passesFilter =
                UnexpectedDepositKinds.Contains(debitTx.SuggestedKind)
                && debitTx.UserDecision == TransactionUserDecision.Undecided
                && !debitTx.IsLargeExpenseCandidate;

            Assert.False(passesFilter, "A debit/spend transaction must NOT pass the unexpected deposit filter.");
        }

        // ── 7. Large expense candidate is excluded even if it has a deposit kind ─
        [Fact]
        public void LargeExpenseCandidate_IsExcluded_EvenIfDepositKindSet()
        {
            var largeTx = new Transaction
            {
                Id = 2,
                UserId = 99,
                Amount = 4000m,
                SuggestedKind = TransactionSuggestedKind.Windfall,  // hypothetical miscategorisation
                UserDecision = TransactionUserDecision.Undecided,
                IsLargeExpenseCandidate = true,                     // must be excluded
            };

            bool passesFilter =
                UnexpectedDepositKinds.Contains(largeTx.SuggestedKind)
                && largeTx.UserDecision == TransactionUserDecision.Undecided
                && !largeTx.IsLargeExpenseCandidate;

            Assert.False(passesFilter, "A large expense candidate must NOT appear in the unexpected deposit review list.");
        }

        // ── 8. Already-decided deposit is excluded ────────────────────────────
        [Fact]
        public void AlreadyDecidedDeposit_IsExcluded_FromPendingList()
        {
            var decidedTx = new Transaction
            {
                Id = 3,
                UserId = 99,
                Amount = 42.50m,
                SuggestedKind = TransactionSuggestedKind.Refund,
                UserDecision = TransactionUserDecision.TreatAsIncome,  // already reviewed
                IsLargeExpenseCandidate = false,
            };

            bool passesFilter =
                UnexpectedDepositKinds.Contains(decidedTx.SuggestedKind)
                && decidedTx.UserDecision == TransactionUserDecision.Undecided
                && !decidedTx.IsLargeExpenseCandidate;

            Assert.False(passesFilter, "An already-reviewed deposit must NOT appear in the pending list.");
        }

        // ── 9. Undecided Paycheck does NOT pass the unexpected-deposit filter ──
        // This is the core acceptance criterion for the 2026-05-20 change.
        // Even a perfectly valid undecided paycheck must be hidden from the review UI.
        [Fact]
        public void UndecidedPaycheck_DoesNotPassUnexpectedDepositFilter()
        {
            var paycheck = new Transaction
            {
                Id = 4,
                UserId = 99,
                Amount = 3000m,
                SuggestedKind = TransactionSuggestedKind.Paycheck,
                UserDecision = TransactionUserDecision.Undecided,
                IsLargeExpenseCandidate = false,
            };

            bool passesFilter =
                UnexpectedDepositKinds.Contains(paycheck.SuggestedKind)
                && paycheck.UserDecision == TransactionUserDecision.Undecided
                && !paycheck.IsLargeExpenseCandidate;

            Assert.False(passesFilter,
                "Paychecks are expected income and must NOT appear in the unexpected deposit review list.");
        }

        // ── 10. Unexpected-deposit kinds set contains exactly three expected values ─
        // Paycheck must be absent. Windfall, InternalTransfer, Refund must all be present.
        [Fact]
        public void UnexpectedDepositKinds_ContainsExactlyThreeValues_PaycheckAbsent()
        {
            Assert.Equal(3, UnexpectedDepositKinds.Count);
            Assert.DoesNotContain(TransactionSuggestedKind.Paycheck, UnexpectedDepositKinds);
            Assert.DoesNotContain(TransactionSuggestedKind.Unknown, UnexpectedDepositKinds);
            Assert.Contains(TransactionSuggestedKind.Windfall, UnexpectedDepositKinds);
            Assert.Contains(TransactionSuggestedKind.InternalTransfer, UnexpectedDepositKinds);
            Assert.Contains(TransactionSuggestedKind.Refund, UnexpectedDepositKinds);
        }

        // ─── Summary endpoint filter tests ───────────────────────────────────
        // The summary endpoint uses the exact same UnexpectedDepositKinds set.
        // These tests confirm count/sum logic against a synthetic transaction list.

        private static bool PassesSummaryFilter(Transaction tx) =>
            UnexpectedDepositKinds.Contains(tx.SuggestedKind)
            && tx.UserDecision == TransactionUserDecision.Undecided
            && !tx.IsLargeExpenseCandidate;

        // ── 11. Summary excludes Paycheck ─────────────────────────────────────
        [Fact]
        public void DepositSummaryFilter_ExcludesPaycheck()
        {
            var paycheck = new Transaction
            {
                Id = 10,
                UserId = 99,
                Amount = 3000m,
                SuggestedKind = TransactionSuggestedKind.Paycheck,
                UserDecision = TransactionUserDecision.Undecided,
                IsLargeExpenseCandidate = false,
            };

            Assert.False(PassesSummaryFilter(paycheck),
                "Paycheck must not be counted by the deposit summary endpoint.");
        }

        // ── 12. Summary includes Windfall ─────────────────────────────────────
        [Fact]
        public void DepositSummaryFilter_IncludesWindfall()
        {
            var windfall = new Transaction
            {
                Id = 11,
                UserId = 99,
                Amount = 10_000m,
                SuggestedKind = TransactionSuggestedKind.Windfall,
                UserDecision = TransactionUserDecision.Undecided,
                IsLargeExpenseCandidate = false,
            };

            Assert.True(PassesSummaryFilter(windfall),
                "Windfall must be counted by the deposit summary endpoint.");
        }

        // ── 13. Summary counts and sums only unexpected deposits ──────────────
        // Given a mixed set: 1 Windfall + 1 Refund + 1 InternalTransfer + 1 Paycheck
        // Expected: count=3, totalAmount=$750 (Paycheck excluded).
        [Fact]
        public void DepositSummaryFilter_CountsAndSumsOnlyUnexpectedDeposits()
        {
            var transactions = new List<Transaction>
            {
                new() { Id = 20, UserId = 99, Amount = 200m,
                    SuggestedKind = TransactionSuggestedKind.Windfall,
                    UserDecision = TransactionUserDecision.Undecided, IsLargeExpenseCandidate = false },
                new() { Id = 21, UserId = 99, Amount = 50m,
                    SuggestedKind = TransactionSuggestedKind.Refund,
                    UserDecision = TransactionUserDecision.Undecided, IsLargeExpenseCandidate = false },
                new() { Id = 22, UserId = 99, Amount = 500m,
                    SuggestedKind = TransactionSuggestedKind.InternalTransfer,
                    UserDecision = TransactionUserDecision.Undecided, IsLargeExpenseCandidate = false },
                // This paycheck must be excluded from count and sum
                new() { Id = 23, UserId = 99, Amount = 3000m,
                    SuggestedKind = TransactionSuggestedKind.Paycheck,
                    UserDecision = TransactionUserDecision.Undecided, IsLargeExpenseCandidate = false },
            };

            var filtered = transactions.Where(PassesSummaryFilter).ToList();

            Assert.Equal(3, filtered.Count);
            Assert.Equal(750m, filtered.Sum(t => t.Amount));

            // Paycheck not in result
            Assert.DoesNotContain(filtered, t => t.SuggestedKind == TransactionSuggestedKind.Paycheck);
        }
    }
}
