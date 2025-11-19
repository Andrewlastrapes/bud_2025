// File: screens/onboarding/PlaidConnectScreen.js

import React, { useState, useEffect } from 'react';
import { View, StyleSheet, ActivityIndicator } from 'react-native';
import { Text, Button } from 'react-native-paper';
import { SafeAreaView } from 'react-native-safe-area-context';
import axios from 'axios';
import { usePlaidLink } from 'react-plaid-link';
import { auth } from '../../firebaseConfig';

const API_BASE_URL = 'http://localhost:5150';

export default function PlaidConnectScreen({ navigation }) {
    const [linkToken, setLinkToken] = useState(null);
    const [isPlaidLoading, setIsPlaidLoading] = useState(false); // Renamed state for clarity
    const [error, setError] = useState(null);

    // --- Helper to get headers for API calls ---
    const getAuthHeader = async () => {
        const user = auth.currentUser;
        if (!user) throw new Error("No user logged in.");
        const token = await user.getIdToken();
        return { headers: { 'Authorization': `Bearer ${token}` } };
    };

    // 1. Called when Plaid Link is successful
    const onPlaidSuccess = React.useCallback(async (public_token, metadata) => {
        setError(null);
        setIsPlaidLoading(true); // Show spinner while exchanging token
        try {
            const user = auth.currentUser;
            if (!user) throw new Error("Authentication error.");

            // Exchange public token for permanent access token in the backend
            await axios.post(`${API_BASE_URL}/api/plaid/exchange_public_token`, {
                publicToken: public_token,
                firebaseUuid: user.uid
            }, await getAuthHeader());

            // Success! Move to the next setup step.
            navigation.navigate('FixedCostsSetup');

        } catch (e) {
            console.error('Error exchanging token:', e.message);
            setError("Failed to save accounts. Please try again.");
        }
        setIsPlaidLoading(false);
    }, [navigation]);

    // 2. Conditional Plaid Hook (Only initialized when we have a linkToken)
    const PlaidLinkComponent = () => {
        const { open } = usePlaidLink({
            token: linkToken,
            onSuccess: onPlaidSuccess,
            onExit: (exit) => {
                console.log('Plaid Link exited:', exit);
                setLinkToken(null); // Allow user to try again
            },
        });

        // This useEffect runs once the hook is ready with the token
        useEffect(() => {
            if (linkToken) {
                open();
                setLinkToken(null);
            }
        }, [open, linkToken]);

        // This component is invisible but necessary to mount the hook
        return null;
    };

    // 3. Fetches the link_token from your .NET API
    const createLinkToken = async () => {
        setIsPlaidLoading(true); // Use isPlaidLoading to control the button spinner
        setError(null);
        try {
            const user = auth.currentUser;
            if (!user) throw new Error("Please log in again.");

            const response = await axios.post(`${API_BASE_URL}/api/plaid/create_link_token`, {
                firebaseUserId: user.uid
            }, await getAuthHeader());

            setLinkToken(response.data.linkToken);
            console.log("Link token received. Opening Plaid modal...");

        } catch (e) {
            console.error('Error creating link token:', e.message);
            setError("Could not generate link token. Is your .NET API running?");
            setIsPlaidLoading(false); // Stop spinner on error
        }
    };

    // 4. Component renders
    return (
        <SafeAreaView style={styles.container}>

            {/* 🛑 FIX: Render the Plaid hook component ONLY if linkToken is present */}
            {linkToken && <PlaidLinkComponent />}

            <Text style={styles.header}>Connect Your Bank Accounts (2/5)</Text>
            <Text style={styles.subtext}>
                Please connect all credit cards, debit cards, and your primary checking account so we can accurately track your spending.
            </Text>

            <Button
                mode="contained"
                onPress={createLinkToken}
                disabled={!auth.currentUser || isPlaidLoading}
                style={styles.button}
            >
                {/* Text changes based on our local state variable */}
                {isPlaidLoading ? 'Fetching Token...' : 'Connect Now'}
            </Button>

            {error && <Text style={styles.errorText}>{error}</Text>}

            <Button mode="text" onPress={() => navigation.goBack()} style={styles.backButton}>
                Go Back
            </Button>
        </SafeAreaView>
    );
}

const styles = StyleSheet.create({
    container: { flex: 1, padding: 30, justifyContent: 'space-between' },
    loadingContainer: { flex: 1, justifyContent: 'center', alignItems: 'center' },
    header: { fontSize: 24, fontWeight: 'bold', textAlign: 'center', marginBottom: 15 },
    subtext: { fontSize: 16, textAlign: 'center', marginBottom: 30, color: '#666' },
    button: { marginTop: 15 },
    errorText: { color: 'red', textAlign: 'center', marginTop: 10 },
    loadingText: { marginTop: 10 },
    backButton: { marginTop: 10, color: '#333' },
});