-- ============================================================================
-- Production Data Cleanup Script: User 56 Fixed Costs
-- ============================================================================
-- Purpose: Fix suspicious fixed costs created during onboarding for user 56:
--   1. Delete "Amex Send: Add Money" (transfer-like, not a subscription)
--   2. Update "Amazon Prime" $151.37 to recurrence_frequency = 'Yearly'
--   3. Update "RENEWAL MEMBERSHIP FEE" $95 to recurrence_frequency = 'Yearly'
--   4. Leave "TE CERTIFIED ELECTRICAL" unchanged (valid monthly expense)
--
-- IMPORTANT: This script defaults to ROLLBACK. Change to COMMIT only after
-- reviewing the preview output and confirming the changes are correct.
-- ============================================================================

BEGIN;

-- ── Preview: Show all potentially affected rows for user 56 ────────────────
SELECT
    id,
    user_id,
    name,
    amount,
    category,
    type,
    plaid_merchant_name,
    recurrence_frequency,
    next_due_date,
    original_due_day_of_month,
    created_at,
    updated_at
FROM "FixedCosts"
WHERE user_id = 56
  AND (
      name ILIKE '%Amex Send%'
      OR name ILIKE '%Amazon Prime%'
      OR name ILIKE '%RENEWAL MEMBERSHIP FEE%'
      OR name ILIKE '%TE CERTIFIED%'
  )
ORDER BY name, amount;

-- ── 1. Delete false positive transfer-like fixed cost ──────────────────────
-- "Amex Send: Add Money" is a wallet load / account transfer, not a
-- subscription or recurring bill. It should never have been created as a
-- fixed cost.
DELETE FROM "FixedCosts"
WHERE user_id = 56
  AND name ILIKE '%Amex Send%';

-- ── 2. Correct annual fixed costs ──────────────────────────────────────────
-- Amazon Prime $151.37 and RENEWAL MEMBERSHIP FEE $95 both have next_due_date
-- approximately one year out, indicating they are annual subscriptions, not
-- monthly. Update recurrence_frequency to 'Yearly' to match their actual
-- billing cycle.
UPDATE "FixedCosts"
SET
    recurrence_frequency = 'Yearly',
    updated_at = NOW()
WHERE user_id = 56
  AND (
      (name ILIKE '%Amazon Prime%' AND amount BETWEEN 150 AND 153)
      OR (name ILIKE '%RENEWAL MEMBERSHIP FEE%' AND amount BETWEEN 94 AND 96)
  );

-- ── 3. Verify result ────────────────────────────────────────────────────────
-- Re-query the same rows to confirm the changes. Expected result:
--   - "Amex Send: Add Money" should be absent (deleted)
--   - "Amazon Prime" should have recurrence_frequency = 'Yearly'
--   - "RENEWAL MEMBERSHIP FEE" should have recurrence_frequency = 'Yearly'
--   - "TE CERTIFIED ELECTRICAL" should be unchanged (if present)
SELECT
    id,
    user_id,
    name,
    amount,
    recurrence_frequency,
    next_due_date,
    updated_at
FROM "FixedCosts"
WHERE user_id = 56
  AND (
      name ILIKE '%Amex Send%'
      OR name ILIKE '%Amazon Prime%'
      OR name ILIKE '%RENEWAL MEMBERSHIP FEE%'
      OR name ILIKE '%TE CERTIFIED%'
  )
ORDER BY name, amount;

-- ── ROLLBACK by default ─────────────────────────────────────────────────────
-- Change this to COMMIT only after reviewing the preview and verification
-- output above and confirming the changes are correct.
ROLLBACK;
-- COMMIT;
