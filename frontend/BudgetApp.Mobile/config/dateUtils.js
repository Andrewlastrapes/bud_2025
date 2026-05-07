/**
 * dateUtils.js — Shared date helpers for the Budget app.
 *
 * Accepts:
 *   • YYYY-MM-DD  (ISO date from API/Plaid)
 *   • MM/DD       (simple user input — year is inferred)
 *   • MM/DD/YYYY  (full manual input)
 *
 * Does NOT silently produce invalid dates — every parsing path either
 * returns a well-formed ISO string or throws a user-visible Error.
 */

// ─── Internal: infer the year for a MM/DD input ──────────────────────────────
// If the date falls on today or later this calendar year → use current year.
// If it has already passed (strictly before today) → use next year.
const _inferYear = (month, day) => {
  const today = new Date();
  const currentYear = today.getFullYear();
  const candidate = new Date(currentYear, month - 1, day);
  const todayMidnight = new Date(
    today.getFullYear(),
    today.getMonth(),
    today.getDate(),
  );
  return candidate < todayMidnight ? currentYear + 1 : currentYear;
};

// ─── getISODate ───────────────────────────────────────────────────────────────
/**
 * Convert a date string to a JavaScript ISO string (local midnight).
 *
 * Accepts: YYYY-MM-DD | MM/DD | MM/DD/YYYY
 * Returns null for empty / null / undefined input.
 * Throws a user-friendly Error for malformed input.
 *
 * @param {string|null|undefined} dateString
 * @param {string} fieldName  – used in error messages (e.g. "Rent due date")
 * @returns {string|null}  ISO 8601 string, or null
 */
export const getISODate = (dateString, fieldName = "Date") => {
  if (!dateString || !String(dateString).trim()) return null;

  const trimmed = String(dateString).trim();

  // ── Format 1: YYYY-MM-DD (from API / Plaid) ──────────────────────────────
  if (/^\d{4}-\d{2}-\d{2}$/.test(trimmed)) {
    const [y, m, d] = trimmed.split("-").map(Number);
    const date = new Date(y, m - 1, d); // local midnight
    if (
      date.getFullYear() !== y ||
      date.getMonth() !== m - 1 ||
      date.getDate() !== d
    ) {
      throw new Error(`Invalid date for ${fieldName}.`);
    }
    return date.toISOString();
  }

  // ── Format 2: MM/DD or MM/DD/YYYY (user input) ───────────────────────────
  const parts = trimmed.split("/");
  if (parts.length < 2 || parts.length > 3) {
    throw new Error(`Invalid date format for ${fieldName}. Please use MM/DD.`);
  }

  const month = Number(parts[0]);
  const day = Number(parts[1]);
  const year = parts.length === 3 ? Number(parts[2]) : _inferYear(month, day);

  if (isNaN(month) || isNaN(day) || isNaN(year)) {
    throw new Error(`Invalid date format for ${fieldName}. Please use MM/DD.`);
  }

  const date = new Date(year, month - 1, day);
  if (
    date.getFullYear() !== year ||
    date.getMonth() !== month - 1 ||
    date.getDate() !== day
  ) {
    throw new Error(`Invalid date for ${fieldName}. Please use MM/DD.`);
  }

  return date.toISOString();
};

// ─── formatDisplayDate ────────────────────────────────────────────────────────
/**
 * Format any date string (ISO, YYYY-MM-DD, or full ISO timestamp from API)
 * as "MM/DD/YYYY" for display.
 *
 * Extracts YYYY-MM-DD from the raw string first (regex) to avoid UTC/local
 * timezone surprises when the API returns a full ISO timestamp like
 * "2026-06-01T04:00:00.000Z".
 *
 * Returns null for null / empty / unparseable input.
 *
 * @param {string|null|undefined} dateStr
 * @returns {string|null}
 */
export const formatDisplayDate = (dateStr) => {
  if (!dateStr) return null;
  try {
    // Prefer extracting YYYY-MM-DD directly to avoid timezone offset issues
    const isoMatch = String(dateStr).match(/^(\d{4})-(\d{2})-(\d{2})/);
    if (isoMatch) {
      const [, y, m, d] = isoMatch;
      return `${m}/${d}/${y}`;
    }
    // Fallback for other date formats
    const parsed = new Date(dateStr);
    if (isNaN(parsed.getTime())) return null;
    const mo = String(parsed.getMonth() + 1).padStart(2, "0");
    const da = String(parsed.getDate()).padStart(2, "0");
    return `${mo}/${da}/${parsed.getFullYear()}`;
  } catch {
    return null;
  }
};

// ─── formatMMDD ───────────────────────────────────────────────────────────────
/**
 * Format a stored date (ISO or YYYY-MM-DD) as "MM/DD" for use in edit input
 * fields.  Returns '' for null / empty / unparseable input.
 *
 * @param {string|null|undefined} dateStr
 * @returns {string}
 */
export const formatMMDD = (dateStr) => {
  if (!dateStr) return "";
  try {
    const isoMatch = String(dateStr).match(/^(\d{4})-(\d{2})-(\d{2})/);
    if (isoMatch) {
      const [, , m, d] = isoMatch;
      return `${m}/${d}`;
    }
    return "";
  } catch {
    return "";
  }
};

// ─── getNextMonthFirstDay ─────────────────────────────────────────────────────
/**
 * Returns the 1st of next month as a "MM/DD" string (no year).
 * Useful for pre-populating rent due dates.
 *
 * @returns {string}  e.g. "06/01"
 */
export const getNextMonthFirstDay = () => {
  const today = new Date();
  const date = new Date(today.getFullYear(), today.getMonth() + 1, 1);
  const month = String(date.getMonth() + 1).padStart(2, "0");
  const day = String(date.getDate()).padStart(2, "0");
  return `${month}/${day}`;
};
