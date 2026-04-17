/**
 * Design tokens for the Budget App.
 * Import what you need: import { colors, spacing, radius, type, shadow } from './theme';
 */

export const colors = {
  // Brand
  primary: '#4F46E5',       // indigo-600 — modern, trustworthy
  primaryLight: '#6366F1',  // indigo-500

  // Dark palette (onboarding / splash screens)
  darkBg: '#0F172A',           // slate-900
  darkSurface: '#1E293B',      // slate-800
  darkBorder: 'rgba(255,255,255,0.09)',
  darkTextPrimary: '#F1F5F9',  // slate-100
  darkTextSecondary: '#CBD5E1', // slate-300
  darkTextMuted: '#94A3B8',    // slate-400
  darkAccent: '#A5B4FC',       // indigo-300 — accent text on dark

  // Light palette (main app screens)
  lightBg: '#F8FAFC',     // slate-50
  lightSurface: '#FFFFFF',
  lightBorder: 'rgba(0,0,0,0.07)',
  textPrimary: '#0F172A',   // slate-900
  textSecondary: '#475569', // slate-600
  textMuted: '#94A3B8',     // slate-400

  // Semantic
  success: '#0D9488',      // teal-600 — positive balance
  successBg: '#F0FDFA',
  danger: '#DC2626',       // red-600 — over budget
  dangerBg: '#FEF2F2',
  warning: '#D97706',      // amber-600
  warningBg: '#FFFBEB',
  warningBorder: '#FDE68A',

  white: '#FFFFFF',
};

export const spacing = {
  xs: 4,
  sm: 8,
  md: 16,
  lg: 24,
  xl: 32,
  xxl: 48,
};

export const radius = {
  sm: 8,
  md: 12,
  lg: 16,
  xl: 20,
  full: 999,
};

export const type = {
  // Font sizes
  xs: 11,
  sm: 13,
  base: 16,
  lg: 18,
  xl: 22,
  xxl: 28,
  display: 48,

  // Font weights
  light: '300',
  regular: '400',
  medium: '500',
  semibold: '600',
  bold: '700',
  heavy: '800',
};

export const shadow = {
  sm: {
    shadowColor: '#000',
    shadowOffset: { width: 0, height: 1 },
    shadowOpacity: 0.06,
    shadowRadius: 4,
    elevation: 2,
  },
  md: {
    shadowColor: '#000',
    shadowOffset: { width: 0, height: 2 },
    shadowOpacity: 0.09,
    shadowRadius: 10,
    elevation: 4,
  },
  lg: {
    shadowColor: '#000',
    shadowOffset: { width: 0, height: 4 },
    shadowOpacity: 0.12,
    shadowRadius: 18,
    elevation: 8,
  },
};