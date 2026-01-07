
import React, { useEffect, useState } from 'react';
import { View, ScrollView, StyleSheet } from 'react-native';
import { Text, Card, ActivityIndicator, Button, TextInput } from 'react-native-paper';
import axios from 'axios';
import { auth } from '../../firebaseConfig';

const API_BASE_URL = 'http://localhost:5150';

export default function DebtOnboardingScreen({ navigation, route }) {
  const [snapshot, setSnapshot] = useState(null);
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState(null);
  const [perPaycheckInput, setPerPaycheckInput] = useState('');

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

  const goNext = (debtPerPaycheckOrNull) => {
    // Pass everything we received from previous onboarding steps,
    // plus debtPerPaycheck, to the final screen.
    navigation.navigate('PaycheckSavings', {
      ...(route.params ?? {}),
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
    // No dedicated debt payoff
    goNext(null);
  };

  if (isLoading) {
    return (
      <View style={styles.center}>
        <ActivityIndicator />
        <Text style={{ marginTop: 8 }}>Loading your balances…</Text>
      </View>
    );
  }

  if (error) {
    return (
      <View style={styles.container}>
        <Text style={styles.title}>Credit Card Debt</Text>
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
        <Text style={styles.title}>Credit Card Debt</Text>
        <Text style={styles.bodyText}>
          We didn’t detect any outstanding credit card balances from your linked accounts.
        </Text>
        <Button mode="contained" style={{ marginTop: 24 }} onPress={() => goNext(null)}>
          Continue
        </Button>
      </View>
    );
  }

  return (
    <ScrollView contentContainerStyle={styles.container}>
      <Text style={styles.title}>Credit Card Debt</Text>

      <Card style={{ marginTop: 12, marginBottom: 16 }}>
        <Card.Content>
          <Text style={styles.summaryText}>
            We see about <Text style={styles.highlight}>${totalDebt.toFixed(2)}</Text> in credit card balances.
          </Text>
          <Text style={[styles.bodyText, { marginTop: 8 }]}>
            This info comes from your linked credit card accounts. You don’t need to type anything in manually.
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
        How much per paycheck do you want to dedicate to this debt?
      </Text>
      <Text style={styles.bodyText}>
        We’ll treat this like a “fixed cost” that comes out of each paycheck before we calculate your Period Spend
        Limit. You can change it later.
      </Text>

      <TextInput
        mode="outlined"
        label="Debt payoff per paycheck ($)"
        value={perPaycheckInput}
        onChangeText={setPerPaycheckInput}
        keyboardType="numeric"
        style={{ marginTop: 12 }}
      />

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
  error: {
    color: 'red',
    marginTop: 8,
  },
});
