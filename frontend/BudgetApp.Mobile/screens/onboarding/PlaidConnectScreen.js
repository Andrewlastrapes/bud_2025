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
    const [isPlaidLoading, setIsPlaidLoading] = useState(false);
    const [error, setError] = useState(null);

    const getAuthHeader = async () => {
        const user = auth.currentUser;
        if (!user) throw new Error("No user logged in.");
        const token = await user.getIdToken();
        return { headers: { 'Authorization': `Bearer ${token}` } };
    };

    const onPlaidSuccess = React.useCallback(async (public_token, metadata) => {
        setError(null);
        setIsPlaidLoading(true);
        try {
            const user = auth.currentUser;
            if (!user) throw new Error("Authentication error.");

            await axios.post(`${API_BASE_URL}/api/plaid/exchange_public_token`, {
                publicToken: public_token,
                firebaseUuid: user.uid
            }, await getAuthHeader());

            navigation.navigate('FixedCostsSetup');

        } catch (e) {
            console.error('Error exchanging token:', e.message);
            setError("Failed to save accounts. Please try again.");
        }
        setIsPlaidLoading(false);
    }, [navigation]);

    const { open, ready } = usePlaidLink({
        token: linkToken,
        onSuccess: onPlaidSuccess,
        onExit: (exit) => {
            console.log('Plaid Link exited:', exit);
            setLinkToken(null);
            setIsPlaidLoading(false);
        },
    });

    const createLinkToken = async () => {
        setIsPlaidLoading(true);
        setError(null);
        try {
            const user = auth.currentUser;
            if (!user) throw new Error("Please log in again.");

            const response = await axios.post(`${API_BASE_URL}/api/plaid/create_link_token`, {
                firebaseUserId: user.uid
            }, await getAuthHeader());

            setLinkToken(response.data.linkToken);
            console.log("Link token received. Waiting for modal...");

        } catch (e) {
            console.error('Error creating link token:', e.message);
            setError("Could not generate link token. Is your .NET API running?");
            setIsPlaidLoading(false);
        }
    };

    useEffect(() => {
        if (linkToken && ready) {
            open();
            setLinkToken(null);
        }
    }, [linkToken, ready, open]);

    // 5. Component renders
    if (isPlaidLoading) {
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

                <View style={styles.instructionBox}>
                    <Text style={styles.mainInstruction}>
                        Please connect all credit cards, debit cards, and your primary checking account.
                    </Text>
                    <Text style={styles.warningInstruction}>
                        ⚠️ It is **essential** to connect **every** account you use to spend money. If you skip an account, your Dynamic Budget will be inaccurate because we will **miss those charges** going forward.
                    </Text>
                </View>

                {error && <Text style={styles.errorText}>{error}</Text>}

                <Button
                    mode="contained"
                    onPress={createLinkToken}
                    disabled={!auth.currentUser}
                    style={styles.button}
                >
                    Connect Now
                </Button>
            </View>

            {/* Back button fixed to the bottom edge */}
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

const styles = StyleSheet.create({
    safeArea: {
        flex: 1,
        backgroundColor: '#fff'
    },
    contentContainer: {
        flex: 1,
        paddingHorizontal: 25,
        paddingTop: 50, // Pushes content down from the top edge
        justifyContent: 'center', // Centers content vertically
        alignItems: 'center',
    },
    loadingContainer: {
        flex: 1,
        justifyContent: 'center',
        alignItems: 'center',
        backgroundColor: '#fff'
    },
    header: {
        fontSize: 26,
        fontWeight: 'bold',
        textAlign: 'center',
        marginBottom: 40,
        color: '#333',
    },
    instructionBox: {
        marginBottom: 50,
        paddingHorizontal: 10,
    },
    mainInstruction: {
        fontSize: 18,
        textAlign: 'center',
        marginBottom: 10,
        lineHeight: 25,
    },
    subInstruction: {
        fontSize: 14,
        textAlign: 'center',
        color: '#888',
    },
    button: {
        width: '100%',
        marginVertical: 20,
        paddingVertical: 4
    },
    errorText: {
        color: 'red',
        textAlign: 'center',
        marginTop: 10
    },
    backButton: {
        marginBottom: 15, // Padding from bottom edge
        marginHorizontal: 20
    },
    loadingText: { marginTop: 10 },
});