// ─── commonSubscriptions.js ───────────────────────────────────────────────────
//
// Curated list of common recurring subscriptions for the onboarding quick-add
// section in FixedCostsSetupScreen.
//
// Duplicate-detection algorithm
// ──────────────────────────────
// A subscription is considered "covered" (already found by Plaid or manually
// added) if ANY existing cost name matches ANY alias via three rules:
//
//   Rule 1 — exact equality after normalization
//   Rule 2 — existing name CONTAINS the alias
//             (Plaid names are long; alias is the distinctive core substring)
//             Only applied when alias.length >= MIN_ALIAS_LENGTH (5 chars) to
//             avoid short aliases like "max", "hbo", or "ring" triggering on
//             unrelated merchants.
//   Rule 3 — alias CONTAINS the existing name
//             (e.g. existing "prime video" matches alias "prime video channels")
//             Only applied when existing.length >= MIN_ALIAS_LENGTH (5 chars).
//
// Normalization: lowercase → strip non-alphanumeric except spaces → trim →
//                collapse multiple spaces to one.
//
// ─── Manual verification cases ───────────────────────────────────────────────
// The following cases document the expected behavior. Run getCoveredKeys with
// these inputs to verify correct matching:
//
// PASS — "OPENAI *CHATGPT SUBSSAN FRANCISCO" hides ChatGPT
//   norm → "openai chatgpt subssan francisco"
//   rule 2: contains "chatgpt" (7 chars) ✓
//
// PASS — "Prime Video Channels" hides Prime Video
//   norm → "prime video channels"
//   rule 2: contains "prime video" (11 chars) ✓
//
// PASS — "YT PRIMETIME G.CO/HELPPAY#" hides YouTube Premium
//   norm → "yt primetime gcohelppay"
//   rule 2: contains "yt primetime" (12 chars) ✓
//
// PASS — "HBO Max" hides Max
//   norm → "hbo max"
//   rule 1: exact match on alias "hbo max" ✓
//
// FAIL (expected) — "Amazon Fresh Weekly Delivery" does NOT hide Prime Video
//   norm → "amazon fresh weekly delivery"
//   "amazon" is intentionally NOT in the alias list (too broad)
//   None of the prime video aliases match ✓
//
// FAIL (expected) — "APPLE.COM/BILL INTERNET CHARGE" does NOT hide any Apple service
//   norm → "applecom bill internet charge"
//   "applecom bill" is intentionally NOT an alias for any service (too ambiguous)
//   Does not contain "icloud", "apple music", "apple tv" substrings ✓
//   All Apple services remain visible in the quick-add list — user decides
// ─────────────────────────────────────────────────────────────────────────────

// Minimum normalized alias length to apply Rules 2 and 3.
// Aliases shorter than this (e.g. "max", "hbo", "ring") only match exactly.
const MIN_ALIAS_LENGTH = 5;

// ─── Subscription list ────────────────────────────────────────────────────────
// key          — stable identifier used for deduplication; never changes
// label        — display name shown on chip and in the added-items list
// aliases      — every Plaid merchant-name variant that should trigger coverage
//                Be conservative: prefer specific multi-word aliases over
//                single short words that could match unrelated merchants.
export const COMMON_SUBSCRIPTIONS = [
  {
    key: "netflix",
    label: "Netflix",
    aliases: ["netflix"],
  },
  {
    key: "max",
    label: "Max",
    // "hbo max" (with space) and "hbomax" (no space) are both common Plaid formats.
    // Short alias "max" (3 chars) is included for exact-match only (MIN_ALIAS_LENGTH guard).
    // Short alias "hbo" (3 chars) is included for exact-match only.
    aliases: ["max", "hbomax", "hbo max", "hbo"],
  },
  {
    key: "hulu",
    label: "Hulu",
    aliases: ["hulu"],
  },
  {
    key: "disney+",
    label: "Disney+",
    aliases: ["disney+", "disneyplus", "disney plus"],
  },
  {
    key: "appletv+",
    label: "Apple TV+",
    // Specific aliases only — "apple.com/bill" is intentionally excluded
    // because it is ambiguous (could be iCloud, Apple Music, Apple TV+, etc.).
    aliases: ["apple tv+", "apple tv plus", "apple tv"],
  },
  {
    key: "primevideo",
    label: "Prime Video",
    // "amazon" alone is intentionally excluded — Amazon purchases (groceries,
    // products, etc.) are not Prime Video subscriptions.
    aliases: ["prime video", "amazon prime", "prime video channels"],
  },
  {
    key: "youtubepremium",
    label: "YouTube Premium",
    // "yt primetime" captures the common Plaid format "YT PRIMETIME G.CO/HELPPAY#"
    aliases: ["youtube premium", "yt premium", "youtubepremi", "yt primetime"],
  },
  {
    key: "spotify",
    label: "Spotify",
    aliases: ["spotify"],
  },
  {
    key: "applemusic",
    label: "Apple Music",
    // Specific alias only — "apple.com/bill" is intentionally excluded.
    aliases: ["apple music"],
  },
  {
    key: "peacock",
    label: "Peacock",
    aliases: ["peacock", "peacocktv"],
  },
  {
    key: "paramount+",
    label: "Paramount+",
    aliases: ["paramount+", "paramount plus", "paramountplus"],
  },
  {
    key: "audible",
    label: "Audible",
    aliases: ["audible"],
  },
  {
    key: "icloud",
    label: "iCloud",
    // "apple.com/bill" is intentionally excluded — too ambiguous.
    // Only matches when Plaid explicitly surfaces "icloud" in the merchant name.
    aliases: ["icloud", "icloud storage", "icloud drive"],
  },
  {
    key: "googleone",
    label: "Google One",
    aliases: ["google one", "googleone", "google storage"],
  },
  {
    key: "ring",
    label: "Ring",
    // "ring" (4 chars) is below MIN_ALIAS_LENGTH — exact match only.
    aliases: ["ring", "ring.com", "ring alarm"],
  },
  {
    key: "chatgpt",
    label: "ChatGPT",
    // "OPENAI *CHATGPT SUBSSAN FRANCISCO" → norm "openai chatgpt subssan francisco"
    // rule 2: contains "chatgpt" (7 chars) ✓
    // rule 2: contains "openai" (6 chars) ✓
    aliases: ["chatgpt", "openai"],
  },
  {
    key: "claude",
    label: "Claude",
    aliases: ["claude", "anthropic"],
  },
];

// ─── normalizeName ────────────────────────────────────────────────────────────
// Convert any merchant name / alias to a stable lowercase token for comparison.
// Steps: lowercase → strip all non-alphanumeric-except-space → trim →
//        collapse consecutive spaces to one space.
//
// Examples:
//   "OPENAI *CHATGPT SUBSSAN FRANCISCO" → "openai chatgpt subssan francisco"
//   "Prime Video Channels"              → "prime video channels"
//   "Apple.com/bill"                    → "applecom bill"
//   "YT PRIMETIME G.CO/HELPPAY#"        → "yt primetime gcohelppay"
export const normalizeName = (str) =>
  (str || "")
    .toLowerCase()
    .replace(/[^a-z0-9 ]/g, "")
    .trim()
    .replace(/\s+/g, " ");

// ─── isAliasMatch ─────────────────────────────────────────────────────────────
// Returns true when normalizedExisting is considered to match normalizedAlias
// using the three rules described at the top of this file.
const isAliasMatch = (normalizedExisting, normalizedAlias) => {
  // Rule 1: exact equality
  if (normalizedExisting === normalizedAlias) return true;

  // Rule 2: existing name CONTAINS the alias
  // (Plaid names tend to be long with extra junk appended)
  // Guard: only fire when alias is long enough to be distinctive.
  if (
    normalizedAlias.length >= MIN_ALIAS_LENGTH &&
    normalizedExisting.includes(normalizedAlias)
  )
    return true;

  // Rule 3: alias CONTAINS the existing name
  // (handles cases like "prime video" existing name matching alias
  //  "prime video channels")
  // Guard: only fire when existing name is long enough to be distinctive.
  if (
    normalizedExisting.length >= MIN_ALIAS_LENGTH &&
    normalizedAlias.includes(normalizedExisting)
  )
    return true;

  return false;
};

// ─── getCoveredKeys ───────────────────────────────────────────────────────────
// Returns a Set<string> of subscription keys that are already "covered" by
// either Plaid-discovered recurring costs or manually-added costs.
//
// Parameters:
//   plaidRecurrings — array of { name, ... } from the Plaid recurring endpoint
//   manualCosts     — array of { name, ... } from otherManualCosts + quickAddSubs
//
// A subscription key is covered if any of its aliases matches any existing
// cost name using isAliasMatch().
export const getCoveredKeys = (plaidRecurrings = [], manualCosts = []) => {
  // Build the set of normalized existing names once (O(n) not O(n*m*aliases))
  const existingNorms = [
    ...plaidRecurrings.map((c) => normalizeName(c.name)),
    ...manualCosts.map((c) => normalizeName(c.name)),
  ];

  const covered = new Set();

  for (const sub of COMMON_SUBSCRIPTIONS) {
    const normalizedAliases = sub.aliases.map(normalizeName);

    const isCovered = existingNorms.some((existingNorm) =>
      normalizedAliases.some((aliasNorm) =>
        isAliasMatch(existingNorm, aliasNorm),
      ),
    );

    if (isCovered) {
      covered.add(sub.key);
    }
  }

  return covered;
};
