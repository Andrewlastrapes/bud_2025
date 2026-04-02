/**
 * debugLog.js
 * Intercepts console.log/warn/error globally and stores entries
 * so any screen can subscribe and display them for debugging.
 * Also forwards errors/warnings to Sentry when initialized.
 */

import * as Sentry from '@sentry/react-native';

const MAX_LOGS = 80;
const _logs = [];
const _listeners = new Set();

function _notify() {
  const snapshot = [..._logs];
  _listeners.forEach((fn) => fn(snapshot));
}

function _addLog(level, args) {
  const msg = args
    .map((a) => {
      if (typeof a === 'string') return a;
      try {
        return JSON.stringify(a);
      } catch {
        return String(a);
      }
    })
    .join(' ');

  const now = new Date();
  const time = `${now.getHours().toString().padStart(2, '0')}:${now
    .getMinutes()
    .toString()
    .padStart(2, '0')}:${now.getSeconds().toString().padStart(2, '0')}`;

  _logs.push({ level, msg, time });
  if (_logs.length > MAX_LOGS) _logs.shift();
  _notify();
}

// ---- Patch console once ----
const _origLog = console.log;
const _origWarn = console.warn;
const _origError = console.error;

console.log = (...args) => {
  _origLog(...args);
  _addLog('log', args);
};

console.warn = (...args) => {
  _origWarn(...args);
  _addLog('warn', args);
  // Forward warnings to Sentry as breadcrumbs for context
  try {
    Sentry.addBreadcrumb({
      category: 'console',
      level: 'warning',
      message: args
        .map((a) => (typeof a === 'string' ? a : JSON.stringify(a)))
        .join(' '),
    });
  } catch (_) {
    // Sentry not yet initialized — safe to ignore
  }
};

console.error = (...args) => {
  _origError(...args);
  _addLog('error', args);
  // Forward errors to Sentry as captured messages
  try {
    const msg = args
      .map((a) => (typeof a === 'string' ? a : JSON.stringify(a)))
      .join(' ');
    // If the first arg is an Error instance, use captureException for a full stack trace
    const firstArg = args[0];
    if (firstArg instanceof Error) {
      Sentry.captureException(firstArg);
    } else {
      Sentry.captureMessage(msg, 'error');
    }
  } catch (_) {
    // Sentry not yet initialized — safe to ignore
  }
};

// ---- Public API ----

/** Subscribe to log updates. Returns an unsubscribe function. */
export function subscribeToLogs(fn) {
  _listeners.add(fn);
  fn([..._logs]); // deliver current snapshot immediately
  return () => _listeners.delete(fn);
}

/** Get the current log snapshot. */
export function getLogs() {
  return [..._logs];
}

/** Manually add a debug entry without going through console. */
export function debugLog(level, ...args) {
  _addLog(level, args);
}