/** Paleta e tokens visuais do app (tema escuro único, focado em leitura noturna). */

export const colors = {
  bg: '#0E1116',
  bgElevated: '#161B22',
  surface: '#1C232D',
  surfaceAlt: '#232C38',
  border: '#2B3543',
  borderStrong: '#3A4756',

  text: '#F2F5F9',
  textMuted: '#9BA8B8',
  textFaint: '#6B7887',

  primary: '#5B8DEF',
  primaryDark: '#3D6FD1',
  primarySoft: 'rgba(91, 141, 239, 0.16)',

  known: '#2FBF71',
  knownSoft: 'rgba(47, 191, 113, 0.16)',
  partial: '#E8A33D',
  partialSoft: 'rgba(232, 163, 61, 0.16)',
  forgot: '#E4574C',
  forgotSoft: 'rgba(228, 87, 76, 0.16)',

  streak: '#FF8A3D',
  streakSoft: 'rgba(255, 138, 61, 0.16)',
  premium: '#C9A227',
  premiumSoft: 'rgba(201, 162, 39, 0.16)',

  overlay: 'rgba(6, 9, 13, 0.78)',
} as const;

/** Cores sugeridas na criação de um baralho. */
export const deckColors = [
  '#5B8DEF',
  '#2FBF71',
  '#E8A33D',
  '#E4574C',
  '#A77BF3',
  '#22B8CF',
  '#F06595',
  '#8CB369',
] as const;

export const deckEmojis = [
  '📚', '🇺🇸', '🇪🇸', '🇫🇷', '🇩🇪', '🇮🇹', '🇯🇵', '🇰🇷',
  '🧠', '💼', '⚕️', '⚖️', '🧪', '🎵', '✈️', '🍳',
] as const;

export const spacing = {
  xs: 4,
  sm: 8,
  md: 12,
  lg: 16,
  xl: 24,
  xxl: 32,
} as const;

export const radius = {
  sm: 8,
  md: 12,
  lg: 16,
  xl: 24,
  pill: 999,
} as const;

export const typography = {
  h1: { fontSize: 28, fontWeight: '700' as const, color: colors.text },
  h2: { fontSize: 22, fontWeight: '700' as const, color: colors.text },
  h3: { fontSize: 17, fontWeight: '600' as const, color: colors.text },
  body: { fontSize: 15, fontWeight: '400' as const, color: colors.text },
  bodyMuted: { fontSize: 15, fontWeight: '400' as const, color: colors.textMuted },
  caption: { fontSize: 13, fontWeight: '500' as const, color: colors.textMuted },
  tiny: { fontSize: 11, fontWeight: '600' as const, color: colors.textFaint },
} as const;
