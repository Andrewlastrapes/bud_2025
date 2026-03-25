import React, { useState } from 'react';
import { View, StyleSheet } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { Text, TextInput, Button, ActivityIndicator } from 'react-native-paper';
import { auth } from '../firebaseConfig';
import {
    createUserWithEmailAndPassword,
    signInWithEmailAndPassword
} from 'firebase/auth';

export default function LoginScreen() {
    const [email, setEmail] = useState('');
    const [password, setPassword] = useState('');
    const [isLoading, setIsLoading] = useState(false);
    const [error, setError] = useState(null);

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

    let firebaseUser = null;

    try {
        // 1. Create Firebase user
        const userCredential = await createUserWithEmailAndPassword(auth, email, password);
        firebaseUser = userCredential.user;

        console.log('Firebase user created:', firebaseUser.uid);

        // 2. Register user in your backend DB
        const response = await fetch(`${process.env.EXPO_PUBLIC_API_URL}/api/users/register`, {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
            },
            body: JSON.stringify({
                name: firebaseUser.email?.split('@')[0] || 'User',
                email: firebaseUser.email,
                firebaseUuid: firebaseUser.uid
            })
        });

        const responseText = await response.text();

        if (!response.ok) {
            throw new Error(`Backend registration failed: ${response.status} ${responseText}`);
        }

        console.log('User registered successfully:', responseText);

    } catch (e) {
        console.error('Registration error:', e);
        setError(e.message);

        // 🔥 CRITICAL: rollback Firebase user if DB failed
        if (firebaseUser) {
            try {
                console.log('Deleting Firebase user due to failure...');
                await firebaseUser.delete();
                console.log('Firebase user deleted');
            } catch (deleteError) {
                console.error('Failed to delete Firebase user:', deleteError);
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
