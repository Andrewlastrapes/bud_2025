import React, { useState } from 'react';
import { View, StyleSheet, Alert } from 'react-native';
import { Text, TextInput, Button, ActivityIndicator } from 'react-native-paper';
import { SafeAreaView } from 'react-native-safe-area-context';
import axios from 'axios';
import { auth } from '../../firebaseConfig';

import { API_BASE_URL } from '@/config/api';
;

export default function PaycheckSavingsScreen({ navigation }) {
    const [paycheckAmount, setPaycheckAmount] = useState('');
    const [payDay1, setPayDay1] = useState('1');
    const [payDay2, setPayDay2] = useState('15');
    const [isSaving, setIsSaving] = useState(false);

    // Helper to get auth headers
    const getAuthHeader = async () => {
        const user = auth.currentUser;
        if (!user) throw new Error("No user logged in.");
        const token = await user.getIdToken();
        return { headers: { 'Authorization': `Bearer ${token}` } };
    };

    // --- Calculation to find the next paycheck date based on the two fixed days ---
    const calculateNextPaycheckDate = (day1, day2) => {
        const today = new Date();
        const currentMonth = today.getMonth();
        const currentYear = today.getFullYear();
        const days = [day1, day2].sort((a, b) => a - b);

        let nextDate = null;
        let nextMonth = currentMonth;
        let nextYear = currentYear;

        // 1. Check for paychecks remaining this month
        for (const day of days) {
            const potentialDate = new Date(currentYear, currentMonth, day);
            // Check if the date is today or in the future
            if (potentialDate.getDate() === day && potentialDate >= today) {
                if (!nextDate || potentialDate < nextDate) {
                    nextDate = potentialDate;
                }
            }
        }

        // 2. If no paychecks remain this month, calculate the first one next month
        if (!nextDate) {
            nextMonth += 1;
            if (nextMonth > 11) {
                nextMonth = 0;
                nextYear += 1;
            }
            // The next paycheck will be the first of the two days in the next cycle
            nextDate = new Date(nextYear, nextMonth, days[0]);
        }

        return nextDate;
    };
    // ----------------------------------------------------------------------------------

    const handleFinalize = async () => {
        setIsSaving(true);

        const amount = parseFloat(paycheckAmount);
        const day1 = parseInt(payDay1);
        const day2 = parseInt(payDay2);

        // Validation check for amounts and valid days
        if (isNaN(amount) || amount <= 0) {
            Alert.alert("Missing Info", "Please enter a valid paycheck amount.");
            setIsSaving(false);
            return;
        }

        if (isNaN(day1) || day1 < 1 || day1 > 31 || isNaN(day2) || day2 < 1 || day2 > 31) {
            Alert.alert("Invalid Pay Day", "Please enter valid days of the month (1-31).");
            setIsSaving(false);
            return;
        }

        try {
            const config = await getAuthHeader();

            // CALCULATE THE DATE: The next paycheck date is now determined by the logic above
            const nextPaycheckDate = calculateNextPaycheckDate(day1, day2);

            // Finalize backend call
            const response = await axios.post(`${API_BASE_URL}/api/budget/finalize`, {
                paycheckAmount: amount,
                nextPaycheckDate: nextPaycheckDate, // Send the calculated date
                payDay1: day1,
                payDay2: day2,
            }, config);

            navigation.navigate('DynamicAmountFinal', {
                finalAmount: response.data.dynamicBalance,
                prorateFactor: response.data.prorateFactor
            });

        } catch (e) {
            console.error("Finalization failed:", e);
            Alert.alert("Error Finalizing Budget", e.message || "An unknown API error occurred.");
        }
        setIsSaving(false);
    };

    return (
        <SafeAreaView style={styles.container}>
            <Text style={styles.header}>Paycheck & Timeline (4/5)</Text>

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
            <Text style={styles.info}>
                We use these two days to calculate the entire spending cycle.
            </Text>

            <Button mode="contained" onPress={handleFinalize} loading={isSaving} style={styles.button}>
                Finalize My Dynamic Budget
            </Button>
        </SafeAreaView>
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
});