# Skill: Debug Plaid Sync

Use this skill when transactions are missing, duplicated, pending forever, wrong amount, or wrong category after a Plaid sync.

Work through the checklist in order. Stop when you find the cause.

---

## Checklist

### Missing transactions

- [ ] Is the Plaid item in an error state? Check `PlaidItems.ItemStatus` or call `/item/get` to see if re-link is needed.
- [ ] Was a webhook received? Check server logs for `TRANSACTIONS` webhook events.
- [ ] Did the sync actually run? Check logs for `added/modified/removed` counts on the most recent sync.
- [ ] Is `has_more` true but the sync loop stopped early? The loop must continue until `has_more` is false.
- [ ] Is `next_cursor` stale or null? A null cursor causes a full historical re-sync. A stale cursor means missed updates.
- [ ] Is the transaction being filtered out by cycle date logic? Check if the transaction date falls within the current paycheck cycle window.
- [ ] Is the transaction in the `removed` list from a previous sync but never re-added?

### Duplicate transactions

- [ ] Does the `Transactions` table have a unique constraint on `plaid_transaction_id`? (It should — migration `AddUniqueTransactionConstraintAndCleanupDuplicates`.)
- [ ] Is the sync doing an insert without checking for an existing row? It should use upsert or catch the unique constraint violation.
- [ ] Was a webhook processed twice? The handler must be idempotent.
- [ ] Is there a pending row that was never removed when the posted version arrived? Plaid removes the pending `plaid_transaction_id` and adds a new posted one — both must be handled.
- [ ] Are there duplicate rows with different `plaid_transaction_id` values but the same merchant, amount, and date? This is a Plaid data issue, not a sync bug — flag it but do not auto-merge.

### Pending transactions that never post

- [ ] Plaid models pending → posted as: **remove** the pending transaction, **add** a new posted transaction. If `removed` is not handled, the pending row stays forever.
- [ ] Check if `removed` array processing is skipped or throwing silently.
- [ ] Check if the pending row's `plaid_transaction_id` matches the one in the `removed` list.
- [ ] A pending transaction can stay pending for several business days — this is normal. Confirm the transaction date is recent before treating it as a bug.

### Posted transactions misclassified as pending (or vice versa)

- [ ] Check the `pending` boolean on the Plaid transaction object. `true` = pending, `false` = posted.
- [ ] Check that the DB stores this flag correctly and that the frontend reads it correctly.

### Added / modified / removed handling

- [ ] `added`: insert or upsert into `Transactions`, keyed on `plaid_transaction_id`.
- [ ] `modified`: update the existing row — amount, merchant name, category, pending flag may change.
- [ ] `removed`: delete or soft-delete the row with the matching `plaid_transaction_id`.
- [ ] All three must be processed in every sync call, even if one array is empty.

### Misclassified or wrong-amount transactions

- [ ] Plaid amounts: **positive = spending, negative = income**. Verify the app is not flipping this.
- [ ] Is the merchant name or category coming from `personal_finance_category` (newer) or `category` (older)? Check which Plaid product version is active.
- [ ] Is the transaction being matched to the wrong fixed cost by `FixedCostMatcher`? Check the matching heuristic against the actual merchant name.

### Cursor / sync state issues

- [ ] Is `next_cursor` being saved to the DB after each page? If the process crashes mid-sync, the cursor should resume from the last saved position.
- [ ] Is `next_cursor` being read from the correct `PlaidItem` row (by `UserId` and `item_id`)?
- [ ] If resetting the cursor to null: this triggers a full re-sync from the beginning. All existing transactions for that item should be reconciled or cleared first.

---

## Useful diagnostic steps

```
# Check PlaidItem state for a user
SELECT id, item_id, item_status, cursor, created_at FROM "PlaidItems" WHERE "UserId" = <id>;

# Check recent transactions for a user
SELECT plaid_transaction_id, name, amount, pending, date, created_at
FROM "Transactions"
WHERE "UserId" = <id>
ORDER BY created_at DESC
LIMIT 50;

# Check for duplicate plaid_transaction_ids
SELECT plaid_transaction_id, COUNT(*)
FROM "Transactions"
WHERE "UserId" = <id>
GROUP BY plaid_transaction_id
HAVING COUNT(*) > 1;
```

Do not run these against production directly. Use a read replica or a local copy of the data.
