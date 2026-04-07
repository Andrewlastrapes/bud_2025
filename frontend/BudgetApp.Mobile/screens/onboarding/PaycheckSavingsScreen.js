import React, { useState } from 'react';
import { View, StyleSheet, Alert, Keyboard, TouchableWithoutFeedback } from 'react-native';
import { Text, TextInput, Button, ActivityIndicator } from 'react-native-paper';
import { SafeAreaView } from 'react-native-safe-area-context';
import axios from 'axios';
import { auth } from '../../firebaseConfig';

import { API_BASE_URL } from '../../config/api';

export default function PaycheckSavingsScreen({ navigation, route }) {
    const [paycheckAmount, setPaycheckAmount] = useState('');
    const [payDay1, setPayDay1] = useState('1');
    const [payDay2, setPayDay2] = useState('15');
    const [isSaving, setIsSaving] = useState(false);

    // Debt amount forwarded from DebtOnboardingScreen (may be null if user skipped)
    const debtPerPaycheck = route.params?.debtPerPaycheck ?? null;

    // Helper to get auth headers
    const getAuthHeader = async () => {
        const user = auth.currentUser;
        if (!user) throw new Error('No user logged in.');
        const token = await user.getIdToken();
        return { headers: { Authorization: `Bearer ${token}` } };
    };

    // Calculate the next paycheck date from two fixed pay days
    const calculateNextPaycheckDate = (day1, day2) => {
        const today = new Date();
        const currentMonth = today.getMonth();
        const currentYear = today.getFullYear();
        const days = [day1, day2].sort((a, b) => a - b);

        let nextDate = null;
        let nextMonth = currentMonth;
        let nextYear = currentYear;

        // Check for paychecks remaining this month
        for (const day of days) {
            const potentialDate = new Date(currentYear, currentMonth, day);
            if (potentialDate.getDate() === day && potentialDate >= today) {
                if (!nextDate || potentialDate < nextDate) {
                    nextDate = potentialDate;
                }
            }
        }

        // If no paychecks remain this month, use first pay day next month
        if (!nextDate) {
            nextMonth += 1;
            if (nextMonth > 11) {
                nextMonth = 0;
                nextYear += 1;
            }
            nextDate = new Date(nextYear, nextMonth, days[0]);
        }

        return nextDate;
    };

    const handleFinalize = async () => {
        setIsSaving(true);

        const amount = parseFloat(paycheckAmount);
        const day1 = parseInt(payDay1);
        const day2 = parseInt(payDay2);

        if (isNaN(amount) || amount <= 0) {
            Alert.alert('Missing Info', 'Please enter a valid paycheck amount.');
            setIsSaving(false);
            return;
        }

        if (isNaN(day1) || day1 < 1 || day1 > 31 || isNaN(day2) || day2 < 1 || day2 > 31) {
            Alert.alert('Invalid Pay Day', 'Please enter valid days of the month (1-31).');
            setIsSaving(false);
            return;
        }

        try {
            const config = await getAuthHeader();
            const nextPaycheckDate = calculateNextPaycheckDate(day1, day2);

            const response = await axios.post(
                `${API_BASE_URL}/api/budget/finalize`,
                {
                    paycheckAmount: amount,
                    nextPaycheckDate: nextPaycheckDate,
                    payDay1: day1,
                    payDay2: day2,
                    // Forward the debt amount collected in DebtOnboardingScreen.
                    // null / undefined means no debt payoff allocated this period.
                    debtPerPaycheck: debtPerPaycheck,
                },
                config,
            );

            const data = response.data;

            // Navigate to the final screen with the full breakdown
            navigation.navigate('DynamicAmountFinal', {
                // Core display values
                dynamicSpendableAmount: data.dynamicSpendableAmount,
                finalAmount: data.dynamicBalance, // legacy field for backwards compat

                // Full breakdown
                paycheckAmount:      data.paycheckAmount,
                totalRecurringCosts: data.totalRecurringCosts,
                debtPerPaycheck:     data.debtPerPaycheck,
                savingsContribution: data.savingsContribution,
                effectivePaycheck:   data.effectivePaycheck,
                prorateFactor:       data.prorateFactor,

                // Human-readable explanation from the engine
                explanation: data.explanation,
            });
        } catch (e) {
            console.error('Finalization failed:', e);
            Alert.alert('Error Finalizing Budget', e.message || 'An unknown API error occurred.');
        }

        setIsSaving(false);
    };

    return (
        <TouchableWithoutFeedback onPress={Keyboard.dismiss} accessible={false}>
            <SafeAreaView style={styles.container}>
                <Text style={styles.header}>Paycheck &amp; Timeline (4/5)</Text>

                <Text style={styles.label}>1. What is your NET (take-home) paycheck amount?</Text>
                <TextInput
                    label="Paycheck Amount ($)"
                    value={paycheckAmount}
                    onChangeText={setPaycheckAmount}
                    keyboardType="numeric"
                    style={styles.input}
                    disabled={isSaving}
                />

                <Text style={styles.label}>2. What are your two monthly pay days?</Text>
                <View style={styles.row}>
                    <TextInput
                        label="Day 1 (e.g., 1st)"
                        value={payDay1}
                        onChangeText={setPayDay1}
                        keyboardType="numeric"
                        style={styles.rowInput}
                        disabled={isSaving}
                    />
                    <TextInput
                        label="Day 2 (e.g., 15th)"
                        value={payDay2}
                        onChangeText={setPayDay2}
                        keyboardType="numeric"
                        style={styles.rowInput}
                        disabled={isSaving}
                    />
                </View>

                {debtPerPaycheck != null && (
                    <Text style={styles.debtNote}>
                        Debt payoff included: ${parseFloat(debtPerPaycheck).toFixed(2)} / paycheck
                    </Text>
                )}

                <Text style={styles.info}>
                    We use these two days to calculate the entire spending cycle.
                </Text>

                <Button
                    mode="contained"
                    onPress={handleFinalize}
                    loading={isSaving}
                    style={styles.button}
                >
                    Finalize My Dynamic Budget
                </Button>
            </SafeAreaView>
        </TouchableWithoutFeedback>
    );
}

const styles = StyleSheet.create({
    container: { flex: 1, padding: 30, justifyContent: 'center' },
    header: { fontSize: 24, fontWeight: 'bold', textAlign: 'center', marginBottom: 40 },
    label: { fontSize: 16, marginTop: 15, marginBottom: 5, fontWeight: '500' },
    input: { marginBottom: 15 },
    row: { flexDirection: 'row', justifyContent: 'space-between', marginBottom: 15 },
    rowInput: { width: '48%' },
    button: { marginTop: 30 },
    info: { fontSize: 12, color: '#666', textAlign: 'center', marginTop: 0 },
    debtNote: { fontSize: 13, color: '#6200ee', textAlign: 'center', marginTop: 4, marginBottom: 4 },
});