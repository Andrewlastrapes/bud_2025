import React from 'react';
import { View, StyleSheet } from 'react-native';
import { Text, Button } from 'react-native-paper';
import { SafeAreaView } from 'react-native-safe-area-context';

// NOTE: We will pass the final calculated balance as a prop later
export default function DynamicAmountFinalScreen({ navigation, route }) {
    const { initialAmount, savings } = route.params || {};

    const handleFinish = () => {
        // TODO: This is where we call the backend to flip the 'onboarding_complete' flag to TRUE
        navigation.popToTop(); // Go back to the main app stack
    };

    // Placeholder calculation based on passed props
    const displayedDynamicAmount = initialAmount
        ? (parseFloat(initialAmount) - parseFloat(savings || 0)).toFixed(2)
        : "N/A";

    return (
        <SafeAreaView style={styles.container}>
            <View style={styles.content}>
                <Text style={styles.header}>Your Dynamic Budget is Ready! (5/5)</Text>

                <Text style={styles.label}>You have set your initial spending budget to:</Text>

                <Text style={styles.amount}>
                    ${displayedDynamicAmount}
                </Text>

                <Text style={styles.explanation}>
                    This amount reflects your total paycheck minus your planned savings and estimated fixed costs. We will now subtract your variable spending in real-time.
                </Text>
            </View>

            <Button mode="contained" onPress={handleFinish} style={styles.button}>
                I Understand / Finish Setup
            </Button>
        </SafeAreaView>
    );
}

const styles = StyleSheet.create({
    container: { flex: 1, padding: 30, justifyContent: 'space-between' },
    content: { alignItems: 'center', flex: 1, justifyContent: 'center' },
    header: { fontSize: 24, fontWeight: 'bold', textAlign: 'center', marginBottom: 40 },
    label: { fontSize: 18, color: '#666', marginBottom: 10 },
    amount: { fontSize: 52, fontWeight: 'bold', color: '#6200ee', marginBottom: 20 },
    explanation: { fontSize: 14, textAlign: 'center', color: '#333' },
    button: { marginBottom: 10 },
});