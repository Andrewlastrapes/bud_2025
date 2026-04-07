// File: screens/onboarding/SavingsOnboardingScreen.js
//
// Step 4 of onboarding: collect savings per paycheck.
//
// Key change: the display base is `remainingAfterDebt` (paycheck - fixedCosts - debt),
// NOT the raw paycheck amount. This reflects the user's actual disposable income
// after their debt decision, so they make an informed savings choice.
//
// Makes the final /api/budget/finalize call.

import React, { useState } from 'react';
import {
  View,
  ScrollView,
  StyleSheet,
  Alert,
  Keyboard,
  TouchableWithoutFeedback,
} from 'react-native';
import { Text, TextInput, Button, Card, Divider } from 'react-native-paper';
import { SafeAreaView } from 'react-native-safe-area-context';
import axios from 'axios';
import { auth } from '../../firebaseConfig';
import { API_BASE_URL } from '../../config/api';

export default function SavingsOnboardingScreen({ navigation, route }) {
  const [savingsInput, setSavingsInput] = useState('');
  const [isSaving, setIsSaving] = useState(false);
  const [inputError, setInputError] = useState(null);

  // All params forwarded from prior onboarding screens
  const paycheckAmount     = route.params?.paycheckAmount     ?? 0;
  const payDay1            = route.params?.payDay1            ?? 1;
  const payDay2            = route.params?.payDay2            ?? 15;
  const debtPerPaycheck    = route.params?.debtPerPaycheck    ?? 0;
  const remainingAfterDebt = route.params?.remainingAfterDebt ?? (paycheckAmount - debtPerPaycheck);
  const fixedCostsRemaining = route.params?.fixedCostsRemaining ?? 0;

  const hasDebt = debtPerPaycheck > 0;

  // Calculate next paycheck date from two fixed pay days
  const calculateNextPaycheckDate = (day1, day2) => {
    const today = new Date();
    const currentMonth = today.getMonth();
    const currentYear = today.getFullYear();
    const days = [day1, day2].sort((a, b) => a - b);

    let nextDate = null;
    for (const day of days) {
      const potentialDate = new Date(currentYear, currentMonth, day);
      if (potentialDate.getDate() === day && potentialDate >= today) {
        if (!nextDate || potentialDate < nextDate) nextDate = potentialDate;
      }
    }

    if (!nextDate) {
      let nextMonth = currentMonth + 1;
      let nextYear = currentYear;
      if (nextMonth > 11) { nextMonth = 0; nextYear += 1; }
      nextDate = new Date(nextYear, nextMonth, days[0]);
    }

    return nextDate;
  };

  const getAuthHeader = async () => {
    const user = auth.currentUser;
    if (!user) throw new Error('No user logged in.');
    const token = await user.getIdToken();
    return { headers: { Authorization: `Bearer ${token}` } };
  };

  const handleFinalize = async (savingsPerPaycheck) => {
    setIsSaving(true);

    try {
      const config = await getAuthHeader();
      const nextPaycheckDate = calculateNextPaycheckDate(payDay1, payDay2);

      // Save savings as a fixed cost if > 0
      if (savingsPerPaycheck > 0) {
        await axios.post(
          `${API_BASE_URL}/api/fixed-costs`,
          {
            name: 'Savings Goal',
            amount: savingsPerPaycheck,
            category: 'Savings',
            type: 'manual',
            nextDueDate: null,
          },
          config,
        );
      }

      // Call /api/budget/finalize with all data
      const response = await axios.post(
        `${API_BASE_URL}/api/budget/finalize`,
        {
          paycheckAmount,
          nextPaycheckDate,
          payDay1,
          payDay2,
          debtPerPaycheck: debtPerPaycheck || null,
        },
        config,
      );

      const data = response.data;

      navigation.navigate('DynamicAmountFinal', {
        remainingToSpend:     data.remainingToSpend,
        dynamicSpendableAmount: data.dynamicSpendableAmount ?? data.remainingToSpend,
        paycheckAmount:       data.paycheckAmount,
        fixedCostsRemaining:  data.fixedCostsRemaining,
        baseRemaining:        data.baseRemaining,
        debtPerPaycheck:      data.debtPerPaycheck,
        savingsContribution:  data.savingsContribution,
        explanation:          data.explanation,
      });
    } catch (e) {
      console.error('Finalization failed:', e);
      Alert.alert('Error', e.message || 'An unknown error occurred. Please try again.');
    }

    setIsSaving(false);
  };

  const handleContinue = () => {
    setInputError(null);
    const trimmed = savingsInput.trim();

    if (trimmed === '') {
      handleFinalize(0);
      return;
    }

    const value = parseFloat(trimmed);
    if (Number.isNaN(value) || value < 0) {
      setInputError('Please enter a valid amount, or leave blank for $0 savings.');
      return;
    }

    handleFinalize(value);
  };

  const handleSkip = () => handleFinalize(0);

  // Live preview uses remainingAfterDebt as the starting point
  const savingsValue = parseFloat(savingsInput) || 0;
  const finalRemaining = Math.round((remainingAfterDebt - savingsValue) * 100) / 100;

  return (
    <TouchableWithoutFeedback onPress={Keyboard.dismiss} accessible={false}>
      <SafeAreaView style={styles.container}>
        <ScrollView contentContainerStyle={styles.scrollContent}>

          <Text style={styles.header}>Savings (4/4)</Text>

          {/* After-debt available banner */}
          <Card style={styles.availableCard}>
            <Card.Content>
              <Text style={styles.availableLabel}>
                {hasDebt
                  ? 'After your debt payment, you have:'
                  : 'After fixed costs, you have:'}
              </Text>
              <Text style={[
                styles.availableAmount,
                remainingAfterDebt < 0 && styles.negative,
              ]}>
                ${remainingAfterDebt.toFixed(2)}
              </Text>
              <Text style={styles.availableNote}>
                {hasDebt
                  ? `$${paycheckAmount.toFixed(2)} − $${fixedCostsRemaining.toFixed(2)} fixed − $${debtPerPaycheck.toFixed(2)} debt`
                  : `$${paycheckAmount.toFixed(2)} − $${fixedCostsRemaining.toFixed(2)} fixed costs`}
              </Text>
            </Card.Content>
          </Card>

          {/* Debt warning */}
          {hasDebt && (
            <Card style={styles.warningCard}>
              <Card.Content style={styles.warningContent}>
                <Text style={styles.warningIcon}>⚠️</Text>
                <Text style={styles.warningText}>
                  You're paying off{' '}
                  <Text style={styles.bold}>${debtPerPaycheck.toFixed(2)}</Text>/paycheck in debt.
                  {'\n'}
                  Consider pausing savings until your debt is paid down — but the choice is yours.
                </Text>
              </Card.Content>
            </Card>
          )}

          <Text style={styles.label}>
            How much do you want to save per paycheck?
          </Text>
          <Text style={styles.hint}>
            Leave blank or enter $0 to skip savings for now. You can always update this later.
          </Text>

          <TextInput
            mode="outlined"
            label="Savings per paycheck ($)"
            value={savingsInput}
            onChangeText={(v) => {
              setInputError(null);
              setSavingsInput(v);
            }}
            keyboardType="numeric"
            style={styles.input}
            disabled={isSaving}
          />

          {inputError && <Text style={styles.error}>{inputError}</Text>}

          {/* Live preview — always shown so user sees impact */}
          <Card style={styles.previewCard}>
            <Card.Content>
              <Text style={styles.previewTitle}>Your budget breakdown</Text>
              <Divider style={{ marginVertical: 8 }} />

              <View style={styles.previewRow}>
                <Text style={styles.previewLabel}>Income</Text>
                <Text style={styles.previewValue}>+${paycheckAmount.toFixed(2)}</Text>
              </View>

              {fixedCostsRemaining > 0 && (
                <View style={styles.previewRow}>
                  <Text style={styles.previewLabel}>Fixed costs</Text>
                  <Text style={[styles.previewValue, styles.deduction]}>
                    −${fixedCostsRemaining.toFixed(2)}
                  </Text>
                </View>
              )}

              {hasDebt && (
                <View style={styles.previewRow}>
                  <Text style={styles.previewLabel}>Debt payoff</Text>
                  <Text style={[styles.previewValue, styles.deduction]}>
                    −${debtPerPaycheck.toFixed(2)}
                  </Text>
                </View>
              )}

              {savingsValue > 0 && (
                <View style={styles.previewRow}>
                  <Text style={styles.previewLabel}>Savings</Text>
                  <Text style={[styles.previewValue, styles.deduction]}>
                    −${savingsValue.toFixed(2)}
                  </Text>
                </View>
              )}

              <Divider style={{ marginVertical: 8 }} />

              <View style={styles.previewRow}>
                <Text style={[styles.previewLabel, styles.bold]}>Final remaining</Text>
                <Text style={[
                  styles.previewValue,
                  styles.bold,
                  finalRemaining < 0 && styles.negativeText,
                ]}>
                  ${finalRemaining.toFixed(2)}
                </Text>
              </View>

              {finalRemaining < 0 && (
                <Text style={styles.warningNote}>
                  ⚠️ Savings amount exceeds your remaining budget. Consider a lower amount.
                </Text>
              )}
            </Card.Content>
          </Card>

          <Button
            mode="contained"
            onPress={handleContinue}
            loading={isSaving}
            style={styles.button}
            contentStyle={styles.buttonContent}
          >
            {isSaving ? 'Calculating…' : 'See My Final Budget'}
          </Button>

          <Button
            mode="text"
            onPress={handleSkip}
            disabled={isSaving}
            style={{ marginTop: 4 }}
          >
            Skip savings for now ($0)
          </Button>

        </ScrollView>
      </SafeAreaView>
    </TouchableWithoutFeedback>
  );
}

const styles = StyleSheet.create({
  container: { flex: 1, backgroundColor: '#fff' },
  scrollContent: { padding: 24, paddingBottom: 48 },

  header: {
    fontSize: 24,
    fontWeight: 'bold',
    textAlign: 'center',
    marginBottom: 20,
    color: '#1a1a1a',
  },

  // After-debt banner
  availableCard: {
    backgroundColor: '#e8f5e9',
    borderRadius: 14,
    marginBottom: 16,
  },
  availableLabel: {
    fontSize: 13,
    color: '#444',
    marginBottom: 4,
  },
  availableAmount: {
    fontSize: 36,
    fontWeight: 'bold',
    color: '#2e7d32',
    letterSpacing: -0.5,
  },
  availableNote: {
    fontSize: 12,
    color: '#666',
    marginTop: 4,
  },

  // Debt warning
  warningCard: {
    backgroundColor: '#fff8e1',
    borderRadius: 12,
    marginBottom: 16,
    borderLeftWidth: 4,
    borderLeftColor: '#f59e0b',
  },
  warningContent: {
    flexDirection: 'row',
    alignItems: 'flex-start',
  },
  warningIcon: {
    fontSize: 18,
    marginRight: 8,
    marginTop: 2,
  },
  warningText: {
    fontSize: 13,
    color: '#78350f',
    lineHeight: 20,
    flex: 1,
  },

  label: {
    fontSize: 15,
    fontWeight: '600',
    color: '#333',
    marginBottom: 4,
  },
  hint: {
    fontSize: 13,
    color: '#888',
    marginBottom: 12,
    lineHeight: 18,
  },
  input: { marginBottom: 4 },

  error: {
    color: '#c62828',
    fontSize: 13,
    marginTop: 4,
    marginBottom: 8,
  },

  // Live preview
  previewCard: {
    backgroundColor: '#f5f5f5',
    borderRadius: 12,
    marginTop: 20,
    marginBottom: 8,
  },
  previewTitle: {
    fontSize: 14,
    fontWeight: '600',
    color: '#444',
  },
  previewRow: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    paddingVertical: 3,
  },
  previewLabel: {
    fontSize: 14,
    color: '#555',
    flex: 1,
  },
  previewValue: {
    fontSize: 14,
    color: '#333',
    fontVariant: ['tabular-nums'],
  },
  warningNote: {
    fontSize: 12,
    color: '#b71c1c',
    marginTop: 8,
    fontStyle: 'italic',
  },
  deduction: {
    color: '#c62828',
  },
  bold: {
    fontWeight: '700',
    color: '#1a1a1a',
  },
  negative: {
    color: '#c62828',
  },
  negativeText: {
    color: '#c62828',
  },

  button: { marginTop: 24, borderRadius: 10 },
  buttonContent: { paddingVertical: 6 },
});