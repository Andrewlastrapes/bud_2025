import React, { useState, useEffect } from 'react';
import { View, StyleSheet, ScrollView } from 'react-native';
import { Text, Button, Card, Checkbox, ActivityIndicator, TextInput, List } from 'react-native-paper';
import { SafeAreaView } from 'react-native-safe-area-context';
import axios from 'axios';
import { auth } from '../../firebaseConfig';

const API_BASE_URL = 'http://localhost:5150';

export default function FixedCostsSetupScreen({ navigation }) {
    const [isLoading, setIsLoading] = useState(true);
    const [plaidRecurrings, setPlaidRecurrings] = useState([]);
    const [manualRent, setManualRent] = useState('');
    const [manualCar, setManualCar] = useState('');
    const [confirmedCosts, setConfirmedCosts] = useState({}); // To track which Plaid costs are approved

    // --- Helper function to get the auth token ---
    const getAuthHeader = async () => {
        const user = auth.currentUser;
        if (!user) throw new Error("No user logged in.");
        const token = await user.getIdToken();
        return { headers: { 'Authorization': `Bearer ${token}` } };
    };

    // --- Fetch Plaid Recurring Data ---
    const fetchPlaidData = async () => {
        try {
            const config = await getAuthHeader();
            // Call the new backend endpoint
            const response = await axios.get(`${API_BASE_URL}/api/plaid/recurring`, config);

            // Only consider outflow streams (expenses) for fixed costs
            const outflow = response.data.outflow_streams || [];

            // Map the Plaid data for the UI
            const initialCosts = outflow.map(stream => ({
                id: stream.stream_id,
                name: stream.description,
                amount: stream.last_amount.amount,
                frequency: stream.frequency,
                // Automatically check if Plaid has high confidence
                isApproved: stream.confidence_level === 'HIGH' || stream.confidence_level === 'MEDIUM',
            }));

            setPlaidRecurrings(initialCosts);

        } catch (e) {
            console.error("Failed to fetch Plaid recurring data:", e);
        }
        setIsLoading(false);
    };

    useEffect(() => {
        fetchPlaidData();
    }, []);

    // --- Logic for saving all costs (Manual + Plaid) ---
    const handleNext = async () => {
        setIsLoading(true);
        try {
            const config = await getAuthHeader();
            const costsToSave = [];

            // 1. Save Manual Costs (Rent/Car)
            if (manualRent && parseFloat(manualRent) > 0) {
                costsToSave.push({ name: 'Rent/Mortgage', amount: parseFloat(manualRent), category: 'Housing', type: 'manual' });
            }
            if (manualCar && parseFloat(manualCar) > 0) {
                costsToSave.push({ name: 'Car Payment', amount: parseFloat(manualCar), category: 'Transportation', type: 'manual' });
            }

            // 2. Save Confirmed Plaid Costs
            plaidRecurrings.forEach(cost => {
                // If user approves OR if we auto-approved it and they didn't uncheck
                if (cost.isApproved) {
                    costsToSave.push({
                        name: cost.name,
                        amount: cost.amount,
                        category: 'Subscription', // Placeholder
                        type: 'plaid_discovered',
                        plaidMerchantName: cost.name // Use name for merchant match
                    });
                }
            });

            // --- API Call: Send all costs to the backend (Need a bulk endpoint eventually, but one-by-one for now) ---
            for (const cost of costsToSave) {
                await axios.post(`${API_BASE_URL}/api/fixed-costs`, cost, config);
            }

            // Success! Move to the next step.
            navigation.navigate('PaycheckSavings');

        } catch (e) {
            console.error("Failed to save costs:", e);
            alert("Error saving fixed costs. Please check your inputs.");
        }
        setIsLoading(false);
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
            <Text style={styles.header}>Fixed Costs Setup (3/5)</Text>
            <ScrollView contentContainerStyle={styles.scrollContent}>

                {/* --- A. Manual Entry (For Rent/Mortgage, etc.) --- */}
                <Card style={styles.card}>
                    <Card.Title title="Manual Costs (Rent, Loans)" />
                    <Card.Content>
                        <Text style={styles.subHeader}>We can't always find these. Please enter your known major payments.</Text>

                        <TextInput
                            label="Rent or Mortgage Payment ($)"
                            value={manualRent}
                            onChangeText={setManualRent}
                            keyboardType="numeric"
                            style={styles.input}
                        />
                        <TextInput
                            label="Car Payment / Loan ($)"
                            value={manualCar}
                            onChangeText={setManualCar}
                            keyboardType="numeric"
                            style={styles.input}
                        />
                    </Card.Content>
                </Card>

                {/* --- B. Plaid Discovery --- */}
                <Card style={styles.card}>
                    <Card.Title title="Plaid Suggestions" subtitle="Confirm the fixed costs we found in your history." />
                    <Card.Content>
                        {plaidRecurrings.length === 0 ? (
                            <Text style={styles.infoText}>No recurring subscriptions detected yet (Plaid needs more history).</Text>
                        ) : (
                            <List.Section>
                                {plaidRecurrings.map((cost) => (
                                    <List.Item
                                        key={cost.id}
                                        title={cost.name}
                                        description={`$${cost.amount} | ${cost.frequency}`}
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

            <Button mode="contained" onPress={handleNext} style={styles.bottomButton}>
                Next: Paycheck & Savings
            </Button>

        </SafeAreaView>
    );
}

const styles = StyleSheet.create({
    container: { flex: 1, backgroundColor: '#f0f0f0' },
    loadingContainer: { flex: 1, justifyContent: 'center', alignItems: 'center' },
    header: { fontSize: 24, fontWeight: 'bold', textAlign: 'center', marginVertical: 15 },
    subHeader: { fontSize: 16, marginBottom: 10, textAlign: 'center', color: '#333' },
    scrollContent: { paddingBottom: 100 },
    card: { marginHorizontal: 15, marginVertical: 10 },
    input: { marginBottom: 10 },
    infoText: { padding: 10, color: '#999' },
    bottomButton: { margin: 20 },
});