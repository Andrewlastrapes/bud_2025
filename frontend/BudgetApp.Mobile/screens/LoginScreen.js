import React, { useState } from 'react';
import { View, ScrollView, StyleSheet } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { Text, TextInput, Button, ActivityIndicator } from 'react-native-paper';
import { auth } from '../firebaseConfig';
import {
    createUserWithEmailAndPassword,
    signInWithEmailAndPassword,
    signOut,
} from 'firebase/auth';

export default function LoginScreen() {
    const [email, setEmail] = useState('');
    const [password, setPassword] = useState('');
    const [isLoading, setIsLoading] = useState(false);
    const [error, setError] = useState(null);
    const [debugLogs, setDebugLogs] = useState([]);

    const addLog = (msg) => {
        console.log('[DEBUG]', msg);
        setDebugLogs((prev) => [...prev, `${new Date().toISOString().slice(11, 23)} — ${msg}`]);
    };

    const clearLogs = () => setDebugLogs([]);

    const handleSignIn = async () => {
        setIsLoading(true);
        setError(null);
        try {
            await signInWithEmailAndPassword(auth, email, password);
        } catch (e) {
            setError(e.message);
        }
        setIsLoading(false);
    };

    const handleSignUp = async () => {
        setIsLoading(true);
        setError(null);
        clearLogs();

        let firebaseCreated = false;
        let firebaseEmail = null;
        let firebaseUid = null;

        try {
            // Step 1: Create Firebase user
            addLog('Step 1: Creating Firebase user...');
            const userCredential = await createUserWithEmailAndPassword(auth, email, password);
            firebaseCreated = true;
            firebaseEmail = userCredential.user.email;
            firebaseUid = userCredential.user.uid;
            addLog(`Step 1 OK: uid=${firebaseUid}`);

            // Step 2: Sign out immediately so onAuthStateChanged does NOT navigate
            //         the user forward until DB registration is also confirmed.
            addLog('Step 2: Signing out to block premature navigation...');
            await signOut(auth);
            addLog('Step 2 OK: signed out');

            // Step 3: Register user in the backend DB
            const registerUrl = `${process.env.EXPO_PUBLIC_API_URL}/api/users/register`;
            addLog(`Step 3: POST ${registerUrl}`);
            addLog(`  body: name=${firebaseEmail?.split('@')[0]}, email=${firebaseEmail}, uid=${firebaseUid}`);

            const response = await fetch(
                registerUrl,
                {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({
                        name: firebaseEmail?.split('@')[0] || 'User',
                        email: firebaseEmail,
                        firebaseUuid: firebaseUid,
                    }),
                },
            );

            addLog(`Step 3 response: status=${response.status} ok=${response.ok}`);
            const responseText = await response.text();
            addLog(`Step 3 body: ${responseText.slice(0, 300)}`);

            if (!response.ok) {
                throw new Error(
                    `Backend registration failed: ${response.status} ${responseText}`,
                );
            }

            addLog('Step 3 OK: user registered in DB');

            // Step 4: Both steps succeeded — sign back in to trigger navigation to onboarding.
            // ⚠️  NAVIGATION DISABLED FOR DEBUG — uncomment the line below when ready.
            addLog('Step 4: Navigation disabled for debug. Registration complete.');
            // await signInWithEmailAndPassword(auth, email, password);

        } catch (e) {
            addLog(`ERROR: ${e.message}`);
            console.error('Registration error:', e);
            setError(e.message);

            // Rollback: delete the Firebase user if it was created but DB registration failed.
            if (firebaseCreated) {
                try {
                    addLog('Rollback: re-signing in to delete Firebase user...');
                    const cred = await signInWithEmailAndPassword(auth, email, password);
                    await cred.user.delete();
                    addLog('Rollback OK: Firebase user deleted');
                    // onAuthStateChanged fires null after delete, keeping user on login screen.
                } catch (rollbackError) {
                    addLog(`Rollback FAILED: ${rollbackError.message}`);
                    console.error('Failed to delete Firebase user during rollback:', rollbackError);
                }
            }
        }

        setIsLoading(false);
    };

    return (
        <SafeAreaView style={styles.container}>
            <ScrollView contentContainerStyle={styles.scroll} keyboardShouldPersistTaps="handled">
                <Text style={styles.title}>Welcome</Text>

                {/* ── Config debug info ── */}
                <Text style={styles.debugLabel}>Firebase project: {auth.app.options.projectId}</Text>
                <Text style={styles.debugLabel}>App ID: {auth.app.options.appId}</Text>
                <Text style={styles.debugLabel}>API URL: {process.env.EXPO_PUBLIC_API_URL || '⚠️  NOT SET'}</Text>

                <TextInput
                    label="Email"
                    value={email}
                    onChangeText={setEmail}
                    autoCapitalize="none"
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
                            Sign Up (debug)
                        </Button>
                        {debugLogs.length > 0 && (
                            <Button mode="text" onPress={clearLogs} style={styles.button}>
                                Clear Logs
                            </Button>
                        )}
                    </>
                )}

                {error && <Text style={styles.error} selectable>{error}</Text>}

                {/* ── Step-by-step debug log ── */}
                {debugLogs.length > 0 && (
                    <View style={styles.logBox}>
                        <Text style={styles.logTitle}>Debug Log</Text>
                        {debugLogs.map((line, i) => (
                            <Text key={i} style={styles.logLine} selectable>{line}</Text>
                        ))}
                    </View>
                )}
            </ScrollView>
        </SafeAreaView>
    );
}

const styles = StyleSheet.create({
    container: {
        flex: 1,
    },
    scroll: {
        padding: 20,
        paddingTop: 40,
    },
    title: {
        fontSize: 24,
        fontWeight: 'bold',
        textAlign: 'center',
        marginBottom: 16,
    },
    debugLabel: {
        fontSize: 11,
        color: '#666',
        marginBottom: 2,
    },
    input: {
        marginBottom: 10,
        marginTop: 8,
    },
    button: {
        marginTop: 10,
    },
    error: {
        marginTop: 10,
        color: 'red',
        textAlign: 'center',
    },
    logBox: {
        marginTop: 20,
        padding: 12,
        backgroundColor: '#1e1e1e',
        borderRadius: 8,
    },
    logTitle: {
        color: '#aaa',
        fontWeight: 'bold',
        marginBottom: 6,
        fontSize: 12,
    },
    logLine: {
        color: '#d4f5a5',
        fontFamily: 'monospace',
        fontSize: 11,
        marginBottom: 3,
    },
});