import React from 'react';
import { createNativeStackNavigator } from '@react-navigation/native-stack';
// New Firebase and UI Imports
import { Button } from 'react-native-paper';
import { auth } from '../firebaseConfig';
import { signOut } from 'firebase/auth';

import WelcomeScreen from '../screens/onboarding/WelcomeScreen';
import PlaidConnectScreen from '../screens/onboarding/PlaidConnectScreen';
import FixedCostsSetupScreen from '../screens/onboarding/FixedCostsSetupScreen';
import PaycheckSavingsScreen from '../screens/onboarding/PaycheckSavingsScreen';
import DynamicAmountFinalScreen from '../screens/onboarding/DynamicAmountFinalScreen';

const Stack = createNativeStackNavigator();

// Function to handle the actual logout
const handleLogout = () => {
    signOut(auth)
        .catch(error => console.error("Logout Error:", error));
    // App.js will automatically detect the state change and render the LoginScreen
};

export default function OnboardingStack() {
    return (
        <Stack.Navigator
            screenOptions={{
                // This applies the button to every screen in the stack
                headerRight: () => (
                    <Button
                        onPress={handleLogout}
                        uppercase={false}
                    >
                        Logout
                    </Button>
                ),
            }}
        >
            {/* Page 1: Hide header, as there's nothing to go back to */}
            <Stack.Screen
                name="Welcome"
                component={WelcomeScreen}
                options={{ headerShown: false }}
            />

            {/* Pages 2-5: Headers and Back Buttons are automatically shown */}
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