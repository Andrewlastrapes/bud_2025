import React, { useState, useEffect } from 'react';
import { View, StyleSheet, ScrollView, Alert, FlatList } from 'react-native';
import { Text, Button, Card, Checkbox, ActivityIndicator, TextInput, List, IconButton, Modal, Portal } from 'react-native-paper';
import { SafeAreaView } from 'react-native-safe-area-context';
import axios from 'axios';
import { auth } from '../../firebaseConfig';
import { useIsFocused } from '@react-navigation/native';

const API_BASE_URL = 'http://localhost:5150';

// --- Utility: Calculate 1st Day of Next Month ---
const getNextMonthFirstDay = () => {
    const today = new Date();
    const date = new Date(today.getFullYear(), today.getMonth() + 1, 1);

    const month = (date.getMonth() + 1).toString().padStart(2, '0');
    const day = date.getDate().toString().padStart(2, '0');
    const year = date.getFullYear();
    return `${month}/${day}/${year}`;
};


// --- Utility: Safely convert date string to ISO string ---
const getISODate = (dateString, fieldName = 'Date') => {
    if (!dateString) return null;
    const date = new Date(dateString);
    if (isNaN(date.getTime())) {
        throw new Error(`Invalid date format for ${fieldName}. Please use MM/DD/YYYY.`);
    }
    return date.toISOString();
};


// --- Helper function to get the auth token ---
const getAuthHeader = async () => {
    const user = auth.currentUser;
    if (!user) throw new Error("No user logged in.");
    const token = await user.getIdToken();
    return { headers: { 'Authorization': `Bearer ${token}` } };
};

export default function FixedCostsSetupScreen({ navigation }) {
    const [isLoading, setIsLoading] = useState(true);
    const [isSaving, setIsSaving] = useState(false);

    // Mandatory Fixed Inputs
    const [manualRent, setManualRent] = useState('');
    const [manualCar, setManualCar] = useState('');
    const [manualStudentLoan, setManualStudentLoan] = useState('');
    const [manualSavingsGoal, setManualSavingsGoal] = useState('');

    // Date Inputs
    const [manualRentDueDate, setManualRentDueDate] = useState('');
    const [manualCarDueDate, setManualCarDueDate] = useState(''); // NEW STATE
    const [manualStudentLoanDueDate, setManualStudentLoanDueDate] = useState(''); // NEW STATE

    // Dynamic List State
    const [otherManualCosts, setOtherManualCosts] = useState([]);
    const [isAddModalVisible, setIsAddModalVisible] = useState(false);
    const [newCostName, setNewCostName] = useState('');
    const [newCostAmount, setNewCostAmount] = useState('');
    const [newCostDueDate, setNewCostDueDate] = useState('');

    // Plaid Discovery State
    const [plaidRecurrings, setPlaidRecurrings] = useState([]);


    // --- Lifecycle: Fetch Plaid Data and Set Initial Date ---
    useEffect(() => {
        // Pre-populate Rent due date
        setManualRentDueDate(getNextMonthFirstDay());
        fetchPlaidData();
    }, []);

    const fetchPlaidData = async () => {
        // ... (API fetching logic remains the same) ...
        try {
            const config = await getAuthHeader();
            const response = await axios.get(`${API_BASE_URL}/api/plaid/recurring`, config);

            const outflow = response.data.outflow_streams || [];

            const initialCosts = outflow.map(stream => ({
                id: stream.stream_id,
                name: stream.description,
                amount: stream.last_amount.amount,
                frequency: stream.frequency,
                nextDueDate: stream.next_projected_date,
                isApproved: stream.confidence_level === 'HIGH' || stream.confidence_level === 'MEDIUM',
            }));

            setPlaidRecurrings(initialCosts);
        } catch (e) {
            console.error("Failed to fetch Plaid recurring data:", e);
        }
        setIsLoading(false);
    };

    // --- Utility: Add Item to Dynamic List ---
    const handleAddNewCost = () => {
        try {
            getISODate(newCostDueDate, newCostName);
        } catch (error) {
            Alert.alert("Invalid Date", error.message);
            return;
        }

        if (!newCostName || !newCostAmount) {
            Alert.alert("Missing Info", "Please enter both a name and an amount.");
            return;
        }

        const newCost = {
            id: Date.now(),
            name: newCostName,
            amount: parseFloat(newCostAmount),
            nextDueDate: newCostDueDate,
        };

        setOtherManualCosts([...otherManualCosts, newCost]);

        // Clear inputs and close modal
        setNewCostName('');
        setNewCostAmount('');
        setNewCostDueDate('');
        setIsAddModalVisible(false);
    };

    // --- Logic for saving all costs (Manual + Plaid) ---
    const handleNext = async () => {
        setIsSaving(true);
        try {
            const config = await getAuthHeader();
            const costsToSave = [];

            // Helper function to safely add a cost to the array
            const addManualCost = (name, amount, dueDate, category = 'Other') => {
                if (amount && parseFloat(amount) > 0) {
                    costsToSave.push({
                        name: name,
                        amount: parseFloat(amount),
                        category: category,
                        type: 'manual',
                        nextDueDate: getISODate(dueDate, name), // SAFE CONVERSION
                    });
                }
            };

            // 1. Mandatory Fixed Costs (All now include dates where necessary)
            addManualCost('Rent/Mortgage', manualRent, manualRentDueDate, 'Housing');
            addManualCost('Car Payment', manualCar, manualCarDueDate, 'Transportation'); // USING NEW DATE STATE
            addManualCost('Student Loan', manualStudentLoan, manualStudentLoanDueDate, 'Loan'); // USING NEW DATE STATE
            addManualCost('Savings Goal', manualSavingsGoal, null, 'Savings'); // Savings goal passes NULL date

            // 2. Dynamic Manual Costs (from the list)
            otherManualCosts.forEach(cost => {
                addManualCost(cost.name, cost.amount, cost.nextDueDate);
            });


            // 3. Save Confirmed Plaid Costs
            plaidRecurrings.forEach(cost => {
                if (cost.isApproved) {
                    costsToSave.push({
                        name: cost.name,
                        amount: cost.amount,
                        category: 'Subscription',
                        type: 'plaid_discovered',
                        plaidMerchantName: cost.name,
                        nextDueDate: getISODate(cost.nextDueDate, cost.name), // SAFE CONVERSION FOR PLAID DATE
                    });
                }
            });

            // --- API Call: Send all costs to the backend (one-by-one) ---
            for (const cost of costsToSave) {
                await axios.post(`${API_BASE_URL}/api/fixed-costs`, cost, config);
            }

            // Success! Move to the next step.
            navigation.navigate('DebtOnboarding');

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
            <Text style={styles.header}>Recurring Costs Setup (3/5)</Text>
            <ScrollView contentContainerStyle={styles.scrollContent}>

                {/* --- A. Mandatory Payments & Savings (Fixed Inputs) --- */}
                <Card style={styles.card}>
                    <Card.Title title="Mandatory Payments & Savings" />
                    <Card.Content>
                        <Text style={styles.subHeader}>
                            These are charges that occur for nearly the same amount at the same time each month. We subtract the total of these costs *upfront*.
                        </Text>

                        {/* RENT: Amount and Date on the same row */}
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
                                label="Next Due Date (MM/DD/YYYY)"
                                value={manualRentDueDate}
                                onChangeText={setManualRentDueDate}
                                style={styles.rowInput}
                            />
                        </View>

                        {/* CAR PAYMENT: Amount and Date on the same row */}
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
                                label="Next Due Date (MM/DD/YYYY)"
                                value={manualCarDueDate}
                                onChangeText={setManualCarDueDate}
                                style={styles.rowInput}
                            />
                        </View>

                        {/* STUDENT LOAN: Amount and Date on the same row */}
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
                                label="Next Due Date (MM/DD/YYYY)"
                                value={manualStudentLoanDueDate}
                                onChangeText={setManualStudentLoanDueDate}
                                style={styles.rowInput}
                            />
                        </View>

                        {/* SAVINGS GOAL: No Date Needed */}
                        <Text style={styles.inputLabel}>Monthly Savings Goal</Text>
                        <TextInput
                            label="Amount ($)"
                            value={manualSavingsGoal}
                            onChangeText={setManualSavingsGoal}
                            keyboardType="numeric"
                            style={styles.input}
                        />

                    </Card.Content>
                </Card>

                {/* --- B. Other Manual Costs (Dynamic List) --- */}
                <Card style={styles.card}>
                    <Card.Title title="Other Recurring Bills" subtitle="Internet, Phone, Gym, etc." />
                    <Card.Content>
                        <FlatList
                            data={otherManualCosts}
                            keyExtractor={(item) => item.id.toString()}
                            renderItem={({ item }) => (
                                <List.Item
                                    title={`${item.name} - $${item.amount.toFixed(2)}`}
                                    description={`Due: ${item.nextDueDate}`}
                                    right={() => (
                                        <IconButton icon="close" onPress={() => handleRemoveCost(item.id)} />
                                    )}
                                />
                            )}
                        />
                        <Button mode="outlined" onPress={() => setIsAddModalVisible(true)} style={{ marginTop: 10 }}>
                            + Add New Recurring Bill
                        </Button>
                    </Card.Content>
                </Card>


                {/* --- C. Plaid Discovery (Same as before) --- */}
                <Card style={styles.card}>
                    <Card.Title title="Plaid Suggestions" subtitle="Confirm the fixed costs we found in your history." />
                    <Card.Content>
                        {plaidRecurrings.length === 0 ? (
                            <Text style={styles.infoText}>No recurring subscriptions detected yet.</Text>
                        ) : (
                            <List.Section>
                                {plaidRecurrings.map((cost) => (
                                    <List.Item
                                        key={cost.id}
                                        title={cost.name}
                                        description={`$${cost.amount.toFixed(2)} | Due: ${cost.nextDueDate} | ${cost.frequency}`}
                                        right={() => (
                                            <Checkbox.Android
                                                status={cost.isApproved ? 'checked' : 'unchecked'}
                                                onPress={() => {
                                                    setPlaidRecurrings(prev =>
                                                        prev.map(c =>
                                                            c.id === cost.id ? { ...c, isApproved: !c.isApproved } : c
                                                        )
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

            <Button mode="contained" onPress={handleNext} loading={isSaving} style={styles.bottomButton}>
                Next: Finalize Paycheck
            </Button>

            {/* --- Modal to Add Dynamic Recurring Costs --- */}
            <Portal>
                <Modal visible={isAddModalVisible} onDismiss={() => setIsAddModalVisible(false)} contentContainerStyle={styles.modal}>
                    <Card>
                        <Card.Title title="Add New Recurring Cost" />
                        <Card.Content>
                            <TextInput
                                label="Bill Name (e.g., Internet, Gym)"
                                value={newCostName}
                                onChangeText={setNewCostName}
                                style={styles.input}
                            />
                            <TextInput
                                label="Amount ($)"
                                value={newCostAmount}
                                onChangeText={setNewCostAmount}
                                keyboardType="numeric"
                                style={styles.input}
                            />
                            <TextInput
                                label="Next Due Date (MM/DD/YYYY)"
                                value={newCostDueDate}
                                onChangeText={setNewCostDueDate}
                                style={styles.input}
                            />
                            <Button mode="contained" onPress={handleAddNewCost}>Add Bill</Button>
                        </Card.Content>
                    </Card>
                </Modal>
            </Portal>

        </SafeAreaView>
    );
}

const styles = StyleSheet.create({
    container: { flex: 1, backgroundColor: '#f0f0f0' },
    loadingContainer: { flex: 1, justifyContent: 'center', alignItems: 'center' },
    header: { fontSize: 24, fontWeight: 'bold', textAlign: 'center', marginVertical: 15 },
    scrollContent: { paddingBottom: 100 },
    card: { marginHorizontal: 15, marginVertical: 10 },
    input: { marginBottom: 10 },
    row: { flexDirection: 'row', justifyContent: 'space-between', marginBottom: 10 },
    rowInput: { width: '48%' },
    inputLabel: { fontSize: 16, marginTop: 15, marginBottom: 5, fontWeight: '500' },
    subHeader: { fontSize: 14, textAlign: 'center', marginBottom: 10, color: '#333', lineHeight: 20 },
    infoText: { fontSize: 13, color: '#666', padding: 5, textAlign: 'center' },
    bottomButton: { margin: 20 },
    modal: { backgroundColor: 'white', padding: 20, margin: 20, borderRadius: 8 },
});