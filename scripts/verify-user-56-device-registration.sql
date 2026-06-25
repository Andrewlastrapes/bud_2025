-- ============================================================================
-- Verification Script: User 56 Device Registration
-- ============================================================================
-- Purpose: Verify that user 56 now has an active device row after the
-- frontend fix is deployed and the user logs in again.
--
-- Expected result BEFORE fix + user login:
--   56 | june19th@gmail.com | 0 | 0 | (null)
--
-- Expected result AFTER fix + user login:
--   56 | june19th@gmail.com | 1 | 1 | <recent timestamp>
--
-- This is a READ-ONLY query. No data is modified.
-- ============================================================================

SELECT
  u.id AS user_id,
  u.email,
  count(d.id) AS device_rows,
  count(d.id) FILTER (WHERE d.is_active) AS active_device_rows,
  max(d.updated_at) AS last_device_update
FROM "Users" u
LEFT JOIN "UserDevices" d ON d.user_id = u.id
WHERE u.id IN (46, 51, 55, 56)
GROUP BY u.id, u.email
ORDER BY u.id;

-- ── Additional diagnostic: show all device rows for user 56 ─────────────────
SELECT
  id,
  user_id,
  expo_push_token,
  platform,
  is_active,
  created_at,
  updated_at
FROM "UserDevices"
WHERE user_id = 56
ORDER BY created_at DESC;
