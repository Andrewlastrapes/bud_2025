import React from 'react';
import { createNativeStackNavigator } from '@react-navigation/native-stack';
import { Button } from 'react-native-paper';
import { auth } from '../firebaseConfig';
import { signOut } from 'firebase/auth';

import WelcomeScreen from '../screens/onboarding/WelcomeScreen';
import PlaidConnectScreen from '../screens/onboarding/PlaidConnectScreen';
import DepositOnboardingScreen from '../screens/onboarding/DepositOnboardingScreen';
import FixedCostsSetupScreen from '../screens/onboarding/FixedCostsSetupScreen';
import DebtOnboardingScreen from '../screens/onboarding/DebtOnboardingScreen';
import SavingsOnboardingScreen from '../screens/onboarding/SavingsOnboardingScreen';
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

      {/* Setup: connect bank accounts via Plaid */}
      <Stack.Screen
        name="ConnectPlaid"
        component={PlaidConnectScreen}
        options={{ title: 'Connect Banks' }}
      />

      {/* Step 1: collect income / paycheck info */}
      <Stack.Screen
        name="DepositOnboarding"
        component={DepositOnboardingScreen}
        options={{ title: '1. Your Income' }}
      />

      {/* Step 2: enter fixed recurring costs (rent, car, etc.) */}
      <Stack.Screen
        name="FixedCostsSetup"
        component={FixedCostsSetupScreen}
        options={{ title: '2. Fixed Costs' }}
      />

      {/* Step 3: credit card debt — how much to pay each paycheck */}
      <Stack.Screen
        name="DebtOnboarding"
        component={DebtOnboardingScreen}
        options={{ title: '3. Debt' }}
      />

      {/* Step 4: savings per paycheck (with debt warning if applicable) */}
      <Stack.Screen
        name="SavingsOnboarding"
        component={SavingsOnboardingScreen}
        options={{ title: '4. Savings' }}
      />

      {/* Final: show the calculated remaining-to-spend amount */}
      <Stack.Screen
        name="DynamicAmountFinal"
        component={DynamicAmountFinalScreen}
        options={{ title: 'Your Budget' }}
      />
    </Stack.Navigator>
  );
}