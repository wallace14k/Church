/**
 * Tokens de design do Congrega.
 *
 * Segunda troca de identidade visual nesta sessão: o cliente pediu para as
 * telas seguirem o padrão de referência de um dashboard SaaS real (Mercury) —
 * sans-serif do início ao fim, acento índigo, cartões brancos com borda fina
 * em vez de cartão cinza chapado, sidebar clara. O sistema anterior (Steep:
 * serifada + pêssego) foi substituído inteiro; nenhuma tela usa mais
 * `SourceSerif4`.
 *
 * A estrutura dos componentes (sidebar recolhível, cartão, avatar, stat card)
 * não mudou — só a superfície: cor, fonte, borda. É por isso que essa segunda
 * troca foi bem mais rápida que a primeira.
 */

export const palette = {
  /** Tinta principal. Todo texto e a maior parte dos traços de UI. */
  inkBlack: '#171923',

  /** Canvas — branco puro, o fundo dominante do sistema. */
  paperWhite: '#FFFFFF',

  /** Fundo da sidebar e de superfícies levemente destacadas do canvas. */
  fogWhite: '#FAFAFB',

  /** Preenchimento de input, hover de item de lista. */
  mistGray: '#F3F4F6',

  /**
   * Cor de link, texto auxiliar.
   *
   * Escurecido para acessibilidade — mede 4.6:1 sobre branco, WCAG AA. Ver
   * `tokens.test.ts`.
   */
  slateGray: '#6B7280',

  /** Rótulo terciário, categoria, texto de apoio pequeno. */
  ashGray: '#8A8F98',

  /** Texto de placeholder — o cinza mais claro com função. */
  smokeGray: '#A1A5AE',

  /**
   * Acento — a única cor saturada do sistema. Botão primário, link com peso,
   * indicador de progresso, foco de campo.
   */
  indigo: '#5B5FEE',
  indigoDeep: '#4338CA',
  indigoWash: '#EEF0FF',

  /** Positivo — variação/entrega concluída, checkmark de verificação. */
  successGreen: '#1A8245',
  successWash: '#E7F6EC',

  /** Traço de campo inválido e delta negativo. */
  errorRed: '#D33B2C',
  errorWash: '#FDECEA',
} as const;

export interface ColorScheme {
  readonly background: string;
  readonly surface: string;
  readonly surfaceNeutral: string;
  readonly surfaceTinted: string;
  readonly hairline: string;
  readonly divider: string;
  readonly text: string;
  readonly textBody: string;
  readonly textMuted: string;
  readonly placeholder: string;
  readonly textOnDark: string;
  readonly brand: string;
  readonly brandSecondary: string;
  readonly success: string;
  readonly danger: string;
  readonly disabled: string;
}

export const colors: ColorScheme = {
  background: palette.paperWhite,
  surface: palette.paperWhite,
  /** Fundo do cartão neutro — usado com moderação; a maioria dos cartões é branca com borda. */
  surfaceNeutral: palette.mistGray,
  surfaceTinted: palette.indigoWash,
  hairline: '#E5E7EB',
  divider: '#E5E7EB',

  text: palette.inkBlack,
  textBody: palette.inkBlack,
  textMuted: palette.slateGray,
  placeholder: palette.smokeGray,
  textOnDark: palette.paperWhite,

  brand: palette.indigo,
  brandSecondary: palette.indigoDeep,
  success: palette.successGreen,

  /** Usado em borda de campo inválido e delta negativo — nunca em texto de leitura corrida. */
  danger: palette.errorRed,
  disabled: '#D1D5DB',
};

/**
 * Família tipográfica — uma voz só.
 *
 * O padrão de referência não usa serifada em nenhum momento: título, corpo,
 * número de saldo, tudo na mesma família sans, diferenciada por peso e
 * tamanho. `Inter` cobre isso com a variação de peso que o sistema precisa
 * (400 a 600) e suporte nativo a pt-BR.
 */
export const fonts = {
  regular: 'Inter_400Regular',
  medium: 'Inter_500Medium',
  semibold: 'Inter_600SemiBold',
} as const;

/** Escala tipográfica — uma voz, com hierarquia por peso e tamanho. */
export const type = {
  caption: { fontFamily: fonts.regular, fontSize: 12, lineHeight: 18 },
  captionBody: { fontFamily: fonts.regular, fontSize: 13, lineHeight: 19 },
  body: { fontFamily: fonts.regular, fontSize: 15, lineHeight: 22 },
  bodyLg: { fontFamily: fonts.regular, fontSize: 17, lineHeight: 24 },
  bodyStrong: { fontFamily: fonts.medium, fontSize: 15, lineHeight: 22 },
  subheading: { fontFamily: fonts.medium, fontSize: 17, lineHeight: 24 },

  /** Rótulo eyebrow: caixa alta, cinza terciário. Só para etiqueta e categoria. */
  eyebrow: { fontFamily: fonts.medium, fontSize: 11, lineHeight: 15, letterSpacing: 0.6 },

  headingSm: { fontFamily: fonts.semibold, fontSize: 20, lineHeight: 26, letterSpacing: -0.2 },
  heading: { fontFamily: fonts.semibold, fontSize: 24, lineHeight: 30, letterSpacing: -0.3 },
  headingLg: { fontFamily: fonts.semibold, fontSize: 30, lineHeight: 36, letterSpacing: -0.4 },
  display: { fontFamily: fonts.semibold, fontSize: 38, lineHeight: 44, letterSpacing: -0.6 },
} as const;

/** Escala de 4px. */
export const space = {
  4: 4,
  8: 8,
  12: 12,
  16: 16,
  20: 20,
  24: 24,
  28: 28,
  32: 32,
  40: 40,
  48: 48,
  64: 64,
  80: 80,
  96: 96,
  124: 124,
  128: 128,
  160: 160,
} as const;

/**
 * Raios nomeados.
 *
 * Mais contidos que o sistema anterior — o padrão de referência usa cartão a
 * 12–16px, não 24px, e é isso que faz a superfície branca com borda fina
 * parecer "produto" em vez de "cartão de apresentação".
 */
export const radius = {
  inputs: 10,
  cards: 12,
  images: 10,
  buttons: 9999,
  smallCards: 8,
  elevatedCards: 14,
  tags: 9999,
} as const;

/**
 * Elevação.
 *
 * Sombra sutil o bastante para separar cartão de canvas sem pesar — o padrão
 * de referência apoia a separação sobretudo na borda de 1px, e usa sombra só
 * como reforço muito leve.
 */
export const elevation = {
  none: {},
  floating: {
    shadowColor: '#0F172A',
    shadowOpacity: 0.06,
    shadowRadius: 12,
    shadowOffset: { width: 0, height: 4 },
    elevation: 2,
  },
  popover: {
    shadowColor: '#0F172A',
    shadowOpacity: 0.1,
    shadowRadius: 24,
    shadowOffset: { width: 0, height: 8 },
    elevation: 6,
  },
} as const;

/** Alvos de toque. 44pt é o mínimo praticável em toque. */
export const touch = {
  minTarget: 44,
  comfortable: 48,
} as const;

export const layout = {
  pageMaxWidth: 1200,
  sidebarWidth: 240,
  sectionGap: 64,
  cardPadding: 20,
  elementGap: 8,
} as const;

export const motion = {
  fast: 120,
  normal: 200,
  slow: 320,
} as const;
