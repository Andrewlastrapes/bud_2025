// File: screens/onboarding/DynamicAmountFinalScreen.js

import React, { useState } from 'react';
import { View, StyleSheet, Alert, ActivityIndicator } from 'react-native';
import { Text, Button } from 'react-native-paper';
import { SafeAreaView } from 'react-native-safe-area-context';
import axios from 'axios';
import { StackActions } from '@react-navigation/native';

import { auth } from '../../firebaseConfig';

import { API_BASE_URL } from '@/config/api';
;

export default function DynamicAmountFinalScreen({ navigation, route }) {
    const { finalAmount, prorateFactor } = route.params || {};
    const [isChecking, setIsChecking] = useState(false);

    // Helper to get auth headers
    const getAuthHeader = async () => {
        const user = auth.currentUser;
        if (!user) throw new Error('No user logged in.');
        const token = await user.getIdToken();
        return { headers: { Authorization: `Bearer ${token}` } };
    };

    const handleFinish = async () => {
        setIsChecking(true);

        try {
            const config = await getAuthHeader();
            let profile;
            let retryCount = 0;

            // Poll backend until onboardingComplete is true (max 5 retries)
            do {
                if (retryCount > 0) {
                    await new Promise((resolve) => setTimeout(resolve, 500));
                }

                const response = await axios.get(
                    `${API_BASE_URL}/api/users/profile`,
                    config,
                );
                profile = response.data;

                console.log('profile', profile);

                if (profile.onboardingComplete) {
                    // ✅ Use parent navigator, which knows about "App"
                    const parentNav = navigation.getParent();

                    if (parentNav) {
                        parentNav.dispatch(StackActions.replace('App'));
                    } else {
                        // Fallback if there is no parent for some reason
                        navigation.navigate('App');
                    }

                    return;
                }

                retryCount++;
            } while (retryCount < 5);

            // If we hit retry limit
            Alert.alert(
                'Setup Error',
                'Failed to confirm setup status. Please log out and log back in.',
            );
        } catch (e) {
            console.error('Error verifying final setup status', e);
            Alert.alert(
                'Network Error',
                'Could not verify final setup status. Please try again.',
            );
        } finally {
            setIsChecking(false);
        }
    };

    const displayedAmount = finalAmount || '0.00';

    if (isChecking) {
        return (
            <View style={styles.loadingContainer}>
                <ActivityIndicator size="large" />
                <Text>Finalizing your setup...</Text>
            </View>
        );
    }

    return (
        <SafeAreaView style={styles.container}>
            <View style={styles.content}>
                <Text style={styles.header}>Your Dynamic Budget is Ready! (5/5)</Text>

                <Text style={styles.label}>
                    Your current spending budget until your next check is:
                </Text>

                <Text style={styles.amount}>${displayedAmount}</Text>

                <Text style={styles.explanation}>
                    This amount is your paycheck minus all recurring costs, prorated by{' '}
                    {prorateFactor || '0.00'}x to reflect the remaining days in your
                    current cycle.
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
    header: {
        fontSize: 24,
        fontWeight: 'bold',
        textAlign: 'center',
        marginBottom: 40,
    },
    label: { fontSize: 18, color: '#666', marginBottom: 10 },
    amount: {
        fontSize: 52,
        fontWeight: 'bold',
        color: '#6200ee',
        marginBottom: 20,
    },
    explanation: { fontSize: 14, textAlign: 'center', color: '#333' },
    button: { marginBottom: 10 },
    loadingContainer: { flex: 1, justifyContent: 'center', alignItems: 'center' },
});
