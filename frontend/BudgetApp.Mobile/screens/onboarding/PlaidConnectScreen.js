import React, { useState, useEffect } from 'react';
import { View, StyleSheet, ActivityIndicator, Text as RNText } from 'react-native';
import { Text, Button } from 'react-native-paper';
import { SafeAreaView } from 'react-native-safe-area-context';
import axios from 'axios';
import { usePlaidLink } from 'react-plaid-link';
import { auth } from '../../firebaseConfig';

const API_BASE_URL = 'http://localhost:5150';

// --- Helper to get headers for API calls ---
const getAuthHeader = async () => {
    const user = auth.currentUser;
    if (!user) throw new Error("No user logged in.");
    const token = await user.getIdToken();
    return { headers: { 'Authorization': `Bearer ${token}` } };
};

export default function PlaidConnectScreen({ navigation }) {
    const [linkToken, setLinkToken] = useState(null);
    const [isFetchingToken, setIsFetchingToken] = useState(false); // Controls API call spinner
    const [error, setError] = useState(null);
    const [numAccountsConnected, setNumAccountsConnected] = useState(0); // Tracks connected accounts

    // 1. Called when Plaid Link is successful
    const onPlaidSuccess = React.useCallback(async (public_token, metadata) => {
        setError(null);
        setIsFetchingToken(true);
        try {
            const user = auth.currentUser;
            if (!user) throw new Error("Authentication error.");

            // Exchange public token for permanent access token in the backend
            await axios.post(`${API_BASE_URL}/api/plaid/exchange_public_token`, {
                publicToken: public_token,
                firebaseUuid: user.uid
            }, await getAuthHeader());

            // Success: Update the counter and show the choice screen
            setNumAccountsConnected(prev => prev + 1);
            setLinkToken(null);

        } catch (e) {
            console.error('Error exchanging token:', e.message);
            setError("Failed to save accounts. Please try again.");
        }
        setIsFetchingToken(false); // Unfreeze button regardless of success/fail
    }, [navigation]);

    // 2. Initialize Plaid Hook
    const { open } = usePlaidLink({
        token: linkToken,
        onSuccess: onPlaidSuccess,
        onExit: (exit) => {
            console.log('Plaid Link exited:', exit);
            setLinkToken(null);
            setIsFetchingToken(false); // Unfreeze button on exit
        },
    });

    // 3. Fetches the link_token from your .NET API (Click 1)
    // FILE: PlaidConnectScreen.js
    const createLinkToken = async () => {
        setIsFetchingToken(true);
        setError(null);
        try {
            const user = auth.currentUser;
            if (!user) throw new Error("Please log in again.");

            const response = await axios.post(
                `${API_BASE_URL}/api/plaid/create_link_token`,
                { firebaseUserId: user.uid },
                await getAuthHeader()
            );

            setLinkToken(response.data.linkToken);
            console.log("Token Fetched. Waiting for second click to open modal.");
        } catch (e) {
            console.error('Error creating link token:', e.message);
            setError("Could not generate link token. Is your .NET API running?");
        } finally {
            // IMPORTANT: always clear spinner
            setIsFetchingToken(false);
        }
    };


    // 4. Component renders
    // --- Post-Connect Navigation Component ---
    // FILE: PlaidConnectScreen.js
    const PostConnectContent = () => (
        <View style={styles.postConnectBox}>
            <Text style={styles.successHeader}>
                ✅ Success! {numAccountsConnected} Account(s) Connected
            </Text>
            <RNText style={styles.mainInstruction}>
                Do you have any other cards or checking accounts you use for spending?
            </RNText>

            {/* Same state machine as PreConnectContent */}
            {isFetchingToken && !linkToken ? (
                // State 1: fetching new link token
                <View style={styles.loadingPlaceholder}>
                    <ActivityIndicator size="small" />
                    <Text style={styles.loadingText}>Fetching Token...</Text>
                </View>
            ) : linkToken ? (
                // State 2: link token ready, now user must click to open Plaid
                <Button
                    mode="contained"
                    onPress={open}
                    style={styles.postConnectButton}
                >
                    Open Plaid Link
                </Button>
            ) : (
                // State 3: default path to start another connection
                <Button
                    mode="contained"
                    onPress={createLinkToken}
                    style={styles.postConnectButton}
                >
                    Connect Another Account
                </Button>
            )}

            <Button
                mode="outlined"
                onPress={() => navigation.navigate('FixedCostsSetup')}
                style={styles.postConnectButton}
            >
                Continue to Fixed Costs Setup
            </Button>
        </View>
    );

    // --- Pre-Connect Content Component ---
    const PreConnectContent = () => (
        <>
            <View style={styles.instructionBox}>
                <RNText style={styles.mainInstruction}>
                    Please connect all credit cards, debit cards, and your primary checking account.
                </RNText>
                <RNText style={styles.warningInstruction}>
                    ⚠️ It is **essential** to connect **every** account you use to spend money. If you skip an account, your Dynamic Budget will be inaccurate because we will **miss those charges** going forward.
                </RNText>
            </View>

            {isFetchingToken && !linkToken ? (
                // State 1: Fetching token (API call in progress)
                <View style={styles.loadingPlaceholder}>
                    <ActivityIndicator size="small" />
                    <Text style={styles.loadingText}>Fetching Token...</Text>
                </View>
            ) : linkToken ? (
                // State 2: Token fetched (Ready for synchronous click)
                <Button
                    mode="contained"
                    onPress={open} // FIX: CALL OPEN SYNCHRONOUSLY HERE (Click 2)
                    style={styles.button}
                >
                    Open Plaid Link
                </Button>
            ) : (
                // State 3: Default (Click to start fetch)
                <Button
                    mode="contained"
                    onPress={createLinkToken}
                    disabled={!auth.currentUser}
                    style={styles.button}
                >
                    Connect Now
                </Button>
            )}
        </>
    );


    if (isFetchingToken && !linkToken) { // Only show full loading screen if we are waiting for the token
        return (
            <View style={styles.loadingContainer}>
                <ActivityIndicator size="large" />
                <Text style={styles.loadingText}>Connecting to Plaid...</Text>
            </View>
        );
    }

    return (
        <SafeAreaView style={styles.safeArea}>
            <View style={styles.contentContainer}>
                <Text style={styles.header}>Connect Your Accounts (2/5)</Text>

                {error && <Text style={styles.errorText}>{error}</Text>}

                {/* Conditional Render: Show PostConnect or PreConnect */}
                {numAccountsConnected > 0 ? <PostConnectContent /> : <PreConnectContent />}

            </View>

            <Button
                mode="text"
                onPress={() => navigation.goBack()}
                style={styles.backButton}
            >
                Go Back
            </Button>
        </SafeAreaView>
    );
}

// --- Stylesheet (Not Omitted) ---
const styles = StyleSheet.create({
    safeArea: { flex: 1, backgroundColor: '#fff' },
    contentContainer: { flex: 1, paddingHorizontal: 25, paddingTop: 50, justifyContent: 'flex-start', alignItems: 'center', },
    loadingContainer: { flex: 1, justifyContent: 'center', alignItems: 'center', backgroundColor: '#fff' },
    header: { fontSize: 26, fontWeight: 'bold', textAlign: 'center', marginBottom: 20, color: '#333' },

    // Pre-Connect Styles
    instructionBox: { marginBottom: 30, paddingHorizontal: 10, borderColor: '#f0f0f0', padding: 15, borderRadius: 8, },
    mainInstruction: { fontSize: 18, textAlign: 'center', marginBottom: 10, lineHeight: 25, },
    warningInstruction: { fontSize: 14, textAlign: 'center', color: 'red', fontWeight: 'bold', },
    button: { width: '100%', marginVertical: 10, paddingVertical: 4 },
    loadingPlaceholder: { paddingVertical: 10, flexDirection: 'row', alignItems: 'center', justifyContent: 'center', width: '100%' },
    loadingText: { marginLeft: 10, fontSize: 16 },

    // Post-Connect Styles
    postConnectBox: { width: '100%', alignItems: 'center', padding: 20, borderWidth: 1, borderColor: '#6200ee', borderRadius: 10, marginTop: 40 },
    successHeader: { fontSize: 22, fontWeight: 'bold', color: 'green', marginBottom: 20 },
    postConnectButton: { width: '100%', marginVertical: 8, paddingVertical: 4 },

    // General Styles
    errorText: { color: 'red', textAlign: 'center', marginTop: 10 },
    backButton: { marginBottom: 15, marginHorizontal: 20 },
});