import React, { useState, useEffect } from 'react';
import { View, ActivityIndicator, StyleSheet } from 'react-native';
import { NavigationContainer } from '@react-navigation/native';
import { createBottomTabNavigator } from '@react-navigation/bottom-tabs';
import { createNativeStackNavigator } from '@react-navigation/native-stack';
import { PaperProvider, DefaultTheme } from 'react-native-paper';
import { SafeAreaProvider } from 'react-native-safe-area-context';
import MaterialCommunityIcons from 'react-native-vector-icons/MaterialCommunityIcons';

// Import our auth object
import { auth } from './firebaseConfig';
import { onAuthStateChanged } from 'firebase/auth';

// --- Import Screens ---
import HomeScreen from './screens/HomeScreen';
import TransactionsScreen from './screens/TransactionsScreen';
import SettingsScreen from './screens/SettingsScreen';
import LoginScreen from './screens/LoginScreen';

// --- Create Navigators ---
const Stack = createNativeStackNavigator();
const Tab = createBottomTabNavigator();

/**
 * This is our main app, shown ONLY to logged-in users.
 */
function AppTabs() {
  return (
    <Tab.Navigator
      initialRouteName="Home"
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
            <MaterialCommunityIcons name="format-list-bulleted" color={color} size={size} />
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
 * This is the loading screen, shown while we check for a user.
 */
function LoadingScreen() {
  return (
    <View style={styles.container}>
      <ActivityIndicator size="large" />
    </View>
  );
}

/**
 * This is the main App component.
 * It's now the "gatekeeper" that decides what to show.
 */
export default function App() {
  const [user, setUser] = useState(null); // The logged-in user
  const [isLoading, setIsLoading] = useState(true); // Check if we're validating auth

  // Listen for auth state changes
  useEffect(() => {
    const unsubscribe = onAuthStateChanged(auth, (user) => {
      setUser(user); // Set the user (or null)
      setIsLoading(false); // Done loading
    });

    // Cleanup subscription on unmount
    return () => unsubscribe();
  }, []);

  return (
    <SafeAreaProvider>
      <PaperProvider theme={DefaultTheme}>
        <NavigationContainer>
          {/* This is the "gatekeeper" logic:
            - If loading, show loading screen
            - If no user, show Login screen
            - If user, show the main app
          */}
          {isLoading ? (
            <LoadingScreen />
          ) : user ? (
            <AppTabs />
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
  container: {
    flex: 1,
    justifyContent: 'center',
    alignItems: 'center',
  },
});