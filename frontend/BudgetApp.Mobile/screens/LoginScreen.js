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
        try {
            await createUserWithEmailAndPassword(auth, email, password);
            // No navigation needed, the listener will handle it!
        } catch (e) {
            setError(e.message);
        }
        setIsLoading(false);
    };

    return (
        <SafeAreaView style={styles.container}>
            <Text style={styles.title}>Welcome</Text>

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