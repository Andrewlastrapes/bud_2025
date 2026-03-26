import React, { useState, useEffect } from 'react';
import { View, StyleSheet } from 'react-native';
import { Text, Button, ActivityIndicator } from 'react-native-paper';
import { auth } from '../../firebaseConfig';
import { API_BASE_URL } from '../../config/api';

// Plaid Link SDK — requires native build (EAS). Lazy-require to surface a clear
// error instead of a silent crash when the native module is missing.
let PlaidLink = null;
try {
  PlaidLink = require('react-native-plaid-link-sdk').PlaidLink;
} catch (e) {
  console.error('[PlaidConnect] Failed to load react-native-plaid-link-sdk:', e.message);
}

export default function PlaidConnectScreen({ navigation }) {
  const [linkToken, setLinkToken] = useState(null);
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState(null);

  useEffect(() => {
    if (!PlaidLink) {
      setError(
        'Plaid native module not available. Make sure you are using an EAS build, not Expo Go, and that react-native-plaid-link-sdk is listed in app.json plugins.',
      );
      setIsLoading(false);
      return;
    }
    fetchLinkToken();
  }, []);

  const fetchLinkToken = async () => {
    try {
      setIsLoading(true);
      setError(null);
      const user = auth.currentUser;
      if (!user) throw new Error('Not authenticated');

      const idToken = await user.getIdToken();
      console.log('[PlaidConnect] fetching link token...');

      const response = await fetch(`${API_BASE_URL}/api/plaid/create_link_token`, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          Authorization: `Bearer ${idToken}`,
        },
        body: JSON.stringify({ firebaseUserId: user.uid }),
      });

      if (!response.ok) {
        const errorText = await response.text();
        throw new Error(`Failed to create link token (${response.status}): ${errorText}`);
      }

      const data = await response.json();
      console.log('[PlaidConnect] link token received');
      setLinkToken(data.linkToken);
    } catch (err) {
      console.error('[PlaidConnect] fetchLinkToken error:', err.message);
      setError(err.message);
    } finally {
      setIsLoading(false);
    }
  };

  const handleSuccess = async (success) => {
    try {
      const user = auth.currentUser;
      if (!user) throw new Error('Not authenticated');

      const idToken = await user.getIdToken();
      console.log('[PlaidConnect] exchanging public token...');

      const response = await fetch(`${API_BASE_URL}/api/plaid/exchange_public_token`, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          Authorization: `Bearer ${idToken}`,
        },
        body: JSON.stringify({
          publicToken: success.publicToken,
          firebaseUuid: user.uid,
        }),
      });

      if (!response.ok) {
        const errorText = await response.text();
        throw new Error(`Failed to exchange token (${response.status}): ${errorText}`);
      }

      console.log('[PlaidConnect] token exchanged, navigating to PaycheckSavings');
      navigation.navigate('PaycheckSavings');
    } catch (err) {
      console.error('[PlaidConnect] handleSuccess error:', err.message);
      setError(err.message);
    }
  };

  const handleExit = (exit) => {
    if (exit?.error) {
      console.error('[PlaidConnect] Plaid exit error:', JSON.stringify(exit.error));
    }
  };

  if (isLoading) {
    return (
      <View style={styles.container}>
        <ActivityIndicator size="large" />
        <Text style={styles.loadingText}>Connecting to Plaid...</Text>
      </View>
    );
  }

  if (error) {
    return (
      <View style={styles.container}>
        <Text style={styles.errorText} selectable>{error}</Text>
        <Button mode="contained" onPress={fetchLinkToken} style={{ marginBottom: 12 }}>
          Retry
        </Button>
        <Button mode="outlined" onPress={() => navigation.navigate('PaycheckSavings')}>
          Skip for now
        </Button>
      </View>
    );
  }

  return (
    <View style={styles.container}>
      <Text style={styles.title}>Connect Your Bank</Text>
      <Text style={styles.subtitle}>
        Securely connect your bank account to automatically track your spending.
      </Text>

      {linkToken && PlaidLink ? (
        <PlaidLink
          tokenConfig={{ token: linkToken }}
          onSuccess={handleSuccess}
          onExit={handleExit}
        >
          <Button mode="contained" style={styles.button}>
            Connect Bank Account
          </Button>
        </PlaidLink>
      ) : (
        <Text style={styles.errorText}>Failed to load Plaid. Please retry.</Text>
      )}

      <Button
        mode="outlined"
        style={styles.skipButton}
        onPress={() => navigation.navigate('PaycheckSavings')}
      >
        Skip for now
      </Button>
    </View>
  );
}

const styles = StyleSheet.create({
  container: {
    flex: 1,
    padding: 20,
    justifyContent: 'center',
    alignItems: 'center',
  },
  title: {
    fontSize: 24,
    fontWeight: 'bold',
    marginBottom: 12,
    textAlign: 'center',
  },
  subtitle: {
    fontSize: 16,
    textAlign: 'center',
    marginBottom: 32,
    color: '#666',
  },
  button: {
    marginTop: 20,
    width: 220,
  },
  skipButton: {
    marginTop: 12,
  },
  loadingText: {
    marginTop: 10,
  },
  errorText: {
    color: 'red',
    marginBottom: 20,
    textAlign: 'center',
  },
});