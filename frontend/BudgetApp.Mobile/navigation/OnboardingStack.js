import React from 'react';
import { createNativeStackNavigator } from '@react-navigation/native-stack';
import WelcomeScreen from '../screens/onboarding/WelcomeScreen';
import PlaidConnectScreen from '../screens/onboarding/PlaidConnectScreen';
import PaycheckSavingsScreen from '../screens/onboarding/PaycheckSavingsScreen';
import FixedCostsSetupScreen from '../screens/onboarding/FixedCostsSetupScreen';
import DebtOnboardingScreen from '../screens/onboarding/DebtOnboardingScreen';
import DynamicAmountFinalScreen from '../screens/onboarding/DynamicAmountFinalScreen';

const Stack = createNativeStackNavigator();

export default function OnboardingStack() {
  return (
    <Stack.Navigator
      initialRouteName="Welcome"
      screenOptions={{
        headerTintColor: '#6200ee',
      }}
    >
      <Stack.Screen name="Welcome" component={WelcomeScreen} options={{ title: 'Welcome' }} />
      <Stack.Screen name="PlaidConnect" component={PlaidConnectScreen} options={{ title: 'Connect Bank' }} />
      <Stack.Screen name="PaycheckSavings" component={PaycheckSavingsScreen} options={{ title: 'Paycheck & Savings' }} />
      <Stack.Screen name="FixedCostsSetup" component={FixedCostsSetupScreen} options={{ title: 'Fixed Costs' }} />
      <Stack.Screen name="DebtOnboarding" component={DebtOnboardingScreen} options={{ title: 'Debt' }} />
      <Stack.Screen name="DynamicAmountFinal" component={DynamicAmountFinalScreen} options={{ title: 'Review & Finalize' }} />
    </Stack.Navigator>
  );
}