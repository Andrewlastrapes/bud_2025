import React from 'react';
import {
    View,
    Text,
    StyleSheet,
    ScrollView,
    TouchableOpacity,
    StatusBar,
} from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';

export default function HowItWorksScreen({ navigation }) {
    return (
        <SafeAreaView style={styles.safe}>
            <StatusBar barStyle="light-content" backgroundColor="#0F172A" />
            <ScrollView
                contentContainerStyle={styles.scrollContent}
                showsVerticalScrollIndicator={false}
            >
                {/* Title */}
                <Text style={styles.title}>How it works</Text>
                <View style={styles.divider} />

                {/* Paragraphs */}
                <Text style={styles.body}>
                    We start with your next paycheck and work backward from reality.
                </Text>

                <Text style={styles.body}>
                    First, we subtract the fixed and recurring expenses expected to hit{' '}
                    <Text style={styles.highlight}>before your next paycheck</Text>
                    {' '}— things like rent, subscriptions, utilities, loan payments, and other bills that are already spoken for. That gives you a clearer picture of what money is actually available.
                </Text>

                <Text style={styles.body}>
                    If you're paying off debt, we also factor in the{' '}
                    <Text style={styles.highlight}>amount you plan to put toward debt each paycheck</Text>
                    . That lets the app show not just what you can safely spend now, but also how long it could take to get out of debt based on the amount you choose. As the app gets smarter about your accounts, it can also help account for the cost of interest so your payoff plan is grounded in reality.
                </Text>

                <Text style={styles.body}>
                    If you're not in debt — or once you're out — the app can factor in{' '}
                    <Text style={styles.highlight}>per-paycheck savings</Text>
                    {' '}too, so you can keep building stability without losing track of what's safe to spend.
                </Text>

                <Text style={[styles.body, styles.lastBody]}>
                    The result is a{' '}
                    <Text style={styles.highlight}>dynamic amount</Text>
                    : a paycheck-by-paycheck number that updates as transactions come in, bills get closer, and your situation changes. Notifications help keep that number current, so you always know where you stand instead of guessing.
                </Text>

                {/* CTA */}
                <TouchableOpacity
                    style={styles.ctaButton}
                    onPress={() => navigation.navigate('ConnectPlaid')}
                    activeOpacity={0.82}
                >
                    <Text style={styles.ctaLabel}>Next</Text>
                </TouchableOpacity>
            </ScrollView>
        </SafeAreaView>
    );
}

const styles = StyleSheet.create({
    safe: {
        flex: 1,
        backgroundColor: '#0F172A',
    },
    scrollContent: {
        paddingHorizontal: 28,
        paddingTop: 48,
        paddingBottom: 36,
    },

    // Header
    title: {
        fontSize: 26,
        fontWeight: '700',
        color: '#F1F5F9',
        marginBottom: 16,
        letterSpacing: -0.3,
    },
    divider: {
        height: 2,
        width: 36,
        backgroundColor: '#4F46E5',
        borderRadius: 2,
        marginBottom: 32,
    },

    // Body
    body: {
        fontSize: 16,
        fontWeight: '400',
        color: '#94A3B8',
        lineHeight: 27,
        marginBottom: 22,
    },
    lastBody: {
        marginBottom: 40,
    },
    highlight: {
        color: '#A5B4FC',
        fontWeight: '600',
    },

    // CTA
    ctaButton: {
        backgroundColor: '#4F46E5',
        borderRadius: 14,
        height: 56,
        alignItems: 'center',
        justifyContent: 'center',
        width: '100%',
        marginBottom: 8,
    },
    ctaLabel: {
        color: '#FFFFFF',
        fontSize: 16,
        fontWeight: '600',
        letterSpacing: 0.4,
    },
});