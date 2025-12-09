// File: App.js

import React, { useState, useEffect } from 'react';
import { View, ActivityIndicator, StyleSheet } from 'react-native';
import { NavigationContainer } from '@react-navigation/native';
import { createBottomTabNavigator } from '@react-navigation/bottom-tabs';
import { createNativeStackNavigator } from '@react-navigation/native-stack';
import { PaperProvider, DefaultTheme, Button } from 'react-native-paper';
import { SafeAreaProvider } from 'react-native-safe-area-context';
import MaterialCommunityIcons from 'react-native-vector-icons/MaterialCommunityIcons';
import axios from 'axios';

import { auth } from './firebaseConfig';
import { onAuthStateChanged, signOut } from 'firebase/auth';

// --- Screens & Navigators ---
import HomeScreen from './screens/HomeScreen';
import TransactionsScreen from './screens/TransactionsScreen';
import SettingsScreen from './screens/SettingsScreen';
import LoginScreen from './screens/LoginScreen';
import FixedCostsScreen from './screens/FixedCostsScreen';
import OnboardingStack from './navigation/OnboardingStack';
import DepositReviewScreen from './screens/DepositReviewScreen';

// --- API Base URL ---
const API_BASE_URL = 'http://localhost:5150';

// --- Navigators ---
const Stack = createNativeStackNavigator();
const Tab = createBottomTabNavigator();

/**
 * Bottom tab navigator for the main app
 */
function AppTabs() {
  return (
    <Tab.Navigator
      screenOptions={{
        tabBarActiveTintColor: '#6200ee',
        headerShown: false,
      }}
    >
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
            <MaterialCommunityIcons
              name="format-list-bulleted"
              color={color}
              size={size}
            />
          ),
        }}
      />
      <Tab.Screen
        name="FixedCosts"
        component={FixedCostsScreen}
        options={{
          tabBarLabel: 'Fixed',
          tabBarIcon: ({ color, size }) => (
            <MaterialCommunityIcons
              name="currency-usd"
              color={color}
              size={size}
            />
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
 * Simple loading screen used in a few places
 */
function LoadingScreen() {
  return (
    <View style={styles.loadingContainer}>
      <ActivityIndicator size="large" />
    </View>
  );
}

/**
 * Main content navigator:
 * - Always defines both "App" and "OnboardingFlow"
 * - Chooses initialRouteName based on onboardingComplete flag
 */
function MainContentNavigator({ fbUser }) {
  const [dbUser, setDbUser] = useState(null);
  const [isLoading, setIsLoading] = useState(true);

  const fetchUserProfile = async (retryCount = 0) => {
    try {
      const idToken = await fbUser.getIdToken();
      const response = await axios.get(`${API_BASE_URL}/api/users/profile`, {
        headers: { Authorization: `Bearer ${idToken}` },
      });

      console.log('Fetched user profile:', response.data);
      setDbUser(response.data);
      setIsLoading(false);
    } catch (e) {
      if (e.response && e.response.status === 404 && retryCount < 5) {
        console.warn(
          `[RETRYING] User not in DB yet. Waiting... (Attempt ${retryCount + 1
          })`,
        );
        await new Promise((resolve) => setTimeout(resolve, 1000));
        await fetchUserProfile(retryCount + 1);
        return;
      }

      console.error('Failed to fetch user profile:', e);
      setDbUser(null);
      setIsLoading(false);
    }
  };

  useEffect(() => {
    fetchUserProfile();
  }, [fbUser]);

  if (isLoading) {
    return <LoadingScreen />;
  }

  const initialRouteName =
    dbUser && !dbUser.onboardingComplete ? 'OnboardingFlow' : 'App';

  return (
    <Stack.Navigator
      initialRouteName={initialRouteName}
      screenOptions={{ headerShown: false }}
    >
      {/* Main app tabs – always registered */}
      <Stack.Screen
        name="App"
        component={AppTabs}
        options={{
          headerShown: true,
          title:
            dbUser?.name ||
            fbUser?.email?.split('@')[0] ||
            'Budget App',
          headerRight: () => (
            <Button onPress={() => signOut(auth)}>
              Logout
            </Button>
          ),
        }}
      />

      <Stack.Screen
        name="DepositReview"
        component={DepositReviewScreen}
        options={{
          headerShown: true,
          title: 'Review Deposits',
        }}
      />

      {/* Onboarding flow – only present while onboarding is incomplete */}
      {!dbUser?.onboardingComplete && (
        <Stack.Screen
          name="OnboardingFlow"
          component={OnboardingStack}
          options={{ headerShown: false }}
        />
      )}
    </Stack.Navigator>
  );
}

/**
 * Root app: gatekeeper on Firebase auth
 */
export default function App() {
  const [fbUser, setFbUser] = useState(null);
  const [isLoading, setIsLoading] = useState(true);

  useEffect(() => {
    const unsubscribe = onAuthStateChanged(auth, async (user) => {
      setFbUser(user);
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
            <MainContentNavigator fbUser={fbUser} />
          ) : (
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
  loadingContainer: {
    flex: 1,
    justifyContent: 'center',
    alignItems: 'center',
  },
});
