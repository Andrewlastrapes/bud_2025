import React, { useState } from 'react';
import { View, StyleSheet } from 'react-native';
import { Text, TextInput, Button } from 'react-native-paper';
import { SafeAreaView } from 'react-native-safe-area-context';

export default function PaycheckSavingsScreen({ navigation }) {
    const [paycheckAmount, setPaycheckAmount] = useState('');
    const [payDate, setPayDate] = useState('15th & 30th'); // Placeholder for date input
    const [savingsGoal, setSavingsGoal] = useState('');

    const handleNext = () => {
        // NOTE: In the next step, we will call an API here to save these values 
        // and calculate the prorated amount. For now, we navigate.
        navigation.navigate('DynamicAmountFinal', {
            // Pass data to the final screen to display
            initialAmount: paycheckAmount,
            payDate: payDate,
            savings: savingsGoal
        });
    };

    return (
        <SafeAreaView style={styles.container}>
            <Text style={styles.header}>Paycheck & Savings Setup (4/5)</Text>

            <Text style={styles.label}>1. What is your typical paycheck amount?</Text>
            <TextInput
                label="Paycheck Amount ($)"
                value={paycheckAmount}
                onChangeText={setPaycheckAmount}
                keyboardType="numeric"
                style={styles.input}
            />

            <Text style={styles.label}>2. How much do you want to save per cycle?</Text>
            <TextInput
                label="Savings Goal ($)"
                value={savingsGoal}
                onChangeText={setSavingsGoal}
                keyboardType="numeric"
                style={styles.input}
            />

            <Text style={styles.info}>
                *We use these values to determine your starting dynamic amount.*
            </Text>

            <Button mode="contained" onPress={handleNext} style={styles.button}>
                Calculate My Budget
            </Button>
        </SafeAreaView>
    );
}

const styles = StyleSheet.create({
    container: { flex: 1, padding: 30, justifyContent: 'space-around' },
    header: { fontSize: 22, fontWeight: 'bold', textAlign: 'center', marginBottom: 20 },
    label: { fontSize: 16, marginTop: 15, marginBottom: 5, fontWeight: '500' },
    input: { marginBottom: 15 },
    info: { fontSize: 12, color: '#666', textAlign: 'center', marginTop: 20 },
    button: { marginTop: 30 },
});