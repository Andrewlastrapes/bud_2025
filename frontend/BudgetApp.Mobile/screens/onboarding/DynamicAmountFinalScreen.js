import React, { useState } from 'react';
import { View, ScrollView, StyleSheet, Alert, ActivityIndicator, StatusBar } from 'react-native';
import { Text, Button, Divider } from 'react-native-paper';
import { SafeAreaView } from 'react-native-safe-area-context';
import axios from 'axios';
import { StackActions } from '@react-navigation/native';
import { auth } from '../../firebaseConfig';
import { API_BASE_URL } from '../../config/api';

// ─── Design tokens ────────────────────────────────────────────────────────────
const C = {
  primary:   '#5B21B6',
  primaryLt: '#7C3AED',
  success:   '#10B981',
  successBg: '#ECFDF5',
  danger:    '#EF4444',
  surface:   '#FFFFFF',
  bg:        '#F5F3FF',
  text:      '#1E1B4B',
  muted:     '#6B7280',
  border:    '#E5E7EB',
};

export default function DynamicAmountFinalScreen({ navigation, route }) {
  const {
    remainingToSpend,
    dynamicSpendableAmount,
    paycheckAmount,
    fixedCostsRemaining,
    baseRemaining,
    debtPerPaycheck,
    savingsContribution,
    explanation,
  } = route.params || {};

  const [isChecking, setIsChecking] = useState(false);

  const displayAmount =
    remainingToSpend != null        ? parseFloat(remainingToSpend).toFixed(2)
    : dynamicSpendableAmount != null ? parseFloat(dynamicSpendableAmount).toFixed(2)
    : '0.00';

  const isPositive = parseFloat(displayAmount) >= 0;

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
      let retryCount = 0;
      do {
        if (retryCount > 0) await new Promise(r => setTimeout(r, 500));
        const { data: profile } = await axios.get(`${API_BASE_URL}/api/users/profile`, config);
        if (profile.onboardingComplete) {
          const parentNav = navigation.getParent();
          if (parentNav) parentNav.dispatch(StackActions.replace('App'));
          else navigation.navigate('App');
          return;
        }
        retryCount++;
      } while (retryCount < 5);
      Alert.alert('Setup Error', 'Failed to confirm setup. Please log out and try again.');
    } catch (e) {
      Alert.alert('Network Error', 'Could not verify setup status.');
    } finally {
      setIsChecking(false);
    }
  };

  if (isChecking) {
    return (
      <View style={s.loadingContainer}>
        <ActivityIndicator size="large" color={C.primary} />
        <Text style={{ marginTop: 14, color: C.muted, fontSize: 15 }}>Finalizing your setup…</Text>
      </View>
    );
  }

  return (
    <SafeAreaView style={s.safe}>
      <StatusBar barStyle="light-content" backgroundColor={C.primary} />
      <ScrollView contentContainerStyle={s.scroll} showsVerticalScrollIndicator={false}>

        {/* Hero header */}
        <View style={s.hero}>
          <Text style={s.heroEyebrow}>YOUR DYNAMIC BUDGET</Text>
          <Text style={s.heroLabel}>You have until your next paycheck:</Text>
          <Text style={[s.heroAmount, !isPositive && { color: '#FECACA' }]}>
            ${displayAmount}
          </Text>
          {isPositive && (
            <View style={s.heroBadge}>
              <Text style={s.heroBadgeTxt}>✓ Budget set</Text>
            </View>
          )}
        </View>

        {/* Breakdown card */}
        {paycheckAmount != null && (
          <View style={s.card}>
            <Text style={s.cardTitle}>How we calculated this</Text>
            <Divider style={s.div} />

            <Row label="Income (net)" value={paycheckAmount} color={C.success} prefix="+" />

            {parseFloat(fixedCostsRemaining) > 0 && (
              <Row label="Fixed costs" value={fixedCostsRemaining} color={C.danger} prefix="−" />
            )}

            {/* Subtotal: before debt & savings */}
            {baseRemaining != null && (parseFloat(debtPerPaycheck) > 0 || parseFloat(savingsContribution) > 0) && (
              <>
                <View style={s.subtotalRow}>
                  <Text style={s.subtotalLabel}>Before debt & savings</Text>
                  <Text style={[s.subtotalValue, parseFloat(baseRemaining) < 0 && { color: C.danger }]}>
                    ${parseFloat(baseRemaining).toFixed(2)}
                  </Text>
                </View>
                <Divider style={[s.div, { marginVertical: 6 }]} />
              </>
            )}

            {parseFloat(debtPerPaycheck) > 0 && (
              <Row label="Debt payoff" value={debtPerPaycheck} color={C.danger} prefix="−" />
            )}

            {parseFloat(savingsContribution) > 0 && (
              <Row label="Savings goal" value={savingsContribution} color={C.danger} prefix="−" />
            )}

            <Divider style={s.div} />

            <View style={s.totalRow}>
              <Text style={s.totalLabel}>Remaining to spend</Text>
              <Text style={[s.totalValue, !isPositive && { color: C.danger }]}>
                ${displayAmount}
              </Text>
            </View>
          </View>
        )}

        {/* Explanation monospace card */}
        {explanation && (
          <View style={s.explainCard}>
            <Text style={s.explainHead}>DETAILS</Text>
            <Text style={s.explainTxt}>{explanation}</Text>
          </View>
        )}

        {/* Info nudge */}
        <View style={s.infoCard}>
          <Text style={s.infoTxt}>
            💡 Your budget updates automatically as new transactions arrive from your linked accounts.
          </Text>
        </View>

      </ScrollView>

      <View style={s.footer}>
        <Button mode="contained" onPress={handleFinish}
          style={s.btn} contentStyle={s.btnContent} labelStyle={s.btnLabel} buttonColor={C.primary}>
          I Understand — Start Using My Budget
        </Button>
      </View>
    </SafeAreaView>
  );
}

function Row({ label, value, color, prefix }) {
  return (
    <View style={s.row}>
      <Text style={s.rowLabel}>{label}</Text>
      <Text style={[s.rowValue, { color }]}>
        {prefix}${Math.abs(parseFloat(value)).toFixed(2)}
      </Text>
    </View>
  );
}

const s = StyleSheet.create({
  safe:             { flex: 1, backgroundColor: C.bg },
  scroll:           { paddingBottom: 110 },
  loadingContainer: { flex: 1, justifyContent: 'center', alignItems: 'center', backgroundColor: C.bg },

  // Hero
  hero: {
    backgroundColor: C.primary,
    paddingTop: 32, paddingBottom: 40,
    paddingHorizontal: 28,
    alignItems: 'center',
  },
  heroEyebrow: { fontSize: 11, fontWeight: '700', letterSpacing: 2, color: '#C4B5FD', marginBottom: 10 },
  heroLabel:   { fontSize: 15, color: '#DDD6FE', marginBottom: 12, textAlign: 'center' },
  heroAmount:  { fontSize: 64, fontWeight: '900', color: '#FFFFFF', letterSpacing: -2, textAlign: 'center' },
  heroBadge:   { marginTop: 14, backgroundColor: 'rgba(255,255,255,0.15)', borderRadius: 20, paddingHorizontal: 14, paddingVertical: 5 },
  heroBadgeTxt:{ fontSize: 13, color: '#FFFFFF', fontWeight: '600' },

  // Breakdown card
  card: {
    backgroundColor: C.surface, borderRadius: 24, padding: 22,
    margin: 20, marginBottom: 12,
    shadowColor: '#5B21B6', shadowOpacity: 0.08, shadowRadius: 12, shadowOffset: { width: 0, height: 4 },
    elevation: 4,
  },
  cardTitle: { fontSize: 14, fontWeight: '700', color: C.muted, letterSpacing: 0.5, marginBottom: 12 },
  div:       { backgroundColor: '#F3F4F6', marginVertical: 10 },

  row:      { flexDirection: 'row', justifyContent: 'space-between', paddingVertical: 5 },
  rowLabel: { fontSize: 14, color: C.muted, flex: 1 },
  rowValue: { fontSize: 15, fontWeight: '600', fontVariant: ['tabular-nums'] },

  subtotalRow:  { flexDirection: 'row', justifyContent: 'space-between', paddingVertical: 5 },
  subtotalLabel:{ fontSize: 13, color: C.text, fontWeight: '600', flex: 1 },
  subtotalValue:{ fontSize: 14, fontWeight: '700', color: C.text, fontVariant: ['tabular-nums'] },

  totalRow:   { flexDirection: 'row', justifyContent: 'space-between', alignItems: 'center', paddingVertical: 4 },
  totalLabel: { fontSize: 16, fontWeight: '800', color: C.text, flex: 1 },
  totalValue: { fontSize: 22, fontWeight: '900', color: C.success, fontVariant: ['tabular-nums'] },

  // Explanation
  explainCard: {
    backgroundColor: '#1E1B4B', borderRadius: 20, padding: 18,
    marginHorizontal: 20, marginBottom: 12,
  },
  explainHead: { fontSize: 10, fontWeight: '700', color: '#7C3AED', letterSpacing: 2, marginBottom: 10 },
  explainTxt:  { fontSize: 13, color: '#C4B5FD', lineHeight: 22, fontFamily: 'monospace' },

  // Info
  infoCard: { backgroundColor: '#EDE9FE', borderRadius: 16, padding: 16, marginHorizontal: 20, marginBottom: 12 },
  infoTxt:  { fontSize: 13, color: C.primary, lineHeight: 20 },

  // Footer
  footer:     { position: 'absolute', bottom: 0, left: 0, right: 0, backgroundColor: C.surface, padding: 20, borderTopWidth: 1, borderTopColor: C.border },
  btn:        { borderRadius: 16 },
  btnContent: { paddingVertical: 8 },
  btnLabel:   { fontSize: 16, fontWeight: '700', letterSpacing: 0.3 },
});