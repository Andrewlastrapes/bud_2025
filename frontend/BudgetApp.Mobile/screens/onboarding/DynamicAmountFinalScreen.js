// File: screens/onboarding/DynamicAmountFinalScreen.js

import React, { useState } from 'react';
import { View, ScrollView, StyleSheet, Alert, ActivityIndicator } from 'react-native';
import { Text, Button, Divider } from 'react-native-paper';
import { SafeAreaView } from 'react-native-safe-area-context';
import axios from 'axios';
import { StackActions } from '@react-navigation/native';

import { auth } from '../../firebaseConfig';
import { API_BASE_URL } from '../../config/api';

export default function DynamicAmountFinalScreen({ navigation, route }) {
    const {
        dynamicSpendableAmount,
        finalAmount,          // legacy fallback
        paycheckAmount,
        totalRecurringCosts,
        debtPerPaycheck,
        savingsContribution,
        effectivePaycheck,
        prorateFactor,
        explanation,
    } = route.params || {};

    const [isChecking, setIsChecking] = useState(false);

    // Resolve primary display value — prefer the new field, fall back to legacy string
    const displayAmount =
        dynamicSpendableAmount != null
            ? parseFloat(dynamicSpendableAmount).toFixed(2)
            : finalAmount || '0.00';

    // Resolve prorate percentage for display
    const proratePercent =
        prorateFactor != null
            ? Math.round(parseFloat(prorateFactor) * 100)
            : null;

    // Helper to get auth headers
    const getAuthHeader = async () => {
        const user = auth.currentUser;
        if (!user) throw new Error('No user logged in.');
        const token = await user.getIdToken();
        return { headers: { Authorization: `Bearer ${token}` } };
    };

    const handleFinish = async () => {
        setIsChecking(true);

        try {
            const config = await getAuthHeader();
            let profile;
            let retryCount = 0;

            // Poll backend until onboardingComplete is true (max 5 retries)
            do {
                if (retryCount > 0) {
                    await new Promise(resolve => setTimeout(resolve, 500));
                }

                const response = await axios.get(`${API_BASE_URL}/api/users/profile`, config);
                profile = response.data;

                if (profile.onboardingComplete) {
                    const parentNav = navigation.getParent();
                    if (parentNav) {
                        parentNav.dispatch(StackActions.replace('App'));
                    } else {
                        navigation.navigate('App');
                    }
                    return;
                }

                retryCount++;
            } while (retryCount < 5);

            Alert.alert(
                'Setup Error',
                'Failed to confirm setup status. Please log out and log back in.',
            );
        } catch (e) {
            console.error('Error verifying final setup status', e);
            Alert.alert('Network Error', 'Could not verify final setup status. Please try again.');
        } finally {
            setIsChecking(false);
        }
    };

    if (isChecking) {
        return (
            <View style={styles.loadingContainer}>
                <ActivityIndicator size="large" />
                <Text style={styles.loadingText}>Finalizing your setup...</Text>
            </View>
        );
    }

    return (
        <SafeAreaView style={styles.container}>
            <ScrollView contentContainerStyle={styles.scrollContent} showsVerticalScrollIndicator={false}>

                <Text style={styles.header}>Your Dynamic Budget is Ready! (5/5)</Text>

                {/* ── Big spendable number ── */}
                <View style={styles.amountBox}>
                    <Text style={styles.amountLabel}>You can spend until your next paycheck:</Text>
                    <Text style={styles.amountValue}>${displayAmount}</Text>
                    {proratePercent != null && (
                        <Text style={styles.prorateTag}>{proratePercent}% of pay cycle remaining</Text>
                    )}
                </View>

                {/* ── Breakdown table ── */}
                {paycheckAmount != null && (
                    <View style={styles.breakdownCard}>
                        <Text style={styles.breakdownTitle}>How we calculated this</Text>
                        <Divider style={styles.divider} />

                        <BreakdownRow
                            label="Paycheck (net)"
                            value={paycheckAmount}
                            isPositive
                        />

                        {parseFloat(totalRecurringCosts) > 0 && (
                            <BreakdownRow
                                label="Upcoming bills"
                                value={parseFloat(totalRecurringCosts) - parseFloat(debtPerPaycheck || 0) - parseFloat(savingsContribution || 0)}
                                isDeduction
                                indent
                            />
                        )}

                        {parseFloat(debtPerPaycheck) > 0 && (
                            <BreakdownRow
                                label="Debt payoff"
                                value={debtPerPaycheck}
                                isDeduction
                                indent
                            />
                        )}

                        {parseFloat(savingsContribution) > 0 && (
                            <BreakdownRow
                                label="Savings goal"
                                value={savingsContribution}
                                isDeduction
                                indent
                            />
                        )}

                        <Divider style={styles.divider} />

                        <BreakdownRow
                            label="Effective paycheck"
                            value={effectivePaycheck}
                            isPositive={parseFloat(effectivePaycheck) >= 0}
                            bold
                        />

                        {proratePercent != null && (
                            <BreakdownRow
                                label={`× ${proratePercent}% time remaining`}
                                value={null}
                                isMultiplier
                            />
                        )}

                        <Divider style={styles.divider} />

                        <BreakdownRow
                            label="Spendable now"
                            value={dynamicSpendableAmount ?? finalAmount}
                            isPositive={parseFloat(dynamicSpendableAmount ?? finalAmount) >= 0}
                            bold
                            large
                        />
                    </View>
                )}

                {/* ── Engine explanation (if no breakdown, show this instead) ── */}
                {explanation ? (
                    <View style={styles.explanationCard}>
                        <Text style={styles.explanationText}>{explanation}</Text>
                    </View>
                ) : paycheckAmount == null ? (
                    <Text style={styles.fallbackExplanation}>
                        This amount is your paycheck minus all recurring costs, prorated by{' '}
                        {proratePercent != null ? `${proratePercent}%` : `${prorateFactor ?? '0.00'}x`} to
                        reflect the remaining days in your current cycle.
                    </Text>
                ) : null}

            </ScrollView>

            <Button
                mode="contained"
                onPress={handleFinish}
                style={styles.button}
                contentStyle={styles.buttonContent}
            >
                I Understand — Finish Setup
            </Button>
        </SafeAreaView>
    );
}

// ─── Small helper component for breakdown rows ────────────────────────────────

function BreakdownRow({ label, value, isPositive, isDeduction, isMultiplier, bold, large, indent }) {
    const formattedValue =
        value != null
            ? `${isDeduction ? '−' : ''}$${Math.abs(parseFloat(value)).toFixed(2)}`
            : null;

    return (
        <View style={[styles.row, indent && styles.rowIndent]}>
            <Text style={[styles.rowLabel, bold && styles.rowBold, large && styles.rowLarge]}>
                {label}
            </Text>
            {formattedValue != null && (
                <Text
                    style={[
                        styles.rowValue,
                        bold && styles.rowBold,
                        large && styles.rowLarge,
                        isDeduction ? styles.rowDeduction : isPositive ? styles.rowPositive : null,
                    ]}
                >
                    {formattedValue}
                </Text>
            )}
        </View>
    );
}

// ─── Styles ───────────────────────────────────────────────────────────────────

const styles = StyleSheet.create({
    container: { flex: 1, backgroundColor: '#f5f5f5' },
    scrollContent: { padding: 24, paddingBottom: 100 },
    loadingContainer: { flex: 1, justifyContent: 'center', alignItems: 'center' },
    loadingText: { marginTop: 12, color: '#666' },

    header: {
        fontSize: 22,
        fontWeight: 'bold',
        textAlign: 'center',
        marginBottom: 24,
        color: '#1a1a1a',
    },

    // Big amount block
    amountBox: {
        alignItems: 'center',
        backgroundColor: '#fff',
        borderRadius: 16,
        padding: 24,
        marginBottom: 20,
        shadowColor: '#000',
        shadowOpacity: 0.06,
        shadowRadius: 8,
        elevation: 2,
    },
    amountLabel: {
        fontSize: 15,
        color: '#666',
        marginBottom: 8,
        textAlign: 'center',
    },
    amountValue: {
        fontSize: 56,
        fontWeight: 'bold',
        color: '#6200ee',
        letterSpacing: -1,
    },
    prorateTag: {
        marginTop: 8,
        fontSize: 13,
        color: '#888',
        backgroundColor: '#f0ebff',
        paddingHorizontal: 12,
        paddingVertical: 4,
        borderRadius: 20,
        overflow: 'hidden',
    },

    // Breakdown card
    breakdownCard: {
        backgroundColor: '#fff',
        borderRadius: 16,
        padding: 20,
        marginBottom: 16,
        shadowColor: '#000',
        shadowOpacity: 0.06,
        shadowRadius: 8,
        elevation: 2,
    },
    breakdownTitle: {
        fontSize: 15,
        fontWeight: '600',
        color: '#333',
        marginBottom: 12,
    },
    divider: {
        marginVertical: 10,
        backgroundColor: '#eee',
    },

    // Breakdown rows
    row: {
        flexDirection: 'row',
        justifyContent: 'space-between',
        alignItems: 'center',
        paddingVertical: 4,
    },
    rowIndent: {
        paddingLeft: 16,
    },
    rowLabel: {
        fontSize: 14,
        color: '#555',
        flex: 1,
    },
    rowValue: {
        fontSize: 14,
        color: '#333',
        fontVariant: ['tabular-nums'],
    },
    rowBold: {
        fontWeight: '700',
        color: '#1a1a1a',
    },
    rowLarge: {
        fontSize: 16,
    },
    rowPositive: {
        color: '#2e7d32',
    },
    rowDeduction: {
        color: '#c62828',
    },

    // Explanation text (from engine)
    explanationCard: {
        backgroundColor: '#fff',
        borderRadius: 16,
        padding: 20,
        marginBottom: 16,
        shadowColor: '#000',
        shadowOpacity: 0.06,
        shadowRadius: 8,
        elevation: 2,
    },
    explanationText: {
        fontSize: 14,
        color: '#444',
        lineHeight: 22,
    },
    fallbackExplanation: {
        fontSize: 13,
        textAlign: 'center',
        color: '#666',
        lineHeight: 20,
        marginBottom: 16,
    },

    button: {
        margin: 20,
        marginBottom: 10,
        borderRadius: 12,
    },
    buttonContent: {
        paddingVertical: 6,
    },
});