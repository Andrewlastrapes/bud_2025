import React, { useState, useEffect } from "react";
import { View, StyleSheet, ScrollView, Alert, FlatList } from "react-native";
import {
  Text,
  Button,
  Card,
  Checkbox,
  ActivityIndicator,
  TextInput,
  List,
  IconButton,
  Modal,
  Portal,
  Chip,
} from "react-native-paper";
import { SafeAreaView } from "react-native-safe-area-context";
import axios from "axios";
import { auth } from "../../firebaseConfig";

import { API_BASE_URL } from "../../config/api";
import {
  COMMON_SUBSCRIPTIONS,
  getCoveredKeys,
} from "../../config/commonSubscriptions";

// --- Utility: Calculate 1st Day of Next Month as MM/DD ---
const getNextMonthFirstDay = () => {
  const today = new Date();
  const date = new Date(today.getFullYear(), today.getMonth() + 1, 1);
  const month = (date.getMonth() + 1).toString().padStart(2, "0");
  const day = date.getDate().toString().padStart(2, "0");
  return `${month}/${day}`;
};

// --- Utility: Infer year for a MM/DD date ---
const inferYear = (month, day) => {
  const today = new Date();
  const currentYear = today.getFullYear();
  const candidate = new Date(currentYear, month - 1, day);
  const todayMidnight = new Date(
    today.getFullYear(),
    today.getMonth(),
    today.getDate(),
  );
  return candidate < todayMidnight ? currentYear + 1 : currentYear;
};

// --- Utility: Safely convert any date string to an ISO string ---
const getISODate = (dateString, fieldName = "Date") => {
  if (!dateString || !String(dateString).trim()) return null;

  const trimmed = String(dateString).trim();

  if (/^\d{4}-\d{2}-\d{2}$/.test(trimmed)) {
    const [y, m, d] = trimmed.split("-").map(Number);
    const date = new Date(y, m - 1, d);
    if (
      date.getFullYear() !== y ||
      date.getMonth() !== m - 1 ||
      date.getDate() !== d
    ) {
      throw new Error(`Invalid date for ${fieldName}.`);
    }
    return date.toISOString();
  }

  const parts = trimmed.split("/");
  if (parts.length < 2 || parts.length > 3) {
    throw new Error(`Invalid date format for ${fieldName}. Please use MM/DD.`);
  }

  const month = Number(parts[0]);
  const day = Number(parts[1]);
  const year = parts.length === 3 ? Number(parts[2]) : inferYear(month, day);

  if (isNaN(month) || isNaN(day) || isNaN(year)) {
    throw new Error(`Invalid date format for ${fieldName}. Please use MM/DD.`);
  }

  const date = new Date(year, month - 1, day);
  if (
    date.getFullYear() !== year ||
    date.getMonth() !== month - 1 ||
    date.getDate() !== day
  ) {
    throw new Error(`Invalid date for ${fieldName}. Please use MM/DD.`);
  }

  return date.toISOString();
};

// --- Utility: Format any date string for display (MM/DD/YYYY) ---
const formatDisplayDate = (dateStr) => {
  if (!dateStr) return "Unknown";
  try {
    const iso = getISODate(dateStr);
    if (!iso) return "Unknown";
    const d = new Date(iso);
    const m = (d.getMonth() + 1).toString().padStart(2, "0");
    const day = d.getDate().toString().padStart(2, "0");
    const y = d.getFullYear();
    return `${m}/${day}/${y}`;
  } catch {
    return "Unknown";
  }
};

// --- Utility: Infer next projected date from last_date + frequency ---
const inferNextDate = (lastDateStr, frequency) => {
  if (!lastDateStr) return null;
  try {
    const [y, m, d] = lastDateStr.split("-").map(Number);
    const last = new Date(y, m - 1, d);
    let next;
    const freq = (frequency || "").toUpperCase();
    if (freq === "WEEKLY") next = new Date(last);
    next?.setDate(last.getDate() + 7);
    if (freq === "BIWEEKLY") {
      next = new Date(last);
      next.setDate(last.getDate() + 14);
    }
    if (freq === "SEMI_MONTHLY") {
      next = new Date(last);
      next.setDate(last.getDate() + 15);
    }
    if (freq === "MONTHLY") {
      next = new Date(last);
      next.setMonth(last.getMonth() + 1);
    }
    if (freq === "ANNUALLY") {
      next = new Date(last);
      next.setFullYear(last.getFullYear() + 1);
    }
    if (!next || isNaN(next.getTime())) return null;
    const nm = (next.getMonth() + 1).toString().padStart(2, "0");
    const nd = next.getDate().toString().padStart(2, "0");
    return `${next.getFullYear()}-${nm}-${nd}`;
  } catch {
    return null;
  }
};

// --- Helper function to get the auth token ---
const getAuthHeader = async () => {
  const user = auth.currentUser;
  if (!user) throw new Error("No user logged in.");
  const token = await user.getIdToken();
  return { headers: { Authorization: `Bearer ${token}` } };
};

export default function FixedCostsSetupScreen({ navigation, route }) {
  const [isLoading, setIsLoading] = useState(true);
  const [isSaving, setIsSaving] = useState(false);

  // Income params forwarded from DepositOnboardingScreen
  const incomeParams = {
    paycheckAmount: route.params?.paycheckAmount,
    payDay1: route.params?.payDay1,
    payDay2: route.params?.payDay2,
  };

  // Mandatory Fixed Inputs
  const [manualRent, setManualRent] = useState("");
  const [manualCar, setManualCar] = useState("");
  const [manualStudentLoan, setManualStudentLoan] = useState("");

  // Date Inputs (stored as MM/DD — year is inferred on save)
  const [manualRentDueDate, setManualRentDueDate] = useState("");
  const [manualCarDueDate, setManualCarDueDate] = useState("");
  const [manualStudentLoanDueDate, setManualStudentLoanDueDate] = useState("");

  // Dynamic List State (free-form "Other Recurring Bills")
  const [otherManualCosts, setOtherManualCosts] = useState([]);

  // Quick-add subscription drafts (subscriptions selected from the chip list
  // that the user has already completed with amount + due date)
  const [quickAddSubs, setQuickAddSubs] = useState([]);

  // Modal state — shared for both free-form add and subscription quick-add
  const [isAddModalVisible, setIsAddModalVisible] = useState(false);
  const [newCostName, setNewCostName] = useState("");
  const [newCostAmount, setNewCostAmount] = useState("");
  const [newCostDueDate, setNewCostDueDate] = useState("");
  // When non-empty this is a quick-add subscription tap (name is pre-filled
  // and locked); when empty this is a free-form manual bill add.
  const [quickAddName, setQuickAddName] = useState("");

  // Plaid Discovery State
  const [plaidRecurrings, setPlaidRecurrings] = useState([]);

  // ── Derived: subscription chips to show ───────────────────────────────────
  // Recomputed on every render from current state — no useEffect needed.
  // A subscription is hidden if it is already covered by Plaid or any manual
  // entry (otherManualCosts OR completed quickAddSubs).
  const coveredKeys = getCoveredKeys(plaidRecurrings, [
    ...otherManualCosts,
    ...quickAddSubs,
  ]);
  const availableSubs = COMMON_SUBSCRIPTIONS.filter(
    (s) => !coveredKeys.has(s.key),
  );

  // --- Lifecycle: Fetch Plaid Data and Set Initial Date ---
  useEffect(() => {
    setManualRentDueDate(getNextMonthFirstDay());
    fetchPlaidData();
  }, []);

  const fetchPlaidData = async () => {
    try {
      const config = await getAuthHeader();
      const response = await axios.get(
        `${API_BASE_URL}/api/plaid/recurring`,
        config,
      );

      const outflow = response.data.outflow_streams || [];

      const initialCosts = outflow.map((stream) => {
        const rawNextDate =
          stream.next_projected_date ||
          inferNextDate(stream.last_date, stream.frequency);

        const confidenceLevel =
          stream.confidence_level ||
          (stream.status === "MATURE"
            ? "HIGH"
            : stream.status === "EARLY_DETECTION"
              ? "MEDIUM"
              : "LOW");

        return {
          id: stream.stream_id,
          name: stream.description || stream.merchant_name || "Unknown",
          amount: stream.last_amount?.amount ?? 0,
          frequency: stream.frequency || "UNKNOWN",
          nextDueDate: rawNextDate || null,
          isApproved:
            confidenceLevel === "HIGH" || confidenceLevel === "MEDIUM",
        };
      });

      setPlaidRecurrings(initialCosts);
    } catch (e) {
      console.error("Failed to fetch Plaid recurring data:", e);
    }
    setIsLoading(false);
  };

  // --- Remove Item from free-form dynamic list ---
  const handleRemoveCost = (id) => {
    setOtherManualCosts((prev) => prev.filter((c) => c.id !== id));
  };

  // --- Remove completed quick-add subscription (re-shows chip) ---
  const handleRemoveQuickAdd = (id) => {
    setQuickAddSubs((prev) => prev.filter((c) => c.id !== id));
  };

  // --- Open the add modal for a quick-add chip tap ---
  const handleChipPress = (label) => {
    setQuickAddName(label);
    setNewCostName(label); // pre-fill; not editable in modal
    setNewCostAmount("");
    setNewCostDueDate("");
    setIsAddModalVisible(true);
  };

  // --- Open the add modal for a free-form bill ---
  const handleOpenFreeFormModal = () => {
    setQuickAddName("");
    setNewCostName("");
    setNewCostAmount("");
    setNewCostDueDate("");
    setIsAddModalVisible(true);
  };

  // --- Close modal and fully reset its state ---
  const handleCloseModal = () => {
    setIsAddModalVisible(false);
    setQuickAddName("");
    setNewCostName("");
    setNewCostAmount("");
    setNewCostDueDate("");
  };

  // --- Add a bill from the modal ---
  // Routes to quickAddSubs (subscription) or otherManualCosts (free-form)
  const handleAddNewCost = () => {
    const name = quickAddName || newCostName;
    if (!name || !newCostAmount) {
      Alert.alert("Missing Info", "Please enter both a name and an amount.");
      return;
    }

    try {
      getISODate(newCostDueDate, name);
    } catch (error) {
      Alert.alert("Invalid Date", error.message);
      return;
    }

    const newEntry = {
      id: Date.now(),
      name,
      amount: parseFloat(newCostAmount),
      nextDueDate: newCostDueDate,
    };

    if (quickAddName) {
      // Subscription quick-add: goes into dedicated list
      setQuickAddSubs((prev) => [...prev, newEntry]);
    } else {
      // Free-form bill: goes into the Other Recurring Bills list
      setOtherManualCosts((prev) => [...prev, newEntry]);
    }

    handleCloseModal();
  };

  // --- Logic for saving all costs (Manual + Quick-add + Plaid) ---
  const handleNext = async () => {
    setIsSaving(true);
    try {
      const config = await getAuthHeader();
      const costsToSave = [];

      const addManualCost = (
        name,
        amount,
        dueDate,
        category = "Other",
        recurrenceFrequency = "Monthly",
      ) => {
        if (amount && parseFloat(amount) > 0) {
          costsToSave.push({
            name,
            amount: parseFloat(amount),
            category,
            type: "manual",
            recurrenceFrequency,
            nextDueDate: getISODate(dueDate, name),
          });
        }
      };

      // 1. Mandatory Fixed Costs
      addManualCost("Rent/Mortgage", manualRent, manualRentDueDate, "Housing");
      addManualCost(
        "Car Payment",
        manualCar,
        manualCarDueDate,
        "Transportation",
      );
      addManualCost(
        "Student Loan",
        manualStudentLoan,
        manualStudentLoanDueDate,
        "Loan",
      );

      // 2. Dynamic Manual Costs (free-form "Other Recurring Bills")
      otherManualCosts.forEach((cost) => {
        addManualCost(cost.name, cost.amount, cost.nextDueDate);
      });

      // 3. Quick-add subscriptions — always Subscription category, Monthly
      quickAddSubs.forEach((cost) => {
        addManualCost(
          cost.name,
          cost.amount,
          cost.nextDueDate,
          "Subscription",
          "Monthly",
        );
      });

      // 4. Save Confirmed Plaid Costs
      plaidRecurrings.forEach((cost) => {
        if (cost.isApproved) {
          costsToSave.push({
            name: cost.name,
            amount: cost.amount,
            category: "Subscription",
            type: "plaid_discovered",
            recurrenceFrequency: "Monthly",
            plaidMerchantName: cost.name,
            nextDueDate: getISODate(cost.nextDueDate, cost.name),
          });
        }
      });

      for (const cost of costsToSave) {
        await axios.post(`${API_BASE_URL}/api/fixed-costs`, cost, config);
      }

      navigation.navigate("DebtOnboarding", incomeParams);
    } catch (error) {
      console.error("Failed to save costs:", error);
      Alert.alert("Error Saving Costs", error.message);
    }
    setIsSaving(false);
  };

  if (isLoading) {
    return (
      <View style={styles.loadingContainer}>
        <ActivityIndicator size="large" />
        <Text>Analyzing your transactions...</Text>
      </View>
    );
  }

  return (
    <SafeAreaView style={styles.container}>
      <Text style={styles.header}>Fixed Costs (2/4)</Text>
      <ScrollView contentContainerStyle={styles.scrollContent}>
        {/* --- A. Mandatory Payments & Savings (Fixed Inputs) --- */}
        <Card style={styles.card}>
          <Card.Title title="Mandatory Payments & Savings" />
          <Card.Content>
            <Text style={styles.subHeader}>
              These are charges that occur for nearly the same amount at the
              same time each month. We subtract the total of these costs
              *upfront*.
            </Text>

            {/* RENT */}
            <Text style={styles.inputLabel}>Rent or Mortgage</Text>
            <View style={styles.row}>
              <TextInput
                label="Amount ($)"
                value={manualRent}
                onChangeText={setManualRent}
                keyboardType="numeric"
                style={styles.rowInput}
              />
              <TextInput
                label="Due Date (MM/DD)"
                value={manualRentDueDate}
                onChangeText={setManualRentDueDate}
                style={styles.rowInput}
                placeholder="MM/DD"
              />
            </View>

            {/* CAR PAYMENT */}
            <Text style={styles.inputLabel}>Car Payment</Text>
            <View style={styles.row}>
              <TextInput
                label="Amount ($)"
                value={manualCar}
                onChangeText={setManualCar}
                keyboardType="numeric"
                style={styles.rowInput}
              />
              <TextInput
                label="Due Date (MM/DD)"
                value={manualCarDueDate}
                onChangeText={setManualCarDueDate}
                style={styles.rowInput}
                placeholder="MM/DD"
              />
            </View>

            {/* STUDENT LOAN */}
            <Text style={styles.inputLabel}>Student Loan Payment</Text>
            <View style={styles.row}>
              <TextInput
                label="Amount ($)"
                value={manualStudentLoan}
                onChangeText={setManualStudentLoan}
                keyboardType="numeric"
                style={styles.rowInput}
              />
              <TextInput
                label="Due Date (MM/DD)"
                value={manualStudentLoanDueDate}
                onChangeText={setManualStudentLoanDueDate}
                style={styles.rowInput}
                placeholder="MM/DD"
              />
            </View>
          </Card.Content>
        </Card>

        {/* --- B. Other Manual Costs (Dynamic List) --- */}
        <Card style={styles.card}>
          <Card.Title
            title="Other Recurring Bills"
            subtitle="Internet, Phone, Gym, etc."
          />
          <Card.Content>
            <FlatList
              data={otherManualCosts}
              keyExtractor={(item) => item.id.toString()}
              scrollEnabled={false}
              renderItem={({ item }) => (
                <List.Item
                  title={`${item.name} - $${parseFloat(item.amount).toFixed(2)}`}
                  description={`Due: ${formatDisplayDate(item.nextDueDate)}`}
                  right={() => (
                    <IconButton
                      icon="close"
                      onPress={() => handleRemoveCost(item.id)}
                    />
                  )}
                />
              )}
            />
            <Button
              mode="outlined"
              onPress={handleOpenFreeFormModal}
              style={{ marginTop: 10 }}
            >
              + Add New Recurring Bill
            </Button>
          </Card.Content>
        </Card>

        {/* --- D. Common Subscriptions Quick-Add --- */}
        <Card style={styles.card}>
          <Card.Title
            title="Common subscriptions"
            subtitle="Add any subscriptions Plaid didn't find."
          />
          <Card.Content>
            {availableSubs.length === 0 ? (
              <Text style={styles.infoText}>
                All common subscriptions are already accounted for. ✓
              </Text>
            ) : (
              <>
                <Text style={styles.chipHint}>
                  Tap a subscription to add it. Already-found services are
                  hidden.
                </Text>
                <View style={styles.chipRow}>
                  {availableSubs.map((sub) => (
                    <Chip
                      key={sub.key}
                      icon="plus"
                      onPress={() => handleChipPress(sub.label)}
                      style={styles.subChip}
                      compact
                    >
                      {sub.label}
                    </Chip>
                  ))}
                </View>
              </>
            )}

            {/* Already-added quick-add subscriptions */}
            {quickAddSubs.length > 0 && (
              <>
                <Text style={styles.addedLabel}>Added:</Text>
                {quickAddSubs.map((item) => (
                  <List.Item
                    key={item.id}
                    title={`${item.name} — $${parseFloat(item.amount).toFixed(2)}`}
                    description={`Due: ${formatDisplayDate(item.nextDueDate)}  ·  Monthly`}
                    left={() => <List.Icon icon="check-circle-outline" />}
                    right={() => (
                      <IconButton
                        icon="close"
                        onPress={() => handleRemoveQuickAdd(item.id)}
                      />
                    )}
                  />
                ))}
              </>
            )}
          </Card.Content>
        </Card>

        {/* --- C. Plaid Discovery --- */}
        <Card style={styles.card}>
          <Card.Title
            title="Suggested from your accounts"
            subtitle="Confirm the recurring costs we found in your history."
          />
          <Card.Content>
            {plaidRecurrings.length === 0 ? (
              <Text style={styles.infoText}>
                No recurring subscriptions detected yet.
              </Text>
            ) : (
              <List.Section>
                {plaidRecurrings.map((cost) => (
                  <List.Item
                    key={cost.id}
                    title={cost.name}
                    description={`$${parseFloat(cost.amount).toFixed(2)} | Due: ${formatDisplayDate(cost.nextDueDate)} | ${cost.frequency}`}
                    right={() => (
                      <Checkbox.Android
                        status={cost.isApproved ? "checked" : "unchecked"}
                        onPress={() => {
                          setPlaidRecurrings((prev) =>
                            prev.map((c) =>
                              c.id === cost.id
                                ? { ...c, isApproved: !c.isApproved }
                                : c,
                            ),
                          );
                        }}
                      />
                    )}
                  />
                ))}
              </List.Section>
            )}
          </Card.Content>
        </Card>
      </ScrollView>

      <Button
        mode="contained"
        onPress={handleNext}
        loading={isSaving}
        style={styles.bottomButton}
      >
        Next: Debt
      </Button>

      {/* --- Modal: Add Recurring Bill (free-form) or Subscription (quick-add) --- */}
      <Portal>
        <Modal
          visible={isAddModalVisible}
          onDismiss={handleCloseModal}
          contentContainerStyle={styles.modal}
        >
          <Card>
            <Card.Title
              title={
                quickAddName ? `Add ${quickAddName}` : "Add New Recurring Bill"
              }
            />
            <Card.Content>
              {/* Name field: locked when quick-adding a subscription */}
              <TextInput
                label="Bill Name"
                value={newCostName}
                onChangeText={quickAddName ? undefined : setNewCostName}
                editable={!quickAddName}
                style={[styles.input, quickAddName ? styles.inputLocked : null]}
              />
              <TextInput
                label="Amount ($)"
                value={newCostAmount}
                onChangeText={setNewCostAmount}
                keyboardType="numeric"
                style={styles.input}
              />
              <TextInput
                label="Due Date (MM/DD) — optional"
                value={newCostDueDate}
                onChangeText={setNewCostDueDate}
                style={styles.input}
                placeholder="MM/DD"
              />
              {quickAddName ? (
                <Text style={styles.modalHint}>
                  Category: Subscription · Recurrence: Monthly
                </Text>
              ) : null}
              <Button
                mode="contained"
                onPress={handleAddNewCost}
                style={{ marginTop: 8 }}
              >
                {quickAddName ? `Add ${quickAddName}` : "Add Bill"}
              </Button>
              <Button onPress={handleCloseModal} style={{ marginTop: 4 }}>
                Cancel
              </Button>
            </Card.Content>
          </Card>
        </Modal>
      </Portal>
    </SafeAreaView>
  );
}

const styles = StyleSheet.create({
  container: { flex: 1, backgroundColor: "#f0f0f0" },
  loadingContainer: { flex: 1, justifyContent: "center", alignItems: "center" },
  header: {
    fontSize: 24,
    fontWeight: "bold",
    textAlign: "center",
    marginVertical: 15,
  },
  scrollContent: { paddingBottom: 100 },
  card: { marginHorizontal: 15, marginVertical: 10 },
  input: { marginBottom: 10 },
  inputLocked: { backgroundColor: "#f5f5f5", opacity: 0.85 },
  row: {
    flexDirection: "row",
    justifyContent: "space-between",
    marginBottom: 10,
  },
  rowInput: { width: "48%" },
  inputLabel: {
    fontSize: 16,
    marginTop: 15,
    marginBottom: 5,
    fontWeight: "500",
  },
  subHeader: {
    fontSize: 14,
    textAlign: "center",
    marginBottom: 10,
    color: "#333",
    lineHeight: 20,
  },
  infoText: { fontSize: 13, color: "#666", padding: 5, textAlign: "center" },
  chipHint: { fontSize: 12, color: "#666", marginBottom: 10 },
  chipRow: { flexDirection: "row", flexWrap: "wrap", gap: 8, marginBottom: 8 },
  subChip: { marginBottom: 4 },
  addedLabel: {
    fontSize: 13,
    fontWeight: "600",
    color: "#444",
    marginTop: 8,
    marginBottom: 2,
  },
  modalHint: { fontSize: 12, color: "#888", marginBottom: 8 },
  bottomButton: { margin: 20 },
  modal: { backgroundColor: "white", padding: 20, margin: 20, borderRadius: 8 },
});
