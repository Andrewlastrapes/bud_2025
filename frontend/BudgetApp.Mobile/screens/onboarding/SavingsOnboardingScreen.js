import React, { useState } from 'react';
import {
  View,
  ScrollView,
  StyleSheet,
  Alert,
  Keyboard,
  TouchableWithoutFeedback,
  StatusBar,
} from 'react-native';
import { Text, TextInput, Button, Divider } from 'react-native-paper';
import { SafeAreaView } from 'react-native-safe-area-context';
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
  warning:   '#F59E0B',
  warnBg:    '#FFFBEB',
  surface:   '#FFFFFF',
  bg:        '#F5F3FF',
  text:      '#1E1B4B',
  muted:     '#6B7280',
  border:    '#E5E7EB',
};

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

export default function SavingsOnboardingScreen({ navigation, route }) {
  const [savingsInput, setSavingsInput] = useState('');
  const [isSaving,     setIsSaving]     = useState(false);
  const [inputError,   setInputError]   = useState(null);

  const paycheckAmount      = route.params?.paycheckAmount      ?? 0;
  const payDay1             = route.params?.payDay1             ?? 1;
  const payDay2             = route.params?.payDay2             ?? 15;
  const debtPerPaycheck     = route.params?.debtPerPaycheck     ?? 0;
  const remainingAfterDebt  = route.params?.remainingAfterDebt  ?? (paycheckAmount - debtPerPaycheck);
  const fixedCostsRemaining = route.params?.fixedCostsRemaining ?? 0;
  const hasDebt             = debtPerPaycheck > 0;

  const calcNextPaycheck = (d1, d2) => {
    const today = new Date();
    const days  = [d1, d2].sort((a, b) => a - b);
    let next = null;
    for (const day of days) {
      const d = new Date(today.getFullYear(), today.getMonth(), day);
      if (d.getDate() === day && d >= today) { if (!next || d < next) next = d; }
    }
    if (!next) {
      let m = today.getMonth() + 1, y = today.getFullYear();
      if (m > 11) { m = 0; y++; }
      next = new Date(y, m, days[0]);
    }
    return next;
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
      const nextPaycheckDate = calcNextPaycheck(payDay1, payDay2);

      if (savingsPerPaycheck > 0) {
        await axios.post(`${API_BASE_URL}/api/fixed-costs`,
          { name: 'Savings Goal', amount: savingsPerPaycheck, category: 'Savings', type: 'manual', nextDueDate: null }, config);
      }

      const { data } = await axios.post(`${API_BASE_URL}/api/budget/finalize`,
        { paycheckAmount, nextPaycheckDate, payDay1, payDay2, debtPerPaycheck: debtPerPaycheck || null }, config);

      navigation.navigate('DynamicAmountFinal', {
        remainingToSpend:      data.remainingToSpend,
        dynamicSpendableAmount: data.dynamicSpendableAmount ?? data.remainingToSpend,
        paycheckAmount:        data.paycheckAmount,
        fixedCostsRemaining:   data.fixedCostsRemaining,
        baseRemaining:         data.baseRemaining,
        debtPerPaycheck:       data.debtPerPaycheck,
        savingsContribution:   data.savingsContribution,
        explanation:           data.explanation,
      });
    } catch (e) {
      console.error('Finalization failed:', e);
      Alert.alert('Error', e.message || 'An unknown error occurred.');
    }
    setIsSaving(false);
  };

  const handleContinue = () => {
    setInputError(null);
    const trimmed = savingsInput.trim();
    if (trimmed === '') { handleFinalize(0); return; }
    const v = parseFloat(trimmed);
    if (isNaN(v) || v < 0) { setInputError('Enter a valid amount or leave blank for $0.'); return; }
    handleFinalize(v);
  };

  const savingsValue   = parseFloat(savingsInput) || 0;
  const finalRemaining = Math.round((remainingAfterDebt - savingsValue) * 100) / 100;

  return (
    <TouchableWithoutFeedback onPress={Keyboard.dismiss} accessible={false}>
      <SafeAreaView style={s.safe}>
        <StatusBar barStyle="dark-content" backgroundColor={C.bg} />
        <ScrollView contentContainerStyle={s.scroll}>

          <StepDots current={4} total={4} />

          <Text style={s.eyebrow}>STEP 4 OF 4</Text>
          <Text style={s.heading}>Savings</Text>
          <Text style={s.sub}>Set aside money each paycheck before you spend it.</Text>

          {/* After-debt available */}
          <View style={s.availCard}>
            <Text style={s.availLabel}>
              {hasDebt ? 'After debt payment, you have' : 'After fixed costs, you have'}
            </Text>
            <Text style={[s.availAmt, remainingAfterDebt < 0 && { color: C.danger }]}>
              ${remainingAfterDebt.toFixed(2)}
            </Text>
            <Text style={s.availNote}>
              {hasDebt
                ? `$${paycheckAmount.toFixed(2)} − $${fixedCostsRemaining.toFixed(2)} fixed − $${debtPerPaycheck.toFixed(2)} debt`
                : `$${paycheckAmount.toFixed(2)} − $${fixedCostsRemaining.toFixed(2)} fixed costs`}
            </Text>
          </View>

          {/* Debt warning */}
          {hasDebt && (
            <View style={s.warnCard}>
              <Text style={s.warnIcon}>⚠️</Text>
              <View style={{ flex: 1 }}>
                <Text style={s.warnTitle}>Paying off debt</Text>
                <Text style={s.warnBody}>
                  You're already putting <Text style={{ fontWeight: '700' }}>${debtPerPaycheck.toFixed(2)}</Text>/paycheck toward debt.
                  Consider pausing savings until your debt is reduced — but the choice is yours.
                </Text>
              </View>
            </View>
          )}

          <Text style={s.fieldLabel}>Savings per paycheck</Text>
          <Text style={s.hint}>Leave blank or $0 to skip. You can change this anytime.</Text>

          <TextInput
            mode="outlined"
            label="$0.00"
            value={savingsInput}
            onChangeText={v => { setInputError(null); setSavingsInput(v); }}
            keyboardType="numeric"
            style={s.input}
            outlineStyle={s.inputOutline}
            disabled={isSaving}
            left={<TextInput.Affix text="$" />}
          />
          {inputError && <Text style={s.errorTxt}>{inputError}</Text>}

          {/* Breakdown preview */}
          <View style={s.previewCard}>
            <Text style={s.previewHead}>Budget breakdown</Text>
            <Divider style={{ marginVertical: 10, backgroundColor: '#E5E7EB' }} />

            <View style={s.row}><Text style={s.pl}>Income</Text><Text style={[s.pv, { color: C.success }]}>+${paycheckAmount.toFixed(2)}</Text></View>
            {fixedCostsRemaining > 0 && <View style={s.row}><Text style={s.pl}>Fixed costs</Text><Text style={[s.pv, { color: C.danger }]}>−${fixedCostsRemaining.toFixed(2)}</Text></View>}
            {hasDebt && <View style={s.row}><Text style={s.pl}>Debt payoff</Text><Text style={[s.pv, { color: C.danger }]}>−${debtPerPaycheck.toFixed(2)}</Text></View>}
            {savingsValue > 0 && <View style={s.row}><Text style={s.pl}>Savings</Text><Text style={[s.pv, { color: C.danger }]}>−${savingsValue.toFixed(2)}</Text></View>}

            <Divider style={{ marginVertical: 10, backgroundColor: '#E5E7EB' }} />

            <View style={s.row}>
              <Text style={[s.pl, { fontWeight: '700', color: C.text }]}>Remaining to spend</Text>
              <Text style={[s.pv, { fontWeight: '800', fontSize: 16, color: finalRemaining >= 0 ? C.success : C.danger }]}>
                ${finalRemaining.toFixed(2)}
              </Text>
            </View>

            {finalRemaining < 0 && (
              <Text style={{ fontSize: 12, color: C.danger, marginTop: 8, fontStyle: 'italic' }}>
                ⚠️ Savings amount exceeds your remaining budget.
              </Text>
            )}
          </View>

          <Button mode="contained" onPress={handleContinue} loading={isSaving}
            style={s.btn} contentStyle={s.btnContent} labelStyle={s.btnLabel} buttonColor={C.primary}>
            {isSaving ? 'Calculating…' : 'See My Budget →'}
          </Button>
          <Button mode="text" onPress={() => handleFinalize(0)} disabled={isSaving}
            style={{ marginTop: 4 }} textColor={C.muted}>
            Skip savings ($0)
          </Button>

        </ScrollView>
      </SafeAreaView>
    </TouchableWithoutFeedback>
  );
}

const s = StyleSheet.create({
  safe:   { flex: 1, backgroundColor: C.bg },
  scroll: { padding: 24, paddingBottom: 56 },

  eyebrow: { fontSize: 11, fontWeight: '700', letterSpacing: 1.5, color: C.primaryLt, marginBottom: 6 },
  heading: { fontSize: 28, fontWeight: '800', color: C.text, marginBottom: 6, letterSpacing: -0.5 },
  sub:     { fontSize: 15, color: C.muted, lineHeight: 22, marginBottom: 20 },

  availCard: {
    backgroundColor: C.successBg, borderRadius: 20, padding: 20, marginBottom: 16,
    borderWidth: 1, borderColor: '#A7F3D0',
  },
  availLabel: { fontSize: 12, fontWeight: '600', color: '#065F46', letterSpacing: 0.3, marginBottom: 4 },
  availAmt:   { fontSize: 36, fontWeight: '800', color: C.success, letterSpacing: -1 },
  availNote:  { fontSize: 12, color: '#6EE7B7', marginTop: 4 },

  warnCard: {
    flexDirection: 'row', alignItems: 'flex-start',
    backgroundColor: C.warnBg, borderRadius: 16, padding: 16, marginBottom: 20,
    borderWidth: 1, borderColor: '#FDE68A', gap: 10,
  },
  warnIcon:  { fontSize: 20, marginTop: 1 },
  warnTitle: { fontSize: 13, fontWeight: '700', color: '#92400E', marginBottom: 4 },
  warnBody:  { fontSize: 13, color: '#92400E', lineHeight: 19 },

  fieldLabel: { fontSize: 13, fontWeight: '600', color: C.text, marginBottom: 4, letterSpacing: 0.2 },
  hint:       { fontSize: 12, color: C.muted, marginBottom: 10 },
  input:      { backgroundColor: C.surface },
  inputOutline: { borderRadius: 12, borderColor: C.border },
  errorTxt:   { color: C.danger, fontSize: 13, marginTop: 2, marginBottom: 8 },

  previewCard: {
    backgroundColor: C.surface, borderRadius: 20, padding: 20, marginTop: 16,
    borderWidth: 1, borderColor: C.border, marginBottom: 8,
  },
  previewHead: { fontSize: 13, fontWeight: '700', color: C.muted, letterSpacing: 0.5 },
  row: { flexDirection: 'row', justifyContent: 'space-between', paddingVertical: 4 },
  pl:  { fontSize: 14, color: C.muted, flex: 1 },
  pv:  { fontSize: 14, color: C.text, fontVariant: ['tabular-nums'] },

  btn:        { borderRadius: 16, marginTop: 20 },
  btnContent: { paddingVertical: 8 },
  btnLabel:   { fontSize: 16, fontWeight: '700', letterSpacing: 0.3 },
});