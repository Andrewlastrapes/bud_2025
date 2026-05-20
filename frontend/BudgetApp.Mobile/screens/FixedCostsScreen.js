import React, { useState, useEffect } from "react";
import { View, StyleSheet, FlatList, Alert } from "react-native";
import {
  Text,
  Button,
  List,
  TextInput,
  Modal,
  Portal,
  Card,
  IconButton,
  ActivityIndicator,
  Snackbar,
  Chip,
} from "react-native-paper";
import { SafeAreaView } from "react-native-safe-area-context";
import axios from "axios";
import { auth } from "../firebaseConfig";
import { useIsFocused } from "@react-navigation/native";

import { API_BASE_URL } from "../config/api";
import { getISODate, formatDisplayDate, formatMMDD } from "../config/dateUtils";

// ─── Category options shown as chips in Add/Edit modals ──────────────────────
const CATEGORIES = [
  "Housing",
  "Transportation",
  "Loan",
  "Subscription",
  "Savings",
  "Other",
];

// ─── Recurrence frequency options ────────────────────────────────────────────
// "Monthly" is the default and covers 95% of bills.
// "OneTime" costs are NEVER auto-advanced — they sit with their original due date.
const FREQUENCIES = ["Monthly", "BiMonthly", "Quarterly", "Annual", "OneTime"];

// Human-readable labels shown on chips and in the list description
const FREQ_LABELS = {
  Monthly: "Monthly",
  BiMonthly: "Every 2 months",
  Quarterly: "Quarterly",
  Annual: "Annual",
  OneTime: "One-time",
};

export default function FixedCostsScreen() {
  const [costs, setCosts] = useState([]);
  const [isLoading, setIsLoading] = useState(false);

  // ── Add modal state ──
  const [isAddModalVisible, setIsAddModalVisible] = useState(false);
  const [newName, setNewName] = useState("");
  const [newAmount, setNewAmount] = useState("");
  const [newCategory, setNewCategory] = useState("Other");
  const [newDueDate, setNewDueDate] = useState("");
  const [newFrequency, setNewFrequency] = useState("Monthly");

  // ── Edit modal state ──
  const [isEditModalVisible, setIsEditModalVisible] = useState(false);
  const [editingCost, setEditingCost] = useState(null); // the full cost object
  const [editName, setEditName] = useState("");
  const [editAmount, setEditAmount] = useState("");
  const [editCategory, setEditCategory] = useState("Other");
  const [editDueDate, setEditDueDate] = useState("");
  const [editFrequency, setEditFrequency] = useState("Monthly");

  // ── Snackbar ──
  const [snackbarMessage, setSnackbarMessage] = useState("");

  const isFocused = useIsFocused();

  // ─── Auth helper ──────────────────────────────────────────────────────────
  const getAuthHeader = async () => {
    const user = auth.currentUser;
    if (!user) throw new Error("No user logged in.");
    const token = await user.getIdToken();
    return { headers: { Authorization: `Bearer ${token}` } };
  };

  // ─── Fetch ────────────────────────────────────────────────────────────────
  const fetchCosts = async () => {
    setIsLoading(true);
    try {
      const config = await getAuthHeader();
      const response = await axios.get(
        `${API_BASE_URL}/api/fixed-costs`,
        config,
      );
      setCosts(response.data);
    } catch (e) {
      console.error("Failed to fetch costs:", e);
    }
    setIsLoading(false);
  };

  useEffect(() => {
    if (isFocused) fetchCosts();
  }, [isFocused]);

  // ─── Add ──────────────────────────────────────────────────────────────────
  const handleAddCost = async () => {
    if (!newName.trim()) {
      Alert.alert("Missing Info", "Please enter a name.");
      return;
    }
    const amount = parseFloat(newAmount);
    if (isNaN(amount) || amount <= 0) {
      Alert.alert("Invalid Amount", "Please enter a positive amount.");
      return;
    }

    let nextDueDate = null;
    try {
      nextDueDate = getISODate(newDueDate, "Due Date");
    } catch (e) {
      Alert.alert("Invalid Date", e.message);
      return;
    }

    try {
      const config = await getAuthHeader();
      await axios.post(
        `${API_BASE_URL}/api/fixed-costs`,
        {
          name: newName.trim(),
          amount,
          category: newCategory,
          type: "manual",
          nextDueDate,
          recurrenceFrequency: newFrequency,
        },
        config,
      );

      // Reset add modal
      setNewName("");
      setNewAmount("");
      setNewCategory("Other");
      setNewDueDate("");
      setNewFrequency("Monthly");
      setIsAddModalVisible(false);

      await fetchCosts();

      const isOneTime = newFrequency === "OneTime";
      const msg = nextDueDate
        ? isOneTime
          ? "Fixed cost saved. This is a one-time cost — it won't auto-advance after it's matched."
          : "Fixed cost saved. The due date will automatically advance after each matched transaction."
        : "Fixed cost saved (no due date set — it will not affect the current budget period until a due date is added).";
      setSnackbarMessage(msg);
    } catch (e) {
      console.error("Failed to add cost:", e);
      Alert.alert("Error", "Failed to save fixed cost.");
    }
  };

  // ─── Open edit modal ──────────────────────────────────────────────────────
  const openEditModal = (cost) => {
    setEditingCost(cost);
    setEditName(cost.name || "");
    setEditAmount(String(cost.amount || ""));
    setEditCategory(cost.category || "Other");
    setEditDueDate(formatMMDD(cost.nextDueDate) || "");
    // Fall back to Monthly for older costs that don't have the field yet
    setEditFrequency(cost.recurrenceFrequency || "Monthly");
    setIsEditModalVisible(true);
  };

  // ─── Save edit ────────────────────────────────────────────────────────────
  const handleSaveEdit = async () => {
    if (!editName.trim()) {
      Alert.alert("Missing Info", "Please enter a name.");
      return;
    }
    const amount = parseFloat(editAmount);
    if (isNaN(amount) || amount <= 0) {
      Alert.alert("Invalid Amount", "Please enter a positive amount.");
      return;
    }

    let nextDueDate = null;
    try {
      nextDueDate = getISODate(editDueDate, "Due Date");
    } catch (e) {
      Alert.alert("Invalid Date", e.message);
      return;
    }

    try {
      const config = await getAuthHeader();
      await axios.put(
        `${API_BASE_URL}/api/fixed-costs/${editingCost.id}`,
        {
          name: editName.trim(),
          amount,
          category: editCategory,
          type: editingCost.type || "manual",
          nextDueDate,
          recurrenceFrequency: editFrequency,
        },
        config,
      );

      setIsEditModalVisible(false);
      setEditingCost(null);

      await fetchCosts();

      const isOneTime = editFrequency === "OneTime";
      const msg = nextDueDate
        ? isOneTime
          ? "Fixed cost updated. One-time cost — due date won't auto-advance."
          : "Fixed cost updated. Due date will auto-advance after each matched transaction."
        : "Fixed cost updated (no due date — not included in current budget period).";
      setSnackbarMessage(msg);
    } catch (e) {
      console.error("Failed to update cost:", e);
      Alert.alert("Error", "Failed to update fixed cost.");
    }
  };

  // ─── Delete ───────────────────────────────────────────────────────────────
  const handleDeleteCost = async (id) => {
    Alert.alert("Delete Fixed Cost", "Are you sure you want to delete this?", [
      { text: "Cancel", style: "cancel" },
      {
        text: "Delete",
        style: "destructive",
        onPress: async () => {
          try {
            const config = await getAuthHeader();
            await axios.delete(`${API_BASE_URL}/api/fixed-costs/${id}`, config);
            await fetchCosts();
            setSnackbarMessage("Fixed cost deleted.");
          } catch (e) {
            console.error("Failed to delete cost:", e);
            Alert.alert("Error", "Failed to delete fixed cost.");
          }
        },
      },
    ]);
  };

  // ─── Render list item ─────────────────────────────────────────────────────
  const renderItem = ({ item }) => {
    const displayDate = formatDisplayDate(item.nextDueDate);
    const dueLine = displayDate
      ? `Due: ${displayDate}`
      : "⚠️ No due date — not included in budget";
    const categoryLine = item.category || "other";
    const freqLabel =
      FREQ_LABELS[item.recurrenceFrequency] ||
      item.recurrenceFrequency ||
      "Monthly";

    return (
      <List.Item
        title={item.name}
        description={`$${item.amount.toFixed(2)}  ·  ${categoryLine}  ·  ${freqLabel}\n${dueLine}`}
        descriptionNumberOfLines={2}
        left={() => (
          <List.Icon
            icon={item.type === "manual" ? "account-edit" : "bank-check"}
          />
        )}
        right={() => (
          <View style={styles.rowActions}>
            <IconButton
              icon="pencil"
              iconColor="#555"
              size={20}
              onPress={() => openEditModal(item)}
            />
            <IconButton
              icon="delete"
              iconColor="red"
              size={20}
              onPress={() => handleDeleteCost(item.id)}
            />
          </View>
        )}
      />
    );
  };

  // ─── Category chip row ────────────────────────────────────────────────────
  const renderCategoryChips = (selected, onSelect) => (
    <View style={styles.chipRow}>
      {CATEGORIES.map((cat) => (
        <Chip
          key={cat}
          selected={selected === cat}
          onPress={() => onSelect(cat)}
          style={styles.chip}
          compact
        >
          {cat}
        </Chip>
      ))}
    </View>
  );

  // ─── Frequency chip row ───────────────────────────────────────────────────
  const renderFrequencyChips = (selected, onSelect) => (
    <View style={styles.chipRow}>
      {FREQUENCIES.map((freq) => (
        <Chip
          key={freq}
          selected={selected === freq}
          onPress={() => onSelect(freq)}
          style={styles.chip}
          compact
        >
          {FREQ_LABELS[freq]}
        </Chip>
      ))}
    </View>
  );

  // ─── Render ───────────────────────────────────────────────────────────────
  return (
    <SafeAreaView style={styles.container}>
      <Button
        mode="contained"
        onPress={() => setIsAddModalVisible(true)}
        style={styles.addButton}
      >
        Add Fixed Cost
      </Button>

      {isLoading ? (
        <ActivityIndicator style={{ marginTop: 20 }} />
      ) : costs.length === 0 ? (
        <Text style={styles.emptyText}>
          No fixed costs yet. Tap above to add one.
        </Text>
      ) : (
        <FlatList
          data={costs}
          keyExtractor={(item) => item.id.toString()}
          renderItem={renderItem}
        />
      )}

      {/* ── Add Modal ─────────────────────────────────────────────────────── */}
      <Portal>
        <Modal
          visible={isAddModalVisible}
          onDismiss={() => setIsAddModalVisible(false)}
          contentContainerStyle={styles.modal}
        >
          <Card>
            <Card.Title title="Add Fixed Cost" />
            <Card.Content>
              <TextInput
                label="Name (e.g., Rent, Netflix)"
                value={newName}
                onChangeText={setNewName}
                style={styles.input}
              />
              <TextInput
                label="Amount ($)"
                value={newAmount}
                onChangeText={setNewAmount}
                keyboardType="numeric"
                style={styles.input}
              />
              <Text style={styles.label}>Category</Text>
              {renderCategoryChips(newCategory, setNewCategory)}
              <Text style={styles.label}>How often does this recur?</Text>
              {renderFrequencyChips(newFrequency, setNewFrequency)}
              <TextInput
                label="Next Due Date (MM/DD) — required to affect budget"
                value={newDueDate}
                onChangeText={setNewDueDate}
                placeholder="MM/DD"
                style={styles.input}
              />
              <Text style={styles.hint}>
                {newFrequency === "OneTime"
                  ? "One-time cost: enter the due date. It won't auto-advance after it's matched."
                  : "The due date will automatically advance to the next period after each matched transaction — you don't need to update it manually."}
              </Text>
              <Button
                mode="contained"
                onPress={handleAddCost}
                style={{ marginTop: 10 }}
              >
                Save
              </Button>
            </Card.Content>
          </Card>
        </Modal>
      </Portal>

      {/* ── Edit Modal ────────────────────────────────────────────────────── */}
      <Portal>
        <Modal
          visible={isEditModalVisible}
          onDismiss={() => setIsEditModalVisible(false)}
          contentContainerStyle={styles.modal}
        >
          <Card>
            <Card.Title title="Edit Fixed Cost" />
            <Card.Content>
              <TextInput
                label="Name"
                value={editName}
                onChangeText={setEditName}
                style={styles.input}
              />
              <TextInput
                label="Amount ($)"
                value={editAmount}
                onChangeText={setEditAmount}
                keyboardType="numeric"
                style={styles.input}
              />
              <Text style={styles.label}>Category</Text>
              {renderCategoryChips(editCategory, setEditCategory)}
              <Text style={styles.label}>How often does this recur?</Text>
              {renderFrequencyChips(editFrequency, setEditFrequency)}
              <TextInput
                label="Next Due Date (MM/DD)"
                value={editDueDate}
                onChangeText={setEditDueDate}
                placeholder="MM/DD"
                style={styles.input}
              />
              <Text style={styles.hint}>
                {editFrequency === "OneTime"
                  ? "One-time cost: the due date won't auto-advance after being matched."
                  : "The due date will automatically advance after each matched transaction — no need to update it manually each month."}
              </Text>
              <Button
                mode="contained"
                onPress={handleSaveEdit}
                style={{ marginTop: 10 }}
              >
                Save Changes
              </Button>
            </Card.Content>
          </Card>
        </Modal>
      </Portal>

      {/* ── Snackbar ──────────────────────────────────────────────────────── */}
      <Snackbar
        visible={!!snackbarMessage}
        onDismiss={() => setSnackbarMessage("")}
        duration={5000}
        action={{ label: "OK", onPress: () => setSnackbarMessage("") }}
      >
        {snackbarMessage}
      </Snackbar>
    </SafeAreaView>
  );
}

const styles = StyleSheet.create({
  container: { flex: 1 },
  addButton: { margin: 16 },
  emptyText: {
    textAlign: "center",
    marginTop: 40,
    color: "#666",
    fontSize: 14,
  },
  modal: { padding: 16 },
  input: { marginBottom: 10 },
  label: { fontSize: 13, color: "#555", marginBottom: 6, marginTop: 4 },
  hint: { fontSize: 12, color: "#888", marginTop: 4, marginBottom: 6 },
  chipRow: {
    flexDirection: "row",
    flexWrap: "wrap",
    gap: 6,
    marginBottom: 12,
  },
  chip: { marginBottom: 4 },
  rowActions: { flexDirection: "row", alignItems: "center" },
});
