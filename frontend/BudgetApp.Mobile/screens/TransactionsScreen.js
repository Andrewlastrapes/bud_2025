import React, { useState, useEffect } from 'react';
import { View, StyleSheet, FlatList, Alert, Platform } from 'react-native'; // ⬅️ Ensure Platform is here
import { Text, Button, List, ActivityIndicator, IconButton } from 'react-native-paper';
import { SafeAreaView } from 'react-native-safe-area-context';
import axios from 'axios';
import { auth } from '../firebaseConfig';
import { useIsFocused } from '@react-navigation/native';

import { API_BASE_URL } from '@/config/api';
;

export default function TransactionsScreen() {
    const [transactions, setTransactions] = useState([]);
    const [isLoading, setIsLoading] = useState(false);
    const [isSyncing, setIsSyncing] = useState(false);
    const isFocused = useIsFocused();

    const getAuthHeader = async () => {
        const user = auth.currentUser;
        if (!user) throw new Error("No user logged in.");
        const token = await user.getIdToken();
        return { headers: { 'Authorization': `Bearer ${token}` } };
    };

    const fetchTransactions = async () => {
        setIsLoading(true);
        try {
            const config = await getAuthHeader();
            const response = await axios.get(`${API_BASE_URL}/api/transactions`, config);
            setTransactions(response.data);
        } catch (e) {
            console.error("Failed to fetch transactions:", e);
        }
        setIsLoading(false);
    };

    const handleSync = async () => {
        setIsSyncing(true);
        try {
            const config = await getAuthHeader();
            const response = await axios.post(`${API_BASE_URL}/api/transactions/sync`, null, config);

            console.log("Sync complete:", response.data);

            await fetchTransactions();

        } catch (e) {
            console.error("Failed to sync transactions:", e);
            alert("Sync failed. Please try again.");
        }
        setIsSyncing(false);
    };

    // --- LOGIC TO ADD/SAVE FIXED COST ---
    const saveFixedCost = async (transaction) => {
        try {
            const config = await getAuthHeader();

            // The 'name' used for the transaction list display is the best match string.
            const merchantMatchName = transaction.merchantName || transaction.name;

            // Payload matches the FixedCost model
            const payload = {
                name: transaction.name,
                amount: transaction.amount,
                // Use the best available merchant name for the backend matching logic
                plaidMerchantName: merchantMatchName,
                category: "subscription",
                type: "manual"
            };

            await axios.post(`${API_BASE_URL}/api/fixed-costs`, payload, config);

            Alert.alert("Success", "Added to Fixed Costs. Future charges will be ignored.");
        } catch (e) {
            console.error("Failed to save fixed cost:", e);
            // Error handling needs to be aware of the 500 error structure
            const errorMessage = e.response?.data?.detail || e.message;
            Alert.alert("Error", `Could not save fixed cost: ${errorMessage}`);
        }
    };

    // --- METHOD CALLED BY onPress ---
    const handleMarkAsRecurring = (transaction) => {
        // console.log("--- ATTEMPTING TO MARK RECURRING ---"); // Removed log

        const onConfirm = () => {
            saveFixedCost(transaction);
        };

        // Platform-specific Alert Fix (Allows custom buttons on mobile, simple confirm on web)
        if (Platform.OS === 'web') {
            const confirmed = window.confirm(`Mark "${transaction.name}" as a recurring fixed cost?`);
            if (confirmed) {
                onConfirm();
            }
        } else {
            Alert.alert(
                "Mark as Fixed Cost?",
                `Do you want to add "${transaction.name}" ($${transaction.amount.toFixed(2)}) to your Fixed Costs?`,
                [
                    { text: "Cancel", style: "cancel" },
                    {
                        text: "Yes, Mark as Recurring",
                        onPress: onConfirm
                    }
                ]
            );
        }
    };
    // ------------------------------------------------------------------

    useEffect(() => {
        if (isFocused) {
            fetchTransactions();
        }
    }, [isFocused]);

    return (
        <SafeAreaView style={styles.container}>
            <Button
                mode="contained"
                onPress={handleSync}
                loading={isSyncing}
                style={styles.syncButton}
            >
                Sync New Transactions
            </Button>

            {isLoading ? (
                <ActivityIndicator style={{ marginTop: 20 }} />
            ) : (
                <FlatList
                    data={transactions}
                    keyExtractor={(item) => item.plaidTransactionId || item.id.toString()}
                    renderItem={({ item }) => (
                        <List.Item
                            title={item.name}
                            description={item.merchantName}
                            onPress={() => handleMarkAsRecurring(item)} // Now calls the platform-aware function
                            right={() => (
                                <View style={{ flexDirection: 'row', alignItems: 'center' }}>
                                    <Text style={item.amount < 0 ? styles.amountIncome : styles.amountExpense}>
                                        {item.amount > 0 ? `-$${item.amount.toFixed(2)}` : `+$${Math.abs(item.amount).toFixed(2)}`}
                                    </Text>
                                    <IconButton icon="dots-vertical" size={20} />
                                </View>
                            )}
                        />
                    )}
                />
            )}
        </SafeAreaView>
    );
}

const styles = StyleSheet.create({
    container: {
        flex: 1,
    },
    syncButton: {
        margin: 20,
    },
    amountExpense: {
        fontSize: 16,
        color: '#000',
        paddingRight: 5,
    },
    amountIncome: {
        fontSize: 16,
        color: 'green',
        paddingRight: 5,
    },
});