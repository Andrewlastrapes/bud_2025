import React, { useState } from 'react';
import {
  View,
  StyleSheet,
  Alert,
  Keyboard,
  TouchableWithoutFeedback,
  ScrollView,
  StatusBar,
} from 'react-native';
import { Text, TextInput, Button } from 'react-native-paper';
import { SafeAreaView } from 'react-native-safe-area-context';

// ─── Design tokens ────────────────────────────────────────────────────────────
const C = {
  primary:   '#5B21B6',
  primaryLt: '#7C3AED',
  surface:   '#FFFFFF',
  bg:        '#F5F3FF',
  text:      '#1E1B4B',
  muted:     '#6B7280',
  border:    '#E5E7EB',
  error:     '#EF4444',
};

function StepDots({ current, total }) {
  return (
    <View style={dot.row}>
      {Array.from({ length: total }).map((_, i) => (
        <View key={i} style={[dot.dot, i + 1 === current && dot.active]} />
      ))}
    </View>
  );
}
const dot = StyleSheet.create({
  row:    { flexDirection: 'row', gap: 6, justifyContent: 'center', marginBottom: 28 },
  dot:    { width: 8, height: 8, borderRadius: 4, backgroundColor: '#DDD6FE' },
  active: { width: 24, backgroundColor: C.primary },
});

export default function DepositOnboardingScreen({ navigation }) {
  const [paycheckAmount, setPaycheckAmount] = useState('');
  const [payDay1, setPayDay1] = useState('1');
  const [payDay2, setPayDay2] = useState('15');

  const handleNext = () => {
    const amount = parseFloat(paycheckAmount);
    const day1   = parseInt(payDay1, 10);
    const day2   = parseInt(payDay2, 10);

    if (isNaN(amount) || amount <= 0) {
      Alert.alert('Missing Info', 'Please enter a valid paycheck amount.');
      return;
    }
    if (isNaN(day1) || day1 < 1 || day1 > 31 || isNaN(day2) || day2 < 1 || day2 > 31) {
      Alert.alert('Invalid Pay Days', 'Please enter valid days of the month (1–31).');
      return;
    }
    if (day1 === day2) {
      Alert.alert('Invalid Pay Days', 'Your two pay days must be different.');
      return;
    }

    navigation.navigate('FixedCostsSetup', { paycheckAmount: amount, payDay1: day1, payDay2: day2 });
  };

  return (
    <TouchableWithoutFeedback onPress={Keyboard.dismiss} accessible={false}>
      <SafeAreaView style={s.safe}>
        <StatusBar barStyle="dark-content" backgroundColor={C.bg} />
        <ScrollView contentContainerStyle={s.scroll} keyboardShouldPersistTaps="handled">

          <StepDots current={1} total={4} />

          <Text style={s.eyebrow}>STEP 1 OF 4</Text>
          <Text style={s.heading}>Your Income</Text>
          <Text style={s.sub}>
            Tell us your take-home pay and the days you get paid each month.
          </Text>

          <View style={s.fieldGroup}>
            <Text style={s.label}>Take-home paycheck amount</Text>
            <TextInput
              mode="outlined"
              label="$0.00"
              value={paycheckAmount}
              onChangeText={setPaycheckAmount}
              keyboardType="numeric"
              style={s.input}
              outlineStyle={s.inputOutline}
              left={<TextInput.Affix text="$" />}
            />
          </View>

          <View style={s.fieldGroup}>
            <Text style={s.label}>Two monthly pay days</Text>
            <Text style={s.hint}>e.g. paid on the 1st and 15th → enter "1" and "15"</Text>
            <View style={s.row}>
              <TextInput
                mode="outlined"
                label="Day 1"
                value={payDay1}
                onChangeText={setPayDay1}
                keyboardType="numeric"
                style={[s.input, s.half]}
                outlineStyle={s.inputOutline}
              />
              <TextInput
                mode="outlined"
                label="Day 2"
                value={payDay2}
                onChangeText={setPayDay2}
                keyboardType="numeric"
                style={[s.input, s.half]}
                outlineStyle={s.inputOutline}
              />
            </View>
          </View>

          <View style={s.infoBox}>
            <Text style={s.infoText}>
              💡 We use these dates to figure out which bills fall between now and your next paycheck.
            </Text>
          </View>

          <Button
            mode="contained"
            onPress={handleNext}
            style={s.btn}
            contentStyle={s.btnContent}
            labelStyle={s.btnLabel}
            buttonColor={C.primary}
          >
            Next: Fixed Costs →
          </Button>

        </ScrollView>
      </SafeAreaView>
    </TouchableWithoutFeedback>
  );
}

const s = StyleSheet.create({
  safe:   { flex: 1, backgroundColor: C.bg },
  scroll: { padding: 24, paddingBottom: 48 },

  eyebrow: { fontSize: 11, fontWeight: '700', letterSpacing: 1.5, color: C.primaryLt, marginBottom: 6 },
  heading: { fontSize: 30, fontWeight: '800', color: C.text, marginBottom: 8, letterSpacing: -0.5 },
  sub:     { fontSize: 15, color: C.muted, lineHeight: 22, marginBottom: 32 },

  fieldGroup: { marginBottom: 20 },
  label:  { fontSize: 13, fontWeight: '600', color: C.text, marginBottom: 6, letterSpacing: 0.2 },
  hint:   { fontSize: 12, color: C.muted, marginBottom: 8 },
  input:  { backgroundColor: C.surface },
  inputOutline: { borderRadius: 12, borderColor: C.border },
  row:    { flexDirection: 'row', gap: 12 },
  half:   { flex: 1 },

  infoBox: {
    backgroundColor: '#EDE9FE',
    borderRadius: 14,
    padding: 14,
    marginBottom: 28,
  },
  infoText: { fontSize: 13, color: C.primary, lineHeight: 20 },

  btn:        { borderRadius: 16, marginTop: 4 },
  btnContent: { paddingVertical: 8 },
  btnLabel:   { fontSize: 16, fontWeight: '700', letterSpacing: 0.3 },
});