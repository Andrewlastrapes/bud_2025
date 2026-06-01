#!/usr/bin/env node
/**
 * log-write.js
 * PostToolUse hook — appends a one-line entry to .claude/write.log for every tool use.
 *
 * Format:
 *   2026-06-01T23:29:53Z | tool=write_file | path=BudgetApp.Api/services/TransactionService.cs
 *
 * Never logs file contents or values that look like secrets.
 *
 * Test manually:
 *   echo '{"tool_name":"write_file","tool_input":{"path":"BudgetApp.Api/services/Foo.cs"}}' | node .claude/hooks/log-write.js
 */

"use strict";

const fs = require("fs");
const path = require("path");

const LOG_FILE = path.join(__dirname, "..", "write.log");

// Field names that might contain secrets — never log their values
const SECRET_FIELD_PATTERNS = [
  /secret/i,
  /password/i,
  /token/i,
  /connection/i,
  /apikey/i,
  /api_key/i,
  /credential/i,
  /private_key/i,
];

function looksLikeSecret(key) {
  return SECRET_FIELD_PATTERNS.some((p) => p.test(key));
}

function readStdin() {
  return new Promise((resolve) => {
    let data = "";
    process.stdin.setEncoding("utf8");
    process.stdin.on("data", (chunk) => {
      data += chunk;
    });
    process.stdin.on("end", () => resolve(data));
    setTimeout(() => resolve(data), 500);
  });
}

function extractFields(payload) {
  if (!payload || typeof payload !== "object") return {};

  const toolName =
    payload.tool_name || payload.tool || payload.name || "unknown";

  // Try common field names for the file path
  const filePath =
    payload.tool_input?.path ||
    payload.tool_input?.file_path ||
    payload.tool_input?.filename ||
    payload.input?.path ||
    payload.path ||
    null;

  return { toolName, filePath };
}

async function main() {
  let raw = "";
  try {
    raw = await readStdin();
  } catch {
    process.exit(0);
  }

  let payload = null;
  if (raw.trim()) {
    try {
      payload = JSON.parse(raw);
    } catch {
      // Can't parse — log a minimal entry anyway
    }
  }

  const { toolName, filePath } = extractFields(payload);

  // Reject paths that look like they contain secret values
  const safePath = filePath && !looksLikeSecret(filePath) ? filePath : null;

  const timestamp = new Date().toISOString();
  const parts = [`ts=${timestamp}`, `tool=${toolName}`];
  if (safePath) parts.push(`path=${safePath}`);

  const line = parts.join(" | ") + "\n";

  try {
    fs.appendFileSync(LOG_FILE, line, "utf8");
  } catch (err) {
    process.stderr.write(
      `[log-write] Could not write to ${LOG_FILE}: ${err.message}\n`,
    );
  }

  process.exit(0);
}

main().catch((err) => {
  process.stderr.write(`[log-write] Unexpected error: ${err.message}\n`);
  process.exit(0);
});
