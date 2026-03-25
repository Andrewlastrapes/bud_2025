import React, { useState, useEffect } from 'react';
import { View, ScrollView, StyleSheet, Platform } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { Text, TextInput, Button, ActivityIndicator } from 'react-native-paper';
import { auth } from '../firebaseConfig';
import {
  createUserWithEmailAndPassword,
  signInWithEmailAndPassword,
} from 'firebase/auth';
import { subscribeToLogs } from '../config/debugLog';

export default function LoginScreen() {
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [isLoading, setIsLoading] = useState(false);
  const [error, setError] = useState(null);
  const [logs, setLogs] = useState([]);

  useEffect(() => {
    const unsub = subscribeToLogs((entries) => setLogs(entries));
    return unsub;
  }, []);

  const handleSignIn = async () => {
    setIsLoading(true);
    setError(null);
    try {
      console.log(`[Login] signIn: ${email}`);
      await signInWithEmailAndPassword(auth, email, password);
      console.log('[Login] signIn OK — waiting for onAuthStateChanged');
    } catch (e) {
      console.error('[Login] signIn error:', e.message);
      setError(e.message);
    }
    setIsLoading(false);
  };

  const handleSignUp = async () => {
    setIsLoading(true);
    setError(null);

    let fbUser = null;

    try {
      // 1. Create Firebase user — this fires onAuthStateChanged, but
      //    MainContentNavigator will retry fetching the profile while
      //    we simultaneously register in the backend below.
      console.log('[Register] Step 1: createUserWithEmailAndPassword...');
      const cred = await createUserWithEmailAndPassword(auth, email, password);
      fbUser = cred.user;
      console.log(`[Register] Step 1 OK uid=${fbUser.uid}`);

      // 2. Get ID token to pass as Bearer
      console.log('[Register] Step 2: getIdToken...');
      const idToken = await fbUser.getIdToken();
      console.log(`[Register] Step 2 OK token length=${idToken.length}`);

      // 3. Register in backend — UID is derived from the bearer token server-side
      //    Body only needs { email, name }
      const apiUrl = process.env.EXPO_PUBLIC_API_URL;
      const registerUrl = `${apiUrl}/api/users/register`;
      console.log(`[Register] Step 3: POST ${registerUrl}`);
      console.log(`[Register]   headers: Authorization=Bearer [${idToken.length} chars]`);
      console.log(`[Register]   body: { email: "${email}", name: "${email.split('@')[0]}" }`);

      let response;
      try {
        response = await fetch(registerUrl, {
          method: 'POST',
          headers: {
            'Content-Type': 'application/json',
            Authorization: `Bearer ${idToken}`,
          },
          body: JSON.stringify({
            email,
            name: email.split('@')[0],
          }),
        });
      } catch (netErr) {
        // Network-level failure (no internet, DNS, TLS, ATS block, etc.)
        console.error(`[Register] Step 3 NETWORK ERROR: ${netErr.message}`);
        console.error(`[Register]   URL was: ${registerUrl}`);
        console.error(`[Register]   Platform: ${Platform.OS}`);
        throw netErr;
      }

      const responseText = await response.text();
      console.log(`[Register] Step 3 HTTP ${response.status}: ${responseText.slice(0, 300)}`);

      if (!response.ok) {
        throw new Error(`Backend registration failed ${response.status}: ${responseText}`);
      }

      console.log('[Register] Done — onAuthStateChanged + profile fetch should handle navigation');
    } catch (e) {
      console.error(`[Register] FAILED: ${e.message}`);
      setError(e.message);

      // Rollback Firebase user if backend failed
      if (fbUser) {
        try {
          console.log('[Register] Rollback: deleting Firebase user...');
          await fbUser.delete();
          console.log('[Register] Rollback OK — user deleted, staying on Login');
        } catch (rb) {
          console.error(`[Register] Rollback failed: ${rb.message}`);
        }
      }
    }

    setIsLoading(false);
  };

  return (
    <SafeAreaView style={styles.container}>
      <ScrollView contentContainerStyle={styles.scroll} keyboardShouldPersistTaps="handled">
        <Text style={styles.title}>Welcome</Text>

        <Text style={styles.meta}>Firebase: {auth.app.options.projectId}</Text>
        <Text style={styles.meta}>API: {process.env.EXPO_PUBLIC_API_URL || '⚠️ NOT SET'}</Text>
        <Text style={styles.meta}>Platform: {Platform.OS}</Text>

        <TextInput
          label="Email"
          value={email}
          onChangeText={setEmail}
          autoCapitalize="none"
          keyboardType="email-address"
          style={styles.input}
        />
        <TextInput
          label="Password"
          value={password}
          onChangeText={setPassword}
          secureTextEntry
          style={styles.input}
        />

        {isLoading ? (
          <ActivityIndicator style={{ marginTop: 20 }} />
        ) : (
          <>
            <Button mode="contained" onPress={handleSignIn} style={styles.button}>
              Login
            </Button>
            <Button mode="outlined" onPress={handleSignUp} style={styles.button}>
              Sign Up
            </Button>
          </>
        )}

        {error ? (
          <Text style={styles.error} selectable>
            {error}
          </Text>
        ) : null}

        {/* ── Debug Log — always visible, no nesting ── */}
        <View style={styles.logBox}>
          <Text style={styles.logTitle}>DEBUG LOG ({logs.length} entries)</Text>
          {logs.length === 0 ? (
            <Text style={styles.logLine}>— no logs yet —</Text>
          ) : (
            logs.map((entry, i) => (
              <Text
                key={i}
                selectable
                style={[
                  styles.logLine,
                  entry.level === 'error' && styles.logError,
                  entry.level === 'warn' && styles.logWarn,
                ]}
              >
                {entry.time} {entry.msg}
              </Text>
            ))
          )}
        </View>
      </ScrollView>
    </SafeAreaView>
  );
}

const styles = StyleSheet.create({
  container: { flex: 1 },
  scroll: { padding: 20, paddingTop: 40 },
  title: { fontSize: 24, fontWeight: 'bold', textAlign: 'center', marginBottom: 12 },
  meta: { fontSize: 11, color: '#666', marginBottom: 2 },
  input: { marginBottom: 10, marginTop: 8 },
  button: { marginTop: 10 },
  error: { marginTop: 10, color: 'red', textAlign: 'center' },
  logBox: {
    marginTop: 24,
    padding: 10,
    backgroundColor: '#1e1e1e',
    borderRadius: 8,
  },
  logTitle: {
    color: '#888',
    fontWeight: 'bold',
    fontSize: 11,
    marginBottom: 6,
    fontFamily: Platform.OS === 'ios' ? 'Menlo' : 'monospace',
  },
  logLine: {
    color: '#d4f5a5',
    fontSize: 10,
    fontFamily: Platform.OS === 'ios' ? 'Menlo' : 'monospace',
    marginBottom: 2,
  },
  logError: { color: '#ff6b6b' },
  logWarn: { color: '#ffd93d' },
});