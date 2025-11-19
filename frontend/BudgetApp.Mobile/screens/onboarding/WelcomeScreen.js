import { View, Text, StyleSheet } from 'react-native';
import React from 'react';
import { Button } from 'react-native-paper';

export default function WelcomeScreen({ navigation }) {
    return (
        <View style={styles.container}>
            <Text style={styles.title}>Welcome to Budget App!</Text>
            <Text style={styles.text}>
                This app helps you simplify your finances by showing you your "Dynamic Amount"—the money you have left to spend until your next paycheck.
            </Text>
            <Button mode="contained" onPress={() => navigation.navigate('ConnectPlaid')}>
                Let's Get Started
            </Button>
        </View>
    );
}
const styles = StyleSheet.create({
    container: { flex: 1, padding: 30, justifyContent: 'space-around', alignItems: 'center' },
    title: { fontSize: 24, fontWeight: 'bold', marginBottom: 15 },
    text: { fontSize: 16, textAlign: 'center' },
});