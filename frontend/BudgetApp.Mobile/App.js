import React, { useState, useEffect } from 'react';
import { View, ActivityIndicator, StyleSheet } from 'react-native';
import { NavigationContainer } from '@react-navigation/native';
import { createBottomTabNavigator } from '@react-navigation/bottom-tabs';
import { createNativeStackNavigator } from '@react-navigation/native-stack';
import { PaperProvider, DefaultTheme, Button } from 'react-native-paper';
import { SafeAreaProvider } from 'react-native-safe-area-context';
import MaterialCommunityIcons from 'react-native-vector-icons/MaterialCommunityIcons';
import axios from 'axios';

// Import our auth object
import { auth } from './firebaseConfig';
import { onAuthStateChanged, signOut } from 'firebase/auth';

// --- Import Screens & Navigators ---
import HomeScreen from './screens/HomeScreen';
import TransactionsScreen from './screens/TransactionsScreen';
import SettingsScreen from './screens/SettingsScreen';
import LoginScreen from './screens/LoginScreen';
import FixedCostsScreen from './screens/FixedCostsScreen';
import OnboardingStack from './navigation/OnboardingStack';

// --- API Base URL ---
const API_BASE_URL = 'http://localhost:5150';

// --- Create Navigators ---
const Stack = createNativeStackNavigator();
const Tab = createBottomTabNavigator();

/**
 * Main Tab Navigator (The visible tabs at the bottom)
 */
// File: App.js

/**
 * This is our main tab navigator.
 * Must define all screens it contains here.
 */
function AppTabs() {
  return (
    <Tab.Navigator
      screenOptions={{
        tabBarActiveTintColor: '#6200ee',
        headerShown: false,
      }}
    >
      {/* 🛑 FIX: Ensure these three screens are defined here 🛑 */}
      <Tab.Screen
        name="Home"
        component={HomeScreen}
        options={{
          tabBarLabel: 'Home',
          tabBarIcon: ({ color, size }) => (
            <MaterialCommunityIcons name="home" color={color} size={size} />
          ),
        }}
      />
      <Tab.Screen
        name="Transactions"
        component={TransactionsScreen}
        options={{
          tabBarLabel: 'Spending',
          tabBarIcon: ({ color, size }) => (
            <MaterialCommunityIcons name="format-list-bulleted" color={color} size={size} />
          ),
        }}
      />
      <Tab.Screen
        name="FixedCosts"
        component={FixedCostsScreen}
        options={{
          tabBarLabel: 'Fixed',
          tabBarIcon: ({ color, size }) => (
            <MaterialCommunityIcons name="currency-usd" color={color} size={size} />
          ),
        }}
      />
      <Tab.Screen
        name="Settings"
        component={SettingsScreen}
        options={{
          tabBarLabel: 'Settings',
          tabBarIcon: ({ color, size }) => (
            <MaterialCommunityIcons name="cog" color={color} size={size} />
          ),
        }}
      />
    </Tab.Navigator>
  );
}

/**
 * Loading screen
 */
function LoadingScreen() {
  return (
    <View style={styles.container}>
      <ActivityIndicator size="large" />
    </View>
  );
}

/**
 * Content Navigator (Checks the DB and routes to AppTabs or Onboarding)
 */
function MainContentNavigator({ fbUser }) {
  const [dbUser, setDbUser] = useState(null);
  const [isLoading, setIsLoading] = useState(true);

  // Fetches the user's full database record (including onboarding_complete)
  const fetchUserProfile = async (retryCount = 0) => {
    try {
      const idToken = await fbUser.getIdToken();
      const response = await axios.get(`${API_BASE_URL}/api/users/profile`, {
        headers: { 'Authorization': `Bearer ${idToken}` }
      });

      console.log("Fetched user profile:", response.data);
      setDbUser(response.data);
      setIsLoading(false);
    } catch (e) {
      // 🛑 FIX: If we get a 404 (user not found) AND haven't exceeded retries, try again
      if (e.response && e.response.status === 404 && retryCount < 5) {
        console.warn(`[RETRYING] User not in DB yet. Waiting... (Attempt ${retryCount + 1})`);
        await new Promise(resolve => setTimeout(resolve, 1000)); // Wait 1 second
        await fetchUserProfile(retryCount + 1); // Recurse with incremented count
        return;
      }

      // Final failure or non-404 error
      console.error("Failed to fetch user profile:", e);
      setDbUser(null);
      setIsLoading(false);
    }
  };

  useEffect(() => {
    // This runs ONCE when MainContentNavigator first mounts (i.e., user logs in)
    fetchUserProfile();
  }, [fbUser]); // Dependency on fbUser is necessary

  if (isLoading) {
    return <LoadingScreen />;
  }

  // --- GATEKEEPER LOGIC ---
  if (dbUser && !dbUser.onboardingComplete) {
    return (
      <Stack.Navigator screenOptions={{ headerShown: false }}>
        <Stack.Screen name="OnboardingFlow" component={OnboardingStack} />
      </Stack.Navigator>
    );
  }

  // User is logged in and onboarding is complete, show main app
  return (
    <Stack.Navigator>
      <Stack.Screen
        name="App"
        component={AppTabs}
        options={{
          title: dbUser?.name || fbUser?.email?.split('@')[0] || 'Budget App',
          headerRight: () => (
            <Button onPress={() => signOut(auth)}>
              Logout
            </Button>
          ),
        }}
      />
    </Stack.Navigator>
  );
}


/**
 * Main App Component (The initial Gatekeeper)
 */
export default function App() {
  const [fbUser, setFbUser] = useState(null);
  const [isLoading, setIsLoading] = useState(true);

  // Listen for Firebase auth state changes
  useEffect(() => {
    const unsubscribe = onAuthStateChanged(auth, async (user) => {
      setFbUser(user);
      // NOTE: We rely on the MainContentNavigator to handle DB lookups and delays
      setIsLoading(false);
    });
    return () => unsubscribe();
  }, []);

  return (
    <SafeAreaProvider>
      <PaperProvider theme={DefaultTheme}>
        <NavigationContainer>
          {isLoading ? (
            <LoadingScreen />
          ) : fbUser ? (
            // Logged in: Check DB status via MainContentNavigator
            <MainContentNavigator fbUser={fbUser} />
          ) : (
            // Logged out: Show Login Screen
            <Stack.Navigator>
              <Stack.Screen
                name="Login"
                component={LoginScreen}
                options={{ headerShown: false }}
              />
            </Stack.Navigator>
          )}
        </NavigationContainer>
      </PaperProvider>
    </SafeAreaProvider>
  );
}

const styles = StyleSheet.create({
  container: {
    flex: 1,
    justifyContent: 'center',
    alignItems: 'center',
  },
});