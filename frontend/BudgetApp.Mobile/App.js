import React, { useState, useEffect } from 'react';
import { View, ActivityIndicator, StyleSheet } from 'react-native';
import { NavigationContainer } from '@react-navigation/native';
import { createBottomTabNavigator } from '@react-navigation/bottom-tabs';
import { createNativeStackNavigator } from '@react-navigation/native-stack';
import { PaperProvider, DefaultTheme, Button } from 'react-native-paper';
import { SafeAreaProvider } from 'react-native-safe-area-context';
import MaterialCommunityIcons from 'react-native-vector-icons/MaterialCommunityIcons';

// Import auth object
import { auth } from './firebaseConfig';
import { onAuthStateChanged, signOut } from 'firebase/auth';

// --- Import Screens ---
import HomeScreen from './screens/HomeScreen';
import TransactionsScreen from './screens/TransactionsScreen';
import SettingsScreen from './screens/SettingsScreen';
import LoginScreen from './screens/LoginScreen';

// --- Create Navigators ---
const Stack = createNativeStackNavigator();
const Tab = createBottomTabNavigator();

/**
 * This is the main tab navigator.
 * We HIDE the header here because the parent Stack will provide it.
 */
function AppTabs() {
  return (
    <Tab.Navigator
      initialRouteName="Home"
      screenOptions={{
        tabBarActiveTintColor: '#6200ee',
        headerShown: false, // Hide tab-level headers
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
 * Main App Component
 */
export default function App() {
  const [user, setUser] = useState(null);
  const [isLoading, setIsLoading] = useState(true);

  useEffect(() => {
    const unsubscribe = onAuthStateChanged(auth, (user) => {

      if (user) {
        console.log("✅ LOGGED IN USER:", user.email);
        console.log("   UID:", user.uid);
      } else {
        console.log("❌ No user is logged in");
      }
      setUser(user);
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
          ) : user ? (
            // --- LOGGED IN STACK ---
            // We wrap AppTabs in a Stack to give it a global header
            <Stack.Navigator>
              <Stack.Screen
                name="App"
                component={AppTabs}
                options={{
                  // Title is the user's email prefix (e.g., "andrewlastrapes")
                  title: user.email ? user.email.split('@')[0] : 'Budget App',
                  // Right header button is Logout
                  headerRight: () => (
                    <Button onPress={() => signOut(auth)}>Logout</Button>
                  ),
                }}
              />
            </Stack.Navigator>
          ) : (
            // --- LOGGED OUT STACK ---
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