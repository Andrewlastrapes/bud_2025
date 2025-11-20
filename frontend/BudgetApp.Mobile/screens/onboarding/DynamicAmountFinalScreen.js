import React from 'react';
import { View, StyleSheet } from 'react-native';
import { Text, Button } from 'react-native-paper';
import { SafeAreaView } from 'react-native-safe-area-context';
import axios from 'axios';
import { auth } from '../../firebaseConfig';

const API_BASE_URL = 'http://localhost:5150';

export default function DynamicAmountFinalScreen({ navigation, route }) {
    // FIX: Destructure the correct props: finalAmount and prorateFactor
    const { finalAmount, prorateFactor } = route.params || {};

    const handleFinish = async () => {
        // The backend API /finalize already flipped the 'onboarding_complete' flag to TRUE, 
        // so we just need to navigate away.
        navigation.popToTop(); // Go back to the main app stack
    };

    // Use the correctly passed amount for display
    const displayedAmount = finalAmount || '0.00';

    return (
        <SafeAreaView style={styles.container}>
            <View style={styles.content}>
                <Text style={styles.header}>Your Dynamic Budget is Ready! (5/5)</Text>

                <Text style={styles.label}>Your current spending budget until your next check is:</Text>

                <Text style={styles.amount}>
                    ${displayedAmount}
                </Text>

                <Text style={styles.explanation}>
                    This amount is your paycheck minus all recurring costs, prorated by {prorateFactor || '0.00'}x to reflect the remaining days in your current cycle.
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