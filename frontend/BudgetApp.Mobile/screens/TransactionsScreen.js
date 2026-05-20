// File: screens/TransactionsScreen.js

import React, { useState, useEffect } from "react";
import { View, StyleSheet, FlatList, Alert, Platform } from "react-native";
import { Text, Button, ActivityIndicator } from "react-native-paper";
import { SafeAreaView } from "react-native-safe-area-context";
import axios from "axios";
import { auth } from "../firebaseConfig";
import { useIsFocused } from "@react-navigation/native";

import { API_BASE_URL } from "../config/api";
import TransactionCard from "../components/TransactionCard";
import { colors, spacing, type as typeTokens } from "../config/theme";

// Sign-convention note:
// Transaction.Amount is always stored as a positive absolute value in this app
// (Math.Abs of the Plaid raw amount). Never use amount sign to determine
// whether a transaction is a debit or credit. Type is determined by screen
// context or SuggestedKind. This screen shows all transactions as "spend"
// cards in a general ledger view.

export default function TransactionsScreen() {
  const [transactions, setTransactions] = useState([]);
  const [isLoading, setIsLoading] = useState(false);
  const [isSyncing, setIsSyncing] = useState(false);
  const isFocused = useIsFocused();

  const getAuthHeader = async () => {
    const user = auth.currentUser;
    if (!user) throw new Error("No user logged in.");
    const token = await user.getIdToken();
    return { headers: { Authorization: `Bearer ${token}` } };
  };

  const fetchTransactions = async () => {
    setIsLoading(true);
    try {
      const config = await getAuthHeader();
      const response = await axios.get(
        `${API_BASE_URL}/api/transactions`,
        config,
      );
      setTransactions(response.data);
    } catch (e) {
      console.error("Failed to fetch transactions:", e);
    }
    setIsLoading(false);
  };

  const handleSync = async () => {
    setIsSyncing(true);
    try {
      const config = await getAuthHeader();
      const response = await axios.post(
        `${API_BASE_URL}/api/transactions/sync`,
        null,
        config,
      );
      console.log("Sync complete:", response.data);
      await fetchTransactions();
    } catch (e) {
      console.error("Failed to sync transactions:", e);
      alert("Sync failed. Please try again.");
    }
    setIsSyncing(false);
  };

  // Mark a transaction as a recurring fixed cost
  const saveFixedCost = async (transaction) => {
    try {
      const config = await getAuthHeader();
      const merchantMatchName = transaction.merchantName || transaction.name;

      const payload = {
        name: transaction.name,
        // Amount is always a positive absolute value — display and store as-is.
        amount: transaction.amount,
        plaidMerchantName: merchantMatchName,
        category: "subscription",
        type: "manual",
      };

      await axios.post(`${API_BASE_URL}/api/fixed-costs`, payload, config);
      Alert.alert(
        "Added to Fixed Costs",
        "Future charges from this merchant will be excluded from your dynamic budget.",
      );
    } catch (e) {
      console.error("Failed to save fixed cost:", e);
      const errorMessage = e.response?.data?.detail || e.message;
      Alert.alert("Error", `Could not save fixed cost: ${errorMessage}`);
    }
  };

  const handleMarkAsRecurring = (transaction) => {
    const confirmMsg = `Mark "${transaction.merchantName || transaction.name}" as a recurring fixed cost?`;

    if (Platform.OS === "web") {
      if (window.confirm(confirmMsg)) {
        saveFixedCost(transaction);
      }
    } else {
      Alert.alert(
        "Mark as Fixed Cost?",
        `Add "${transaction.merchantName || transaction.name}" ($${transaction.amount.toFixed(2)}) to your Fixed Costs?`,
        [
          { text: "Cancel", style: "cancel" },
          {
            text: "Yes, mark recurring",
            onPress: () => saveFixedCost(transaction),
          },
        ],
      );
    }
  };

  useEffect(() => {
    if (isFocused) {
      fetchTransactions();
    }
  }, [isFocused]);

  const renderItem = ({ item }) => (
    // All rows on this screen are "spend" cards (general ledger view).
    // Type is set by screen context, NOT by amount sign.
    <TransactionCard
      type="spend"
      merchantName={item.merchantName}
      name={item.name}
      // Amount is always a positive absolute value — display as $X.XX.
      // Do NOT use amount sign to determine display sign here.
      amount={typeof item.amount === "number" ? item.amount : null}
      date={item.date}
    >
      <Button
        mode="text"
        compact
        style={styles.recurringButton}
        labelStyle={styles.recurringButtonLabel}
        onPress={() => handleMarkAsRecurring(item)}
      >
        Mark as recurring
      </Button>
    </TransactionCard>
  );

  return (
    <SafeAreaView style={styles.container}>
      {/* ── Sync button ── */}
      <View style={styles.syncBar}>
        <Button
          mode="contained"
          onPress={handleSync}
          loading={isSyncing}
          style={styles.syncButton}
          labelStyle={styles.syncButtonLabel}
          contentStyle={styles.syncButtonContent}
        >
          Sync transactions
        </Button>
      </View>

      {isLoading ? (
        <View style={styles.centered}>
          <ActivityIndicator size="large" color={colors.primary} />
          <Text style={styles.loadingText}>Loading transactions…</Text>
        </View>
      ) : transactions.length === 0 ? (
        <View style={styles.centered}>
          <Text style={styles.emptyIcon}>🧾</Text>
          <Text style={styles.emptyHeading}>No transactions yet.</Text>
          <Text style={styles.emptySubtext}>
            Tap "Sync transactions" to pull your latest activity.
          </Text>
        </View>
      ) : (
        <FlatList
          data={transactions}
          keyExtractor={(item) => item.plaidTransactionId || item.id.toString()}
          renderItem={renderItem}
          contentContainerStyle={styles.listContent}
        />
      )}
    </SafeAreaView>
  );
}

const styles = StyleSheet.create({
  container: {
    flex: 1,
    backgroundColor: colors.lightBg,
  },

  // ── Sync bar ──────────────────────────────────────────────
  syncBar: {
    paddingHorizontal: spacing.md,
    paddingTop: spacing.md,
    paddingBottom: spacing.sm,
  },
  syncButton: {
    borderRadius: spacing.sm,
  },
  syncButtonContent: {
    height: 46,
  },
  syncButtonLabel: {
    fontSize: typeTokens.sm,
    fontWeight: typeTokens.semibold,
    letterSpacing: 0.2,
  },

  listContent: {
    paddingHorizontal: spacing.md,
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

  // ── Card action ───────────────────────────────────────────
  recurringButton: {
    alignSelf: "flex-start",
    marginTop: -spacing.xs,
  },
  recurringButtonLabel: {
    fontSize: typeTokens.sm,
    color: colors.primary,
    fontWeight: typeTokens.medium,
  },
});
