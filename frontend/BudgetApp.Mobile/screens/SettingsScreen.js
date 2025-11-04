import React, { useState, useEffect } from 'react';
import { View, StyleSheet } from 'react-native';
import { Text, Button, ActivityIndicator } from 'react-native-paper';
import { SafeAreaView } from 'react-native-safe-area-context';
import axios from 'axios';

// --- THE FIX: Import the correct hook for web ---
import { usePlaidLink } from 'react-plaid-link';

// Use localhost for the simulator/web
const API_BASE_URL = 'http://localhost:5150';

export default function SettingsScreen() {
    const [linkToken, setLinkToken] = useState(null);
    const [isLoading, setIsLoading] = useState(false);

    // This is the callback Plaid runs on success
    const onPlaidSuccess = React.useCallback((public_token, metadata) => {
        console.log('Plaid Link success!', public_token);

        // TODO: Send the public_token to your backend
        // e.g., axios.post(`${API_BASE_URL}/api/plaid/exchange_public_token`, { publicToken: public_token });

    }, []);

    // 1. Initialize the hook
    const { open, ready } = usePlaidLink({
        token: linkToken,
        onSuccess: onPlaidSuccess,
        onExit: (exit) => console.log('Plaid Link exited:', exit),
    });

    // 2. Fetches the link_token from your .NET API
    const createLinkToken = async () => {
        setIsLoading(true);
        try {
            const response = await axios.post(`${API_BASE_URL}/api/plaid/create_link_token`);
            console.log("Link token created successfully.");
            setLinkToken(response.data.linkToken);
        } catch (error) {
            console.error('Error creating link token:', error);
            // Make sure your .NET API is running!
        }
        setIsLoading(false);
    };

    // 3. This Effect hook waits for the linkToken and for Plaid to be ready
    //    Then it automatically opens the Plaid popup.
    useEffect(() => {
        if (linkToken && ready) {
            open();
            setLinkToken(null); // Clear the token after opening
        }
    }, [linkToken, ready, open]);

    return (
        <SafeAreaView style={styles.container}>
            <Text style={styles.text}>Settings Screen</Text>

            <Button
                mode="contained"
                onPress={createLinkToken} // The button now just fetches the token
                disabled={isLoading}
                style={{ marginTop: 20 }}
            >
                {isLoading ? <ActivityIndicator color="white" /> : 'Connect Bank Account'}
            </Button>
        </SafeAreaView>
    );
}

const styles = StyleSheet.create({
    container: {
        flex: 1,
        justifyContent: 'center',
        alignItems: 'center',
    },
    text: {
        fontSize: 20,
        fontWeight: 'bold',
        marginBottom: 10,
    },
});