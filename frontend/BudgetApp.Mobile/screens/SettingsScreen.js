import React, { useState, useEffect, useCallback } from 'react';
import { View, StyleSheet, FlatList, Platform } from 'react-native';
import { Text, Button, ActivityIndicator, List } from 'react-native-paper';
import { SafeAreaView } from 'react-native-safe-area-context';
import axios from 'axios';
import { auth } from '../firebaseConfig';
import { useIsFocused } from '@react-navigation/native';
import { API_BASE_URL } from '../config/api';

// --- Helper to get auth headers ---
const getAuthHeader = async () => {
    const user = auth.currentUser;
    if (!user) throw new Error('No user is logged in.');
    const idToken = await user.getIdToken();
    return { headers: { Authorization: `Bearer ${idToken}` } };
};

// ─── Web: uses react-plaid-link (iframe-based) ────────────────────────────────
function WebAddAccountButton({ linkToken, createLinkToken, onPlaidSuccess, isLoading }) {
    const { usePlaidLink } = require('react-plaid-link');

    const { open, ready } = usePlaidLink({
        token: linkToken,
        onSuccess: onPlaidSuccess,
        onExit: (exit) => console.log('[Settings] Plaid exited:', exit),
    });

    // Auto-open as soon as the token is loaded and the SDK is ready
    useEffect(() => {
        if (linkToken && ready) {
            open();
        }
    }, [linkToken, ready, open]);

    return (
        <Button
            mode="contained"
            onPress={createLinkToken}
            disabled={isLoading}
            style={styles.addButton}
        >
            {isLoading ? <ActivityIndicator color="white" /> : 'Add New Account'}
        </Button>
    );
}

// ─── Native: uses react-native-plaid-link-sdk ─────────────────────────────────
function NativeAddAccountButton({ linkToken, setLinkToken, createLinkToken, onPlaidSuccess, isLoading, setError }) {
    const plaid = require('react-native-plaid-link-sdk');
    const { create, open } = plaid;

    // Initialise the native SDK whenever we receive a fresh token
    useEffect(() => {
        if (!linkToken) return;
        create({ token: linkToken });
    }, [linkToken]);

    const handleOpenPlaid = async () => {
        try {
            await open({
                onSuccess: onPlaidSuccess,
                onExit: (exit) => {
                    console.log('[Settings] Plaid exited:', exit);
                    setLinkToken(null);
                },
            });
        } catch (e) {
            console.error('[Settings] Plaid open failed:', e);
            setError('Could not open Plaid. Please try again.');
            setLinkToken(null);
        }
    };

    // Once the token is created and create() has been called, auto-open Plaid
    useEffect(() => {
        if (!linkToken) return;
        handleOpenPlaid();
    }, [linkToken]);

    return (
        <Button
            mode="contained"
            onPress={createLinkToken}
            disabled={isLoading}
            style={styles.addButton}
        >
            {isLoading ? <ActivityIndicator color="white" /> : 'Add New Account'}
        </Button>
    );
}

// ─── Main screen ──────────────────────────────────────────────────────────────
export default function SettingsScreen() {
    const [linkToken, setLinkToken] = useState(null);
    const [isLoading, setIsLoading] = useState(false);
    const [error, setError] = useState(null);
    const [accounts, setAccounts] = useState([]);
    const isFocused = useIsFocused();

    // --- Fetch connected accounts ---
    const fetchAccounts = async () => {
        setError(null);
        try {
            const config = await getAuthHeader();
            const response = await axios.get(`${API_BASE_URL}/api/plaid/accounts`, config);
            setAccounts(response.data);
        } catch (e) {
            console.error('[Settings] Error fetching accounts:', e.message);
            setError('Could not fetch accounts. ' + e.message);
        }
    };

    useEffect(() => {
        if (isFocused) {
            fetchAccounts();
        }
    }, [isFocused]);

    // --- Plaid success callback ---
    const onPlaidSuccess = useCallback(async (publicTokenOrSuccess, metadata) => {
        // Normalise: web SDK passes a string; native SDK passes { publicToken, metadata }
        const publicToken =
            typeof publicTokenOrSuccess === 'string'
                ? publicTokenOrSuccess
                : publicTokenOrSuccess?.publicToken;

        setError(null);
        setLinkToken(null);
        try {
            const user = auth.currentUser;
            if (!user) throw new Error('No user logged in.');
            const config = await getAuthHeader();
            await axios.post(
                `${API_BASE_URL}/api/plaid/exchange_public_token`,
                { publicToken, firebaseUuid: user.uid },
                config,
            );
            console.log('[Settings] Token exchanged successfully.');
            fetchAccounts();
        } catch (e) {
            console.error('[Settings] Error exchanging token:', e.message);
            setError(e.message);
        }
    }, []);

    // --- Create Plaid link token ---
    const createLinkToken = async () => {
        setIsLoading(true);
        setError(null);
        try {
            const user = auth.currentUser;
            if (!user) throw new Error('No user is logged in.');
            const config = await getAuthHeader();
            const response = await axios.post(
                `${API_BASE_URL}/api/plaid/create_link_token`,
                { firebaseUserId: user.uid },
                config,
            );
            console.log('[Settings] Link token created.');
            setLinkToken(response.data.linkToken);
        } catch (e) {
            console.error('[Settings] Error creating link token:', e.message);
            setError(e.message);
        } finally {
            setIsLoading(false);
        }
    };

    const plaidButtonProps = {
        linkToken,
        setLinkToken,
        createLinkToken,
        onPlaidSuccess,
        isLoading,
        setError,
    };

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
                    />
                )}
                style={styles.list}
            />

            {Platform.OS === 'web' ? (
                <WebAddAccountButton {...plaidButtonProps} />
            ) : (
                <NativeAddAccountButton {...plaidButtonProps} />
            )}

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
    addButton: {
        margin: 20,
    },
    error: {
        margin: 20,
        color: 'red',
        textAlign: 'center',
    },
});