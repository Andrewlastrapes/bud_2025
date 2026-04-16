import React, { useEffect, useState } from 'react';
import {
  View,
  ScrollView,
  StyleSheet,
  TouchableOpacity,
  StatusBar,
} from 'react-native';
import { Text, Card, ActivityIndicator, Button, TextInput, Divider } from 'react-native-paper';
import axios from 'axios';
import { auth } from '../../firebaseConfig';
import { API_BASE_URL } from '../../config/api';

// ─── Design tokens ────────────────────────────────────────────────────────────
const C = {
  primary:   '#5B21B6',
  primaryLt: '#7C3AED',
  success:   '#10B981',
  successBg: '#ECFDF5',
  danger:    '#EF4444',
  dangerBg:  '#FEF2F2',
  warning:   '#F59E0B',
  warnBg:    '#FFFBEB',
  infoBg:    '#EDE9FE',
  blue:      '#1D4ED8',
  blueBg:    '#EFF6FF',
  surface:   '#FFFFFF',
  bg:        '#F5F3FF',
  text:      '#1E1B4B',
  muted:     '#6B7280',
  border:    '#E5E7EB',
};

const PRESETS = [100, 200, 300, 500];

// ─── Interest-range math ───────────────────────────────────────────────────────
// Returns [{apr, months, totalInterest}] for 10 %, 20 %, 30 % APR.
//
// Assumptions:
//   • Interest compounds monthly (standard credit-card model)
//   • monthlyPayment = selectedAmount × 2  (caller converts paychecks → months)
//   • When monthlyPayment = 0 a 24-month illustration is used instead
//   • Formula: standard fixed-payment amortization
//       n = ceil(−ln(1 − P·r / M) / ln(1+r))
//       totalInterest = n·M − P
function calcInterestScenarios(principal, monthlyPayment) {
  return [0.10, 0.20, 0.30].map(apr => {
    const r = apr / 12; // monthly rate

    if (monthlyPayment > 0) {
      if (monthlyPayment <= principal * r) {
        // Payment doesn't cover accruing interest at this rate
        return { apr: apr * 100, months: null, totalInterest: null, monthlyInterest: principal * r };
      }
      const months = Math.ceil(
        -Math.log(1 - (principal * r) / monthlyPayment) / Math.log(1 + r),
      );
      return { apr: apr * 100, months, totalInterest: Math.max(0, months * monthlyPayment - principal) };
    }

    // No payment selected → 24-month illustration
    const n   = 24;
    const pmt = r > 0
      ? (principal * r * Math.pow(1 + r, n)) / (Math.pow(1 + r, n) - 1)
      : principal / n;
    return { apr: apr * 100, months: n, totalInterest: Math.max(0, n * pmt - principal) };
  });
}

// ─── InterestRangeCard component ──────────────────────────────────────────────
function InterestRangeCard({ principal, monthlyPayment }) {
  if (!principal || principal <= 0) return null;

  const scenarios      = calcInterestScenarios(principal, monthlyPayment);
  const isIllustrative = !monthlyPayment || monthlyPayment <= 0;

  return (
    <View style={ir.card}>
      <Text style={ir.title}>💡 Interest Cost Scenarios</Text>
      <Text style={ir.sub}>
        {isIllustrative
          ? `$${principal.toFixed(0)} balance · 24-month illustration`
          : `$${(monthlyPayment / 2).toFixed(0)}/paycheck · ~2 paychecks/month`}
      </Text>

      {scenarios.map(({ apr, months, totalInterest, monthlyInterest }) => (
        <View key={apr} style={ir.row}>
          <Text style={ir.aprLabel}>{apr}% APR</Text>
          {totalInterest !== null ? (
            <Text style={ir.cost}>
              ~${totalInterest.toFixed(0)} in interest
              {'  '}
              <Text style={ir.months}>({months} mo)</Text>
            </Text>
          ) : (
            <Text style={[ir.cost, ir.tooLow]}>
              ⚠️ ~${monthlyInterest.toFixed(0)}/mo interest — payment too low
            </Text>
          )}
        </View>
      ))}

      <Text style={ir.disclaimer}>
        Estimate only · actual cost depends on your card's real APR.
      </Text>
    </View>
  );
}

function StepDots({ current, total }) {
  return (
    <View style={dot.row}>
      {Array.from({ length: total }).map((_, i) => (
        <View key={i} style={[dot.dot, i + 1 === current && dot.active]} />
      ))}
    </View>
  );
}
const dot = StyleSheet.create({
  row:    { flexDirection: 'row', gap: 6, justifyContent: 'center', marginBottom: 20 },
  dot:    { width: 8, height: 8, borderRadius: 4, backgroundColor: '#DDD6FE' },
  active: { width: 24, backgroundColor: C.primary },
});

export default function DebtOnboardingScreen({ navigation, route }) {
  const [snapshot,      setSnapshot]      = useState(null);
  const [baseBudget,    setBaseBudget]    = useState(null);
  const [loadingDebt,   setLoadingDebt]   = useState(true);
  const [loadingBase,   setLoadingBase]   = useState(true);
  const [loadError,     setLoadError]     = useState(null);
  const [isExpanded,    setIsExpanded]    = useState(false);
  const [selectedPreset, setSelectedPreset] = useState(null);
  const [customInput,   setCustomInput]   = useState('');
  const [inputError,    setInputError]    = useState(null);

  const paycheckAmount = route.params?.paycheckAmount ?? 0;
  const payDay1        = route.params?.payDay1 ?? 1;
  const payDay2        = route.params?.payDay2 ?? 15;

  const getAuthHeader = async () => {
    const user = auth.currentUser;
    if (!user) throw new Error('Not logged in.');
    const token = await user.getIdToken();
    return { headers: { Authorization: `Bearer ${token}` } };
  };

  const calcNextPaycheck = (d1, d2) => {
    const today = new Date();
    const days  = [d1, d2].sort((a, b) => a - b);
    let nextDate = null;
    for (const day of days) {
      const d = new Date(today.getFullYear(), today.getMonth(), day);
      if (d.getDate() === day && d >= today) {
        if (!nextDate || d < nextDate) nextDate = d;
      }
    }
    if (!nextDate) {
      let m = today.getMonth() + 1, y = today.getFullYear();
      if (m > 11) { m = 0; y++; }
      nextDate = new Date(y, m, days[0]);
    }
    return nextDate;
  };

  useEffect(() => {
    const load = async () => {
      try {
        const config = await getAuthHeader();
        const p1 = axios.get(`${API_BASE_URL}/api/debt/snapshot`, config)
          .then(r => setSnapshot(r.data))
          .catch(() => setSnapshot({ totalDebt: 0, accounts: [] }))
          .finally(() => setLoadingDebt(false));
        const p2 = axios.post(`${API_BASE_URL}/api/budget/base`,
          { paycheckAmount, payDay1, payDay2, nextPaycheckDate: calcNextPaycheck(payDay1, payDay2) }, config)
          .then(r => setBaseBudget(r.data))
          .catch(() => setBaseBudget({ paycheckAmount, fixedCostsRemaining: 0, baseRemaining: paycheckAmount }))
          .finally(() => setLoadingBase(false));
        await Promise.all([p1, p2]);
      } catch (e) {
        setLoadError('Could not load your data.');
        setLoadingDebt(false);
        setLoadingBase(false);
      }
    };
    load();
  }, []);

  const isLoading     = loadingDebt || loadingBase;
  const totalDebt     = snapshot?.totalDebt ?? 0;
  const baseRemaining = baseBudget?.baseRemaining ?? paycheckAmount;
  const canPayInFull  = totalDebt > 0 && baseRemaining >= totalDebt;

  const getAmount = () => {
    const c = parseFloat(customInput);
    if (!isNaN(c) && c > 0) return c;
    if (selectedPreset != null) return selectedPreset;
    return 0;
  };

  const selectedAmount    = getAmount();
  const remainAfterDebt   = baseRemaining - selectedAmount;
  const numPaychecks      = selectedAmount > 0 && totalDebt > 0
    ? Math.ceil(totalDebt / selectedAmount) : null;

  const goNext = (debt) => navigation.navigate('SavingsOnboarding', {
    paycheckAmount,
    payDay1,
    payDay2,
    debtPerPaycheck: debt,
    baseRemaining,
    remainingAfterDebt: Math.round((baseRemaining - debt) * 100) / 100,
    fixedCostsRemaining: baseBudget?.fixedCostsRemaining ?? 0,
  });

  const handleContinue = () => {
    setInputError(null);
    if (selectedAmount < 0 || isNaN(selectedAmount)) { setInputError('Enter a valid amount.'); return; }
    goNext(selectedAmount);
  };

  if (isLoading) {
    return (
      <View style={s.center}>
        <ActivityIndicator size="large" color={C.primary} />
        <Text style={s.loadText}>Loading your accounts…</Text>
      </View>
    );
  }

  if (!snapshot || totalDebt <= 0) {
    return (
      <View style={[s.center, { padding: 32 }]}>
        <View style={s.noDebtIcon}><Text style={{ fontSize: 32 }}>✅</Text></View>
        <Text style={s.noDebtTitle}>No debt detected</Text>
        <Text style={s.noDebtBody}>
          We didn't find any outstanding credit card balances in your linked accounts.
        </Text>
        <Button mode="contained" onPress={() => goNext(0)} style={s.btn}
          contentStyle={s.btnContent} labelStyle={s.btnLabel} buttonColor={C.primary}>
          Continue →
        </Button>
      </View>
    );
  }

  return (
    <ScrollView style={s.safe} contentContainerStyle={s.scroll} keyboardShouldPersistTaps="handled">
      <StatusBar barStyle="dark-content" />
      <StepDots current={3} total={4} />

      <Text style={s.eyebrow}>STEP 3 OF 4</Text>
      <Text style={s.heading}>Credit Card Debt</Text>
      <Text style={s.sub}>Choose how much to put toward debt each paycheck.</Text>

      {/* Available banner */}
      <View style={s.availCard}>
        <Text style={s.availLabel}>Available before debt & savings</Text>
        <Text style={[s.availAmt, baseRemaining < 0 && { color: C.danger }]}>
          ${baseRemaining.toFixed(2)}
        </Text>
        <Text style={s.availNote}>
          ${paycheckAmount.toFixed(2)} paycheck − ${(baseBudget?.fixedCostsRemaining ?? 0).toFixed(2)} fixed costs
        </Text>
      </View>

      {/* Collapsible debt list */}
      <TouchableOpacity style={s.collapseBtn} onPress={() => setIsExpanded(v => !v)} activeOpacity={0.7}>
        <View style={{ flex: 1 }}>
          <Text style={s.collapseLabel}>
            Total debt: <Text style={{ color: C.danger, fontWeight: '700' }}>${totalDebt.toFixed(2)}</Text>
          </Text>
        </View>
        <Text style={s.chevron}>{isExpanded ? '▲' : '▼'}</Text>
      </TouchableOpacity>

      {isExpanded && (
        <View style={s.accountList}>
          {snapshot.accounts?.map((a, i) => (
            <View key={i} style={s.accountRow}>
              <View style={{ flex: 1 }}>
                <Text style={s.acctName}>{a.institutionName} — {a.accountName}{a.mask ? ` ••••${a.mask}` : ''}</Text>
              </View>
              <Text style={s.acctBal}>${a.currentBalance.toFixed(2)}</Text>
            </View>
          ))}
        </View>
      )}

      {/* Pay in full */}
      {canPayInFull && (
        <View style={s.fullCard}>
          <Text style={s.fullTitle}>🎉 You can clear all debt this paycheck!</Text>
          <Text style={s.fullBody}>
            Your available budget (${baseRemaining.toFixed(2)}) covers your total debt (${totalDebt.toFixed(2)}).
          </Text>
          <Button mode="contained" onPress={() => { setCustomInput(totalDebt.toFixed(2)); goNext(totalDebt); }}
            style={[s.btn, { marginTop: 12 }]} contentStyle={s.btnContent}
            labelStyle={s.btnLabel} buttonColor={C.blue}>
            Pay off all ${totalDebt.toFixed(2)} now
          </Button>
        </View>
      )}

      {/* Presets */}
      <Text style={s.sectionTitle}>Payment per paycheck</Text>
      <View style={s.presetRow}>
        {PRESETS.map(amt => (
          <TouchableOpacity
            key={amt}
            style={[s.preset, selectedPreset === amt && s.presetActive]}
            onPress={() => { setInputError(null); setCustomInput(''); setSelectedPreset(selectedPreset === amt ? null : amt); }}
          >
            <Text style={[s.presetTxt, selectedPreset === amt && s.presetTxtActive]}>${amt}</Text>
          </TouchableOpacity>
        ))}
      </View>

      <TextInput
        mode="outlined"
        label="Custom amount ($)"
        value={customInput}
        onChangeText={v => { setInputError(null); setCustomInput(v); setSelectedPreset(null); }}
        keyboardType="numeric"
        style={s.input}
        outlineStyle={s.inputOutline}
        placeholder="e.g. 250"
      />
      {inputError && <Text style={s.errorTxt}>{inputError}</Text>}

      {/* Payoff timeline */}
      {numPaychecks != null && selectedAmount > 0 && (
        <View style={s.timelineCard}>
          <Text style={s.timelineTxt}>
            💳 <Text style={{ fontWeight: '700' }}>${selectedAmount.toFixed(2)}/paycheck</Text> pays off{' '}
            <Text style={{ fontWeight: '700' }}>${totalDebt.toFixed(2)}</Text> in{' '}
            <Text style={{ fontWeight: '700', color: C.primary }}>{numPaychecks} {numPaychecks === 1 ? 'paycheck' : 'paychecks'}</Text>.
          </Text>
        </View>
      )}

      {/* Interest range scenarios — always shown when debt exists */}
      <InterestRangeCard
        principal={totalDebt}
        monthlyPayment={selectedAmount > 0 ? selectedAmount * 2 : 0}
      />

      {/* Live preview */}
      {selectedAmount > 0 && (
        <View style={[s.previewCard, remainAfterDebt < 0 && s.previewCardDanger]}>
          <Text style={s.previewTitle}>After debt payment</Text>
          <Divider style={{ marginVertical: 10, backgroundColor: '#E5E7EB' }} />
          <View style={s.previewRow}><Text style={s.pl}>Available</Text><Text style={s.pv}>${baseRemaining.toFixed(2)}</Text></View>
          <View style={s.previewRow}><Text style={s.pl}>Debt</Text><Text style={[s.pv, { color: C.danger }]}>−${selectedAmount.toFixed(2)}</Text></View>
          <Divider style={{ marginVertical: 10, backgroundColor: '#E5E7EB' }} />
          <View style={s.previewRow}>
            <Text style={[s.pl, s.bold]}>Left for savings & spending</Text>
            <Text style={[s.pv, s.bold, remainAfterDebt < 0 && { color: C.danger }]}>${remainAfterDebt.toFixed(2)}</Text>
          </View>
          {remainAfterDebt < 0 && <Text style={s.warnTxt}>⚠️ Amount exceeds available budget.</Text>}
        </View>
      )}

      <Button mode="contained" onPress={handleContinue} style={s.btn}
        contentStyle={s.btnContent} labelStyle={s.btnLabel} buttonColor={C.primary}>
        {selectedAmount > 0 ? `Continue with $${selectedAmount.toFixed(2)}/paycheck →` : 'Continue (no payment) →'}
      </Button>
      <Button mode="text" onPress={() => goNext(0)} style={{ marginTop: 4 }} textColor={C.muted}>
        Skip — no debt payment
      </Button>
    </ScrollView>
  );
}

const s = StyleSheet.create({
  safe:   { flex: 1, backgroundColor: C.bg },
  scroll: { padding: 24, paddingBottom: 56 },
  center: { flex: 1, justifyContent: 'center', alignItems: 'center', backgroundColor: C.bg },

  loadText: { marginTop: 14, color: C.muted, fontSize: 15 },

  eyebrow: { fontSize: 11, fontWeight: '700', letterSpacing: 1.5, color: C.primaryLt, marginBottom: 6 },
  heading: { fontSize: 28, fontWeight: '800', color: C.text, marginBottom: 6, letterSpacing: -0.5 },
  sub:     { fontSize: 15, color: C.muted, lineHeight: 22, marginBottom: 20 },

  // Available banner
  availCard: {
    backgroundColor: C.successBg,
    borderRadius: 20,
    padding: 20,
    marginBottom: 16,
    borderWidth: 1,
    borderColor: '#A7F3D0',
  },
  availLabel: { fontSize: 12, fontWeight: '600', color: '#065F46', letterSpacing: 0.3, marginBottom: 4 },
  availAmt:   { fontSize: 38, fontWeight: '800', color: C.success, letterSpacing: -1 },
  availNote:  { fontSize: 12, color: '#6EE7B7', marginTop: 4 },

  // Collapsible
  collapseBtn: {
    flexDirection: 'row', alignItems: 'center',
    backgroundColor: C.surface, borderRadius: 14, padding: 16,
    marginBottom: 2, borderWidth: 1, borderColor: C.border,
  },
  collapseLabel: { fontSize: 15, color: C.text, fontWeight: '500' },
  chevron:       { fontSize: 13, color: C.muted },

  // Account list
  accountList: { backgroundColor: C.surface, borderRadius: 14, overflow: 'hidden', marginBottom: 16, borderWidth: 1, borderColor: C.border },
  accountRow:  { flexDirection: 'row', alignItems: 'center', padding: 14, borderBottomWidth: 1, borderBottomColor: '#F3F4F6' },
  acctName:    { fontSize: 13, color: C.text },
  acctBal:     { fontSize: 14, fontWeight: '700', color: C.danger },

  // Pay in full
  fullCard: {
    backgroundColor: C.blueBg, borderRadius: 20, padding: 18, marginBottom: 20,
    borderWidth: 1, borderColor: '#BFDBFE',
  },
  fullTitle: { fontSize: 15, fontWeight: '700', color: C.blue, marginBottom: 6 },
  fullBody:  { fontSize: 13, color: '#3B82F6', lineHeight: 20 },

  sectionTitle: { fontSize: 15, fontWeight: '700', color: C.text, marginTop: 8, marginBottom: 12 },

  // Presets
  presetRow:      { flexDirection: 'row', gap: 10, marginBottom: 14, flexWrap: 'wrap' },
  preset:         { borderWidth: 1.5, borderColor: C.primaryLt, borderRadius: 12, paddingHorizontal: 20, paddingVertical: 11, backgroundColor: C.surface },
  presetActive:   { backgroundColor: C.primary, borderColor: C.primary },
  presetTxt:      { fontSize: 15, fontWeight: '700', color: C.primaryLt },
  presetTxtActive:{ color: '#FFFFFF' },

  input:        { backgroundColor: C.surface, marginBottom: 4 },
  inputOutline: { borderRadius: 12, borderColor: C.border },
  errorTxt:     { color: C.danger, fontSize: 13, marginBottom: 8, marginTop: 2 },

  // Timeline
  timelineCard: { backgroundColor: C.infoBg, borderRadius: 14, padding: 14, marginTop: 14 },
  timelineTxt:  { fontSize: 14, color: C.primary, lineHeight: 22 },

  // Preview
  previewCard:      { backgroundColor: C.surface, borderRadius: 18, padding: 18, marginTop: 14, borderWidth: 1, borderColor: C.border },
  previewCardDanger:{ borderColor: '#FECACA' },
  previewTitle:     { fontSize: 13, fontWeight: '700', color: C.muted, letterSpacing: 0.5 },
  previewRow:       { flexDirection: 'row', justifyContent: 'space-between', paddingVertical: 3 },
  pl:               { fontSize: 14, color: C.muted, flex: 1 },
  pv:               { fontSize: 14, color: C.text, fontVariant: ['tabular-nums'] },
  warnTxt:          { fontSize: 12, color: C.danger, marginTop: 8, fontStyle: 'italic' },

  // No debt
  noDebtIcon:  { width: 72, height: 72, borderRadius: 36, backgroundColor: C.successBg, alignItems: 'center', justifyContent: 'center', marginBottom: 16 },
  noDebtTitle: { fontSize: 22, fontWeight: '800', color: C.text, marginBottom: 8 },
  noDebtBody:  { fontSize: 15, color: C.muted, textAlign: 'center', lineHeight: 22, marginBottom: 28, paddingHorizontal: 8 },

  bold: { fontWeight: '700', color: C.text },
  btn: { borderRadius: 16, marginTop: 20 },
  btnContent:  { paddingVertical: 8 },
  btnLabel:    { fontSize: 16, fontWeight: '700', letterSpacing: 0.3 },
});

// ─── Styles for InterestRangeCard ──────────────────────────────────────────────
const ir = StyleSheet.create({
  card: {
    backgroundColor: C.warnBg,
    borderRadius: 18,
    padding: 18,
    marginTop: 14,
    borderWidth: 1,
    borderColor: '#FDE68A',
  },
  title:      { fontSize: 14, fontWeight: '700', color: '#92400E', marginBottom: 4 },
  sub:        { fontSize: 12, color: '#B45309', marginBottom: 12 },
  row: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'center',
    paddingVertical: 7,
    borderBottomWidth: 1,
    borderBottomColor: '#FDE68A',
  },
  aprLabel:   { fontSize: 13, fontWeight: '700', color: '#78350F', width: 72 },
  cost:       { fontSize: 13, color: '#92400E', flex: 1, textAlign: 'right' },
  months:     { fontSize: 11, color: '#B45309' },
  tooLow:     { color: C.danger, fontSize: 12 },
  disclaimer: { fontSize: 11, color: '#B45309', marginTop: 10, fontStyle: 'italic', textAlign: 'center' },
});
