import React from 'react';
import { createNativeStackNavigator } from '@react-navigation/native-stack';
import { Button } from 'react-native-paper';
import { auth } from '../firebaseConfig';
import { signOut } from 'firebase/auth';

import WelcomeScreen from '../screens/onboarding/WelcomeScreen';
import PlaidConnectScreen from '../screens/onboarding/PlaidConnectScreen';
import FixedCostsSetupScreen from '../screens/onboarding/FixedCostsSetupScreen';
import PaycheckSavingsScreen from '../screens/onboarding/PaycheckSavingsScreen';
import DebtOnboardingScreen from '../screens/onboarding/DebtOnboardingScreen';
import DynamicAmountFinalScreen from '../screens/onboarding/DynamicAmountFinalScreen';

const Stack = createNativeStackNavigator();

const handleLogout = () => {
  signOut(auth).catch((error) => console.error('Logout Error:', error));
};

export default function OnboardingStack() {
  return (
    <Stack.Navigator
      screenOptions={{
        headerRight: () => (
          <Button onPress={handleLogout} uppercase={false}>
            Logout
          </Button>
        ),
      }}
    >
      {/* Welcome: no header — nothing to go back to */}
      <Stack.Screen
        name="Welcome"
        component={WelcomeScreen}
        options={{ headerShown: false }}
      />
      {/* Steps 1–4 — names must match every navigation.navigate() call in each screen */}
      <Stack.Screen
        name="ConnectPlaid"
        component={PlaidConnectScreen}
        options={{ title: '1. Connect Banks' }}
      />
      <Stack.Screen
        name="FixedCostsSetup"
        component={FixedCostsSetupScreen}
        options={{ title: '2. Fixed Costs' }}
      />
      <Stack.Screen
        name="DebtOnboarding"
        component={DebtOnboardingScreen}
        options={{ title: 'Credit card debt' }}
      />
      <Stack.Screen
        name="PaycheckSavings"
        component={PaycheckSavingsScreen}
        options={{ title: '3. Paycheck & Savings' }}
      />
      <Stack.Screen
        name="DynamicAmountFinal"
        component={DynamicAmountFinalScreen}
        options={{ title: '4. Final Budget' }}
      />
    </Stack.Navigator>
  );
}