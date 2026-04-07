// File: screens/onboarding/DebtOnboardingScreen.js
//
// Step 3 of onboarding: show the user their credit card debt (from Plaid)
// and let them choose how much to put toward it each paycheck.
//
// Shows a live preview: "If you put $X toward debt, you'll have ~$Y left."
// Forwards all params to SavingsOnboardingScreen which makes the finalize call.

import React, { useEffect, useState } from 'react';
import { View, ScrollView, StyleSheet } from 'react-native';
import { Text, Card, ActivityIndicator, Button, TextInput } from 'react-native-paper';
import axios from 'axios';
import { auth } from '../../firebaseConfig';
import { API_BASE_URL } from '../../config/api';

export default function DebtOnboardingScreen({ navigation, route }) {
  const [snapshot, setSnapshot] = useState(null);
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState(null);
  const [perPaycheckInput, setPerPaycheckInput] = useState('');

  // Income params forwarded from DepositOnboardingScreen → FixedCostsSetup → here
  const paycheckAmount = route.params?.paycheckAmount ?? null;
  const payDay1 = route.params?.payDay1 ?? null;
  const payDay2 = route.params?.payDay2 ?? null;

  useEffect(() => {
    const fetchDebtSnapshot = async () => {
      try {
        const user = auth.currentUser;
        if (!user) {
          setError('Not logged in.');
          setIsLoading(false);
          return;
        }

        const token = await user.getIdToken();
        const response = await axios.get(`${API_BASE_URL}/api/debt/snapshot`, {
          headers: { Authorization: `Bearer ${token}` },
        });

        setSnapshot(response.data);
      } catch (e) {
        console.error('Failed to load debt snapshot', e);
        setError('Could not load your credit card balances.');
      } finally {
        setIsLoading(false);
      }
    };

    fetchDebtSnapshot();
  }, []);

  // Live preview: paycheck - debtInput (simplified — fixed costs/savings not yet finalised)
  const debtInputValue = parseFloat(perPaycheckInput) || 0;
  const previewRemaining =
    paycheckAmount != null
      ? (parseFloat(paycheckAmount) - debtInputValue).toFixed(2)
      : null;

  const goNext = (debtPerPaycheckOrNull) => {
    navigation.navigate('SavingsOnboarding', {
      paycheckAmount,
      payDay1,
      payDay2,
      debtPerPaycheck: debtPerPaycheckOrNull,
    });
  };

  const handleContinueWithDebt = () => {
    setError(null);

    const trimmed = perPaycheckInput.trim();
    if (trimmed === '') {
      setError('Enter an amount per paycheck, or tap Skip if you do not want to dedicate anything right now.');
      return;
    }

    const value = parseFloat(trimmed);
    if (Number.isNaN(value) || value < 0) {
      setError('Please enter a valid non-negative amount.');
      return;
    }

    goNext(value);
  };

  const handleSkip = () => {
    goNext(0);
  };

  if (isLoading) {
    return (
      <View style={styles.center}>
        <ActivityIndicator />
        <Text style={{ marginTop: 8 }}>Loading your balances…</Text>
      </View>
    );
  }

  if (error && !snapshot) {
    return (
      <View style={styles.container}>
        <Text style={styles.title}>Credit Card Debt (3/4)</Text>
        <Text style={styles.error}>{error}</Text>
        <Button mode="outlined" style={{ marginTop: 16 }} onPress={handleSkip}>
          Skip debt setup for now
        </Button>
      </View>
    );
  }

  const totalDebt = snapshot?.totalDebt ?? 0;

  // No credit card balances detected
  if (!snapshot || totalDebt <= 0) {
    return (
      <View style={styles.container}>
        <Text style={styles.title}>Credit Card Debt (3/4)</Text>
        <Text style={styles.bodyText}>
          We didn't detect any outstanding credit card balances from your linked accounts.
        </Text>
        <Button mode="contained" style={{ marginTop: 24 }} onPress={() => goNext(0)}>
          Continue
        </Button>
      </View>
    );
  }

  return (
    <ScrollView contentContainerStyle={styles.container}>
      <Text style={styles.title}>Credit Card Debt (3/4)</Text>

      <Card style={{ marginTop: 12, marginBottom: 16 }}>
        <Card.Content>
          <Text style={styles.summaryText}>
            We see about <Text style={styles.highlight}>${totalDebt.toFixed(2)}</Text> in credit card balances.
          </Text>
          <Text style={[styles.bodyText, { marginTop: 8 }]}>
            This info comes from your linked credit card accounts. You don't need to type anything in manually.
          </Text>
        </Card.Content>
      </Card>

      <Text style={styles.sectionTitle}>Your cards</Text>

      {snapshot.accounts?.map((acct, idx) => (
        <Card key={idx} style={styles.accountCard}>
          <Card.Content>
            <Text style={styles.accountName}>
              {acct.institutionName} — {acct.accountName}
              {acct.mask ? ` ••••${acct.mask}` : ''}
            </Text>
            <Text style={styles.accountBalance}>${acct.currentBalance.toFixed(2)}</Text>
          </Card.Content>
        </Card>
      ))}

      <Text style={[styles.sectionTitle, { marginTop: 24 }]}>
        How much per paycheck do you want to put toward this debt?
      </Text>
      <Text style={styles.bodyText}>
        This comes out of each paycheck before we calculate what you have left to spend. You can change it later.
      </Text>

      <TextInput
        mode="outlined"
        label="Debt payoff per paycheck ($)"
        value={perPaycheckInput}
        onChangeText={(v) => {
          setError(null);
          setPerPaycheckInput(v);
        }}
        keyboardType="numeric"
        style={{ marginTop: 12 }}
      />

      {/* Live preview */}
      {paycheckAmount != null && perPaycheckInput.trim() !== '' && debtInputValue >= 0 && (
        <Card style={styles.previewCard}>
          <Card.Content>
            <Text style={styles.previewText}>
              If you put{' '}
              <Text style={styles.highlight}>${debtInputValue.toFixed(2)}</Text> toward
              debt each paycheck, you'll have approximately{' '}
              <Text style={[styles.highlight, parseFloat(previewRemaining) < 0 && styles.negative]}>
                ${previewRemaining}
              </Text>{' '}
              left to spend.
            </Text>
            <Text style={styles.previewNote}>
              (This preview doesn't yet include fixed costs or savings.)
            </Text>
          </Card.Content>
        </Card>
      )}

      {error && <Text style={styles.error}>{error}</Text>}

      <Button mode="contained" style={{ marginTop: 24 }} onPress={handleContinueWithDebt}>
        Continue with this payoff amount
      </Button>

      <Button mode="text" style={{ marginTop: 8 }} onPress={handleSkip}>
        Skip debt payoff for now
      </Button>
    </ScrollView>
  );
}

const styles = StyleSheet.create({
  container: {
    padding: 20,
    paddingBottom: 32,
  },
  center: {
    flex: 1,
    justifyContent: 'center',
    alignItems: 'center',
    padding: 20,
  },
  title: {
    fontSize: 22,
    fontWeight: '700',
    marginBottom: 8,
  },
  summaryText: {
    fontSize: 16,
  },
  sectionTitle: {
    fontSize: 18,
    fontWeight: '600',
    marginTop: 16,
    marginBottom: 4,
  },
  bodyText: {
    fontSize: 14,
    color: '#555',
  },
  accountCard: {
    marginTop: 8,
  },
  accountName: {
    fontSize: 15,
    fontWeight: '500',
  },
  accountBalance: {
    marginTop: 4,
    fontSize: 14,
  },
  highlight: {
    fontWeight: '700',
  },
  negative: {
    color: '#c62828',
  },
  previewCard: {
    marginTop: 16,
    backgroundColor: '#f3e8ff',
    borderRadius: 12,
  },
  previewText: {
    fontSize: 14,
    color: '#333',
    lineHeight: 22,
  },
  previewNote: {
    fontSize: 12,
    color: '#888',
    marginTop: 6,
  },
  error: {
    color: 'red',
    marginTop: 8,
  },
});