// File: screens/DepositReviewScreen.js

import React, { useEffect, useState } from "react";
import { View, StyleSheet, FlatList } from "react-native";
import { Text, Button, ActivityIndicator, Snackbar } from "react-native-paper";
import { SafeAreaView } from "react-native-safe-area-context";
import axios from "axios";
import { auth } from "../firebaseConfig";
import { useIsFocused } from "@react-navigation/native";

import { API_BASE_URL } from "../config/api";
import TransactionCard from "../components/TransactionCard";
import { colors, spacing, type as typeTokens } from "../config/theme";

// These numeric values MUST match your C# enum TransactionUserDecision
// public enum TransactionUserDecision { Undecided = 0, TreatAsIncome = 1, IgnoreForDynamic = 2, DebtPayment = 3, SavingsFunded = 4 }
const DECISIONS = {
  TreatAsIncome: 1,
  Ignore: 2,
  DebtPayment: 3,
  SavingsFunded: 4,
};

// These numeric values MUST match your C# enum TransactionSuggestedKind
// public enum TransactionSuggestedKind { Unknown = 0, Paycheck = 1, Windfall = 2, InternalTransfer = 3, Refund = 4 }
// Used as a defensive client-side guard: only render rows the backend already
// filtered, but double-check here in case of future regressions.
const DEPOSIT_SUGGESTED_KINDS = [1, 2, 3, 4]; // Paycheck, Windfall, InternalTransfer, Refund

// Human-readable kind labels shown as a subtitle hint on each card.
const KIND_LABELS = {
  1: "Paycheck",
  2: "Windfall",
  3: "Transfer in",
  4: "Refund",
};

export default function DepositReviewScreen() {
  const [transactions, setTransactions] = useState([]);
  const [isLoading, setIsLoading] = useState(false);
  const [snackbar, setSnackbar] = useState({ visible: false, message: "" });

  const isFocused = useIsFocused();

  const getAuthHeader = async () => {
    const user = auth.currentUser;
    if (!user) throw new Error("No user logged in.");
    const token = await user.getIdToken();
    return { headers: { Authorization: `Bearer ${token}` } };
  };

  const fetchDepositsNeedingReview = async () => {
    setIsLoading(true);
    try {
      const config = await getAuthHeader();
      // Use the dedicated deposits endpoint — returns only undecided deposit/inflow
      // transactions (Paycheck, Windfall, InternalTransfer, Refund) pre-filtered
      // by the backend. The client-side filter below is a defensive safety net.
      const response = await axios.get(
        `${API_BASE_URL}/api/transactions/deposits/pending`,
        config,
      );

      // Defensive client-side guard: ensure only true deposit kinds are rendered
      // even if a future backend regression slips a non-deposit row through.
      const creditsNeedingReview = (response.data || []).filter((tx) =>
        DEPOSIT_SUGGESTED_KINDS.includes(tx.suggestedKind),
      );

      setTransactions(creditsNeedingReview);
    } catch (e) {
      console.error("Failed to fetch deposits:", e);
      setSnackbar({
        visible: true,
        message: "Failed to load deposits. Try again later.",
      });
    }
    setIsLoading(false);
  };

  useEffect(() => {
    if (isFocused) {
      fetchDepositsNeedingReview();
    }
  }, [isFocused]);

  const handleDecision = async (txId, decisionValue) => {
    try {
      const config = await getAuthHeader();

      await axios.post(
        `${API_BASE_URL}/api/transactions/${txId}/decision`,
        { decision: decisionValue },
        config,
      );

      // Optimistic update: remove transaction from local list
      setTransactions((prev) => prev.filter((t) => t.id !== txId));

      setSnackbar({
        visible: true,
        message: "Decision saved.",
      });
    } catch (e) {
      console.error("Failed to save decision:", e);
      setSnackbar({
        visible: true,
        message: "Error saving decision.",
      });
    }
  };

  const renderItem = ({ item }) => {
    // Kind label shown as a subtitle below the chip (e.g. "Paycheck", "Refund")
    const kindLabel = KIND_LABELS[item.suggestedKind];

    return (
      <TransactionCard
        type="deposit"
        merchantName={item.merchantName}
        name={kindLabel ? `${kindLabel}` : item.name}
        amount={item.amount}
        date={item.date}
      >
        {/* Row 1: Add to budget  |  Ignore */}
        <View style={styles.buttonRow}>
          <Button
            mode="contained"
            style={[styles.button, styles.primaryButton]}
            labelStyle={styles.primaryButtonLabel}
            onPress={() => handleDecision(item.id, DECISIONS.TreatAsIncome)}
          >
            Add to budget
          </Button>
          <Button
            mode="outlined"
            style={[styles.button, styles.secondaryButton]}
            labelStyle={styles.secondaryButtonLabel}
            onPress={() => handleDecision(item.id, DECISIONS.Ignore)}
          >
            Ignore
          </Button>
        </View>

        {/* Row 2: Debt payment  |  Mark as savings */}
        <View style={styles.buttonRow}>
          <Button
            mode="outlined"
            style={[styles.button, styles.secondaryButton]}
            labelStyle={styles.secondaryButtonLabel}
            onPress={() => handleDecision(item.id, DECISIONS.DebtPayment)}
          >
            Debt payment
          </Button>
          <Button
            mode="outlined"
            style={[styles.button, styles.secondaryButton]}
            labelStyle={styles.secondaryButtonLabel}
            onPress={() => handleDecision(item.id, DECISIONS.SavingsFunded)}
          >
            Savings
          </Button>
        </View>
      </TransactionCard>
    );
  };

  return (
    <SafeAreaView style={styles.container}>
      {isLoading ? (
        <View style={styles.centered}>
          <ActivityIndicator size="large" color={colors.success} />
          <Text style={styles.loadingText}>Loading deposits…</Text>
        </View>
      ) : transactions.length === 0 ? (
        <View style={styles.centered}>
          <Text style={styles.emptyIcon}>💚</Text>
          <Text style={styles.emptyHeading}>No new deposits to review.</Text>
          <Text style={styles.emptySubtext}>
            New paychecks, refunds, and transfers will appear here.
          </Text>
        </View>
      ) : (
        <FlatList
          data={transactions}
          keyExtractor={(item) => item.id.toString()}
          renderItem={renderItem}
          contentContainerStyle={styles.listContent}
        />
      )}

      <Snackbar
        visible={snackbar.visible}
        onDismiss={() => setSnackbar((s) => ({ ...s, visible: false }))}
        duration={2500}
      >
        {snackbar.message}
      </Snackbar>
    </SafeAreaView>
  );
}

const styles = StyleSheet.create({
  container: {
    flex: 1,
    backgroundColor: colors.lightBg,
  },
  listContent: {
    padding: spacing.md,
    paddingBottom: spacing.xl,
  },

  // ── Loading / empty states ────────────────────────────────
  centered: {
    flex: 1,
    justifyContent: "center",
    alignItems: "center",
    padding: spacing.xl,
  },
  loadingText: {
    marginTop: spacing.sm,
    fontSize: typeTokens.sm,
    color: colors.textMuted,
  },
  emptyIcon: {
    fontSize: 40,
    marginBottom: spacing.md,
  },
  emptyHeading: {
    fontSize: typeTokens.lg,
    fontWeight: typeTokens.semibold,
    color: colors.textPrimary,
    marginBottom: spacing.xs,
    textAlign: "center",
  },
  emptySubtext: {
    fontSize: typeTokens.sm,
    color: colors.textMuted,
    textAlign: "center",
    lineHeight: 20,
  },

  // ── Card action buttons ───────────────────────────────────
  buttonRow: {
    flexDirection: "row",
    gap: spacing.sm,
  },
  button: {
    flex: 1,
    borderRadius: spacing.sm,
  },
  primaryButton: {
    // color inherits from RN Paper theme (indigo)
  },
  primaryButtonLabel: {
    fontSize: typeTokens.sm,
    fontWeight: typeTokens.semibold,
    letterSpacing: 0.1,
  },
  secondaryButton: {
    borderColor: colors.lightBorder,
  },
  secondaryButtonLabel: {
    fontSize: typeTokens.sm,
    fontWeight: typeTokens.medium,
    letterSpacing: 0.1,
  },
});
