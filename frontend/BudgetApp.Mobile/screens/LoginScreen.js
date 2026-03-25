import React, { useState, useEffect, useRef } from 'react';
import {
  View,
  Text,
  TextInput,
  TouchableOpacity,
  StyleSheet,
  Alert,
  KeyboardAvoidingView,
  Platform,
  ScrollView,
} from 'react-native';
import { signInWithEmailAndPassword, createUserWithEmailAndPassword } from 'firebase/auth';
import { auth } from '../firebaseConfig';
import axios from 'axios';
import { API_BASE_URL } from '../config/api';
import { subscribeToLogs } from '../config/debugLog';

export default function LoginScreen() {
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [name, setName] = useState('');
  const [isRegistering, setIsRegistering] = useState(false);
  const [logs, setLogs] = useState([]);
  const [showDebug, setShowDebug] = useState(false);
  const logScrollRef = useRef(null);

  useEffect(() => {
    const unsub = subscribeToLogs((entries) => {
      setLogs(entries);
    });
    return unsub;
  }, []);

  // Auto-scroll to bottom when new logs arrive
  useEffect(() => {
    if (showDebug && logScrollRef.current) {
      logScrollRef.current.scrollToEnd({ animated: false });
    }
  }, [logs, showDebug]);

  const handleLogin = async () => {
    try {
      console.log(`[Login] Attempting sign-in for ${email}`);
      await signInWithEmailAndPassword(auth, email, password);
      console.log('[Login] signInWithEmailAndPassword resolved — waiting for onAuthStateChanged');
    } catch (e) {
      console.error('[Login] Sign-in error:', e.message);
      Alert.alert('Login Error', e.message);
    }
  };

  const handleRegister = async () => {
    try {
      console.log(`[Register] Creating account for ${email}`);
      const { user } = await createUserWithEmailAndPassword(auth, email, password);
      const idToken = await user.getIdToken();
      console.log('[Register] Firebase user created, posting to API...');
      await axios.post(
        `${API_BASE_URL}/api/users/register`,
        { email, name },
        { headers: { Authorization: `Bearer ${idToken}` } }
      );
      console.log('[Register] API registration complete');
    } catch (e) {
      console.error('[Register] Error:', e.message);
      Alert.alert('Register Error', e.message);
    }
  };

  const logColor = (level) => {
    if (level === 'error') return '#ff6b6b';
    if (level === 'warn') return '#ffd93d';
    return '#a8ff78';
  };

  return (
    <KeyboardAvoidingView
      style={styles.container}
      behavior={Platform.OS === 'ios' ? 'padding' : 'height'}
    >
      <ScrollView contentContainerStyle={styles.inner}>
        <Text style={styles.title}>💰 BudgetApp</Text>

        {isRegistering && (
          <TextInput
            style={styles.input}
            placeholder="Name"
            value={name}
            onChangeText={setName}
          />
        )}

        <TextInput
          style={styles.input}
          placeholder="Email"
          value={email}
          onChangeText={setEmail}
          keyboardType="email-address"
          autoCapitalize="none"
        />

        <TextInput
          style={styles.input}
          placeholder="Password"
          value={password}
          onChangeText={setPassword}
          secureTextEntry
        />

        <TouchableOpacity
          style={styles.button}
          onPress={isRegistering ? handleRegister : handleLogin}
        >
          <Text style={styles.buttonText}>
            {isRegistering ? 'Register' : 'Login'}
          </Text>
        </TouchableOpacity>

        <TouchableOpacity onPress={() => setIsRegistering(!isRegistering)}>
          <Text style={styles.toggle}>
            {isRegistering
              ? 'Already have an account? Login'
              : "Don't have an account? Register"}
          </Text>
        </TouchableOpacity>

        {/* ── Debug Log Panel ── */}
        <TouchableOpacity
          style={styles.debugToggle}
          onPress={() => setShowDebug((v) => !v)}
        >
          <Text style={styles.debugToggleText}>
            {showDebug ? '▲ Hide Debug Logs' : '▼ Show Debug Logs'}{' '}
            ({logs.length})
          </Text>
        </TouchableOpacity>

        {showDebug && (
          <View style={styles.debugPanel}>
            <ScrollView
              ref={logScrollRef}
              style={styles.debugScroll}
              onContentSizeChange={() =>
                logScrollRef.current?.scrollToEnd({ animated: false })
              }
            >
              {logs.length === 0 ? (
                <Text style={styles.debugEmpty}>No logs yet.</Text>
              ) : (
                logs.map((entry, i) => (
                  <Text
                    key={i}
                    style={[styles.debugEntry, { color: logColor(entry.level) }]}
                  >
                    <Text style={styles.debugTime}>{entry.time} </Text>
                    <Text style={styles.debugLevel}>[{entry.level.toUpperCase()}] </Text>
                    {entry.msg}
                  </Text>
                ))
              )}
            </ScrollView>
          </View>
        )}
      </ScrollView>
    </KeyboardAvoidingView>
  );
}

const styles = StyleSheet.create({
  container: {
    flex: 1,
    backgroundColor: '#f5f5f5',
  },
  inner: {
    flexGrow: 1,
    justifyContent: 'center',
    padding: 24,
  },
  title: {
    fontSize: 32,
    fontWeight: 'bold',
    textAlign: 'center',
    marginBottom: 32,
  },
  input: {
    backgroundColor: '#fff',
    borderWidth: 1,
    borderColor: '#ddd',
    borderRadius: 8,
    padding: 12,
    marginBottom: 16,
    fontSize: 16,
  },
  button: {
    backgroundColor: '#6200ee',
    borderRadius: 8,
    padding: 14,
    alignItems: 'center',
    marginBottom: 16,
  },
  buttonText: {
    color: '#fff',
    fontSize: 16,
    fontWeight: 'bold',
  },
  toggle: {
    textAlign: 'center',
    color: '#6200ee',
    fontSize: 14,
    marginBottom: 24,
  },
  // Debug panel
  debugToggle: {
    alignItems: 'center',
    paddingVertical: 8,
    marginBottom: 4,
  },
  debugToggleText: {
    color: '#999',
    fontSize: 12,
    fontFamily: Platform.OS === 'ios' ? 'Menlo' : 'monospace',
  },
  debugPanel: {
    backgroundColor: '#111',
    borderRadius: 8,
    padding: 8,
    maxHeight: 280,
  },
  debugScroll: {
    flex: 1,
  },
  debugEmpty: {
    color: '#555',
    fontSize: 11,
    fontFamily: Platform.OS === 'ios' ? 'Menlo' : 'monospace',
  },
  debugEntry: {
    fontSize: 10,
    fontFamily: Platform.OS === 'ios' ? 'Menlo' : 'monospace',
    marginBottom: 2,
    flexWrap: 'wrap',
  },
  debugTime: {
    color: '#666',
  },
  debugLevel: {
    fontWeight: 'bold',
  },
});