// File: screens/onboarding/DebtOnboardingScreen.js
//
// Step 3 of onboarding: interactive debt payment decision screen.
//
// Flow:
// 1. Fetch debt snapshot (credit card balances from Plaid)
// 2. Call POST /api/budget/base to get baseRemaining (paycheck - fixedCosts)
// 3. User picks a payment amount (presets or custom)
// 4. Show live payoff timeline + remaining-after-debt preview
// 5. Offer "pay in full" if baseRemaining >= totalDebt
// 6. Forward all data to SavingsOnboardingScreen

import React, { useEffect, useState } from 'react';
import {
  View,
  ScrollView,
  StyleSheet,
  TouchableOpacity,
} from 'react-native';
import { Text, Card, ActivityIndicator, Button, TextInput, Divider } from 'react-native-paper';
import axios from 'axios';
import { auth } from '../../firebaseConfig';
import { API_BASE_URL } from '../../config/api';

// Preset debt payment options shown as quick-select buttons
const PAYMENT_PRESETS = [100, 200, 300, 500];

export default function DebtOnboardingScreen({ navigation, route }) {
  // ── Loading / data state ───────────────────────────────────────────────────
  const [snapshot, setSnapshot] = useState(null);       // credit card snapshot
  const [baseBudget, setBaseBudget] = useState(null);   // { paycheckAmount, fixedCostsRemaining, baseRemaining }
  const [isLoadingDebt, setIsLoadingDebt] = useState(true);
  const [isLoadingBase, setIsLoadingBase] = useState(true);
  const [loadError, setLoadError] = useState(null);

  // ── UI state ───────────────────────────────────────────────────────────────
  const [isExpanded, setIsExpanded] = useState(false);  // collapsible debt list
  const [selectedPreset, setSelectedPreset] = useState(null); // which preset is active
  const [customInput, setCustomInput] = useState('');   // custom amount field
  const [inputError, setInputError] = useState(null);

  // ── Income params from prior screens ──────────────────────────────────────
  const paycheckAmount = route.params?.paycheckAmount ?? 0;
  const payDay1        = route.params?.payDay1 ?? 1;
  const payDay2        = route.params?.payDay2 ?? 15;

  // ── Helpers ───────────────────────────────────────────────────────────────

  const getAuthHeader = async () => {
    const user = auth.currentUser;
    if (!user) throw new Error('Not logged in.');
    const token = await user.getIdToken();
    return { headers: { Authorization: `Bearer ${token}` } };
  };

  // Calculate next paycheck date from two fixed pay days
  const calculateNextPaycheckDate = (day1, day2) => {
    const today = new Date();
    const currentMonth = today.getMonth();
    const currentYear = today.getFullYear();
    const days = [day1, day2].sort((a, b) => a - b);

    let nextDate = null;
    for (const day of days) {
      const d = new Date(currentYear, currentMonth, day);
      if (d.getDate() === day && d >= today) {
        if (!nextDate || d < nextDate) nextDate = d;
      }
    }

    if (!nextDate) {
      let m = currentMonth + 1;
      let y = currentYear;
      if (m > 11) { m = 0; y += 1; }
      nextDate = new Date(y, m, days[0]);
    }

    return nextDate;
  };

  // ── Data fetching ──────────────────────────────────────────────────────────

  useEffect(() => {
    const fetchData = async () => {
      try {
        const config = await getAuthHeader();

        // Fetch debt snapshot (credit card balances from Plaid)
        const debtPromise = axios
          .get(`${API_BASE_URL}/api/debt/snapshot`, config)
          .then((r) => setSnapshot(r.data))
          .catch((e) => {
            console.error('Failed to load debt snapshot:', e);
            // Don't block the screen if debt snapshot fails
            setSnapshot({ totalDebt: 0, accounts: [] });
          })
          .finally(() => setIsLoadingDebt(false));

        // Fetch base budget (paycheck - fixedCosts, before debt/savings)
        const nextPaycheckDate = calculateNextPaycheckDate(payDay1, payDay2);
        const basePromise = axios
          .post(
            `${API_BASE_URL}/api/budget/base`,
            { paycheckAmount, payDay1, payDay2, nextPaycheckDate },
            config,
          )
          .then((r) => setBaseBudget(r.data))
          .catch((e) => {
            console.error('Failed to load base budget:', e);
            // Fallback: compute locally if API fails
            setBaseBudget({
              paycheckAmount,
              fixedCostsRemaining: 0,
              baseRemaining: paycheckAmount,
            });
          })
          .finally(() => setIsLoadingBase(false));

        await Promise.all([debtPromise, basePromise]);
      } catch (e) {
        console.error('DebtOnboarding fetch error:', e);
        setLoadError('Could not load your data. Please try again.');
        setIsLoadingDebt(false);
        setIsLoadingBase(false);
      }
    };

    fetchData();
  }, []);

  // ── Derived values ─────────────────────────────────────────────────────────

  const isLoading = isLoadingDebt || isLoadingBase;
  const totalDebt = snapshot?.totalDebt ?? 0;
  const baseRemaining = baseBudget?.baseRemaining ?? paycheckAmount;
  const canPayInFull = totalDebt > 0 && baseRemaining >= totalDebt;

  // Resolve current selected amount (preset takes priority; custom overrides)
  const resolveSelectedAmount = () => {
    const custom = parseFloat(customInput);
    if (!isNaN(custom) && custom > 0) return custom;
    if (selectedPreset != null) return selectedPreset;
    return 0;
  };

  const selectedAmount = resolveSelectedAmount();

  // Live calculations
  const remainingAfterDebt = Math.max(0, baseRemaining - selectedAmount);
  const numberOfPaychecks =
    selectedAmount > 0 && totalDebt > 0
      ? Math.ceil(totalDebt / selectedAmount)
      : null;

  // ── Navigation ─────────────────────────────────────────────────────────────

  const goNext = (debtPerPaycheck) => {
    navigation.navigate('SavingsOnboarding', {
      paycheckAmount,
      payDay1,
      payDay2,
      debtPerPaycheck,
      baseRemaining,
      remainingAfterDebt: Math.round((baseRemaining - debtPerPaycheck) * 100) / 100,
      fixedCostsRemaining: baseBudget?.fixedCostsRemaining ?? 0,
    });
  };

  const handleContinue = () => {
    setInputError(null);

    if (selectedAmount === 0 && customInput.trim() === '' && selectedPreset === null) {
      // User hasn't selected anything — treat as skip
      goNext(0);
      return;
    }

    if (selectedAmount < 0 || isNaN(selectedAmount)) {
      setInputError('Please enter a valid amount.');
      return;
    }

    goNext(selectedAmount);
  };

  const handleSkip = () => goNext(0);

  const handlePayInFull = () => {
    setSelectedPreset(null);
    setCustomInput(totalDebt.toFixed(2));
    goNext(totalDebt);
  };

  const handlePresetPress = (amount) => {
    setInputError(null);
    setCustomInput(''); // clear custom when preset selected
    setSelectedPreset(amount === selectedPreset ? null : amount); // toggle
  };

  const handleCustomChange = (v) => {
    setInputError(null);
    setCustomInput(v);
    setSelectedPreset(null); // clear preset when typing custom
  };

  // ── Loading state ──────────────────────────────────────────────────────────

  if (isLoading) {
    return (
      <View style={styles.center}>
        <ActivityIndicator size="large" />
        <Text style={{ marginTop: 12, color: '#666' }}>Loading your balances…</Text>
      </View>
    );
  }

  if (loadError && !snapshot) {
    return (
      <View style={styles.center}>
        <Text style={styles.errorText}>{loadError}</Text>
        <Button mode="outlined" style={{ marginTop: 16 }} onPress={handleSkip}>
          Skip debt setup for now
        </Button>
      </View>
    );
  }

  // ── No debt detected ───────────────────────────────────────────────────────

  if (!snapshot || totalDebt <= 0) {
    return (
      <View style={styles.container}>
        <Text style={styles.title}>Debt (3/4)</Text>

        <Card style={styles.noDebtCard}>
          <Card.Content>
            <Text style={styles.noDebtText}>✅ No outstanding credit card balances detected.</Text>
            <Text style={[styles.bodyText, { marginTop: 6 }]}>
              We didn't find any credit card debt in your linked accounts.
            </Text>
          </Card.Content>
        </Card>

        <Button mode="contained" style={{ marginTop: 24 }} onPress={() => goNext(0)}>
          Continue
        </Button>
      </View>
    );
  }

  // ── Main screen ────────────────────────────────────────────────────────────

  return (
    <ScrollView contentContainerStyle={styles.container} keyboardShouldPersistTaps="handled">
      <Text style={styles.title}>Debt (3/4)</Text>

      {/* ── Available budget banner ── */}
      <Card style={styles.availableCard}>
        <Card.Content>
          <Text style={styles.availableLabel}>Available before debt & savings</Text>
          <Text style={[
            styles.availableAmount,
            baseRemaining < 0 && styles.negative,
          ]}>
            ${baseRemaining.toFixed(2)}
          </Text>
          <Text style={styles.availableNote}>
            (${paycheckAmount.toFixed(2)} paycheck − ${(baseBudget?.fixedCostsRemaining ?? 0).toFixed(2)} fixed costs)
          </Text>
        </Card.Content>
      </Card>

      {/* ── Collapsible debt accounts list ── */}
      <TouchableOpacity
        style={styles.collapseHeader}
        onPress={() => setIsExpanded((prev) => !prev)}
        activeOpacity={0.7}
      >
        <Text style={styles.collapseTitle}>
          {isExpanded ? '▼' : '▶'}{' '}
          Credit card debt — total:{' '}
          <Text style={styles.highlight}>${totalDebt.toFixed(2)}</Text>
        </Text>
      </TouchableOpacity>

      {isExpanded && (
        <View style={styles.accountList}>
          {snapshot.accounts?.map((acct, idx) => (
            <Card key={idx} style={styles.accountCard}>
              <Card.Content style={styles.accountRow}>
                <View style={{ flex: 1 }}>
                  <Text style={styles.accountName}>
                    {acct.institutionName} — {acct.accountName}
                    {acct.mask ? ` ••••${acct.mask}` : ''}
                  </Text>
                </View>
                <Text style={styles.accountBalance}>${acct.currentBalance.toFixed(2)}</Text>
              </Card.Content>
            </Card>
          ))}
        </View>
      )}

      {/* ── "Pay in full" option ── */}
      {canPayInFull && (
        <Card style={styles.payInFullCard}>
          <Card.Content>
            <Text style={styles.payInFullTitle}>🎉 You can pay off all debt this paycheck!</Text>
            <Text style={styles.payInFullBody}>
              Your available budget (${baseRemaining.toFixed(2)}) covers your total debt (${totalDebt.toFixed(2)}).
            </Text>
            <Button
              mode="contained"
              style={styles.payInFullButton}
              onPress={handlePayInFull}
            >
              Pay off all ${totalDebt.toFixed(2)} now
            </Button>
          </Card.Content>
        </Card>
      )}

      {/* ── Payment selection ── */}
      <Text style={styles.sectionTitle}>How much per paycheck toward debt?</Text>
      <Text style={styles.bodyText}>
        Choose a preset amount or enter a custom value.
      </Text>

      {/* Preset buttons */}
      <View style={styles.presetRow}>
        {PAYMENT_PRESETS.map((amount) => (
          <TouchableOpacity
            key={amount}
            style={[
              styles.presetButton,
              selectedPreset === amount && styles.presetButtonActive,
            ]}
            onPress={() => handlePresetPress(amount)}
          >
            <Text style={[
              styles.presetLabel,
              selectedPreset === amount && styles.presetLabelActive,
            ]}>
              ${amount}
            </Text>
          </TouchableOpacity>
        ))}
      </View>

      {/* Custom input */}
      <TextInput
        mode="outlined"
        label="Custom amount per paycheck ($)"
        value={customInput}
        onChangeText={handleCustomChange}
        keyboardType="numeric"
        style={{ marginTop: 12 }}
        placeholder="e.g. 250"
      />

      {inputError && <Text style={styles.errorText}>{inputError}</Text>}

      {/* ── Payoff timeline ── */}
      {selectedAmount > 0 && numberOfPaychecks != null && (
        <Card style={styles.timelineCard}>
          <Card.Content>
            <Text style={styles.timelineText}>
              💳 At{' '}
              <Text style={styles.highlight}>${selectedAmount.toFixed(2)}</Text>
              {' '}per paycheck, you'll pay off{' '}
              <Text style={styles.highlight}>${totalDebt.toFixed(2)}</Text>
              {' '}in debt in{' '}
              <Text style={styles.highlight}>
                {numberOfPaychecks} {numberOfPaychecks === 1 ? 'paycheck' : 'paychecks'}
              </Text>
              .
            </Text>
          </Card.Content>
        </Card>
      )}

      {/* ── Live remaining preview ── */}
      {selectedAmount > 0 && (
        <Card style={styles.previewCard}>
          <Card.Content>
            <Text style={styles.previewTitle}>After debt payment</Text>
            <Divider style={{ marginVertical: 8 }} />
            <View style={styles.previewRow}>
              <Text style={styles.previewLabel}>Available (base)</Text>
              <Text style={styles.previewValue}>${baseRemaining.toFixed(2)}</Text>
            </View>
            <View style={styles.previewRow}>
              <Text style={styles.previewLabel}>Debt payment</Text>
              <Text style={[styles.previewValue, styles.deduction]}>
                −${selectedAmount.toFixed(2)}
              </Text>
            </View>
            <Divider style={{ marginVertical: 8 }} />
            <View style={styles.previewRow}>
              <Text style={[styles.previewLabel, styles.bold]}>Remaining for savings & spending</Text>
              <Text style={[
                styles.previewValue,
                styles.bold,
                remainingAfterDebt < 0 && styles.negative,
              ]}>
                ${remainingAfterDebt.toFixed(2)}
              </Text>
            </View>
            {remainingAfterDebt < 0 && (
              <Text style={styles.warningNote}>
                ⚠️ This debt payment exceeds your available budget. Consider a lower amount.
              </Text>
            )}
          </Card.Content>
        </Card>
      )}

      {/* ── Action buttons ── */}
      <Button
        mode="contained"
        style={styles.continueButton}
        onPress={handleContinue}
      >
        {selectedAmount > 0
          ? `Continue with $${selectedAmount.toFixed(2)} / paycheck`
          : 'Continue (no debt payment)'}
      </Button>

      <Button mode="text" style={{ marginTop: 4 }} onPress={handleSkip}>
        Skip — no debt payment for now
      </Button>
    </ScrollView>
  );
}

const styles = StyleSheet.create({
  container: {
    padding: 20,
    paddingBottom: 40,
  },
  center: {
    flex: 1,
    justifyContent: 'center',
    alignItems: 'center',
    padding: 24,
  },
  title: {
    fontSize: 22,
    fontWeight: '700',
    marginBottom: 16,
    color: '#1a1a1a',
  },

  // Available budget banner
  availableCard: {
    backgroundColor: '#e8f5e9',
    borderRadius: 14,
    marginBottom: 20,
  },
  availableLabel: {
    fontSize: 13,
    color: '#444',
    marginBottom: 4,
  },
  availableAmount: {
    fontSize: 40,
    fontWeight: 'bold',
    color: '#2e7d32',
    letterSpacing: -0.5,
  },
  availableNote: {
    fontSize: 12,
    color: '#666',
    marginTop: 4,
  },

  // Collapsible header
  collapseHeader: {
    paddingVertical: 12,
    paddingHorizontal: 4,
    borderBottomWidth: 1,
    borderBottomColor: '#eee',
    marginBottom: 8,
  },
  collapseTitle: {
    fontSize: 15,
    color: '#333',
  },

  // Account list
  accountList: {
    marginBottom: 16,
  },
  accountCard: {
    marginTop: 8,
    backgroundColor: '#fafafa',
  },
  accountRow: {
    flexDirection: 'row',
    alignItems: 'center',
  },
  accountName: {
    fontSize: 14,
    color: '#444',
  },
  accountBalance: {
    fontSize: 14,
    fontWeight: '600',
    color: '#c62828',
  },

  // Pay in full
  payInFullCard: {
    backgroundColor: '#e3f2fd',
    borderRadius: 14,
    marginBottom: 20,
    borderLeftWidth: 4,
    borderLeftColor: '#1976d2',
  },
  payInFullTitle: {
    fontSize: 15,
    fontWeight: '600',
    color: '#1565c0',
    marginBottom: 6,
  },
  payInFullBody: {
    fontSize: 13,
    color: '#1976d2',
    marginBottom: 12,
  },
  payInFullButton: {
    backgroundColor: '#1976d2',
  },

  // Section header
  sectionTitle: {
    fontSize: 16,
    fontWeight: '600',
    marginTop: 16,
    marginBottom: 6,
    color: '#222',
  },
  bodyText: {
    fontSize: 13,
    color: '#666',
    marginBottom: 10,
  },

  // Preset buttons
  presetRow: {
    flexDirection: 'row',
    flexWrap: 'wrap',
    gap: 8,
    marginTop: 8,
  },
  presetButton: {
    borderWidth: 1.5,
    borderColor: '#6200ee',
    borderRadius: 8,
    paddingHorizontal: 18,
    paddingVertical: 10,
    backgroundColor: '#fff',
  },
  presetButtonActive: {
    backgroundColor: '#6200ee',
  },
  presetLabel: {
    fontSize: 14,
    color: '#6200ee',
    fontWeight: '600',
  },
  presetLabelActive: {
    color: '#fff',
  },

  // Payoff timeline
  timelineCard: {
    marginTop: 16,
    backgroundColor: '#f3e8ff',
    borderRadius: 12,
  },
  timelineText: {
    fontSize: 14,
    color: '#333',
    lineHeight: 22,
  },

  // Live preview
  previewCard: {
    marginTop: 16,
    backgroundColor: '#f5f5f5',
    borderRadius: 12,
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
  deduction: {
    color: '#c62828',
  },

  // Warning
  warningNote: {
    fontSize: 12,
    color: '#b71c1c',
    marginTop: 8,
    fontStyle: 'italic',
  },

  // No debt
  noDebtCard: {
    backgroundColor: '#e8f5e9',
    borderRadius: 12,
    marginTop: 8,
  },
  noDebtText: {
    fontSize: 16,
    fontWeight: '600',
    color: '#2e7d32',
  },

  // Shared
  highlight: {
    fontWeight: '700',
  },
  bold: {
    fontWeight: '700',
    color: '#1a1a1a',
  },
  negative: {
    color: '#c62828',
  },
  errorText: {
    color: '#c62828',
    fontSize: 13,
    marginTop: 6,
  },

  continueButton: {
    marginTop: 24,
    borderRadius: 10,
  },
});