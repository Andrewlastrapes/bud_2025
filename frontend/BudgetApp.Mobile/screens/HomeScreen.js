import React from 'react';
import { SafeAreaView, Text, StyleSheet } from 'react-native';

export default function HomeScreen() {
    return (
        <SafeAreaView style={styles.container}>
            <Text style={styles.text}>Home / Balance Screen</Text>
            <Text>Your "Dynamic Amount" will go here.</Text>
        </SafeAreaView>
    );
}

const styles = StyleSheet.create({
    container: { flex: 1, justifyContent: 'center', alignItems: 'center' },
    text: { fontSize: 20, fontWeight: 'bold', marginBottom: 10 },
});