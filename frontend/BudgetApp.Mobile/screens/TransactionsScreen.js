import React from 'react';
import { SafeAreaView, Text, StyleSheet } from 'react-native';

export default function TransactionsScreen() {
    return (
        <SafeAreaView style={styles.container}>
            <Text style={styles.text}>Transactions Screen</Text>
            <Text>A list of your spending will go here.</Text>
        </SafeAreaView>
    );
}

const styles = StyleSheet.create({
    container: { flex: 1, justifyContent: 'center', alignItems: 'center' },
    text: { fontSize: 20, fontWeight: 'bold', marginBottom: 10 },
});