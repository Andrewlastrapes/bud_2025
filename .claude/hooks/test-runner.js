#!/usr/bin/env node
/**
 * test-runner.js
 * PostToolUse hook — runs build/test checks after file writes.
 *
 * Behavior:
 *   - Detects whether the written file is backend (BudgetApp.Api/) or frontend (frontend/).
 *   - Backend: runs dotnet build on the API project.
 *   - Frontend: reads the discovered package.json and runs lint/test only if those scripts exist.
 *   - Never invents scripts that do not exist in package.json.
 *
 * By default this is informational (exit 0 always).
 * Set STRICT_CLAUDE_CHECKS=true in environment to fail nonzero on build/test failures.
 *
 * Test manually:
 *   echo '{"tool_input":{"path":"BudgetApp.Api/services/TransactionService.cs"}}' | node .claude/hooks/test-runner.js
 *   echo '{"tool_input":{"path":"frontend/BudgetApp.Mobile/screens/HomeScreen.js"}}' | node .claude/hooks/test-runner.js
 */

"use strict";

const { execSync } = require("child_process");
const fs = require("fs");
const path = require("path");

const STRICT = process.env.STRICT_CLAUDE_CHECKS === "true";

// Resolved from the hook file location: .claude/hooks/ → repo root
const REPO_ROOT = path.resolve(__dirname, "..", "..");

const BACKEND_PROJECT = path.join(
  REPO_ROOT,
  "BudgetApp.Api",
  "BudgetApp.Api.csproj",
);

// Detected at plan time: one package.json in the repo
const FRONTEND_PACKAGE_JSON = path.join(
  REPO_ROOT,
  "frontend",
  "BudgetApp.Mobile",
  "package.json",
);

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

function classify(filePath) {
  if (!filePath) return null;
  // Normalize to forward slashes for matching
  const normalized = filePath.replace(/\\/g, "/");
  if (normalized.startsWith("BudgetApp.Api/")) return "backend";
  if (normalized.startsWith("frontend/")) return "frontend";
  return null;
}

function run(cmd, cwd) {
  try {
    const output = execSync(cmd, {
      cwd,
      stdio: ["ignore", "pipe", "pipe"],
      encoding: "utf8",
    });
    return { ok: true, output: output.trim() };
  } catch (err) {
    const output = (err.stdout || "") + (err.stderr ? "\n" + err.stderr : "");
    return { ok: false, output: output.trim() };
  }
}

function runBackend() {
  if (!fs.existsSync(BACKEND_PROJECT)) {
    console.log("[test-runner] Backend project not found — skipping build.");
    return true;
  }

  console.log("[test-runner] Backend file changed — running dotnet build...");
  const result = run(
    `dotnet build "${BACKEND_PROJECT}" --no-restore -v minimal`,
    REPO_ROOT,
  );

  if (result.ok) {
    console.log("✅ Backend build passed.");
  } else {
    console.log("❌ Backend build failed.");
    if (result.output) console.log(result.output);
  }

  return result.ok;
}

function runFrontend() {
  if (!fs.existsSync(FRONTEND_PACKAGE_JSON)) {
    console.log("[test-runner] Frontend package.json not found — skipping.");
    return true;
  }

  let pkg;
  try {
    pkg = JSON.parse(fs.readFileSync(FRONTEND_PACKAGE_JSON, "utf8"));
  } catch {
    console.log(
      "[test-runner] Could not parse frontend package.json — skipping.",
    );
    return true;
  }

  const scripts = pkg.scripts || {};
  const frontendDir = path.dirname(FRONTEND_PACKAGE_JSON);
  let allPassed = true;

  if (!scripts.lint && !scripts.test) {
    console.log(
      "ℹ️  No lint/test scripts found in frontend package.json — skipping.",
    );
    return true;
  }

  if (scripts.lint) {
    console.log(
      "[test-runner] Frontend file changed — running npm run lint...",
    );
    const result = run("npm run lint", frontendDir);
    if (result.ok) {
      console.log("✅ Frontend lint passed.");
    } else {
      console.log("❌ Frontend lint failed.");
      if (result.output) console.log(result.output);
      allPassed = false;
    }
  }

  if (scripts.test) {
    console.log("[test-runner] Frontend file changed — running npm test...");
    const result = run(
      "npm test -- --passWithNoTests 2>/dev/null || npm test",
      frontendDir,
    );
    if (result.ok) {
      console.log("✅ Frontend test passed.");
    } else {
      console.log("❌ Frontend test failed.");
      if (result.output) console.log(result.output);
      allPassed = false;
    }
  }

  return allPassed;
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
      // Can't parse — nothing to check
      process.exit(0);
    }
  }

  const filePath = extractFilePath(payload);
  const kind = classify(filePath);

  if (!kind) {
    // Not a file we track — skip silently
    process.exit(0);
  }

  let passed = true;

  if (kind === "backend") {
    passed = runBackend();
  } else if (kind === "frontend") {
    passed = runFrontend();
  }

  if (!passed && STRICT) {
    process.exit(1);
  }

  process.exit(0);
}

main().catch((err) => {
  process.stderr.write(`[test-runner] Unexpected error: ${err.message}\n`);
  process.exit(0); // fail open
});
