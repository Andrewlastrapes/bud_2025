#!/usr/bin/env node
/**
 * validate-bash.js
 * PreToolUse hook — blocks or warns on dangerous bash commands.
 *
 * Claude Code sends the tool input as JSON on stdin.
 * This script reads it defensively and exits 2 to block, 0 to allow.
 *
 * Test manually:
 *   echo '{"tool_input":{"command":"rm -rf /"}}' | node .claude/hooks/validate-bash.js
 */

"use strict";

const BLOCKED = [
  /rm\s+-rf/,
  /dotnet\s+ef\s+database\s+drop/i,
  /DROP\s+DATABASE/i,
  /DROP\s+TABLE/i,
  /TRUNCATE\b/i,
  /docker\s+compose\s+down\s+-v/,
  /git\s+reset\s+--hard/,
  /git\s+clean\s+-fd/,
];

// Patterns that look like production secrets being used in commands.
// These warn but do not block by default.
const WARNED = [
  { pattern: /PLAID_SECRET/i, label: "Plaid secret env var" },
  { pattern: /PLAID_CLIENT_ID/i, label: "Plaid client ID env var" },
  {
    pattern: /postgresql:\/\/[^@]+@[^/]*prod/i,
    label: "Production Postgres connection string",
  },
  {
    pattern: /FIREBASE_SERVICE_ACCOUNT/i,
    label: "Firebase service account env var",
  },
  { pattern: /AWS_SECRET_ACCESS_KEY/i, label: "AWS secret access key env var" },
  { pattern: /AWS_ACCESS_KEY_ID/i, label: "AWS access key ID env var" },
];

function readStdin() {
  return new Promise((resolve) => {
    let data = "";
    process.stdin.setEncoding("utf8");
    process.stdin.on("data", (chunk) => {
      data += chunk;
    });
    process.stdin.on("end", () => resolve(data));
    // If stdin is not a pipe (e.g. manual run with no input), resolve after short delay
    setTimeout(() => resolve(data), 500);
  });
}

function extractCommand(payload) {
  // Try common payload shapes defensively
  if (payload && typeof payload === "object") {
    const candidates = [
      payload.tool_input?.command,
      payload.input?.command,
      payload.command,
    ];
    for (const c of candidates) {
      if (typeof c === "string" && c.trim().length > 0) return c;
    }
  }
  return null;
}

async function main() {
  let raw = "";
  try {
    raw = await readStdin();
  } catch {
    // Can't read stdin — allow through
    process.exit(0);
  }

  let payload = null;
  if (raw.trim()) {
    try {
      payload = JSON.parse(raw);
    } catch {
      // Not JSON — allow through, can't inspect
      process.stderr.write(
        "[validate-bash] Warning: could not parse stdin as JSON — allowing command through.\n",
      );
      process.exit(0);
    }
  }

  const command = extractCommand(payload);

  if (!command) {
    // No command found in payload — allow through
    process.exit(0);
  }

  // --- Hard blocks ---
  for (const pattern of BLOCKED) {
    if (pattern.test(command)) {
      process.stderr.write(
        `[validate-bash] BLOCKED: Command matches destructive pattern: ${pattern}\n` +
          `  Command: ${command}\n`,
      );
      process.exit(2);
    }
  }

  // --- Warnings (allow through but log) ---
  for (const { pattern, label } of WARNED) {
    if (pattern.test(command)) {
      process.stderr.write(
        `[validate-bash] WARNING: Command may expose sensitive value (${label}).\n` +
          `  Review before running in production.\n`,
      );
    }
  }

  process.exit(0);
}

main().catch((err) => {
  process.stderr.write(`[validate-bash] Unexpected error: ${err.message}\n`);
  process.exit(0); // fail open — don't block Claude on hook errors
});
