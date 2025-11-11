import React, { useState, useEffect } from 'react';
import { View, StyleSheet, FlatList } from 'react-native';
import { Text, Button, ActivityIndicator, List } from 'react-native-paper';
import { SafeAreaView } from 'react-native-safe-area-context';
import axios from 'axios';
import { usePlaidLink } from 'react-plaid-link';
import { auth } from '../firebaseConfig';
import { useIsFocused } from '@react-navigation/native'; // Import this hook

const API_BASE_URL = 'http://localhost:5150';

export default function SettingsScreen() {
    const [linkToken, setLinkToken] = useState(null);
    const [isLoading, setIsLoading] = useState(false);
    const [error, setError] = useState(null);
    const [accounts, setAccounts] = useState([]); // State for connected accounts
    const isFocused = useIsFocused(); // Hook to check if screen is active

    // --- Helper function to get the auth token ---
    const getAuthHeader = async () => {
        const user = auth.currentUser;
        if (!user) throw new Error("No user is logged in.");
        const idToken = await user.getIdToken();
        return { headers: { 'Authorization': `Bearer ${idToken}` } };
    };

    // --- 1. Function to fetch connected accounts ---
    const fetchAccounts = async () => {
        setError(null);
        try {
            const config = await getAuthHeader();
            const response = await axios.get(`${API_BASE_URL}/api/plaid/accounts`, config);
            setAccounts(response.data);
        } catch (e) {
            console.error('Error fetching accounts:', e.message);
            setError('Could not fetch accounts. ' + e.message);
        }
    };

    // --- 2. Fetch accounts when the screen comes into focus ---
    useEffect(() => {
        if (isFocused) {
            fetchAccounts();
        }
    }, [isFocused]); // Re-run when the screen is focused

    // --- 3. Plaid Success Callback ---
    const onPlaidSuccess = React.useCallback(async (public_token, metadata) => {
        console.log('Plaid Link success! Exchanging token...');
        setError(null);
        try {
            const user = auth.currentUser;
            if (!user) throw new Error("No user logged in.");

            const config = await getAuthHeader(); // Get auth header
            await axios.post(`${API_BASE_URL}/api/plaid/exchange_public_token`, {
                publicToken: public_token,
                firebaseUuid: user.uid
            }, config); // Send token with request

            console.log("Access token exchanged.");
            fetchAccounts(); // Refresh the account list

        } catch (e) {
            console.error('Error exchanging token:', e.message);
            setError(e.message);
        }
    }, []);

    const { open, ready } = usePlaidLink({
        token: linkToken,
        onSuccess: onPlaidSuccess,
        onExit: (exit) => console.log('Plaid Link exited:', exit),
    });

    // --- 4. Create Link Token Function (Updated) ---
    const createLinkToken = async () => {
        setIsLoading(true);
        setError(null);
        try {
            const user = auth.currentUser;
            if (!user) throw new Error("No user is logged in.");

            const config = await getAuthHeader(); // Get auth header
            const response = await axios.post(`${API_BASE_URL}/api/plaid/create_link_token`,
                { firebaseUserId: user.uid }, // Body
                config // Auth header
            );

            console.log("Link token created successfully.");
            setLinkToken(response.data.linkToken);

        } catch (e) {
            console.error('Error creating link token:', e.message);
            setError(e.message);
        }
        setIsLoading(false);
    };

    useEffect(() => {
        if (linkToken && ready) {
            open();
            setLinkToken(null);
        }
    }, [linkToken, ready, open]);

    return (
        <SafeAreaView style={styles.container}>
            <Text style={styles.title}>Connected Accounts</Text>

            <FlatList
                data={accounts}
                keyExtractor={(item) => item.id.toString()}
                renderItem={({ item }) => (
                    <List.Item
                        title={item.institutionName || 'Unknown Institution'}
                        left={() => <List.Icon icon="bank" />}
                    // We can add a "delete" button here later
                    />
                )}
                style={styles.list}
            />

            <Button
                mode="contained"
                onPress={createLinkToken}
                disabled={isLoading}
                style={{ margin: 20 }}
            >
                {isLoading ? <ActivityIndicator color="white" /> : 'Add New Account'}
            </Button>
            {error && <Text style={styles.error}>{error}</Text>}
        </SafeAreaView>
    );
}

const styles = StyleSheet.create({
    container: {
        flex: 1,
    },
    title: {
        fontSize: 24,
        fontWeight: 'bold',
        textAlign: 'center',
        marginVertical: 20,
    },
    list: {
        width: '100%',
    },
    error: {
        margin: 20,
        color: 'red',
        textAlign: 'center',
    },
});