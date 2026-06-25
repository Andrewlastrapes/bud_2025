import React, { useState } from "react";
import { View, ScrollView, StyleSheet, Platform } from "react-native";
import { SafeAreaView } from "react-native-safe-area-context";
import { Text, TextInput, Button, ActivityIndicator } from "react-native-paper";
import { auth } from "../firebaseConfig";
import {
  createUserWithEmailAndPassword,
  signInWithEmailAndPassword,
} from "firebase/auth";
import { ensureDeviceRegisteredForCurrentUser } from "../services/notificationRegistration";

export default function LoginScreen() {
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [isLoading, setIsLoading] = useState(false);
  const [error, setError] = useState(null);

  const handleSignIn = async () => {
    setIsLoading(true);
    setError(null);
    try {
      const cred = await signInWithEmailAndPassword(auth, email, password);

      // Register device for push notifications after successful login
      // This handles existing users who may not have device rows yet
      try {
        await ensureDeviceRegisteredForCurrentUser(cred.user);
      } catch (deviceRegError) {
        console.error("[Login] Device registration failed:", deviceRegError);
        // Don't block login flow if device registration fails
      }
    } catch (e) {
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
      const cred = await createUserWithEmailAndPassword(auth, email, password);
      fbUser = cred.user;

      // 2. Get ID token to pass as Bearer
      const idToken = await fbUser.getIdToken();

      // 3. Register in backend — UID is derived from the bearer token server-side
      //    Body only needs { email, name }
      const apiUrl = process.env.EXPO_PUBLIC_API_URL;
      const registerUrl = `${apiUrl}/api/users/register`;

      let response;
      try {
        response = await fetch(registerUrl, {
          method: "POST",
          headers: {
            "Content-Type": "application/json",
            Authorization: `Bearer ${idToken}`,
          },
          body: JSON.stringify({
            email,
            name: email.split("@")[0],
          }),
        });
      } catch (netErr) {
        throw netErr;
      }

      const responseText = await response.text();

      if (!response.ok) {
        throw new Error(
          `Backend registration failed ${response.status}: ${responseText}`,
        );
      }

      // ── CRITICAL FIX: Register device AFTER backend user creation ──────────
      // This eliminates the race condition where device registration arrives
      // before the backend user exists, causing 404 "User not found" errors.
      // The helper has retry logic to handle any remaining edge cases.
      try {
        await ensureDeviceRegisteredForCurrentUser(fbUser);
      } catch (deviceRegError) {
        console.error("[SignUp] Device registration failed:", deviceRegError);
        // Don't block signup flow if device registration fails
        // The App.js auth state listener will retry on next app launch
      }
    } catch (e) {
      setError(e.message);

      // Rollback Firebase user if backend failed
      if (fbUser) {
        try {
          await fbUser.delete();
        } catch (rb) {
          // rollback failed silently
        }
      }
    }

    setIsLoading(false);
  };

  return (
    <SafeAreaView style={styles.container}>
      <ScrollView
        contentContainerStyle={styles.scroll}
        keyboardShouldPersistTaps="handled"
      >
        <Text style={styles.title}>NearPath</Text>
        <Text style={styles.tagline}>
          A no-category paycheck plan to cover bills, pay down debt, and save.
        </Text>

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
            <Button
              mode="contained"
              onPress={handleSignIn}
              style={styles.button}
            >
              Login
            </Button>
            <Button
              mode="outlined"
              onPress={handleSignUp}
              style={styles.button}
            >
              Sign Up
            </Button>
          </>
        )}

        {error ? (
          <Text style={styles.error} selectable>
            {error}
          </Text>
        ) : null}
      </ScrollView>
    </SafeAreaView>
  );
}

const styles = StyleSheet.create({
  container: { flex: 1 },
  scroll: { padding: 20, paddingTop: 40 },
  title: {
    fontSize: 28,
    fontWeight: "bold",
    textAlign: "center",
    marginBottom: 6,
  },
  tagline: {
    fontSize: 13,
    color: "#888",
    textAlign: "center",
    marginBottom: 20,
    lineHeight: 18,
  },
  input: { marginBottom: 10, marginTop: 8 },
  button: { marginTop: 10 },
  error: { marginTop: 10, color: "red", textAlign: "center" },
});
