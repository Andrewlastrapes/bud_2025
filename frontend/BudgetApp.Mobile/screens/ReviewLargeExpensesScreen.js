// File: screens/ReviewLargeExpensesScreen.js

import React, { useState, useEffect } from "react";
import { View, FlatList, StyleSheet } from "react-native";
import {
  Text,
  Button,
  ActivityIndicator,
  Portal,
  Modal,
  TextInput,
} from "react-native-paper";
import { SafeAreaView } from "react-native-safe-area-context";
import axios from "axios";
import { auth } from "../firebaseConfig";
import { useIsFocused } from "@react-navigation/native";

import { API_BASE_URL } from "../config/api";
import TransactionCard from "../components/TransactionCard";
import {
  colors,
  spacing,
  radius,
  type as typeTokens,
  shadow,
} from "../config/theme";

// Enum numeric values must match TransactionUserDecision in C#
const DECISIONS = {
  TreatAsVariableSpend: 10,
  LargeExpenseFromSavings: 11,
  LargeExpenseToFixedCost: 12,
};

export default function ReviewLargeExpensesScreen() {
  const [items, setItems] = useState([]);
  const [loading, setLoading] = useState(true);
  const [installmentModalTx, setInstallmentModalTx] = useState(null);
  const [installmentCount, setInstallmentCount] = useState("2");
  const isFocused = useIsFocused();

  const getAuthHeader = async () => {
    const user = auth.currentUser;
    if (!user) throw new Error("No user logged in.");
    const token = await user.getIdToken();
    return { headers: { Authorization: `Bearer ${token}` } };
  };

  const fetchLargeExpenses = async () => {
    try {
      setLoading(true);
      const config = await getAuthHeader();
      const res = await axios.get(
        `${API_BASE_URL}/api/transactions/large-expenses/pending`,
        config,
      );
      setItems(res.data || []);
    } catch (e) {
      console.error("Failed to load large expenses", e);
      alert("Error loading large expenses");
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    if (isFocused) {
      fetchLargeExpenses();
    }
  }, [isFocused]);

  const applyDecision = async (txId, decision, extra = {}) => {
    try {
      const config = await getAuthHeader();
      await axios.post(
        `${API_BASE_URL}/api/transactions/${txId}/decision`,
        { decision, ...extra },
        config,
      );
      setItems((current) => current.filter((x) => x.id !== txId));
    } catch (e) {
      console.error("Failed to apply decision", e);
      alert("Error saving decision");
    }
  };

  const renderItem = ({ item }) => {
    const amount = typeof item.amount === "number" ? item.amount : 0;

    return (
      <TransactionCard
        type="largeExpense"
        merchantName={item.merchantName}
        name={item.name}
        amount={amount}
        date={item.date}
      >
        {/* Count as normal spending */}
        <Button
          mode="outlined"
          style={styles.actionButton}
          labelStyle={styles.actionButtonLabel}
          onPress={() => applyDecision(item.id, DECISIONS.TreatAsVariableSpend)}
        >
          Count as spending
        </Button>

        {/* Paid from savings */}
        <Button
          mode="outlined"
          style={styles.actionButton}
          labelStyle={styles.actionButtonLabel}
          onPress={() =>
            applyDecision(item.id, DECISIONS.LargeExpenseFromSavings)
          }
        >
          Paid from savings
        </Button>

        {/* Convert to fixed cost (installment) */}
        <Button
          mode="contained"
          style={[styles.actionButton, styles.primaryActionButton]}
          labelStyle={styles.primaryActionButtonLabel}
          onPress={() => {
            setInstallmentModalTx(item);
            setInstallmentCount("2");
          }}
        >
          Spread as fixed cost
        </Button>
      </TransactionCard>
    );
  };

  return (
    <SafeAreaView style={styles.container}>
      {loading ? (
        <View style={styles.centered}>
          <ActivityIndicator size="large" color={colors.warning} />
          <Text style={styles.loadingText}>Loading large expenses…</Text>
        </View>
      ) : items.length === 0 ? (
        <View style={styles.centered}>
          <Text style={styles.emptyIcon}>✅</Text>
          <Text style={styles.emptyHeading}>No large expenses to review.</Text>
          <Text style={styles.emptySubtext}>
            Unusually large purchases will appear here for you to categorize.
          </Text>
        </View>
      ) : (
        <FlatList
          data={items}
          keyExtractor={(item) => String(item.id)}
          renderItem={renderItem}
          contentContainerStyle={styles.listContent}
        />
      )}

      {/* ── Installment modal ── */}
      <Portal>
        <Modal
          visible={!!installmentModalTx}
          onDismiss={() => setInstallmentModalTx(null)}
          contentContainerStyle={styles.modal}
        >
          <Text style={styles.modalTitle}>Spread as fixed cost</Text>

          {installmentModalTx && (
            <View style={styles.modalMeta}>
              <Text style={styles.modalMerchant}>
                {installmentModalTx.merchantName ||
                  installmentModalTx.name ||
                  "Transaction"}
              </Text>
              <Text style={styles.modalAmount}>
                ${Number(installmentModalTx.amount).toFixed(2)}
              </Text>
            </View>
          )}

          <TextInput
            label="Paychecks to spread over"
            value={installmentCount}
            onChangeText={setInstallmentCount}
            keyboardType="numeric"
            mode="outlined"
            style={styles.modalInput}
          />

          {installmentModalTx &&
            installmentCount &&
            Number(installmentCount) > 0 && (
              <Text style={styles.modalHint}>
                ≈ $
                {(
                  Number(installmentModalTx.amount) / Number(installmentCount)
                ).toFixed(2)}{" "}
                per paycheck
              </Text>
            )}

          <View style={styles.modalButtons}>
            <Button
              mode="text"
              onPress={() => setInstallmentModalTx(null)}
              labelStyle={styles.modalCancelLabel}
            >
              Cancel
            </Button>
            <Button
              mode="contained"
              style={styles.modalSaveButton}
              onPress={async () => {
                const n = parseInt(installmentCount, 10);
                if (!n || n <= 0) {
                  alert("Enter a valid number of paychecks.");
                  return;
                }
                if (!installmentModalTx) return;

                await applyDecision(
                  installmentModalTx.id,
                  DECISIONS.LargeExpenseToFixedCost,
                  {
                    installmentCount: n,
                    fixedCostName:
                      installmentModalTx.merchantName ||
                      installmentModalTx.name,
                  },
                );

                setInstallmentModalTx(null);
              }}
            >
              Save
            </Button>
          </View>
        </Modal>
      </Portal>
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
  actionButton: {
    borderRadius: spacing.sm,
    borderColor: colors.lightBorder,
  },
  actionButtonLabel: {
    fontSize: typeTokens.sm,
    fontWeight: typeTokens.medium,
    letterSpacing: 0.1,
  },
  primaryActionButton: {
    // inherits contained style from RN Paper theme
  },
  primaryActionButtonLabel: {
    fontSize: typeTokens.sm,
    fontWeight: typeTokens.semibold,
    letterSpacing: 0.1,
  },

  // ── Modal ─────────────────────────────────────────────────
  modal: {
    margin: spacing.lg,
    padding: spacing.lg,
    backgroundColor: colors.lightSurface,
    borderRadius: radius.lg,
    ...shadow.md,
  },
  modalTitle: {
    fontSize: typeTokens.lg,
    fontWeight: typeTokens.bold,
    color: colors.textPrimary,
    marginBottom: spacing.md,
  },
  modalMeta: {
    flexDirection: "row",
    justifyContent: "space-between",
    alignItems: "center",
    marginBottom: spacing.md,
    padding: spacing.sm,
    backgroundColor: colors.lightBg,
    borderRadius: radius.sm,
  },
  modalMerchant: {
    fontSize: typeTokens.base,
    fontWeight: typeTokens.semibold,
    color: colors.textPrimary,
    flex: 1,
  },
  modalAmount: {
    fontSize: typeTokens.base,
    fontWeight: typeTokens.bold,
    color: colors.textPrimary,
    marginLeft: spacing.sm,
  },
  modalInput: {
    backgroundColor: colors.lightSurface,
    marginBottom: spacing.xs,
  },
  modalHint: {
    fontSize: typeTokens.sm,
    color: colors.textMuted,
    marginBottom: spacing.md,
  },
  modalButtons: {
    flexDirection: "row",
    justifyContent: "flex-end",
    gap: spacing.sm,
    marginTop: spacing.sm,
  },
  modalCancelLabel: {
    color: colors.textSecondary,
  },
  modalSaveButton: {
    borderRadius: spacing.sm,
  },
});
