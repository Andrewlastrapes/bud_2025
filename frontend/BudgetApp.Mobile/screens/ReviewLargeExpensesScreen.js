// File: screens/ReviewLargeExpensesScreen.js

import React, { useState, useEffect } from 'react';
import { View, FlatList, StyleSheet } from 'react-native';
import {
  Text,
  Button,
  Card,
  ActivityIndicator,
  Portal,
  Modal,
  TextInput,
} from 'react-native-paper';
import axios from 'axios';
import { auth } from '../firebaseConfig';
import { useIsFocused } from '@react-navigation/native';

import { API_BASE_URL } from '@/config/api';
;

// Enum numeric values must match TransactionUserDecision in C#
const DECISIONS = {
  TreatAsVariableSpend: 10,
  LargeExpenseFromSavings: 11,
  LargeExpenseToFixedCost: 12,
};

export default function ReviewLargeExpensesScreen() {
  const [items, setItems] = useState([]);
  const [loading, setLoading] = useState(true);
  const [installmentModalTx, setInstallmentModalTx] = useState(null);
  const [installmentCount, setInstallmentCount] = useState('2');
  const isFocused = useIsFocused();

  const getAuthHeader = async () => {
    const user = auth.currentUser;
    if (!user) throw new Error('No user logged in.');
    const token = await user.getIdToken();
    return { headers: { Authorization: `Bearer ${token}` } };
  };

  const fetchLargeExpenses = async () => {
    try {
      setLoading(true);
      const config = await getAuthHeader();
      const res = await axios.get(
        `${API_BASE_URL}/api/transactions/large-expenses/pending`,
        config
      );
      setItems(res.data || []);
    } catch (e) {
      console.error('Failed to load large expenses', e);
      alert('Error loading large expenses');
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    if (isFocused) {
      fetchLargeExpenses();
    }
  }, [isFocused]);

  const applyDecision = async (txId, decision, extra = {}) => {
    try {
      const config = await getAuthHeader();
      await axios.post(
        `${API_BASE_URL}/api/transactions/${txId}/decision`,
        { decision, ...extra },
        config
      );
      setItems(current => current.filter(x => x.id !== txId));
    } catch (e) {
      console.error('Failed to apply decision', e);
      alert('Error saving decision');
    }
  };

  const renderItem = ({ item }) => {
    const title = item.merchantName || item.name || 'Transaction';
    const amount = typeof item.amount === 'number' ? item.amount : 0;

    return (
      <Card style={styles.card}>
        <Card.Title title={title} />
        <Card.Content>
          <Text style={styles.amount}>${amount.toFixed(2)}</Text>
          {item.date && (
            <Text style={styles.date}>
              {new Date(item.date).toLocaleDateString()}
            </Text>
          )}

          <View style={styles.buttonRow}>
            <Button
              mode="outlined"
              onPress={() =>
                applyDecision(item.id, DECISIONS.TreatAsVariableSpend)
              }
            >
              Count as spending
            </Button>
          </View>

          <View style={styles.buttonRow}>
            <Button
              mode="outlined"
              onPress={() =>
                applyDecision(item.id, DECISIONS.LargeExpenseFromSavings)
              }
            >
              Paid from savings
            </Button>
          </View>

          <View style={styles.buttonRow}>
            <Button
              mode="contained"
              onPress={() => {
                setInstallmentModalTx(item);
                setInstallmentCount('2');
              }}
            >
              Convert to fixed cost
            </Button>
          </View>
        </Card.Content>
      </Card>
    );
  };

  return (
    <View style={styles.container}>
      {loading ? (
        <ActivityIndicator style={{ marginTop: 32 }} />
      ) : items.length === 0 ? (
        <View style={styles.emptyContainer}>
          <Text>No large expenses to review right now.</Text>
        </View>
      ) : (
        <FlatList
          data={items}
          keyExtractor={item => String(item.id)}
          renderItem={renderItem}
        />
      )}

      <Portal>
        <Modal
          visible={!!installmentModalTx}
          onDismiss={() => setInstallmentModalTx(null)}
          contentContainerStyle={styles.modal}
        >
          <Text style={styles.modalTitle}>Convert to fixed cost</Text>

          {installmentModalTx && (
            <>
              <Text>
                {installmentModalTx.merchantName || installmentModalTx.name}
              </Text>
              <Text style={{ marginBottom: 8 }}>
                Amount: ${Number(installmentModalTx.amount).toFixed(2)}
              </Text>
            </>
          )}

          <TextInput
            label="Number of paychecks to spread over"
            value={installmentCount}
            onChangeText={setInstallmentCount}
            keyboardType="numeric"
            style={{ marginTop: 8 }}
          />

          <View style={styles.modalButtons}>
            <Button onPress={() => setInstallmentModalTx(null)}>Cancel</Button>
            <Button
              mode="contained"
              onPress={async () => {
                const n = parseInt(installmentCount, 10);
                if (!n || n <= 0) {
                  alert('Enter a valid number of paychecks.');
                  return;
                }
                if (!installmentModalTx) return;

                await applyDecision(installmentModalTx.id, DECISIONS.LargeExpenseToFixedCost, {
                  installmentCount: n,
                  fixedCostName:
                    installmentModalTx.merchantName || installmentModalTx.name,
                });

                setInstallmentModalTx(null);
              }}
            >
              Save
            </Button>
          </View>
        </Modal>
      </Portal>
    </View>
  );
}

const styles = StyleSheet.create({
  container: {
    flex: 1,
    padding: 16,
  },
  card: {
    marginBottom: 12,
  },
  amount: {
    fontSize: 20,
    fontWeight: '600',
    marginTop: 4,
  },
  date: {
    marginTop: 4,
    color: '#666',
  },
  buttonRow: {
    marginTop: 8,
  },
  emptyContainer: {
    marginTop: 32,
    alignItems: 'center',
  },
  modal: {
    margin: 24,
    padding: 16,
    backgroundColor: 'white',
  },
  modalTitle: {
    fontSize: 18,
    fontWeight: '600',
    marginBottom: 8,
  },
  modalButtons: {
    marginTop: 16,
    flexDirection: 'row',
    justifyContent: 'flex-end',
  },
});
