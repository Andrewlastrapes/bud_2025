import React, { useState, useEffect } from 'react';
import {
    View,
    StyleSheet,
    TouchableOpacity,
    ScrollView,
} from 'react-native';
import { Text, Button, TextInput, Modal, Portal, Card } from 'react-native-paper';
import { SafeAreaView } from 'react-native-safe-area-context';
import axios from 'axios';
import { auth } from '../firebaseConfig';
import { useIsFocused } from '@react-navigation/native';

import { API_BASE_URL } from '../config/api';

export default function HomeScreen({ navigation }) {
    const [balance, setBalance] = useState(0);
    const [paycheckInput, setPaycheckInput] = useState('');
    const [visible, setVisible] = useState(false);
    const [isLoading, setIsLoading] = useState(false);
    const [holdCount, setHoldCount] = useState(0);
    const isFocused = useIsFocused();

    // Helper to get auth headers
    const getAuthHeader = async () => {
        const user = auth.currentUser;
        if (!user) throw new Error('No user logged in.');
        const token = await user.getIdToken();
        return { headers: { Authorization: `Bearer ${token}` } };
    };

    const fetchBalance = async () => {
        try {
            const config = await getAuthHeader();
            const response = await axios.get(`${API_BASE_URL}/api/balance`, config);
            setBalance(response.data.amount);
        } catch (e) {
            console.error('Failed to fetch balance:', e);
        }
    };

    const fetchHoldCount = async () => {
        try {
            const config = await getAuthHeader();
            const response = await axios.get(`${API_BASE_URL}/api/transactions/suspicious-holds`, config);
            setHoldCount(response.data.length ?? 0);
        } catch (e) {
            console.error('Failed to fetch hold count:', e);
        }
    };

    useEffect(() => {
        if (isFocused) {
            fetchBalance();
            fetchHoldCount();
        }
    }, [isFocused]);

    const handleSetPaycheck = async () => {
        setIsLoading(true);
        try {
            const config = await getAuthHeader();
            const amount = parseFloat(paycheckInput);
            await axios.post(`${API_BASE_URL}/api/balance`, { amount }, config);
            setBalance(amount);
            setPaycheckInput('');
            setVisible(false);
        } catch (e) {
            console.error('Failed to set paycheck:', e);
            alert('Error updating balance.');
        }
        setIsLoading(false);
    };

    const isOverBudget = balance < 0;
    const absoluteOver = Math.abs(balance);

    return (
        <SafeAreaView style={styles.safe}>
            <ScrollView
                style={styles.scroll}
                contentContainerStyle={styles.scrollContent}
                showsVerticalScrollIndicator={false}
            >
                {/* ── Hero balance card ── */}
                <View style={[styles.balanceCard, isOverBudget && styles.balanceCardDanger]}>
                    <Text style={styles.balanceEyebrow}>
                        {isOverBudget ? 'OVER BUDGET' : 'DYNAMIC BUDGET'}
                    </Text>

                    <Text style={[styles.balanceAmount, isOverBudget && styles.balanceAmountDanger]}>
                        {isOverBudget
                            ? `-$${absoluteOver.toFixed(2)}`
                            : `$${balance.toFixed(2)}`}
                    </Text>

                    {isOverBudget ? (
                        <Text style={styles.balanceDangerNote}>
                            You're over budget by ${absoluteOver.toFixed(2)} until your next paycheck.
                        </Text>
                    ) : (
                        <Text style={styles.balanceSafeNote}>
                            Safe to spend until your next paycheck
                        </Text>
                    )}
                </View>

                {/* ── Hold banner ── */}
                {holdCount > 0 && (
                    <TouchableOpacity
                        style={styles.holdBanner}
                        onPress={() => navigation.navigate('ReviewSuspiciousHolds')}
                        activeOpacity={0.8}
                    >
                        <Text style={styles.holdIcon}>⚠️</Text>
                        <View style={styles.holdTextBlock}>
                            <Text style={styles.holdTitle}>
                                {holdCount} Pending Hold{holdCount > 1 ? 's' : ''} Need Review
                            </Text>
                            <Text style={styles.holdSub}>
                                Gas / hotel / rental holds may be inflated. Tap to adjust.
                            </Text>
                        </View>
                        <Text style={styles.holdChevron}>›</Text>
                    </TouchableOpacity>
                )}

                {/* ── Actions ── */}
                <View style={styles.actionsSection}>
                    <Button
                        mode="contained"
                        onPress={() => setVisible(true)}
                        style={styles.primaryBtn}
                        contentStyle={styles.btnContent}
                        labelStyle={styles.btnLabel}
                    >
                        Edit Upcoming Paycheck
                    </Button>

                    <Button
                        mode="outlined"
                        onPress={() => navigation.navigate('DepositReview')}
                        style={styles.outlinedBtn}
                        contentStyle={styles.btnContent}
                        labelStyle={styles.outlinedBtnLabel}
                    >
                        Review New Deposits
                    </Button>

                    <Button
                        mode="outlined"
                        onPress={() => navigation.navigate('ReviewLargeExpenses')}
                        style={styles.outlinedBtn}
                        contentStyle={styles.btnContent}
                        labelStyle={styles.outlinedBtnLabel}
                    >
                        Review Large Expenses
                    </Button>
                </View>
            </ScrollView>

            {/* ── Paycheck modal ── */}
            <Portal>
                <Modal
                    visible={visible}
                    onDismiss={() => setVisible(false)}
                    contentContainerStyle={styles.modal}
                >
                    <Card style={styles.modalCard}>
                        <Card.Title
                            title="New Paycheck"
                            titleStyle={styles.modalTitle}
                        />
                        <Card.Content>
                            <TextInput
                                label="Amount ($)"
                                value={paycheckInput}
                                onChangeText={setPaycheckInput}
                                keyboardType="numeric"
                                mode="outlined"
                                style={styles.modalInput}
                            />
                            <Button
                                mode="contained"
                                onPress={handleSetPaycheck}
                                loading={isLoading}
                                contentStyle={styles.btnContent}
                                labelStyle={styles.btnLabel}
                            >
                                Save
                            </Button>
                        </Card.Content>
                    </Card>
                </Modal>
            </Portal>
        </SafeAreaView>
    );
}

const styles = StyleSheet.create({
    safe: {
        flex: 1,
        backgroundColor: '#F8FAFC',
    },
    scroll: {
        flex: 1,
    },
    scrollContent: {
        paddingHorizontal: 20,
        paddingTop: 24,
        paddingBottom: 40,
    },

    // ── Balance card ──────────────────────────────────────────────
    balanceCard: {
        backgroundColor: '#FFFFFF',
        borderRadius: 20,
        paddingVertical: 32,
        paddingHorizontal: 28,
        alignItems: 'center',
        marginBottom: 16,
        // shadow
        shadowColor: '#4F46E5',
        shadowOffset: { width: 0, height: 4 },
        shadowOpacity: 0.08,
        shadowRadius: 16,
        elevation: 4,
        borderWidth: 1,
        borderColor: 'rgba(79,70,229,0.06)',
    },
    balanceCardDanger: {
        shadowColor: '#DC2626',
        borderColor: 'rgba(220,38,38,0.08)',
    },
    balanceEyebrow: {
        fontSize: 11,
        fontWeight: '700',
        letterSpacing: 1.8,
        color: '#94A3B8',
        marginBottom: 14,
        textTransform: 'uppercase',
    },
    balanceAmount: {
        fontSize: 52,
        fontWeight: '800',
        color: '#0D9488',   // teal-600 — calm, positive
        letterSpacing: -1.5,
        marginBottom: 10,
    },
    balanceAmountDanger: {
        color: '#DC2626',
    },
    balanceSafeNote: {
        fontSize: 13,
        color: '#94A3B8',
        fontWeight: '400',
    },
    balanceDangerNote: {
        fontSize: 13,
        color: '#DC2626',
        fontWeight: '500',
        textAlign: 'center',
        paddingHorizontal: 12,
    },

    // ── Hold banner ───────────────────────────────────────────────
    holdBanner: {
        flexDirection: 'row',
        alignItems: 'center',
        backgroundColor: '#FFFBEB',
        borderWidth: 1,
        borderColor: '#FDE68A',
        borderRadius: 14,
        paddingVertical: 14,
        paddingHorizontal: 16,
        marginBottom: 16,
    },
    holdIcon: {
        fontSize: 20,
        marginRight: 12,
    },
    holdTextBlock: {
        flex: 1,
    },
    holdTitle: {
        fontSize: 14,
        fontWeight: '700',
        color: '#92400E',
    },
    holdSub: {
        fontSize: 12,
        color: '#B45309',
        marginTop: 2,
        lineHeight: 18,
    },
    holdChevron: {
        fontSize: 22,
        color: '#D97706',
        marginLeft: 8,
        fontWeight: '300',
    },

    // ── Actions ───────────────────────────────────────────────────
    actionsSection: {
        gap: 10,
    },
    primaryBtn: {
        borderRadius: 12,
    },
    outlinedBtn: {
        borderRadius: 12,
        borderColor: '#4F46E5',
    },
    btnContent: {
        height: 50,
    },
    btnLabel: {
        fontSize: 15,
        fontWeight: '600',
        letterSpacing: 0.2,
    },
    outlinedBtnLabel: {
        fontSize: 15,
        fontWeight: '600',
        letterSpacing: 0.2,
        color: '#4F46E5',
    },

    // ── Modal ─────────────────────────────────────────────────────
    modal: {
        paddingHorizontal: 24,
    },
    modalCard: {
        borderRadius: 16,
        overflow: 'hidden',
    },
    modalTitle: {
        fontSize: 18,
        fontWeight: '700',
        color: '#0F172A',
    },
    modalInput: {
        marginBottom: 16,
        backgroundColor: '#FFFFFF',
    },
});