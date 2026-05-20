/**
 * TransactionCard
 *
 * Shared card component for all transaction-related review/notification UIs.
 * Type is always determined by screen context — never by amount sign.
 *
 * Props:
 *   type          "spend" | "deposit" | "largeExpense"  (default: "spend")
 *   merchantName  string | null   — Plaid merchant name
 *   name          string | null   — fallback transaction name
 *   amount        number | null   — absolute positive value (all amounts in DB are positive)
 *   date          string | null   — ISO date string
 *   budgetAmount  number | null   — optional: updated dynamic budget after decision
 *                                   renders a prominent footer row ONLY when provided
 *   children      ReactNode       — action buttons or extra content slot
 */

import React from "react";
import { View, StyleSheet } from "react-native";
import { Text } from "react-native-paper";
import {
  notificationVariants,
  spacing,
  radius,
  type as typeTokens,
  shadow,
  colors,
} from "../config/theme";

// Re-export so screens can reference the same object if needed,
// but the canonical definition lives in theme.js.
export { notificationVariants };

export default function TransactionCard({
  type = "spend",
  merchantName,
  name,
  amount,
  date,
  budgetAmount,
  children,
}) {
  const variant = notificationVariants[type] ?? notificationVariants.spend;

  // Prefer merchant name; fall back to transaction name; then generic label
  const displayName = merchantName || name || variant.label;

  // Amount is always a positive absolute value in this app.
  // Format as "$X.XX". Show nothing if not a valid number.
  const displayAmount =
    typeof amount === "number" && !isNaN(amount)
      ? `$${amount.toFixed(2)}`
      : null;

  // Budget amount: same positive formatting
  const displayBudget =
    typeof budgetAmount === "number" && !isNaN(budgetAmount)
      ? `$${Math.abs(budgetAmount).toFixed(2)}`
      : null;

  const dateLabel = date
    ? new Date(date).toLocaleDateString("en-US", {
        month: "short",
        day: "numeric",
        year: "numeric",
      })
    : null;

  return (
    <View
      style={[
        styles.card,
        shadow.sm,
        {
          backgroundColor: variant.backgroundColor,
          borderColor: variant.borderColor,
        },
      ]}
    >
      {/* Left accent bar — color communicates type at a glance */}
      <View
        style={[styles.accentBar, { backgroundColor: variant.accentColor }]}
      />

      <View style={styles.body}>
        {/* ── Type chip ── */}
        <View
          style={[styles.chip, { backgroundColor: variant.chipBackground }]}
        >
          <Text
            style={[styles.chipText, { color: variant.chipTextColor }]}
            numberOfLines={1}
          >
            {variant.label}
          </Text>
        </View>

        {/* ── Merchant + Amount row (most prominent line) ── */}
        <View style={styles.headerRow}>
          <Text style={styles.merchantName} numberOfLines={2}>
            {displayName}
          </Text>
          {displayAmount ? (
            <Text style={styles.amount}>{displayAmount}</Text>
          ) : null}
        </View>

        {/* ── Date (secondary metadata) ── */}
        {dateLabel ? <Text style={styles.date}>{dateLabel}</Text> : null}

        {/* ── Updated budget footer — only rendered when budgetAmount is provided ── */}
        {displayBudget ? (
          <View
            style={[styles.budgetRow, { borderTopColor: variant.borderColor }]}
          >
            <Text style={styles.budgetLabel}>Updated budget</Text>
            <Text
              style={[styles.budgetAmount, { color: variant.chipTextColor }]}
            >
              {displayBudget}
            </Text>
          </View>
        ) : null}

        {/* ── Action buttons / children slot ── */}
        {children ? <View style={styles.actionsSlot}>{children}</View> : null}
      </View>
    </View>
  );
}

const styles = StyleSheet.create({
  card: {
    flexDirection: "row",
    borderRadius: radius.lg,
    borderWidth: 1,
    marginBottom: spacing.md,
    overflow: "hidden",
  },

  // Left colored accent bar
  accentBar: {
    width: 4,
    // height stretches to fill card via flex
  },

  body: {
    flex: 1,
    paddingHorizontal: spacing.md,
    paddingTop: spacing.sm + 4,
    paddingBottom: spacing.md,
  },

  // ── Type chip ──────────────────────────────────────────────
  chip: {
    alignSelf: "flex-start",
    borderRadius: radius.full,
    paddingHorizontal: spacing.sm + 2,
    paddingVertical: spacing.xs - 1,
    marginBottom: spacing.sm,
  },
  chipText: {
    fontSize: typeTokens.xs,
    fontWeight: typeTokens.bold,
    letterSpacing: 0.5,
    textTransform: "uppercase",
  },

  // ── Merchant + amount row ─────────────────────────────────
  headerRow: {
    flexDirection: "row",
    justifyContent: "space-between",
    alignItems: "flex-start",
    gap: spacing.sm,
    marginBottom: spacing.xs,
  },
  merchantName: {
    flex: 1,
    fontSize: typeTokens.base,
    fontWeight: typeTokens.semibold,
    color: colors.textPrimary,
    lineHeight: 22,
  },
  amount: {
    fontSize: typeTokens.base,
    fontWeight: typeTokens.bold,
    color: colors.textPrimary,
    flexShrink: 0,
  },

  // ── Date ─────────────────────────────────────────────────
  date: {
    fontSize: typeTokens.sm,
    color: colors.textMuted,
    marginBottom: spacing.sm,
  },

  // ── Budget footer ─────────────────────────────────────────
  // Only rendered when budgetAmount prop is provided
  budgetRow: {
    flexDirection: "row",
    justifyContent: "space-between",
    alignItems: "center",
    marginTop: spacing.sm,
    paddingTop: spacing.sm,
    borderTopWidth: 1,
    marginBottom: spacing.sm,
  },
  budgetLabel: {
    fontSize: typeTokens.sm,
    fontWeight: typeTokens.medium,
    color: colors.textSecondary,
  },
  budgetAmount: {
    fontSize: typeTokens.xl,
    fontWeight: typeTokens.heavy,
    letterSpacing: -0.5,
  },

  // ── Actions slot ─────────────────────────────────────────
  actionsSlot: {
    marginTop: spacing.sm,
    gap: spacing.sm,
  },
});
