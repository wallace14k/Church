/**
 * Tokens de design do Congrega.
 *
 * A direção vem do mundo material da igreja brasileira, não de tendência de
 * dashboard: verde-garrafa de estofado de banco e capa de livro de registro,
 * latão de aplique e dos numerais do quadro de hinos, papel levemente
 * esverdeado. O setor inteiro usa azul ou roxo — o verde é a aposta deliberada.
 *
 * Nada aqui é hex solto: componente que precisa de cor lê daqui. Cor escrita à
 * mão dentro de componente é como um design system morre, um commit por vez.
 */

/** Paleta base. Cinco cores nomeadas — o suficiente para ter identidade, pouco para não virar arco-íris. */
export const palette = {
  /** Verde-garrafa. Superfície de marca: cabeçalhos, barra de navegação, estados selecionados. */
  verdeNave: '#0E3B2E',
  verdeNaveClaro: '#1A5842',
  verdeNaveEscuro: '#072219',

  /** Latão. Acento — usado com parcimônia, quase sempre como fio de 1px ou numeral. */
  latao: '#C9A227',
  lataoClaro: '#E3C158',
  lataoEscuro: '#8F7118',

  /** Papel. Fundo claro, esverdeado — não creme, para não cair no default. */
  papel: '#F4F6F3',
  papelFundo: '#FBFCFA',
  papelBorda: '#DCE3DC',

  /** Tinta. Quase-preto com subtom verde: texto e superfícies do tema escuro. */
  tinta: '#14231D',
  tintaMedia: '#3D5148',
  /**
   * Texto secundário no tema claro.
   *
   * Era `#6B7F75` até o teste de contraste reprovar: dava 4.15 sobre o fundo,
   * abaixo dos 4.5 exigidos pelo WCAG AA para texto normal. Escurecido até 4.72.
   * A diferença é quase invisível lado a lado e decide se a secretária consegue
   * ler a tela sob a luz do salão.
   */
  tintaSuave: '#62766C',

  /** Vinho. Encadernação de hinário. Só para erro e ação destrutiva — nunca decorativo. */
  vinho: '#8C2F39',
  /**
   * Vinho para o tema escuro.
   *
   * Era `#B14A55`, que dava apenas 3.18 sobre o fundo escuro. Clareado até 5.84 —
   * mensagem de erro ilegível é pior que não ter mensagem, porque o usuário sabe
   * que algo falhou e não descobre o quê.
   */
  vinhoClaro: '#D4818A',
} as const;

/** Cores por papel semântico. Componente lê daqui, nunca da paleta direta. */
export interface ColorScheme {
  readonly background: string;
  readonly surface: string;
  readonly surfaceSunken: string;
  readonly border: string;
  readonly borderStrong: string;
  readonly brand: string;
  readonly brandContrast: string;
  readonly accent: string;
  readonly text: string;
  readonly textMuted: string;
  readonly textOnBrand: string;
  readonly danger: string;
  readonly dangerContrast: string;
}

export const lightColors: ColorScheme = {
  background: palette.papelFundo,
  surface: palette.papel,
  surfaceSunken: '#E9EDE8',
  border: palette.papelBorda,
  borderStrong: palette.tintaSuave,
  brand: palette.verdeNave,
  brandContrast: palette.verdeNaveClaro,
  accent: palette.lataoEscuro,
  text: palette.tinta,
  textMuted: palette.tintaSuave,
  textOnBrand: palette.papelFundo,
  danger: palette.vinho,
  dangerContrast: '#F7E9EA',
};

export const darkColors: ColorScheme = {
  background: palette.verdeNaveEscuro,
  surface: '#0D2E24',
  surfaceSunken: '#061A13',
  border: '#1E4436',
  borderStrong: '#2F6350',
  brand: palette.verdeNaveClaro,
  brandContrast: palette.verdeNave,
  accent: palette.lataoClaro,
  text: '#EAF0EC',
  textMuted: '#9FB3A9',
  textOnBrand: '#EAF0EC',
  danger: palette.vinhoClaro,
  dangerContrast: '#3A1418',
};

/**
 * Famílias tipográficas.
 *
 * Três papéis, deliberadamente distintos:
 * - `display` Bricolage Grotesque — variável, com personalidade. Usada com
 *   restrição: títulos de tela e o numeral da placa. É o que dá voz à marca.
 * - `body` IBM Plex Sans — diacríticos excelentes em pt-BR, neutra sem ser
 *   apagada. Carrega o texto sem competir com o display.
 * - `mono` IBM Plex Mono — numerais tabulares. Obrigatória em qualquer coluna
 *   de valores: sem largura fixa de dígito, os centavos não alinham e a leitura
 *   de uma lista de dízimos vira trabalho.
 */
export const fonts = {
  display: 'BricolageGrotesque',
  body: 'IBMPlexSans',
  bodyMedium: 'IBMPlexSans_Medium',
  bodyBold: 'IBMPlexSans_Bold',
  mono: 'IBMPlexMono',
  monoBold: 'IBMPlexMono_Bold',
} as const;

/**
 * Escala tipográfica.
 *
 * Razão ~1.25, truncada em valores inteiros. `lineHeight` explícito em todos:
 * o padrão do React Native varia entre plataformas, e texto que "pula" de altura
 * entre iOS e Android é o tipo de diferença que ninguém vê no simulador e todo
 * mundo vê em produção.
 */
export const type = {
  display: { fontFamily: fonts.display, fontSize: 32, lineHeight: 38, letterSpacing: -0.5 },
  title: { fontFamily: fonts.display, fontSize: 24, lineHeight: 30, letterSpacing: -0.3 },
  heading: { fontFamily: fonts.bodyBold, fontSize: 18, lineHeight: 24 },
  body: { fontFamily: fonts.body, fontSize: 16, lineHeight: 24 },
  bodyStrong: { fontFamily: fonts.bodyMedium, fontSize: 16, lineHeight: 24 },
  caption: { fontFamily: fonts.body, fontSize: 13, lineHeight: 18 },
  /** Rótulo de seção. Maiúsculas com tracking aberto — device estrutural, não decorativo. */
  eyebrow: { fontFamily: fonts.bodyMedium, fontSize: 11, lineHeight: 14, letterSpacing: 1.2 },
  /** Numerais tabulares para valores e códigos. */
  numeral: { fontFamily: fonts.monoBold, fontSize: 20, lineHeight: 26 },
  numeralLarge: { fontFamily: fonts.monoBold, fontSize: 40, lineHeight: 46, letterSpacing: 4 },
} as const;

/** Espaçamento em passos de 4. Números soltos em `margin` são o começo do fim da consistência. */
export const space = {
  xxs: 2,
  xs: 4,
  sm: 8,
  md: 12,
  lg: 16,
  xl: 24,
  xxl: 32,
  xxxl: 48,
} as const;

export const radius = {
  sm: 4,
  md: 8,
  lg: 12,
  pill: 999,
} as const;

/**
 * Alvos de toque.
 *
 * 44pt é o mínimo praticável, não uma sugestão: o check-in acontece com o pai
 * segurando uma criança no colo, de pé, em três minutos antes do culto.
 */
export const touch = {
  minTarget: 44,
  comfortable: 52,
} as const;

/** Duração de animação. Respeitar `prefers-reduced-motion` é obrigatório, não opcional. */
export const motion = {
  instant: 0,
  fast: 120,
  normal: 200,
  slow: 320,
} as const;
