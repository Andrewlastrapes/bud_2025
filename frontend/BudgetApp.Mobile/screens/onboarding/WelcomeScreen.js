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

export default function WelcomeScreen({ navigation }) {
    return (
        <SafeAreaView style={styles.safe}>
            <StatusBar barStyle="light-content" backgroundColor="#0F172A" />
            <ScrollView
                contentContainerStyle={styles.scrollContent}
                showsVerticalScrollIndicator={false}
                bounces={false}
            >
                {/* Wordmark */}
                <View style={styles.wordmarkRow}>
                    <Text style={styles.wordmark}>BUD</Text>
                </View>

                {/* Main copy block */}
                <View style={styles.copyBlock}>
                    <Text style={styles.primaryText}>
                        Most budget apps assume you're already in control.{' '}
                        <Text style={styles.primaryEmphasis}>
                            This one is for when you're not.
                        </Text>
                        {' '}It shows you what's left from each paycheck after bills, spending, and debt, so you can make a real plan, stop falling further behind, and start getting out.
                    </Text>

                    <Text style={styles.secondaryText}>
                        Instead of giving you a generic budget, it gives you a paycheck-by-paycheck plan based on your real bills, spending, balances, and debt — so you can break the cycle and make real progress.
                    </Text>
                </View>

                {/* CTA */}
                <TouchableOpacity
                    style={styles.ctaButton}
                    onPress={() => navigation.navigate('HowItWorks')}
                    activeOpacity={0.82}
                >
                    <Text style={styles.ctaLabel}>Get Started</Text>
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
        flexGrow: 1,
        paddingHorizontal: 28,
        paddingTop: 48,
        paddingBottom: 36,
    },

    // Wordmark
    wordmarkRow: {
        alignItems: 'center',
        marginBottom: 56,
    },
    wordmark: {
        fontSize: 13,
        fontWeight: '700',
        color: '#6366F1',
        letterSpacing: 5,
    },

    // Copy
    copyBlock: {
        flex: 1,
        marginBottom: 48,
    },
    primaryText: {
        fontSize: 20,
        fontWeight: '400',
        color: '#CBD5E1',
        lineHeight: 32,
        marginBottom: 24,
    },
    primaryEmphasis: {
        color: '#F1F5F9',
        fontWeight: '600',
    },
    secondaryText: {
        fontSize: 16,
        fontWeight: '400',
        color: '#64748B',
        lineHeight: 27,
    },

    // CTA
    ctaButton: {
        backgroundColor: '#4F46E5',
        borderRadius: 14,
        height: 56,
        alignItems: 'center',
        justifyContent: 'center',
        width: '100%',
    },
    ctaLabel: {
        color: '#FFFFFF',
        fontSize: 16,
        fontWeight: '600',
        letterSpacing: 0.4,
    },
});