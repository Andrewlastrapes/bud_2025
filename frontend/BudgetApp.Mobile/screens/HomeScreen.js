import React, { useState, useEffect } from 'react';
import { View, StyleSheet } from 'react-native';
import { Text, Button, TextInput, Modal, Portal, Card } from 'react-native-paper';
import { SafeAreaView } from 'react-native-safe-area-context';
import axios from 'axios';
import { auth } from '../firebaseConfig';
import { useIsFocused } from '@react-navigation/native';

const API_BASE_URL = 'http://localhost:5150';

export default function HomeScreen({ navigation }) {
    const [balance, setBalance] = useState(0);
    const [paycheckInput, setPaycheckInput] = useState('');
    const [visible, setVisible] = useState(false); // Controls the modal
    const [isLoading, setIsLoading] = useState(false);
    const isFocused = useIsFocused(); // Refreshes data when tab is opened

    // Helper to get auth headers
    const getAuthHeader = async () => {
        const user = auth.currentUser;
        if (!user) throw new Error("No user logged in.");
        const token = await user.getIdToken();
        return { headers: { 'Authorization': `Bearer ${token}` } };
    };

    // 1. Fetch current balance
    const fetchBalance = async () => {
        try {
            const config = await getAuthHeader();
            const response = await axios.get(`${API_BASE_URL}/api/balance`, config);
            setBalance(response.data.amount);
        } catch (e) {
            console.error("Failed to fetch balance:", e);
        }
    };

    useEffect(() => {
        if (isFocused) fetchBalance();
    }, [isFocused]);

    // 2. Handle setting the new paycheck
    const handleSetPaycheck = async () => {
        setIsLoading(true);
        try {
            const config = await getAuthHeader();
            const amount = parseFloat(paycheckInput);

            await axios.post(`${API_BASE_URL}/api/balance`, { amount }, config);

            // Update UI and close modal
            setBalance(amount);
            setPaycheckInput('');
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
        <SafeAreaView style={styles.container}>
            {/* The Big Dynamic Number */}
            <View style={styles.balanceContainer}>
                <Text style={isOverBudget ? styles.labelOver : styles.label}>
                    {isOverBudget ? 'Over Budget' : 'Dynamic Budget'}
                </Text>

                <Text style={isOverBudget ? styles.amountOver : styles.amount}>
                    {isOverBudget
                        ? `-$${absoluteOver.toFixed(2)}`
                        : `$${balance.toFixed(2)}`}
                </Text>

                {isOverBudget && (
                    <Text style={styles.overBudgetSubtitle}>
                        You&apos;re currently over budget by ${absoluteOver.toFixed(2)}
                        {' '}until your next paycheck.
                    </Text>
                )}
            </View>

            {/* Button to reset/set paycheck */}
            <Button
                mode="contained"
                onPress={() => setVisible(true)}
                style={styles.button}
            >
                Edit Upcoming Paycheck
            </Button>

            {/* Deposit Review entry point */}
            <Button
                mode="outlined"
                onPress={() => navigation.navigate('DepositReview')}
                style={styles.secondaryButton}
            >
                Review New Deposits
            </Button>
            <Button
                mode="outlined"
                style={styles.button}
                onPress={() => navigation.navigate('ReviewLargeExpenses')}
                >
                Review Large Expenses
                </Button>

            {/* Modal for entering amount */}
            <Portal>
                <Modal visible={visible} onDismiss={() => setVisible(false)} contentContainerStyle={styles.modal}>
                    <Card>
                        <Card.Title title="New Paycheck" />
                        <Card.Content>
                            <TextInput
                                label="Amount ($)"
                                value={paycheckInput}
                                onChangeText={setPaycheckInput}
                                keyboardType="numeric"
                                style={{ marginBottom: 15 }}
                            />
                            <Button
                                mode="contained"
                                onPress={handleSetPaycheck}
                                loading={isLoading}
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
    container: {
        flex: 1,
        padding: 20,
        justifyContent: 'center',
    },
    balanceContainer: {
        alignItems: 'center',
        marginBottom: 40,
    },
    label: {
        fontSize: 18,
        color: '#666',
        marginBottom: 10,
    },
    labelOver: {
        fontSize: 18,
        color: '#b00020', // material-ish error red
        marginBottom: 10,
        fontWeight: '600',
    },
    amount: {
        fontSize: 48,
        fontWeight: 'bold',
        color: '#6200ee', // Primary color
    },
    amountOver: {
        fontSize: 48,
        fontWeight: 'bold',
        color: '#b00020',
    },
    overBudgetSubtitle: {
        marginTop: 12,
        fontSize: 14,
        color: '#b00020',
        textAlign: 'center',
        paddingHorizontal: 10,
    },
    button: {
        marginTop: 20,
    },
    secondaryButton: {
        marginTop: 10,
    },
    modal: {
        padding: 20,
    },
});
