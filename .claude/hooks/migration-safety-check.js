#!/usr/bin/env node
/**
 * migration-safety-check.js
 * PostToolUse hook — warns on dangerous patterns in EF Core migration files.
 *
 * Fires after any file write. Checks the written file only if it is inside
 * BudgetApp.Api/Migrations/ and ends in .cs (not Designer.cs or Snapshot.cs).
 *
 * Checks performed on the Up() and Down() methods:
 *   - DROP TABLE / DROP COLUMN (destructive schema changes)
 *   - TRUNCATE (data loss)
 *   - DELETE FROM without WHERE (mass data deletion)
 *   - Down() method that is empty or throws NotImplementedException
 *   - Raw SQL that bypasses EF safety
 *
 * By default this is informational (exit 0).
 * Set STRICT_CLAUDE_CHECKS=true to fail nonzero when issues are found.
 *
 * Test manually:
 *   echo '{"tool_input":{"path":"BudgetApp.Api/Migrations/20260601_Test.cs"}}' | node .claude/hooks/migration-safety-check.js
 */

"use strict";

const fs = require("fs");
const path = require("path");

const STRICT = process.env.STRICT_CLAUDE_CHECKS === "true";
const REPO_ROOT = path.resolve(__dirname, "..", "..");

// Patterns that are dangerous in migration files
const DANGEROUS_PATTERNS = [
  {
    pattern: /migrationBuilder\.Sql\s*\(\s*["'`]?\s*DROP\s+TABLE/i,
    label: "DROP TABLE in raw SQL",
  },
  {
    pattern: /migrationBuilder\.Sql\s*\(\s*["'`]?\s*TRUNCATE/i,
    label: "TRUNCATE in raw SQL",
  },
  {
    pattern:
      /migrationBuilder\.Sql\s*\(\s*["'`]?\s*DELETE\s+FROM\s+\w+\s*["'`]?\s*\)/i,
    label: "DELETE FROM without WHERE clause in raw SQL",
  },
  {
    pattern: /migrationBuilder\.DropTable\s*\(/i,
    label: "DropTable() call — verify this is intentional",
  },
  {
    pattern: /migrationBuilder\.DropColumn\s*\(/i,
    label: "DropColumn() call — data will be lost if column has data",
  },
  {
    pattern: /throw\s+new\s+NotImplementedException/i,
    label:
      "NotImplementedException in migration — Down() may not be reversible",
  },
];

// Checks that apply to the whole file structure
function checkDownMethod(content) {
  const issues = [];

  // Find the Down() method block (very rough heuristic — no full parser)
  const downMatch = content.match(
    /protected\s+override\s+void\s+Down\s*\([^)]*\)\s*\{([^}]*(?:\{[^}]*\}[^}]*)*)\}/s,
  );
  if (downMatch) {
    const downBody = downMatch[1].trim();
    if (downBody.length === 0) {
      issues.push("Down() method is empty — migration cannot be rolled back");
    }
  }

  return issues;
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

function extractFilePath(payload) {
  if (!payload || typeof payload !== "object") return null;
  return (
    payload.tool_input?.path ||
    payload.tool_input?.file_path ||
    payload.tool_input?.filename ||
    payload.input?.path ||
    payload.path ||
    null
  );
}

function isMigrationFile(filePath) {
  if (!filePath) return false;
  const normalized = filePath.replace(/\\/g, "/");
  // Only check migration source files — not Designer.cs or Snapshot.cs
  return (
    normalized.includes("BudgetApp.Api/Migrations/") &&
    normalized.endsWith(".cs") &&
    !normalized.endsWith(".Designer.cs") &&
    !normalized.includes("ModelSnapshot")
  );
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
      process.exit(0);
    }
  }

  const filePath = extractFilePath(payload);

  if (!isMigrationFile(filePath)) {
    process.exit(0);
  }

  const absolutePath = path.isAbsolute(filePath)
    ? filePath
    : path.join(REPO_ROOT, filePath);

  let content;
  try {
    content = fs.readFileSync(absolutePath, "utf8");
  } catch {
    process.stderr.write(
      `[migration-safety-check] Could not read ${absolutePath} — skipping.\n`,
    );
    process.exit(0);
  }

  const issues = [];

  for (const { pattern, label } of DANGEROUS_PATTERNS) {
    if (pattern.test(content)) {
      issues.push(label);
    }
  }

  issues.push(...checkDownMethod(content));

  if (issues.length === 0) {
    console.log(
      `[migration-safety-check] ✅ ${path.basename(filePath)} — no obvious issues found.`,
    );
    process.exit(0);
  }

  console.log(
    `[migration-safety-check] ⚠️  Issues found in ${path.basename(filePath)}:`,
  );
  for (const issue of issues) {
    console.log(`  • ${issue}`);
  }
  console.log("  Review this migration carefully before applying.");

  if (STRICT) {
    process.exit(1);
  }

  process.exit(0);
}

main().catch((err) => {
  process.stderr.write(
    `[migration-safety-check] Unexpected error: ${err.message}\n`,
  );
  process.exit(0); // fail open
});
