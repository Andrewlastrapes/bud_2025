import React, { useState, useEffect } from "react";
import { View, StyleSheet, TouchableOpacity, ScrollView } from "react-native";
import {
  Text,
  Button,
  TextInput,
  Modal,
  Portal,
  Card,
} from "react-native-paper";
import { SafeAreaView } from "react-native-safe-area-context";
import axios from "axios";
import { auth } from "../firebaseConfig";
import { useIsFocused } from "@react-navigation/native";

import { API_BASE_URL } from "../config/api";

export default function HomeScreen({ navigation }) {
  const [balance, setBalance] = useState(0);
  const [paycheckInput, setPaycheckInput] = useState("");
  const [visible, setVisible] = useState(false);
  const [isLoading, setIsLoading] = useState(false);
  const [holdCount, setHoldCount] = useState(0);
  // largeExpenseSummary: { count, totalAmount }
  // Shown as a subtle amber informational banner — does NOT change how
  // large expenses affect the balance (they still subtract immediately).
  // debtSummary: { totalDebt, debtPerPaycheck, paychecksRemaining }
  // totalDebt null → old user, card hidden. Fetched from DB only (no Plaid call).
  const [debtSummary, setDebtSummary] = useState(null);

  const [largeExpenseSummary, setLargeExpenseSummary] = useState({
    count: 0,
    totalAmount: 0,
  });
  // depositSummary: { count, totalAmount } for undecided unexpected deposits
  // (Windfall, InternalTransfer, Refund only — Paycheck is excluded because it
  // is expected income already planned at onboarding).
  // null = not yet loaded; { count: 0 } = loaded but nothing to show.
  const [depositSummary, setDepositSummary] = useState({
    count: 0,
    totalAmount: 0,
  });
  const isFocused = useIsFocused();

  // Helper to get auth headers
  const getAuthHeader = async () => {
    const user = auth.currentUser;
    if (!user) throw new Error("No user logged in.");
    const token = await user.getIdToken();
    return { headers: { Authorization: `Bearer ${token}` } };
  };

  const fetchBalance = async () => {
    try {
      const config = await getAuthHeader();
      const response = await axios.get(`${API_BASE_URL}/api/balance`, config);
      setBalance(response.data.amount);
    } catch (e) {
      console.error("Failed to fetch balance:", e);
    }
  };

  const fetchHoldCount = async () => {
    try {
      const config = await getAuthHeader();
      const response = await axios.get(
        `${API_BASE_URL}/api/transactions/suspicious-holds`,
        config,
      );
      setHoldCount(response.data.length ?? 0);
    } catch (e) {
      console.error("Failed to fetch hold count:", e);
    }
  };

  // Fetches the count + total of unreviewed large expenses.
  // This is informational only — the balance is already impacted.
  const fetchLargeExpenseSummary = async () => {
    try {
      const config = await getAuthHeader();
      const response = await axios.get(
        `${API_BASE_URL}/api/transactions/large-expenses/summary`,
        config,
      );
      setLargeExpenseSummary({
        count: response.data.count ?? 0,
        totalAmount: response.data.totalAmount ?? 0,
      });
    } catch (e) {
      // Non-fatal: banner just won't show if this fails
      console.error("Failed to fetch large expense summary:", e);
    }
  };

  // Fetches the stored debt payoff summary. DB-only — no Plaid call.
  // Non-fatal: if the endpoint fails the card simply stays hidden.
  const fetchDebtSummary = async () => {
    try {
      const config = await getAuthHeader();
      const response = await axios.get(
        `${API_BASE_URL}/api/debt/summary`,
        config,
      );
      setDebtSummary(response.data);
    } catch (e) {
      console.error("Failed to fetch debt summary:", e);
      // leave debtSummary null → card stays hidden
    }
  };

  // Fetches count + total of undecided unexpected deposits (Windfall, InternalTransfer, Refund).
  // Paycheck is excluded — it is expected income already accounted for at onboarding.
  // Non-fatal: banner just won't show if this fails.
  const fetchDepositSummary = async () => {
    try {
      const config = await getAuthHeader();
      const response = await axios.get(
        `${API_BASE_URL}/api/transactions/deposits/pending/summary`,
        config,
      );
      setDepositSummary({
        count: response.data.count ?? 0,
        totalAmount: response.data.totalAmount ?? 0,
      });
    } catch (e) {
      console.error("Failed to fetch deposit summary:", e);
    }
  };

  useEffect(() => {
    if (isFocused) {
      fetchBalance();
      fetchHoldCount();
      fetchLargeExpenseSummary();
      fetchDebtSummary();
      fetchDepositSummary();
    }
  }, [isFocused]);

  const handleSetPaycheck = async () => {
    setIsLoading(true);
    try {
      const config = await getAuthHeader();
      const amount = parseFloat(paycheckInput);
      await axios.post(`${API_BASE_URL}/api/balance`, { amount }, config);
      setBalance(amount);
      setPaycheckInput("");
      setVisible(false);
    } catch (e) {
      console.error("Failed to set paycheck:", e);
      alert("Error updating balance.");
    }
    setIsLoading(false);
  };

  const isOverBudget = balance < 0;
  const absoluteOver = Math.abs(balance);

  return (
    <SafeAreaView style={styles.safe}>
      <ScrollView
        style={styles.scroll}
        contentContainerStyle={styles.scrollContent}
        showsVerticalScrollIndicator={false}
      >
        {/* ── Hero balance card ── */}
        <View
          style={[styles.balanceCard, isOverBudget && styles.balanceCardDanger]}
        >
          <Text style={styles.balanceEyebrow}>
            {isOverBudget ? "OVER BUDGET" : "DYNAMIC BUDGET"}
          </Text>

          <Text
            style={[
              styles.balanceAmount,
              isOverBudget && styles.balanceAmountDanger,
            ]}
          >
            {isOverBudget
              ? `-$${absoluteOver.toFixed(2)}`
              : `$${balance.toFixed(2)}`}
          </Text>

          {isOverBudget ? (
            <Text style={styles.balanceDangerNote}>
              You're over budget by ${absoluteOver.toFixed(2)} until your next
              paycheck.
            </Text>
          ) : (
            <Text style={styles.balanceSafeNote}>
              Safe to spend until your next paycheck
            </Text>
          )}
        </View>

        {/* ── Hold banner ── */}
        {holdCount > 0 && (
          <TouchableOpacity
            style={styles.holdBanner}
            onPress={() => navigation.navigate("ReviewSuspiciousHolds")}
            activeOpacity={0.8}
          >
            <Text style={styles.holdIcon}>⚠️</Text>
            <View style={styles.holdTextBlock}>
              <Text style={styles.holdTitle}>
                {holdCount} Pending Hold{holdCount > 1 ? "s" : ""} Need Review
              </Text>
              <Text style={styles.holdSub}>
                Gas / hotel / rental holds may be inflated. Tap to adjust.
              </Text>
            </View>
            <Text style={styles.holdChevron}>›</Text>
          </TouchableOpacity>
        )}

        {/* ── Large expense banner ── */}
        {/* Informational only — balance is already reduced. The banner lets the
            user know there are large purchases they haven't categorized yet. */}
        {largeExpenseSummary.count > 0 && (
          <TouchableOpacity
            style={styles.largeBanner}
            onPress={() => navigation.navigate("ReviewLargeExpenses")}
            activeOpacity={0.8}
          >
            <Text style={styles.largeIcon}>💳</Text>
            <View style={styles.holdTextBlock}>
              <Text style={styles.largeTitle}>
                {largeExpenseSummary.count} Large Expense
                {largeExpenseSummary.count > 1 ? "s" : ""} Need Review
              </Text>
              <Text style={styles.largeSub}>
                ${largeExpenseSummary.totalAmount.toFixed(2)} already deducted
                from your budget. Tap to categorize.
              </Text>
            </View>
            <Text style={styles.holdChevron}>›</Text>
          </TouchableOpacity>
        )}

        {/* ── Unexpected deposit banner ── */}
        {/* Shown when Windfall, InternalTransfer, or Refund deposits are     */}
        {/* awaiting review. Paycheck is excluded — it is expected income.    */}
        {depositSummary.count > 0 && (
          <TouchableOpacity
            style={styles.depositBanner}
            onPress={() => navigation.navigate("DepositReview")}
            activeOpacity={0.8}
          >
            <Text style={styles.depositIcon}>💚</Text>
            <View style={styles.holdTextBlock}>
              <Text style={styles.depositTitle}>
                {depositSummary.count} Unexpected Deposit
                {depositSummary.count > 1 ? "s" : ""} to Review
              </Text>
              <Text style={styles.depositSub}>
                ${depositSummary.totalAmount.toFixed(2)} awaiting your decision.
                Tap to review.
              </Text>
            </View>
            <Text style={styles.depositChevron}>›</Text>
          </TouchableOpacity>
        )}

        {/* ── Debt payoff progress card ── */}
        {/* Only shown when DebtStartingBalance was captured at onboarding.   */}
        {/* totalDebt null = old user / no Plaid data → card hidden silently. */}
        {debtSummary?.totalDebt != null && debtSummary.totalDebt > 0 && (
          <View style={styles.debtCard}>
            <Text style={styles.debtEyebrow}>DEBT PAYOFF PROGRESS</Text>
            {/* Show netDebtStartingBalance (after cash applied) when available;
                fall back to totalDebt for backward compatibility with old users. */}
            <Text style={styles.debtBalance}>
              $
              {(
                debtSummary.netDebtStartingBalance ?? debtSummary.totalDebt
              ).toFixed(2)}{" "}
              <Text style={styles.debtBalanceSub}>remaining</Text>
            </Text>
            {/* If cash was applied at onboarding, show a helpful context note. */}
            {debtSummary.cashAppliedAtOnboarding > 0 && (
              <Text style={styles.debtCashNote}>
                ${debtSummary.cashAppliedAtOnboarding.toFixed(2)} cash applied
                at setup
              </Text>
            )}
            {debtSummary.debtPerPaycheck > 0 && (
              <Text style={styles.debtDetail}>
                ${debtSummary.debtPerPaycheck.toFixed(2)}/paycheck
                {debtSummary.paychecksRemaining != null
                  ? ` · ~${debtSummary.paychecksRemaining} paychecks to go`
                  : ""}
              </Text>
            )}
          </View>
        )}

        {/* ── Actions ── */}
        <View style={styles.actionsSection}>
          <Button
            mode="contained"
            onPress={() => setVisible(true)}
            style={styles.primaryBtn}
            contentStyle={styles.btnContent}
            labelStyle={styles.btnLabel}
          >
            Edit Upcoming Paycheck
          </Button>

          <Button
            mode="outlined"
            onPress={() => navigation.navigate("DepositReview")}
            style={styles.outlinedBtn}
            contentStyle={styles.btnContent}
            labelStyle={styles.outlinedBtnLabel}
          >
            Review New Deposits
          </Button>

          <Button
            mode="outlined"
            onPress={() => navigation.navigate("ReviewLargeExpenses")}
            style={styles.outlinedBtn}
            contentStyle={styles.btnContent}
            labelStyle={styles.outlinedBtnLabel}
          >
            Review Large Expenses
          </Button>
        </View>
      </ScrollView>

      {/* ── Paycheck modal ── */}
      <Portal>
        <Modal
          visible={visible}
          onDismiss={() => setVisible(false)}
          contentContainerStyle={styles.modal}
        >
          <Card style={styles.modalCard}>
            <Card.Title title="New Paycheck" titleStyle={styles.modalTitle} />
            <Card.Content>
              <TextInput
                label="Amount ($)"
                value={paycheckInput}
                onChangeText={setPaycheckInput}
                keyboardType="numeric"
                mode="outlined"
                style={styles.modalInput}
              />
              <Button
                mode="contained"
                onPress={handleSetPaycheck}
                loading={isLoading}
                contentStyle={styles.btnContent}
                labelStyle={styles.btnLabel}
              >
                Save
              </Button>
            </Card.Content>
          </Card>
        </Modal>
      </Portal>
    </SafeAreaView>
  );
}

const styles = StyleSheet.create({
  safe: {
    flex: 1,
    backgroundColor: "#F8FAFC",
  },
  scroll: {
    flex: 1,
  },
  scrollContent: {
    paddingHorizontal: 20,
    paddingTop: 24,
    paddingBottom: 40,
  },

  // ── Balance card ──────────────────────────────────────────────
  balanceCard: {
    backgroundColor: "#FFFFFF",
    borderRadius: 20,
    paddingVertical: 32,
    paddingHorizontal: 28,
    alignItems: "center",
    marginBottom: 16,
    // shadow
    shadowColor: "#4F46E5",
    shadowOffset: { width: 0, height: 4 },
    shadowOpacity: 0.08,
    shadowRadius: 16,
    elevation: 4,
    borderWidth: 1,
    borderColor: "rgba(79,70,229,0.06)",
  },
  balanceCardDanger: {
    shadowColor: "#DC2626",
    borderColor: "rgba(220,38,38,0.08)",
  },
  balanceEyebrow: {
    fontSize: 11,
    fontWeight: "700",
    letterSpacing: 1.8,
    color: "#94A3B8",
    marginBottom: 14,
    textTransform: "uppercase",
  },
  balanceAmount: {
    fontSize: 52,
    fontWeight: "800",
    color: "#0D9488", // teal-600 — calm, positive
    letterSpacing: -1.5,
    marginBottom: 10,
  },
  balanceAmountDanger: {
    color: "#DC2626",
  },
  balanceSafeNote: {
    fontSize: 13,
    color: "#94A3B8",
    fontWeight: "400",
  },
  balanceDangerNote: {
    fontSize: 13,
    color: "#DC2626",
    fontWeight: "500",
    textAlign: "center",
    paddingHorizontal: 12,
  },

  // ── Hold banner ───────────────────────────────────────────────
  holdBanner: {
    flexDirection: "row",
    alignItems: "center",
    backgroundColor: "#FFFBEB",
    borderWidth: 1,
    borderColor: "#FDE68A",
    borderRadius: 14,
    paddingVertical: 14,
    paddingHorizontal: 16,
    marginBottom: 16,
  },
  holdIcon: {
    fontSize: 20,
    marginRight: 12,
  },
  holdTextBlock: {
    flex: 1,
  },
  holdTitle: {
    fontSize: 14,
    fontWeight: "700",
    color: "#92400E",
  },
  holdSub: {
    fontSize: 12,
    color: "#B45309",
    marginTop: 2,
    lineHeight: 18,
  },
  holdChevron: {
    fontSize: 22,
    color: "#D97706",
    marginLeft: 8,
    fontWeight: "300",
  },

  // ── Large expense banner ──────────────────────────────────────
  largeBanner: {
    flexDirection: "row",
    alignItems: "center",
    backgroundColor: "#EFF6FF",
    borderWidth: 1,
    borderColor: "#BFDBFE",
    borderRadius: 14,
    paddingVertical: 14,
    paddingHorizontal: 16,
    marginBottom: 16,
  },
  largeIcon: {
    fontSize: 20,
    marginRight: 12,
  },
  largeTitle: {
    fontSize: 14,
    fontWeight: "700",
    color: "#1E40AF",
  },
  largeSub: {
    fontSize: 12,
    color: "#3B82F6",
    marginTop: 2,
    lineHeight: 18,
  },

  // ── Unexpected deposit banner ─────────────────────────────────
  // Teal palette (#0D9488 family) — visually distinct from:
  //   hold banner    (amber  #FFFBEB / #FDE68A)
  //   large expense  (blue   #EFF6FF / #BFDBFE)
  depositBanner: {
    flexDirection: "row",
    alignItems: "center",
    backgroundColor: "#F0FDFA",
    borderWidth: 1,
    borderColor: "#99F6E4",
    borderRadius: 14,
    paddingVertical: 14,
    paddingHorizontal: 16,
    marginBottom: 16,
  },
  depositIcon: {
    fontSize: 20,
    marginRight: 12,
  },
  depositTitle: {
    fontSize: 14,
    fontWeight: "700",
    color: "#0F766E",
  },
  depositSub: {
    fontSize: 12,
    color: "#0D9488",
    marginTop: 2,
    lineHeight: 18,
  },
  depositChevron: {
    fontSize: 22,
    color: "#0D9488",
    marginLeft: 8,
    fontWeight: "300",
  },

  // ── Debt payoff card ──────────────────────────────────────────
  debtCard: {
    backgroundColor: "#FDF4FF",
    borderWidth: 1,
    borderColor: "#E9D5FF",
    borderRadius: 14,
    paddingVertical: 16,
    paddingHorizontal: 18,
    marginBottom: 16,
  },
  debtEyebrow: {
    fontSize: 10,
    fontWeight: "700",
    letterSpacing: 1.5,
    color: "#7C3AED",
    marginBottom: 6,
  },
  debtBalance: {
    fontSize: 26,
    fontWeight: "800",
    color: "#5B21B6",
    letterSpacing: -0.5,
    marginBottom: 4,
  },
  debtBalanceSub: {
    fontSize: 14,
    fontWeight: "400",
    color: "#7C3AED",
  },
  debtCashNote: {
    fontSize: 12,
    color: "#7C3AED",
    marginTop: 2,
    marginBottom: 2,
    fontStyle: "italic",
  },
  debtDetail: {
    fontSize: 13,
    color: "#6D28D9",
    marginTop: 2,
  },

  // ── Actions ───────────────────────────────────────────────────
  actionsSection: {
    gap: 10,
  },
  primaryBtn: {
    borderRadius: 12,
  },
  outlinedBtn: {
    borderRadius: 12,
    borderColor: "#4F46E5",
  },
  btnContent: {
    height: 50,
  },
  btnLabel: {
    fontSize: 15,
    fontWeight: "600",
    letterSpacing: 0.2,
  },
  outlinedBtnLabel: {
    fontSize: 15,
    fontWeight: "600",
    letterSpacing: 0.2,
    color: "#4F46E5",
  },

  // ── Modal ─────────────────────────────────────────────────────
  modal: {
    paddingHorizontal: 24,
  },
  modalCard: {
    borderRadius: 16,
    overflow: "hidden",
  },
  modalTitle: {
    fontSize: 18,
    fontWeight: "700",
    color: "#0F172A",
  },
  modalInput: {
    marginBottom: 16,
    backgroundColor: "#FFFFFF",
  },
});
