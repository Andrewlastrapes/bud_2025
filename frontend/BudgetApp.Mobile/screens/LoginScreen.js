import React, { useState } from 'react';
import { View, StyleSheet } from 'react-native';
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
    const [debugMessage, setDebugMessage] = useState('');

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

        let firebaseCreated = false;
        let firebaseEmail = null;
        let firebaseUid = null;

        try {
            // Step 1: Create Firebase user
            setDebugMessage('Creating Firebase user...');

            const userCredential = await createUserWithEmailAndPassword(auth, email, password);
            firebaseCreated = true;
            firebaseEmail = userCredential.user.email;
            firebaseUid = userCredential.user.uid;

            setDebugMessage(`Firebase created: ${firebaseUid}`);


            // Step 2: Sign out immediately so onAuthStateChanged does NOT navigate
            //         the user forward until DB registration is also confirmed.
            await signOut(auth);
            setDebugMessage('Signed out after Firebase create, before backend register');

            const registerUrl = `${process.env.EXPO_PUBLIC_API_URL}/api/users/register`;
            setDebugMessage(`Calling backend: ${registerUrl}`);

            // Step 3: Register user in the backend DB
            const response = await fetch(
                `${process.env.EXPO_PUBLIC_API_URL}/api/users/register`,
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

            const responseText = await response.text();
            setDebugMessage(`Backend responded: ${response.status} ${responseText}`);

            if (!response.ok) {
                throw new Error(
                    `Backend registration failed: ${response.status} ${responseText}`,
                );
            }
     

            // Step 4: Both steps succeeded — sign back in to trigger navigation to onboarding.
            //         App.js onAuthStateChanged will fire, fetch the user profile
            //         (onboardingComplete: false), and route to OnboardingFlow.
            setDebugMessage('Backend registration succeeded. Signing back in...');
            await signInWithEmailAndPassword(auth, email, password);

            setDebugMessage('Signup complete.');
            console.log('User registered in DB successfully:', responseText);
        } catch (e) {
            console.error('Registration error:', e);
            setError(e.message);

            // Rollback: delete the Firebase user if it was created but DB registration failed.
            if (firebaseCreated) {
                try {
                    console.log('Rolling back — deleting Firebase user...');
                                    setDebugMessage('Signup failed. Rolling back Firebase user...');

                    // Re-sign in so we have a current user reference for deletion.
                    const cred = await signInWithEmailAndPassword(auth, email, password);
                    await cred.user.delete();
                    console.log('Firebase user deleted during rollback');
                    // onAuthStateChanged fires null after delete, keeping user on login screen.
                } catch (rollbackError) {
                    console.error('Failed to delete Firebase user during rollback:', rollbackError);
                                    setDebugMessage(`Rollback failed: ${rollbackMessage}`);

                }
            }
        }

        setIsLoading(false);
    };

    return (
        <SafeAreaView style={styles.container}>
            <Text style={styles.title}>Welcome</Text>

            <Text>Project: {auth.app.options.projectId}</Text>
            <Text>App ID: {auth.app.options.appId}</Text>
            <Text>API URL: {process.env.EXPO_PUBLIC_API_URL}</Text>
            <Text selectable>Debug: {debugMessage}</Text>

            <Text selectable>{error}</Text>

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
                        Sign Up
                    </Button>
                </>
            )}

            {error && <Text style={styles.error}>{error}</Text>}
        </SafeAreaView>
    );
}

const styles = StyleSheet.create({
    container: {
        flex: 1,
        justifyContent: 'center',
        padding: 20,
    },
    title: {
        fontSize: 24,
        fontWeight: 'bold',
        textAlign: 'center',
        marginBottom: 20,
    },
    input: {
        marginBottom: 10,
    },
    button: {
        marginTop: 10,
    },
    error: {
        marginTop: 10,
        color: 'red',
        textAlign: 'center',
    },
});
