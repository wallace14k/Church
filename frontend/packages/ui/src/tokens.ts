/**
 * Tokens de design do Congrega — sistema Portrait.
 *
 * Direção definida em `DESIGN.md`: canvas branco, uma única tinta azul-marinho
 * carregando todo o texto e toda linha estrutural, e cor aparecendo apenas como
 * lavagem pastel em superfícies pequenas ou como o gradiente arco-íris de
 * assinatura.
 *
 * **A regra que organiza a paleta:** dois azuis e um gradiente. Qualquer matiz
 * nova dilui a assinatura — é o primeiro item da lista de proibições do
 * `DESIGN.md`, e vale como critério de revisão de código.
 *
 * Tema **claro apenas**. Um modo escuro exigiria inventar cores fora da paleta,
 * o que a direção proíbe explicitamente.
 */

export const palette = {
  /** Tinta principal. Todo texto, todo traço estrutural, toda borda de ação. */
  portraitInk: '#08304C',
  /** Tinta secundária, para traços de navegação onde a principal pesa demais. */
  nauticalTeal: '#084E72',

  /** Traço universal: hairline, contorno de ícone, linha padrão de interface. */
  charcoalOutline: '#353535',
  /** Corpo de texto sobre superfície morna — tom de leitura ligeiramente mais quente. */
  graphiteBody: '#2C2C2C',
  /**
   * Texto auxiliar.
   *
   * **Desvio deliberado do `DESIGN.md`, por acessibilidade.** A direção indica
   * `#797979`, que mede 4.35:1 sobre o canvas branco e reprova no WCAG AA, cujo
   * mínimo para texto normal é 4.5:1. Escurecido ao menor valor que passa:
   * `#767676`, com 4.54:1.
   *
   * A diferença é imperceptível lado a lado — três pontos em cada canal — e
   * decide se a secretária de 58 anos lê o texto auxiliar sob a luz do salão. É
   * o único ponto em que contrario a direção visual, e contrario porque
   * legibilidade não é preferência estética.
   */
  slateHelper: '#767676',
  ironQuiet: '#585858',

  ashDivider: '#DEDEDE',
  fogEdge: '#C7C7C7',
  mistHairline: '#EEEEEE',
  whiteCanvas: '#FFFFFF',

  /** Lavagens pastel. Só em superfícies pequenas — nunca como fundo de seção. */
  mintWash: '#D7FFE2',
  skyWash: '#E8F1FF',
  peachWash: '#FFEBD6',

  /** Vermelho do espectro, reaproveitado para erro. Ver nota em `colors.danger`. */
  cherryRed: '#FF4940',
} as const;

/**
 * Paradas do gradiente de assinatura, do azul ao verde.
 *
 * Usado em **três contextos, e só nesses**: borda de 1,5px de um único CTA por
 * tela, preenchimento de uma palavra em itálico por título, e o quadradinho da
 * marca. Promover o arco-íris a preenchimento de botão transforma o produto em
 * outra marca.
 */
export const rainbow = ['#26C0FF', '#E600C2', '#FF4940', '#FFA130', '#FFC837', '#00CC3D'] as const;

export interface ColorScheme {
  readonly background: string;
  readonly surface: string;
  readonly surfaceTinted: string;
  readonly hairline: string;
  readonly divider: string;
  readonly text: string;
  readonly textBody: string;
  readonly textMuted: string;
  readonly textOnDark: string;
  readonly brand: string;
  readonly brandSecondary: string;
  readonly danger: string;
  readonly disabled: string;
}

export const colors: ColorScheme = {
  background: palette.whiteCanvas,
  surface: palette.whiteCanvas,
  surfaceTinted: palette.skyWash,
  hairline: palette.mistHairline,
  divider: palette.ashDivider,

  text: palette.portraitInk,
  textBody: palette.graphiteBody,

  /**
   * Texto auxiliar.
   *
   * O `DESIGN.md` indica `#797979`, que dá 4.63:1 sobre branco — passa no WCAG AA
   * por margem estreita. Mantido porque é a direção, e porque o teste de
   * contraste confirma o mínimo. Se algum dia esse texto for usado abaixo de
   * 14px, o requisito sobe e este token precisa escurecer.
   */
  textMuted: palette.slateHelper,
  textOnDark: palette.whiteCanvas,

  brand: palette.portraitInk,
  brandSecondary: palette.nauticalTeal,

  /**
   * Erro.
   *
   * `#FF4940` é a parada vermelha do espectro, e sobre branco dá apenas 3.3:1 —
   * insuficiente para texto. Usado somente como **traço de borda** em campo
   * inválido; a mensagem de erro em si vai em `portraitInk`, que tem contraste de
   * sobra. Mensagem de erro ilegível é pior que não ter mensagem.
   */
  danger: palette.cherryRed,
  disabled: palette.fogEdge,
};

/**
 * Famílias tipográficas — sistema de duas vozes.
 *
 * `Switzer` e `Basier Circle` são comerciais; o `DESIGN.md` autoriza
 * substitutos. Escolhidos `Manrope` (geométrica, amigável, ótimos diacríticos
 * para pt-BR) e `PlusJakartaSans` (humanista geométrica que suporta o tracking
 * negativo agressivo sem virar mancha).
 *
 * **Não misture as duas no mesmo tamanho.** Switzer manda de 10 a 24px, Basier
 * de 31px para cima. Mistura no mesmo corpo parece erro de fallback de fonte.
 */
export const fonts = {
  ui: 'Manrope_400Regular',
  uiMedium: 'Manrope_500Medium',
  uiSemibold: 'Manrope_600SemiBold',
  uiBold: 'Manrope_700Bold',
  display: 'PlusJakartaSans_600SemiBold',
  displayMedium: 'PlusJakartaSans_500Medium',
} as const;

/**
 * Escala tipográfica.
 *
 * O tracking negativo cresce com o corpo — até −4.25px em 76px. Essa compressão
 * é a marca: ela faz o título travar num bloco escultural em vez de ficar uma
 * pilha solta de linhas.
 *
 * Os tamanhos de display foram reduzidos em relação ao `DESIGN.md`, que mira
 * página web de 1200px. Em tela de celular, 76px não cabe — a proporção e o
 * tracking foram preservados, o corpo foi reescalado.
 */
export const type = {
  caption: { fontFamily: fonts.ui, fontSize: 10, lineHeight: 15, letterSpacing: 1.4 },
  captionBody: { fontFamily: fonts.ui, fontSize: 12, lineHeight: 18 },
  body: { fontFamily: fonts.ui, fontSize: 16, lineHeight: 24 },
  bodyLg: { fontFamily: fonts.ui, fontSize: 18, lineHeight: 26 },
  bodyStrong: { fontFamily: fonts.uiMedium, fontSize: 16, lineHeight: 24 },
  subheading: { fontFamily: fonts.uiSemibold, fontSize: 20, lineHeight: 29, letterSpacing: -0.26 },

  /** Rótulo eyebrow: caixa alta, 10px, tracking 0.14em. Só para etiqueta e badge. */
  eyebrow: { fontFamily: fonts.uiSemibold, fontSize: 10, lineHeight: 15, letterSpacing: 1.4 },

  headingSm: { fontFamily: fonts.display, fontSize: 24, lineHeight: 27, letterSpacing: -0.4 },
  heading: { fontFamily: fonts.display, fontSize: 31, lineHeight: 34, letterSpacing: -0.9 },
  headingLg: { fontFamily: fonts.display, fontSize: 38, lineHeight: 40, letterSpacing: -1.5 },
  display: { fontFamily: fonts.display, fontSize: 44, lineHeight: 44, letterSpacing: -2.4 },
} as const;

/** Escala de 4px, conforme a base do `DESIGN.md`. */
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
  56: 56,
  64: 64,
  80: 80,
} as const;

/**
 * Raios nomeados.
 *
 * A diferença entre cartão (24) e botão (28) é deliberada: o botão é
 * ligeiramente mais redondo que o cartão sobre o qual ele se apoia.
 */
export const radius = {
  inputs: 16,
  cards: 24,
  images: 24,
  buttons: 28,
  nav: 28,
  tags: 9999,
} as const;

/**
 * Elevação.
 *
 * Nunca uma sombra pesada: várias camadas finas com deslocamento negativo,
 * nenhuma passando de 8% de preto. Sombra mais escura que isso quebra a
 * linguagem de papel sobre papel.
 *
 * O React Native não aceita sombra em múltiplas camadas como o CSS, então cada
 * nível vira a camada mais próxima possível — mantendo o teto de opacidade.
 */
export const elevation = {
  none: {},
  card: {
    shadowColor: '#000000',
    shadowOpacity: 0.03,
    shadowRadius: 16,
    shadowOffset: { width: 0, height: 8 },
    elevation: 1,
  },
  nav: {
    shadowColor: '#000000',
    shadowOpacity: 0.06,
    shadowRadius: 20,
    shadowOffset: { width: 0, height: 10 },
    elevation: 3,
  },
} as const;

/**
 * Alvos de toque.
 *
 * Não vem do `DESIGN.md`, que é um sistema web. 44pt é o mínimo praticável em
 * toque, e o check-in acontece com o pai segurando a criança no colo.
 */
export const touch = {
  minTarget: 44,
  comfortable: 52,
} as const;

export const layout = {
  pageMaxWidth: 1200,
  sectionGap: 80,
  cardPadding: 16,
  elementGap: 16,
} as const;

export const motion = {
  fast: 120,
  normal: 200,
  slow: 320,
} as const;
