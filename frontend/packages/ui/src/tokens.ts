/**
 * Tokens de design do Congrega.
 *
 * Sistema visual **Perk**: lima elétrico sobre neutros quentes, tipografia em
 * dois pesos só, cartão de 28px em pergaminho, hierarquia por contraste tonal
 * — sem sombra. O documento completo, com as decisões e desvios, está em
 * `docs/07-design-system.md`.
 *
 * Substitui o sistema Mercury (índigo sobre branco, Inter 600, cartão de 12px
 * com sombra). A estrutura dos componentes não mudou; mudou a superfície.
 *
 * **A mudança que não é só de superfície:** o token `brand` foi renomeado para
 * `surfaceAccent`. No sistema anterior o índigo servia como preenchimento *e*
 * como cor de texto de link. O lima não pode servir às duas coisas — mede
 * 1,19:1 sobre branco — e trocar só o valor mantendo o nome deixaria cada
 * `color: colors.brand` invisível sem um único erro de compilação. Renomear
 * quebra o build em cada uso e força uma decisão. Ver D1 no documento.
 */

export const palette = {
  /**
   * Lima elétrico — o único acento cromático do sistema.
   *
   * **Só preenchimento.** Como texto ou como traço de estado reprova a WCAG
   * (1,19:1 sobre branco). O teste em `tokens.test.ts` garante que nenhum
   * token de texto receba este valor.
   */
  electricLime: '#BEFF50',

  /** Lima diluído — item ativo de navegação. Único valor derivado (D5). */
  limeWash: '#EEFBD5',

  /** Tinta principal. Todo texto, título e ícone. */
  offBlackInk: '#14140F',

  /** Pergaminho — superfície de cartão e de sidebar. */
  offWhiteCanvas: '#F5F5EB',

  /** Canvas da página e superfície interna ao cartão. */
  pureWhite: '#FFFFFF',

  /** Bordas e divisores. */
  ash: '#D2D2C8',

  /** Texto secundário. Mede 5,15:1 sobre branco e 4,70:1 sobre pergaminho. */
  graphite: '#6E6E64',

  /** Ilha escura rara — dica flutuante da sidebar recolhida. */
  deepCharcoal: '#30302A',

  /** Traço estrutural tênue. */
  stone: '#919183',

  /** Placeholder e lavagem sutil. */
  smoke: '#B9B9B7',

  /**
   * Estado — secundário à paleta de marca, nunca dominante (§15).
   *
   * Mantidos do sistema anterior de propósito: já têm contraste verificado, e
   * trocá-los por tons quentes sem mandato do documento custaria contraste
   * testado em troca de harmonia. Ver D7.
   */
  successGreen: '#1A8245',
  successWash: '#E7F6EC',
  errorRed: '#D33B2C',
  errorWash: '#FDECEA',
} as const;

export interface ColorScheme {
  /** Canvas da página — branco puro. */
  readonly background: string;
  /** Cartão, painel, sidebar — pergaminho. */
  readonly surface: string;
  /** Superfície dentro de um cartão: chip de valor, linha de lista, campo. */
  readonly surfaceInner: string;
  /** Lima cheio — preenchimento de ação primária e de estado selecionado. */
  readonly surfaceAccent: string;
  /** Lima diluído — item ativo de navegação, onde o lima cheio competiria. */
  readonly surfaceAccentSoft: string;
  /** Ilha escura — dica flutuante. */
  readonly surfaceInverse: string;
  readonly hairline: string;
  readonly divider: string;
  readonly text: string;
  readonly textBody: string;
  readonly textMuted: string;
  readonly placeholder: string;
  /** Texto e ícone sobre lima. Tinta, nunca branco: branco sobre lima é 1,4:1. */
  readonly textOnAccent: string;
  /** Texto sobre a ilha escura. */
  readonly textOnDark: string;
  readonly success: string;
  readonly danger: string;
  readonly disabled: string;
}

export const colors: ColorScheme = {
  background: palette.pureWhite,
  surface: palette.offWhiteCanvas,
  surfaceInner: palette.pureWhite,
  surfaceAccent: palette.electricLime,
  surfaceAccentSoft: palette.limeWash,
  surfaceInverse: palette.deepCharcoal,

  hairline: palette.ash,
  divider: palette.ash,

  text: palette.offBlackInk,
  textBody: palette.offBlackInk,
  textMuted: palette.graphite,
  placeholder: palette.graphite,
  textOnAccent: palette.offBlackInk,
  textOnDark: palette.offWhiteCanvas,

  success: palette.successGreen,
  /** Borda de campo inválido e delta negativo — nunca texto de leitura corrida. */
  danger: palette.errorRed,
  disabled: palette.stone,
};

/**
 * Família tipográfica.
 *
 * O documento prefere `OTSono` e declara Inter como fallback. OTSono não está
 * disponível no projeto; Inter é o que o app carrega.
 *
 * **Dois pesos só.** A §3 proíbe 600 e 700 — o peso 600 foi removido daqui e
 * do carregamento em `app/_layout.tsx`. Manter a fonte carregada convidaria ao
 * uso. Ver D2.
 */
export const fonts = {
  regular: 'Inter_400Regular',
  medium: 'Inter_500Medium',
} as const;

/**
 * Escala tipográfica, derivada da tabela da §3.
 *
 * Os nomes das variantes são os do sistema anterior — renomeá-los obrigaria a
 * tocar toda tela sem ganho. `caption` é o "Caption" do documento (com o
 * tracking de 1.2px que ele pede); `captionBody` é o "Body Small", que é o
 * texto de apoio corrido e por isso fica com tracking normal.
 *
 * `headingLg` para em 34 e `display` em 40: a §3 manda ficar na faixa de
 * 28–40 em dashboard e reservar 60–90 para tratamento editorial, que nenhuma
 * tela desta aplicação tem.
 */
export const type = {
  eyebrow: { fontFamily: fonts.medium, fontSize: 10, lineHeight: 14, letterSpacing: 1 },
  caption: { fontFamily: fonts.regular, fontSize: 12, lineHeight: 16, letterSpacing: 1.2 },
  captionBody: { fontFamily: fonts.regular, fontSize: 14, lineHeight: 18 },
  body: { fontFamily: fonts.regular, fontSize: 16, lineHeight: 24 },
  bodyLg: { fontFamily: fonts.regular, fontSize: 18, lineHeight: 27 },
  bodyStrong: { fontFamily: fonts.medium, fontSize: 16, lineHeight: 24 },
  subheading: { fontFamily: fonts.medium, fontSize: 22, lineHeight: 26 },

  headingSm: { fontFamily: fonts.medium, fontSize: 24, lineHeight: 28, letterSpacing: -0.5 },
  heading: { fontFamily: fonts.medium, fontSize: 28, lineHeight: 32, letterSpacing: -0.56 },
  headingLg: { fontFamily: fonts.medium, fontSize: 34, lineHeight: 38, letterSpacing: -0.9 },
  display: { fontFamily: fonts.medium, fontSize: 40, lineHeight: 44, letterSpacing: -1.2 },
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
  60: 60,
  64: 64,
  72: 72,
  80: 80,
  96: 96,
  124: 124,
  128: 128,
  160: 160,
} as const;

/**
 * Raios nomeados, da tabela da §5.
 *
 * Bem mais generosos que o sistema anterior (cartão de 12px): é o arredondamento
 * grande, junto com a ausência de sombra, que dá a leitura de superfície chapada
 * em vez de "cartão flutuante".
 *
 * `buttons` fica em pílula: a §5 aceita "28px ou tratamento de pílula", e a
 * referência mostra pílula.
 */
export const radius = {
  inputs: 8,
  cards: 28,
  /** Superfície interna ao cartão. */
  smallCards: 18,
  images: 18,
  elevatedCards: 28,
  buttons: 9999,
  tags: 9999,
} as const;

/**
 * Elevação.
 *
 * **Sem sombra por padrão** (§6): a hierarquia vem do contraste tonal entre
 * canvas branco e cartão pergaminho. A variante `floating` do sistema anterior
 * foi removida em vez de virar objeto vazio — um token chamado "floating" que
 * não eleva nada é uma armadilha para o próximo a ler.
 *
 * `popover` sobrevive porque o documento abre a exceção justamente para menu
 * flutuante: o seletor de igreja e a dica da sidebar recolhida precisam se
 * separar do que está por baixo, e ali a borda sozinha não resolve.
 */
export const elevation = {
  none: {},
  popover: {
    shadowColor: '#14140F',
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
  /** Linha de lista — o mínimo que o raio de 28px comporta. Ver D3. */
  cardPadding: 24,
  /** Cartão de métrica e painel — a faixa que a §8 pede. */
  panelPadding: 32,
  elementGap: 16,
} as const;

export const motion = {
  fast: 120,
  normal: 200,
  slow: 320,
} as const;
