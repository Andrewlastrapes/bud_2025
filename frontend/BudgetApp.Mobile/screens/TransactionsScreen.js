import React, { useState, useEffect } from 'react';
import { View, StyleSheet, FlatList, Alert, Platform } from 'react-native'; // Ensure Alert and Platform are imported
import { Text, Button, List, ActivityIndicator, IconButton } from 'react-native-paper';
import { SafeAreaView } from 'react-native-safe-area-context';
import axios from 'axios';
import { auth } from '../firebaseConfig';
import { useIsFocused } from '@react-navigation/native';

const API_BASE_URL = 'http://localhost:5150';

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

    // --- LOGIC TO ADD/SAVE FIXED COST (The missing piece) ---
    const saveFixedCost = async (transaction) => {
        try {
            const config = await getAuthHeader();

            // Payload matches the FixedCost model we updated in the backend
            const payload = {
                name: transaction.name,
                amount: transaction.amount,
                // Use PlaidMerchantName for robust future matching
                plaidMerchantName: transaction.merchantName || transaction.name,
                category: "subscription", // Default category
                type: "manual" // Marking it as manually confirmed recurring
            };

            await axios.post(`${API_BASE_URL}/api/fixed-costs`, payload, config);

            Alert.alert("Success", "Added to Fixed Costs. Future charges will be ignored.");
            // We could refresh the Fixed Costs screen here if needed, but not required.
        } catch (e) {
            console.error("Failed to save fixed cost:", e);
            Alert.alert("Error", "Could not save fixed cost.");
        }
    };

    // --- METHOD CALLED BY onPress (The method you were missing) ---
    const handleMarkAsRecurring = (transaction) => {
        console.log("--- ATTEMPTING TO MARK RECURRING ---"); // <-- ADD THIS
        Alert.alert(
            "Mark as Fixed Cost?",
            `Do you want to add "${transaction.name}" ($${transaction.amount}) to your Fixed Costs?`,
            [
                { text: "Cancel", style: "cancel" },
                {
                    text: "Yes, Mark as Recurring",
                    onPress: () => saveFixedCost(transaction)
                }
            ]
        );
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
                            // This is what makes the row clickable
                            onPress={() => handleMarkAsRecurring(item)}
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