import React, { useState, useCallback } from 'react';
import {
  View,
  StyleSheet,
  ActivityIndicator,
  Text as RNText,
  Platform,
} from 'react-native';
import { Text, Button } from 'react-native-paper';
import { SafeAreaView } from 'react-native-safe-area-context';
import axios from 'axios';
import { auth } from '../../firebaseConfig';
import { API_BASE_URL } from '../../config/api';

// --- Helper to get auth headers ---
const getAuthHeader = async () => {
  const user = auth.currentUser;
  if (!user) throw new Error('No user logged in.');
  const token = await user.getIdToken();
  return { headers: { Authorization: `Bearer ${token}` } };
};

// ─── Web Plaid content ────────────────────────────────────────────────────────
// Uses react-plaid-link (web SDK) via require() so it's never bundled on native.
function WebPlaidContent({
  linkToken,
  onPlaidSuccess,
  onPlaidExit,
  createLinkToken,
  isFetchingToken,
  numAccountsConnected,
  navigation,
  error,
}) {
  const { usePlaidLink } = require('react-plaid-link');

  // usePlaidLink re-initialises whenever linkToken changes.
  // 'ready' is false until a valid token is loaded.
  const { open, ready } = usePlaidLink({
    token: linkToken,
    onSuccess: onPlaidSuccess,
    onExit: onPlaidExit,
  });

  const PostConnectContent = () => (
    <View style={styles.postConnectBox}>
      <Text style={styles.successHeader}>
        ✅ Success! {numAccountsConnected} Account(s) Connected
      </Text>

      <RNText style={styles.mainInstruction}>
        Do you have any other cards or checking accounts you use for spending?
      </RNText>

      {isFetchingToken && !linkToken ? (
        <View style={styles.loadingPlaceholder}>
          <ActivityIndicator size="small" />
          <Text style={styles.loadingText}>Fetching Token...</Text>
        </View>
      ) : ready ? (
        <Button
          mode="contained"
          onPress={() => open()}
          style={styles.postConnectButton}
        >
          Open Plaid Link
        </Button>
      ) : (
        <Button
          mode="contained"
          onPress={createLinkToken}
          style={styles.postConnectButton}
        >
          Connect Another Account
        </Button>
      )}

      <Button
        mode="outlined"
        onPress={() => navigation.navigate('FixedCostsSetup')}
        style={styles.postConnectButton}
      >
        Continue to Fixed Costs Setup
      </Button>
    </View>
  );

  const PreConnectContent = () => (
    <>
      <View style={styles.instructionBox}>
        <RNText style={styles.mainInstruction}>
          Please connect all credit cards, debit cards, and your primary checking account.
        </RNText>

        <RNText style={styles.warningInstruction}>
          ⚠️ It is essential to connect every account you use to spend money. If you
          skip an account, your Dynamic Budget will be inaccurate because we will miss
          those charges going forward.
        </RNText>
      </View>

      {isFetchingToken && !linkToken ? (
        <View style={styles.loadingPlaceholder}>
          <ActivityIndicator size="small" />
          <Text style={styles.loadingText}>Fetching Token...</Text>
        </View>
      ) : ready ? (
        <Button
          mode="contained"
          onPress={() => open()}
          style={styles.button}
        >
          Open Plaid Link
        </Button>
      ) : (
        <Button
          mode="contained"
          onPress={createLinkToken}
          disabled={isFetchingToken || !auth.currentUser}
          style={styles.button}
        >
          {isFetchingToken ? 'Fetching token…' : 'Connect Now'}
        </Button>
      )}
    </>
  );

  return (
    <SafeAreaView style={styles.safeArea}>
      <View style={styles.contentContainer}>
        <Text style={styles.header}>Connect Your Accounts (1/4)</Text>

        {!!error && (
          <Text style={styles.errorText} selectable>
            {error}
          </Text>
        )}

        {numAccountsConnected > 0 ? <PostConnectContent /> : <PreConnectContent />}
      </View>

      <Button
        mode="text"
        onPress={() => navigation.goBack()}
        style={styles.backButton}
      >
        Go Back
      </Button>
    </SafeAreaView>
  );
}

// ─── Native Plaid content ─────────────────────────────────────────────────────
// IMPORTANT: we use require() here so web never tries to load the native Plaid SDK.
function NativePlaidContent({
  linkToken,
  onPlaidSuccess,
  onPlaidExit,
  createLinkToken,
  isFetchingToken,
  numAccountsConnected,
  navigation,
  error,
}) {
  const plaid = require('react-native-plaid-link-sdk');

  const { create, open } = plaid;

  React.useEffect(() => {
    if (!linkToken) return;
    create({ token: linkToken });
  }, [linkToken]);

  const handleOpenPlaid = async () => {
    try {
      await open({
        onSuccess: onPlaidSuccess,
        onExit: onPlaidExit,
      });
    } catch (e) {
      console.error('[PlaidConnect] open failed raw:', e);
    }
  };

  const PostConnectContent = () => (
    <View style={styles.postConnectBox}>
      <Text style={styles.successHeader}>
        ✅ Success! {numAccountsConnected} Account(s) Connected
      </Text>

      <RNText style={styles.mainInstruction}>
        Do you have any other cards or checking accounts you use for spending?
      </RNText>

      {isFetchingToken && !linkToken ? (
        <View style={styles.loadingPlaceholder}>
          <ActivityIndicator size="small" />
          <Text style={styles.loadingText}>Fetching Token...</Text>
        </View>
      ) : linkToken ? (
        <Button
          mode="contained"
          onPress={handleOpenPlaid}
          style={styles.postConnectButton}
        >
          Open Plaid Link
        </Button>
      ) : (
        <Button
          mode="contained"
          onPress={createLinkToken}
          style={styles.postConnectButton}
        >
          Connect Another Account
        </Button>
      )}

      <Button
        mode="outlined"
        onPress={() => navigation.navigate('FixedCostsSetup')}
        style={styles.postConnectButton}
      >
        Continue to Fixed Costs Setup
      </Button>
    </View>
  );

  const PreConnectContent = () => (
    <>
      <View style={styles.instructionBox}>
        <RNText style={styles.mainInstruction}>
          Please connect all credit cards, debit cards, and your primary checking account.
        </RNText>

        <RNText style={styles.warningInstruction}>
          ⚠️ It is essential to connect every account you use to spend money. If you
          skip an account, your Dynamic Budget will be inaccurate because we will miss
          those charges going forward.
        </RNText>
      </View>

      {isFetchingToken && !linkToken ? (
        <View style={styles.loadingPlaceholder}>
          <ActivityIndicator size="small" />
          <Text style={styles.loadingText}>Fetching Token...</Text>
        </View>
      ) : linkToken ? (
        <Button
          mode="contained"
          onPress={handleOpenPlaid}
          style={styles.button}
        >
          Open Plaid Link
        </Button>
      ) : (
        <Button
          mode="contained"
          onPress={createLinkToken}
          disabled={!auth.currentUser}
          style={styles.button}
        >
          Connect Now
        </Button>
      )}
    </>
  );

  if (isFetchingToken && !linkToken) {
    return (
      <View style={styles.loadingContainer}>
        <ActivityIndicator size="large" />
        <Text style={styles.loadingText}>Connecting to Plaid...</Text>
      </View>
    );
  }

  return (
    <SafeAreaView style={styles.safeArea}>
      <View style={styles.contentContainer}>
        <Text style={styles.header}>Connect Your Accounts (1/4)</Text>

        {!!error && (
          <Text style={styles.errorText} selectable>
            {error}
          </Text>
        )}

        {numAccountsConnected > 0 ? <PostConnectContent /> : <PreConnectContent />}
      </View>

      <Button
        mode="text"
        onPress={() => navigation.goBack()}
        style={styles.backButton}
      >
        Go Back
      </Button>
    </SafeAreaView>
  );
}

// ─── Screen entry point ───────────────────────────────────────────────────────
export default function PlaidConnectScreen({ navigation }) {
  const [linkToken, setLinkToken] = useState(null);
  const [isFetchingToken, setIsFetchingToken] = useState(false);
  const [error, setError] = useState(null);
  const [numAccountsConnected, setNumAccountsConnected] = useState(0);

  const onPlaidSuccess = useCallback(async (publicTokenOrSuccess, metadata) => {
    // Normalize callback signature:
    // - react-plaid-link (web)               → onSuccess(publicToken: string, metadata)
    // - react-native-plaid-link-sdk (native) → onSuccess({ publicToken, metadata })
    const publicToken =
      typeof publicTokenOrSuccess === 'string'
        ? publicTokenOrSuccess
        : publicTokenOrSuccess?.publicToken;

    setError(null);
    setIsFetchingToken(true);

    try {
      const user = auth.currentUser;
      if (!user) throw new Error('Authentication error.');

      await axios.post(
        `${API_BASE_URL}/api/plaid/exchange_public_token`,
        {
          publicToken,
          firebaseUuid: user.uid,
        },
        await getAuthHeader(),
      );

      console.log('[PlaidConnect] exchange success:', metadata);
      setNumAccountsConnected((prev) => prev + 1);
      setLinkToken(null);
    } catch (e) {
      console.error('[PlaidConnect] Error exchanging token:', e?.message || e);
      setError('Failed to save accounts. Please try again.');
    } finally {
      setIsFetchingToken(false);
    }
  }, []);

  const onPlaidExit = useCallback((errorObj, metadata) => {
    console.log('[PlaidConnect] Plaid Link exited:', errorObj, metadata);
    setLinkToken(null);
    setIsFetchingToken(false);
  }, []);

  const createLinkToken = async () => {
    setIsFetchingToken(true);
    setError(null);

    try {
      const user = auth.currentUser;
      if (!user) throw new Error('Please log in again.');

      const response = await axios.post(
        `${API_BASE_URL}/api/plaid/create_link_token`,
        { firebaseUserId: user.uid },
        await getAuthHeader(),
      );

      setLinkToken(response.data.linkToken);
      console.log('[PlaidConnect] token fetched — waiting for user to open Plaid');
    } catch (e) {
      console.error('[PlaidConnect] Error creating link token:', e?.message || e);
      setError('Could not generate link token. Please try again.');
    } finally {
      setIsFetchingToken(false);
    }
  };

  const sharedProps = {
    linkToken,
    onPlaidSuccess,
    onPlaidExit,
    createLinkToken,
    isFetchingToken,
    numAccountsConnected,
    navigation,
    error,
  };

  // Web: use react-plaid-link (iframe-based Plaid Link)
  if (Platform.OS === 'web') {
    return <WebPlaidContent {...sharedProps} />;
  }

  // Native: use react-native-plaid-link-sdk
  return <NativePlaidContent {...sharedProps} />;
}

const styles = StyleSheet.create({
  safeArea: {
    flex: 1,
    backgroundColor: '#fff',
  },
  contentContainer: {
    flex: 1,
    paddingHorizontal: 25,
    paddingTop: 50,
    justifyContent: 'flex-start',
    alignItems: 'center',
  },
  loadingContainer: {
    flex: 1,
    justifyContent: 'center',
    alignItems: 'center',
    backgroundColor: '#fff',
  },
  header: {
    fontSize: 26,
    fontWeight: 'bold',
    textAlign: 'center',
    marginBottom: 20,
    color: '#333',
  },
  instructionBox: {
    marginBottom: 30,
    paddingHorizontal: 10,
    borderColor: '#f0f0f0',
    padding: 15,
    borderRadius: 8,
  },
  mainInstruction: {
    fontSize: 18,
    textAlign: 'center',
    marginBottom: 10,
    lineHeight: 25,
  },
  warningInstruction: {
    fontSize: 14,
    textAlign: 'center',
    color: 'red',
    fontWeight: 'bold',
  },
  button: {
    width: '100%',
    marginVertical: 10,
    paddingVertical: 4,
  },
  loadingPlaceholder: {
    paddingVertical: 10,
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'center',
    width: '100%',
  },
  loadingText: {
    marginLeft: 10,
    fontSize: 16,
  },
  postConnectBox: {
    width: '100%',
    alignItems: 'center',
    padding: 20,
    borderWidth: 1,
    borderColor: '#6200ee',
    borderRadius: 10,
    marginTop: 40,
  },
  successHeader: {
    fontSize: 22,
    fontWeight: 'bold',
    color: 'green',
    marginBottom: 20,
  },
  postConnectButton: {
    width: '100%',
    marginVertical: 8,
    paddingVertical: 4,
  },
  errorText: {
    color: 'red',
    textAlign: 'center',
    marginTop: 10,
  },
  backButton: {
    marginBottom: 15,
    marginHorizontal: 20,
  },
});