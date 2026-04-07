// File: screens/onboarding/SavingsOnboardingScreen.js
//
// Step 4 of onboarding: collect the user's savings contribution per paycheck.
// Shows a debt warning if the user has debt (does NOT block savings — just warns).
//
// This screen makes the final /api/budget/finalize call once the user
// confirms their savings amount. All prior params are forwarded from route.params.

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
  const paycheckAmount = route.params?.paycheckAmount ?? 0;
  const payDay1 = route.params?.payDay1 ?? 1;
  const payDay2 = route.params?.payDay2 ?? 15;
  const debtPerPaycheck = route.params?.debtPerPaycheck ?? 0;

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
        if (!nextDate || potentialDate < nextDate) {
          nextDate = potentialDate;
        }
      }
    }

    if (!nextDate) {
      let nextMonth = currentMonth + 1;
      let nextYear = currentYear;
      if (nextMonth > 11) {
        nextMonth = 0;
        nextYear += 1;
      }
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

      // If user entered a savings amount, save it as a fixed cost first
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

      // Call /api/budget/finalize with all collected data
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

      // Navigate to the final summary screen
      navigation.navigate('DynamicAmountFinal', {
        remainingToSpend:    data.remainingToSpend,
        dynamicSpendableAmount: data.dynamicSpendableAmount ?? data.remainingToSpend,
        paycheckAmount:      data.paycheckAmount,
        fixedCostsRemaining: data.fixedCostsRemaining,
        debtPerPaycheck:     data.debtPerPaycheck,
        savingsContribution: data.savingsContribution,
        explanation:         data.explanation,
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
      // Treat empty as $0 savings
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

  const handleSkip = () => {
    handleFinalize(0);
  };

  // Live preview: paycheck - savings input
  const savingsValue = parseFloat(savingsInput) || 0;
  const previewRemaining =
    paycheckAmount > 0
      ? (parseFloat(paycheckAmount) - debtPerPaycheck - savingsValue).toFixed(2)
      : null;

  return (
    <TouchableWithoutFeedback onPress={Keyboard.dismiss} accessible={false}>
      <SafeAreaView style={styles.container}>
        <ScrollView contentContainerStyle={styles.scrollContent}>

          <Text style={styles.header}>Savings (4/4)</Text>

          {/* Debt warning — shown only when user has debt */}
          {hasDebt && (
            <Card style={styles.warningCard}>
              <Card.Content style={styles.warningContent}>
                <Text style={styles.warningIcon}>⚠️</Text>
                <Text style={styles.warningText}>
                  You are currently paying off{' '}
                  <Text style={styles.bold}>${debtPerPaycheck.toFixed(2)}</Text> per paycheck in debt.
                  {'\n\n'}
                  Consider pausing savings until your debt is reduced. However, even a small savings
                  contribution is better than none — the choice is yours.
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

          {/* Live preview */}
          {paycheckAmount > 0 && (
            <Card style={styles.previewCard}>
              <Card.Content>
                <Text style={styles.previewTitle}>Estimated budget</Text>
                <Divider style={{ marginVertical: 8 }} />
                <View style={styles.previewRow}>
                  <Text style={styles.previewLabel}>Income</Text>
                  <Text style={styles.previewValue}>+${parseFloat(paycheckAmount).toFixed(2)}</Text>
                </View>
                {debtPerPaycheck > 0 && (
                  <View style={styles.previewRow}>
                    <Text style={styles.previewLabel}>Debt payoff</Text>
                    <Text style={[styles.previewValue, styles.deduction]}>−${debtPerPaycheck.toFixed(2)}</Text>
                  </View>
                )}
                {savingsValue > 0 && (
                  <View style={styles.previewRow}>
                    <Text style={styles.previewLabel}>Savings</Text>
                    <Text style={[styles.previewValue, styles.deduction]}>−${savingsValue.toFixed(2)}</Text>
                  </View>
                )}
                <Divider style={{ marginVertical: 8 }} />
                <View style={styles.previewRow}>
                  <Text style={[styles.previewLabel, styles.bold]}>Approx. remaining</Text>
                  <Text
                    style={[
                      styles.previewValue,
                      styles.bold,
                      parseFloat(previewRemaining) < 0 && styles.negativeText,
                    ]}
                  >
                    ${previewRemaining}
                  </Text>
                </View>
                <Text style={styles.previewNote}>
                  (Fixed costs will be subtracted on the next screen.)
                </Text>
              </Card.Content>
            </Card>
          )}

          <Button
            mode="contained"
            onPress={handleContinue}
            loading={isSaving}
            style={styles.button}
            contentStyle={styles.buttonContent}
          >
            {isSaving ? 'Calculating…' : 'See My Budget'}
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

  warningCard: {
    backgroundColor: '#fff8e1',
    borderRadius: 12,
    marginBottom: 20,
    borderLeftWidth: 4,
    borderLeftColor: '#f59e0b',
  },
  warningContent: {
    flexDirection: 'row',
    alignItems: 'flex-start',
  },
  warningIcon: {
    fontSize: 20,
    marginRight: 10,
    marginTop: 2,
  },
  warningText: {
    fontSize: 14,
    color: '#78350f',
    lineHeight: 22,
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
  },
  previewValue: {
    fontSize: 14,
    color: '#333',
    fontVariant: ['tabular-nums'],
  },
  previewNote: {
    fontSize: 12,
    color: '#999',
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
  negativeText: {
    color: '#c62828',
  },

  button: { marginTop: 24, borderRadius: 10 },
  buttonContent: { paddingVertical: 6 },
});