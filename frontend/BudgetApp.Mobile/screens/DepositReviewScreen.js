// File: screens/DepositReviewScreen.js

import React, { useEffect, useState } from 'react';
import { View, StyleSheet, FlatList } from 'react-native';
import {
    Text,
    Button,
    Card,
    ActivityIndicator,
    Snackbar,
} from 'react-native-paper';
import { SafeAreaView } from 'react-native-safe-area-context';
import axios from 'axios';
import { auth } from '../firebaseConfig';
import { useIsFocused } from '@react-navigation/native';

import { API_BASE_URL } from '@/config/api';
;

// These numeric values MUST match your C# enum TransactionUserDecision
// public enum TransactionUserDecision { Unknown = 0, TreatAsIncome = 1, Ignore = 2, DebtPayment = 3, SavingsFunded = 4 }
const DECISIONS = {
    TreatAsIncome: 1,
    Ignore: 2,
    DebtPayment: 3,
    SavingsFunded: 4,
};

export default function DepositReviewScreen() {
    const [transactions, setTransactions] = useState([]);
    const [isLoading, setIsLoading] = useState(false);
    const [snackbar, setSnackbar] = useState({ visible: false, message: '' });

    const isFocused = useIsFocused();

    const getAuthHeader = async () => {
        const user = auth.currentUser;
        if (!user) throw new Error('No user logged in.');
        const token = await user.getIdToken();
        return { headers: { Authorization: `Bearer ${token}` } };
    };

    const fetchDepositsNeedingReview = async () => {
        setIsLoading(true);
        try {
            const config = await getAuthHeader();
            const response = await axios.get(`${API_BASE_URL}/api/transactions`, config);

            const all = response.data || [];

            // TEMP RULE (MVP):
            // - positive Amount
            // - !countedAsIncome
            // This will include paychecks + windfalls; user decides.
            const creditsNeedingReview = all.filter(
                (tx) =>
                    tx.amount > 0 &&
                    !tx.countedAsIncome && // bool from your Transaction model
                    tx.suggestedKind !== null // optional, mostly to avoid garbage rows
            );

            setTransactions(creditsNeedingReview);
        } catch (e) {
            console.error('Failed to fetch deposits:', e);
            setSnackbar({
                visible: true,
                message: 'Failed to load deposits. Try again later.',
            });
        }
        setIsLoading(false);
    };

    useEffect(() => {
        if (isFocused) {
            fetchDepositsNeedingReview();
        }
    }, [isFocused]);

    const handleDecision = async (txId, decisionValue) => {
        try {
            const config = await getAuthHeader();

            await axios.post(
                `${API_BASE_URL}/api/transactions/${txId}/decision`,
                { decision: decisionValue },
                config
            );

            // Optimistic update: remove transaction from local list
            setTransactions((prev) => prev.filter((t) => t.id !== txId));

            setSnackbar({
                visible: true,
                message: 'Decision saved.',
            });
        } catch (e) {
            console.error('Failed to save decision:', e);
            setSnackbar({
                visible: true,
                message: 'Error saving decision.',
            });
        }
    };

    const renderItem = ({ item }) => {
        const dateLabel = item.date ? new Date(item.date).toLocaleDateString() : '';

        return (
            <Card style={styles.card}>
                <Card.Title
                    title={item.merchantName || item.name || 'Deposit'}
                    subtitle={`${dateLabel} • $${item.amount.toFixed(2)}`}
                />
                <Card.Content>
                    {item.suggestedKind === 1 && ( // if you want to show "Paycheck" as a hint
                        <Text style={styles.tag}>Suggested: Paycheck</Text>
                    )}
                    {item.suggestedKind === 2 && (
                        <Text style={styles.tag}>Suggested: Windfall</Text>
                    )}

                    <View style={styles.buttonRow}>
                        <Button
                            mode="contained"
                            style={styles.button}
                            onPress={() => handleDecision(item.id, DECISIONS.TreatAsIncome)}
                        >
                            Add to Dynamic
                        </Button>
                        <Button
                            mode="outlined"
                            style={styles.button}
                            onPress={() => handleDecision(item.id, DECISIONS.Ignore)}
                        >
                            Ignore
                        </Button>
                    </View>

                    <View style={styles.buttonRow}>
                        <Button
                            mode="outlined"
                            style={styles.button}
                            onPress={() => handleDecision(item.id, DECISIONS.DebtPayment)}
                        >
                            Mark as Debt Payment
                        </Button>
                        <Button
                            mode="outlined"
                            style={styles.button}
                            onPress={() => handleDecision(item.id, DECISIONS.SavingsFunded)}
                        >
                            Mark as Savings
                        </Button>
                    </View>
                </Card.Content>
            </Card>
        );
    };

    return (
        <SafeAreaView style={styles.container}>
            {isLoading ? (
                <View style={styles.loadingContainer}>
                    <ActivityIndicator size="large" />
                    <Text>Loading deposits...</Text>
                </View>
            ) : transactions.length === 0 ? (
                <View style={styles.emptyContainer}>
                    <Text style={styles.emptyText}>
                        No deposits need review right now.
                    </Text>
                </View>
            ) : (
                <FlatList
                    data={transactions}
                    keyExtractor={(item) => item.id.toString()}
                    renderItem={renderItem}
                    contentContainerStyle={styles.listContent}
                />
            )}

            <Snackbar
                visible={snackbar.visible}
                onDismiss={() => setSnackbar((s) => ({ ...s, visible: false }))}
                duration={2500}
            >
                {snackbar.message}
            </Snackbar>
        </SafeAreaView>
    );
}

const styles = StyleSheet.create({
    container: { flex: 1 },
    listContent: { padding: 16, paddingBottom: 32 },
    card: { marginBottom: 12 },
    loadingContainer: { flex: 1, justifyContent: 'center', alignItems: 'center' },
    emptyContainer: { flex: 1, justifyContent: 'center', alignItems: 'center', padding: 32 },
    emptyText: { fontSize: 16, color: '#666', textAlign: 'center' },
    tag: { fontSize: 12, color: '#888', marginBottom: 8 },
    buttonRow: {
        flexDirection: 'row',
        justifyContent: 'space-between',
        marginTop: 8,
    },
    button: { flex: 1, marginHorizontal: 4 },
});
