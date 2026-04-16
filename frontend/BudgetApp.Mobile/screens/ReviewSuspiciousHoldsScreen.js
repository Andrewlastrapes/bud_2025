import React, { useState, useEffect, useCallback } from 'react';
import {
  View,
  Text,
  FlatList,
  TouchableOpacity,
  TextInput,
  StyleSheet,
  ActivityIndicator,
  Alert,
} from 'react-native';
import { getAuth } from 'firebase/auth';
import { API_BASE_URL } from '../config/api';

async function authHeaders() {
  const token = await getAuth().currentUser?.getIdToken();
  return { Authorization: `Bearer ${token}`, 'Content-Type': 'application/json' };
}

export default function ReviewSuspiciousHoldsScreen({ navigation }) {
  const [holds, setHolds] = useState([]);
  const [loading, setLoading] = useState(true);
  const [submitting, setSubmitting] = useState(null); // id of hold being submitted
  const [overrides, setOverrides] = useState({});     // { [id]: string }

  const fetchHolds = useCallback(async () => {
    setLoading(true);
    try {
      const headers = await authHeaders();
      const res = await fetch(`${API_BASE_URL}/api/transactions/suspicious-holds`, { headers });
      const data = await res.json();
      setHolds(data);
      // Pre-fill override input with original amount
      const initial = {};
      data.forEach(h => { initial[h.id] = String(h.amount); });
      setOverrides(initial);
    } catch {
      Alert.alert('Error', 'Could not load suspicious holds.');
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => { fetchHolds(); }, [fetchHolds]);

  const submitOverride = async (hold) => {
    const raw = overrides[hold.id];
    const amount = parseFloat(raw);
    if (isNaN(amount) || amount <= 0) {
      Alert.alert('Invalid amount', 'Please enter a positive dollar amount.');
      return;
    }
    setSubmitting(hold.id);
    try {
      const headers = await authHeaders();
      const res = await fetch(`${API_BASE_URL}/api/transactions/${hold.id}/hold-override`, {
        method: 'POST',
        headers,
        body: JSON.stringify({ overrideAmount: amount }),
      });
      if (!res.ok) {
        const err = await res.json();
        Alert.alert('Error', err?.detail ?? 'Could not save override.');
        return;
      }
      const result = await res.json();
      Alert.alert(
        'Saved',
        `Budget adjusted by $${Math.abs(result.balanceAdjustment).toFixed(2)}.\nNew balance: $${result.newBalance?.toFixed(2) ?? '—'}`,
        [{ text: 'OK', onPress: fetchHolds }],
      );
    } catch {
      Alert.alert('Error', 'Network error — please try again.');
    } finally {
      setSubmitting(null);
    }
  };

  const renderItem = ({ item }) => {
    const isSubmitting = submitting === item.id;
    return (
      <View style={styles.card}>
        <View style={styles.cardHeader}>
          <Text style={styles.merchant}>{item.merchantName ?? item.name}</Text>
          <Text style={styles.holdAmount}>${item.amount.toFixed(2)} hold</Text>
        </View>
        <Text style={styles.subtext}>
          {new Date(item.date).toLocaleDateString()} · Pending pre-auth
        </Text>
        <Text style={styles.hint}>
          This looks like a pre-authorization hold that may be higher than your actual charge.
          Enter the amount you expect to actually be charged:
        </Text>
        <View style={styles.inputRow}>
          <Text style={styles.dollar}>$</Text>
          <TextInput
            style={styles.input}
            keyboardType="decimal-pad"
            value={overrides[item.id] ?? ''}
            onChangeText={val => setOverrides(prev => ({ ...prev, [item.id]: val }))}
            editable={!isSubmitting}
            selectTextOnFocus
          />
          <TouchableOpacity
            style={[styles.btn, isSubmitting && styles.btnDisabled]}
            onPress={() => submitOverride(item)}
            disabled={isSubmitting}
          >
            {isSubmitting
              ? <ActivityIndicator color="#fff" size="small" />
              : <Text style={styles.btnText}>Save</Text>}
          </TouchableOpacity>
        </View>
      </View>
    );
  };

  if (loading) {
    return (
      <View style={styles.centered}>
        <ActivityIndicator size="large" color="#4CAF50" />
      </View>
    );
  }

  if (holds.length === 0) {
    return (
      <View style={styles.centered}>
        <Text style={styles.emptyText}>No suspicious holds to review.</Text>
        <TouchableOpacity style={styles.doneBtn} onPress={() => navigation.goBack()}>
          <Text style={styles.doneBtnText}>Go Back</Text>
        </TouchableOpacity>
      </View>
    );
  }

  return (
    <View style={styles.container}>
      <Text style={styles.title}>Review Pending Holds</Text>
      <Text style={styles.description}>
        Gas stations, hotels, and rental car companies often place large pre-auth holds.
        Override the amount below to keep your budget accurate.
      </Text>
      <FlatList
        data={holds}
        keyExtractor={item => String(item.id)}
        renderItem={renderItem}
        contentContainerStyle={{ paddingBottom: 40 }}
      />
    </View>
  );
}

const styles = StyleSheet.create({
  container:   { flex: 1, backgroundColor: '#f5f5f5', padding: 16 },
  centered:    { flex: 1, justifyContent: 'center', alignItems: 'center', padding: 24 },
  title:       { fontSize: 22, fontWeight: '700', marginBottom: 8, color: '#333' },
  description: { fontSize: 14, color: '#666', marginBottom: 16, lineHeight: 20 },
  card:        { backgroundColor: '#fff', borderRadius: 12, padding: 16, marginBottom: 14, elevation: 2, shadowColor: '#000', shadowOpacity: 0.06, shadowRadius: 6, shadowOffset: { width: 0, height: 2 } },
  cardHeader:  { flexDirection: 'row', justifyContent: 'space-between', alignItems: 'center', marginBottom: 4 },
  merchant:    { fontSize: 16, fontWeight: '600', color: '#222', flex: 1, marginRight: 8 },
  holdAmount:  { fontSize: 16, fontWeight: '700', color: '#e53935' },
  subtext:     { fontSize: 12, color: '#999', marginBottom: 8 },
  hint:        { fontSize: 13, color: '#555', marginBottom: 12, lineHeight: 18 },
  inputRow:    { flexDirection: 'row', alignItems: 'center' },
  dollar:      { fontSize: 18, fontWeight: '600', color: '#333', marginRight: 4 },
  input:       { flex: 1, borderWidth: 1, borderColor: '#ddd', borderRadius: 8, paddingVertical: 8, paddingHorizontal: 12, fontSize: 18, color: '#333', marginRight: 10 },
  btn:         { backgroundColor: '#4CAF50', paddingVertical: 10, paddingHorizontal: 20, borderRadius: 8 },
  btnDisabled: { backgroundColor: '#aaa' },
  btnText:     { color: '#fff', fontWeight: '700', fontSize: 15 },
  emptyText:   { fontSize: 16, color: '#666', marginBottom: 20 },
  doneBtn:     { backgroundColor: '#4CAF50', paddingVertical: 12, paddingHorizontal: 32, borderRadius: 10 },
  doneBtnText: { color: '#fff', fontWeight: '700', fontSize: 16 },
});