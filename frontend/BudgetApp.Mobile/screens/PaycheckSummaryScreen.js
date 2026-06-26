import React, { useState, useEffect } from "react";
import {
  View,
  Text,
  StyleSheet,
  TouchableOpacity,
  ScrollView,
  ActivityIndicator,
  Alert,
} from "react-native";
import { auth } from "../firebaseConfig";
import { API_BASE_URL } from "../config/api";
import { colors } from "../config/theme";

export default function PaycheckSummaryScreen({ route, navigation }) {
  const { summaryId } = route.params ?? {};
  const [summary, setSummary] = useState(null);
  const [loading, setLoading] = useState(true);
  const [submitting, setSubmitting] = useState(false);

  const getAuthHeader = async () => {
    const user = auth.currentUser;
    if (!user) throw new Error("No user logged in.");
    const token = await user.getIdToken(true);
    return { Authorization: `Bearer ${token}` };
  };

  useEffect(() => {
    fetchSummary();
  }, []);

  const fetchSummary = async () => {
    try {
      const headers = await getAuthHeader();
      const res = await fetch(`${API_BASE_URL}/api/paycheck-summary/current`, {
        headers,
      });
      if (res.ok && res.status !== 204) {
        const data = await res.json();
        setSummary(data);
      }
    } catch (e) {
      console.error("[PaycheckSummary] Failed to fetch summary", e);
    } finally {
      setLoading(false);
    }
  };

  const handleDecision = async (decision, amount) => {
    if (!summary) return;
    setSubmitting(true);
    try {
      const headers = await getAuthHeader();
      const body = { decision };
      if (amount !== undefined) body.amount = amount;

      const res = await fetch(
        `${API_BASE_URL}/api/paycheck-summary/${summary.id}/decision`,
        {
          method: "POST",
          headers: {
            ...headers,
            "Content-Type": "application/json",
          },
          body: JSON.stringify(body),
        },
      );

      if (res.ok) {
        if (decision === "Dismiss") {
          navigation.navigate("Home");
        } else {
          // Refresh to show updated state
          await fetchSummary();
          Alert.alert(
            "✅ Decision recorded",
            `Your choice "${decision}" has been saved.`,
          );
        }
      } else {
        Alert.alert("Error", "Could not record decision. Please try again.");
      }
    } catch (e) {
      console.error("[PaycheckSummary] Decision error", e);
      Alert.alert("Error", "Something went wrong.");
    } finally {
      setSubmitting(false);
    }
  };

  const fmt = (val) => (val != null ? `$${Number(val).toFixed(2)}` : "—");
  const fmtDate = (str) => (str ? new Date(str).toLocaleDateString() : "—");

  const styles = StyleSheet.create({
    container: {
      flex: 1,
      backgroundColor: colors.background,
    },
    scrollContent: {
      padding: 20,
      paddingBottom: 40,
    },
    header: {
      fontSize: 22,
      fontWeight: "bold",
      color: colors.text,
      marginBottom: 4,
    },
    subheader: {
      fontSize: 13,
      color: colors.muted,
      marginBottom: 20,
    },
    card: {
      backgroundColor: colors.card,
      borderRadius: 12,
      padding: 16,
      marginBottom: 16,
    },
    cardTitle: {
      fontSize: 14,
      fontWeight: "700",
      color: colors.muted,
      textTransform: "uppercase",
      letterSpacing: 0.5,
      marginBottom: 12,
    },
    row: {
      flexDirection: "row",
      justifyContent: "space-between",
      alignItems: "center",
      paddingVertical: 5,
    },
    rowLabel: {
      fontSize: 14,
      color: colors.text,
    },
    rowValue: {
      fontSize: 14,
      fontWeight: "600",
      color: colors.text,
    },
    divider: {
      height: 1,
      backgroundColor: colors.border ?? "#e0e0e0",
      marginVertical: 8,
    },
    statusBadge: {
      flexDirection: "row",
      alignItems: "center",
      borderRadius: 8,
      paddingHorizontal: 10,
      paddingVertical: 6,
      marginTop: 8,
    },
    statusText: {
      fontSize: 15,
      fontWeight: "700",
    },
    actionTitle: {
      fontSize: 16,
      fontWeight: "700",
      color: colors.text,
      marginBottom: 12,
    },
    actionButton: {
      borderRadius: 10,
      padding: 14,
      marginBottom: 10,
      alignItems: "center",
    },
    actionButtonText: {
      fontSize: 15,
      fontWeight: "600",
    },
    dismissButton: {
      borderRadius: 10,
      padding: 14,
      alignItems: "center",
      marginTop: 8,
      borderWidth: 1,
      borderColor: colors.muted,
    },
    overBudgetNote: {
      fontSize: 14,
      color: colors.text,
      lineHeight: 20,
    },
    decisionBadge: {
      backgroundColor: colors.card,
      borderRadius: 8,
      padding: 10,
      marginBottom: 12,
      alignItems: "center",
    },
    decisionText: {
      fontSize: 13,
      color: colors.muted,
    },
  });

  if (loading) {
    return (
      <View
        style={{
          flex: 1,
          justifyContent: "center",
          alignItems: "center",
          backgroundColor: colors.background,
        }}
      >
        <ActivityIndicator size="large" color={colors.primary} />
      </View>
    );
  }

  if (!summary) {
    return (
      <View
        style={{
          flex: 1,
          justifyContent: "center",
          alignItems: "center",
          backgroundColor: colors.background,
          padding: 20,
        }}
      >
        <Text style={{ color: colors.text, fontSize: 16, textAlign: "center" }}>
          No active paycheck summary found.
        </Text>
        <TouchableOpacity
          style={{
            marginTop: 20,
            padding: 14,
            backgroundColor: colors.card,
            borderRadius: 10,
          }}
          onPress={() => navigation.navigate("Home")}
        >
          <Text style={{ color: colors.primary, fontWeight: "600" }}>
            Go Back
          </Text>
        </TouchableOpacity>
      </View>
    );
  }

  const isUnder = summary.wasUnderBudget;

  return (
    <ScrollView
      style={styles.container}
      contentContainerStyle={styles.scrollContent}
    >
      {/* ── Header ───────────────────────────────────────────────────────── */}
      <Text style={styles.header}>📬 Paycheck Summary</Text>
      <Text style={styles.subheader}>
        {fmtDate(summary.periodStartDate)} – {fmtDate(summary.periodEndDate)}
      </Text>

      {/* ── Decision already recorded? ───────────────────────────────────── */}
      {summary.userDecision && (
        <View style={styles.decisionBadge}>
          <Text style={styles.decisionText}>
            ✅ You chose:{" "}
            <Text style={{ fontWeight: "600" }}>{summary.userDecision}</Text>
          </Text>
        </View>
      )}

      {/* ── Prior Period Recap ────────────────────────────────────────────── */}
      <View style={styles.card}>
        <Text style={styles.cardTitle}>Prior Period Recap</Text>

        <View style={styles.row}>
          <Text style={styles.rowLabel}>Paycheck amount</Text>
          <Text style={styles.rowValue}>{fmt(summary.paycheckAmount)}</Text>
        </View>
        <View style={styles.row}>
          <Text style={styles.rowLabel}>Budget for period</Text>
          <Text style={styles.rowValue}>
            {fmt(summary.priorPeriodStartingBudget)}
          </Text>
        </View>
        <View style={styles.row}>
          <Text style={styles.rowLabel}>Total spent</Text>
          <Text
            style={[styles.rowValue, { color: colors.danger ?? "#e53e3e" }]}
          >
            {fmt(summary.priorPeriodSpend)}
          </Text>
        </View>

        <View style={styles.divider} />

        <View
          style={[
            styles.statusBadge,
            { backgroundColor: isUnder ? "#e6f4ea" : "#fde8e8" },
          ]}
        >
          <Text
            style={[
              styles.statusText,
              { color: isUnder ? "#2d6a4f" : "#c62828" },
            ]}
          >
            {isUnder
              ? `✅ Under budget by ${fmt(summary.leftoverAmount)}`
              : `⚠️ Over budget by ${fmt(summary.overBudgetAmount)}`}
          </Text>
        </View>
      </View>

      {/* ── New Pay Period ────────────────────────────────────────────────── */}
      <View style={styles.card}>
        <Text style={styles.cardTitle}>New Pay Period</Text>

        <View style={styles.row}>
          <Text style={styles.rowLabel}>Next paycheck</Text>
          <Text style={styles.rowValue}>
            {fmtDate(summary.nextPaycheckDate)}
          </Text>
        </View>
        <View style={styles.row}>
          <Text style={styles.rowLabel}>Incoming paycheck</Text>
          <Text style={styles.rowValue}>{fmt(summary.paycheckAmount)}</Text>
        </View>
        <View style={styles.row}>
          <Text style={styles.rowLabel}>Fixed costs before next paycheck</Text>
          <Text
            style={[styles.rowValue, { color: colors.danger ?? "#e53e3e" }]}
          >
            −{fmt(summary.fixedCostsUntilNextPaycheck)}
          </Text>
        </View>
        <View style={styles.row}>
          <Text style={styles.rowLabel}>Savings contribution</Text>
          <Text
            style={[styles.rowValue, { color: colors.danger ?? "#e53e3e" }]}
          >
            −{fmt(summary.savingsContribution)}
          </Text>
        </View>
        <View style={styles.row}>
          <Text style={styles.rowLabel}>Planned debt payment</Text>
          <Text
            style={[styles.rowValue, { color: colors.danger ?? "#e53e3e" }]}
          >
            −{fmt(summary.debtPaymentAmount)}
          </Text>
        </View>

        <View style={styles.divider} />

        <View style={styles.row}>
          <Text style={[styles.rowLabel, { fontWeight: "700" }]}>
            New dynamic budget
          </Text>
          <Text
            style={[
              styles.rowValue,
              { fontSize: 18, color: colors.primary ?? "#3b82f6" },
            ]}
          >
            {fmt(summary.newDynamicBudgetAmount)}
          </Text>
        </View>
      </View>

      {/* ── Suggested Actions ─────────────────────────────────────────────── */}
      <Text style={styles.actionTitle}>What would you like to do?</Text>

      {isUnder ? (
        // Under-budget actions
        <>
          <TouchableOpacity
            style={[
              styles.actionButton,
              { backgroundColor: colors.primary ?? "#3b82f6" },
            ]}
            onPress={() =>
              handleDecision("AddToBudget", summary.leftoverAmount)
            }
            disabled={submitting}
          >
            <Text style={[styles.actionButtonText, { color: "#fff" }]}>
              ➕ Add {fmt(summary.leftoverAmount)} to This Period's Budget
            </Text>
          </TouchableOpacity>

          <TouchableOpacity
            style={[styles.actionButton, { backgroundColor: colors.card }]}
            onPress={() => handleDecision("ExtraDebtPayment")}
            disabled={submitting}
          >
            <Text style={[styles.actionButtonText, { color: colors.text }]}>
              💳 Put Leftover Toward Debt
            </Text>
          </TouchableOpacity>

          <TouchableOpacity
            style={[styles.actionButton, { backgroundColor: colors.card }]}
            onPress={() => handleDecision("TransferToSavings")}
            disabled={submitting}
          >
            <Text style={[styles.actionButtonText, { color: colors.text }]}>
              💰 Transfer to Savings
            </Text>
          </TouchableOpacity>

          <TouchableOpacity
            style={[styles.actionButton, { backgroundColor: colors.card }]}
            onPress={() => handleDecision("KeepAsBuffer")}
            disabled={submitting}
          >
            <Text style={[styles.actionButtonText, { color: colors.text }]}>
              🛡️ Keep as Buffer
            </Text>
          </TouchableOpacity>
        </>
      ) : (
        // Over-budget guidance (no fake actions)
        <View style={styles.card}>
          <Text style={styles.overBudgetNote}>
            You were over budget this period. No automatic adjustments are made.
            {"\n\n"}
            To get back on track, consider reviewing your fixed costs in{" "}
            <Text style={{ fontWeight: "600" }}>Settings → Fixed Costs</Text>,
            or adjusting your debt and savings plan in{" "}
            <Text style={{ fontWeight: "600" }}>Settings → Budget</Text>.
          </Text>
        </View>
      )}

      {/* ── Dismiss ───────────────────────────────────────────────────────── */}
      <TouchableOpacity
        style={styles.dismissButton}
        onPress={() => handleDecision("Dismiss")}
        disabled={submitting}
      >
        <Text style={{ color: colors.muted, fontWeight: "600" }}>
          {submitting ? "Saving…" : "Dismiss Summary"}
        </Text>
      </TouchableOpacity>
    </ScrollView>
  );
}
