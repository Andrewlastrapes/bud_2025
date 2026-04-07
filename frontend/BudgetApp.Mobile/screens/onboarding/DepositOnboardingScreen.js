// File: screens/onboarding/DepositOnboardingScreen.js
//
// Step 1 of onboarding: collect the user's paycheck amount and pay days.
// This screen does NOT call the API — it just collects data and passes it
// forward through route params so subsequent screens can use it for previews
// and the final SavingsOnboardingScreen can call /api/budget/finalize.

import React, { useState } from 'react';
import {
  View,
  StyleSheet,
  Alert,
  Keyboard,
  TouchableWithoutFeedback,
  ScrollView,
} from 'react-native';
import { Text, TextInput, Button } from 'react-native-paper';
import { SafeAreaView } from 'react-native-safe-area-context';

export default function DepositOnboardingScreen({ navigation }) {
  const [paycheckAmount, setPaycheckAmount] = useState('');
  const [payDay1, setPayDay1] = useState('1');
  const [payDay2, setPayDay2] = useState('15');

  const handleNext = () => {
    const amount = parseFloat(paycheckAmount);
    const day1 = parseInt(payDay1, 10);
    const day2 = parseInt(payDay2, 10);

    if (isNaN(amount) || amount <= 0) {
      Alert.alert('Missing Info', 'Please enter a valid paycheck amount.');
      return;
    }

    if (
      isNaN(day1) || day1 < 1 || day1 > 31 ||
      isNaN(day2) || day2 < 1 || day2 > 31
    ) {
      Alert.alert('Invalid Pay Day', 'Please enter valid days of the month (1–31).');
      return;
    }

    if (day1 === day2) {
      Alert.alert('Invalid Pay Days', 'Your two pay days must be different days of the month.');
      return;
    }

    // Pass income data forward — subsequent screens will forward these params
    // until SavingsOnboardingScreen makes the final /api/budget/finalize call.
    navigation.navigate('FixedCostsSetup', {
      paycheckAmount: amount,
      payDay1: day1,
      payDay2: day2,
    });
  };

  return (
    <TouchableWithoutFeedback onPress={Keyboard.dismiss} accessible={false}>
      <SafeAreaView style={styles.container}>
        <ScrollView
          contentContainerStyle={styles.scrollContent}
          keyboardShouldPersistTaps="handled"
        >
          <Text style={styles.header}>Your Income (1/4)</Text>
          <Text style={styles.subtitle}>
            Tell us about your paycheck so we can figure out how much you have
            to spend until your next payday.
          </Text>

          <Text style={styles.label}>What is your NET (take-home) paycheck amount?</Text>
          <TextInput
            mode="outlined"
            label="Paycheck Amount ($)"
            value={paycheckAmount}
            onChangeText={setPaycheckAmount}
            keyboardType="numeric"
            style={styles.input}
          />

          <Text style={styles.label}>What are your two monthly pay days?</Text>
          <Text style={styles.hint}>
            e.g. if you get paid on the 1st and 15th, enter "1" and "15".
          </Text>
          <View style={styles.row}>
            <TextInput
              mode="outlined"
              label="Pay Day 1"
              value={payDay1}
              onChangeText={setPayDay1}
              keyboardType="numeric"
              style={styles.rowInput}
            />
            <TextInput
              mode="outlined"
              label="Pay Day 2"
              value={payDay2}
              onChangeText={setPayDay2}
              keyboardType="numeric"
              style={styles.rowInput}
            />
          </View>

          <Text style={styles.info}>
            We use these two dates to determine which bills fall within your
            current pay window.
          </Text>

          <Button
            mode="contained"
            onPress={handleNext}
            style={styles.button}
            contentStyle={styles.buttonContent}
          >
            Next: Fixed Costs
          </Button>
        </ScrollView>
      </SafeAreaView>
    </TouchableWithoutFeedback>
  );
}

const styles = StyleSheet.create({
  container: { flex: 1, backgroundColor: '#fff' },
  scrollContent: { padding: 28, paddingBottom: 40 },

  header: {
    fontSize: 24,
    fontWeight: 'bold',
    textAlign: 'center',
    marginBottom: 8,
    color: '#1a1a1a',
  },
  subtitle: {
    fontSize: 14,
    color: '#666',
    textAlign: 'center',
    marginBottom: 32,
    lineHeight: 20,
  },

  label: {
    fontSize: 15,
    fontWeight: '600',
    marginTop: 16,
    marginBottom: 4,
    color: '#333',
  },
  hint: {
    fontSize: 13,
    color: '#888',
    marginBottom: 8,
  },
  input: { marginBottom: 4 },
  row: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    marginBottom: 4,
  },
  rowInput: { width: '48%' },

  info: {
    fontSize: 12,
    color: '#999',
    textAlign: 'center',
    marginTop: 12,
    marginBottom: 24,
    lineHeight: 18,
  },

  button: { marginTop: 8, borderRadius: 10 },
  buttonContent: { paddingVertical: 6 },
});