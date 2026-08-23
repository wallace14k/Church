/**
 * Tokens de design do Congrega.
 *
 * Sistema visual **Grove**: superfícies brancas sobre um canvas verde-acinzentado,
 * cartões de 22px com sombra difusa, verde-oliva como cor de ação e âmbar como
 * segunda cor **categórica**. O documento completo, com as decisões e desvios,
 * está em `docs/07-design-system.md`.
 *
 * Substitui o sistema Perk (lima elétrico sobre pergaminho, sem sombra, dois
 * pesos tipográficos). A estrutura dos componentes não mudou; mudou a
 * superfície, a paleta e a escala tipográfica.
 *
 * ---
 *
 * **Os valores não são os do mockup ao pé da letra, e a diferença é medida.**
 * O mockup de referência traz 18 pares que reprovam o mínimo de 4,5:1 da WCAG
 * para texto normal — inclusive o verde principal (`#4c8b22`, 4,18:1, que falha
 * como texto **e** como fundo de texto branco) e o rótulo de seção de 9px em
 * 2,66:1. Cada tom aqui é o do mockup escurecido pelo mínimo necessário para
 * passar; a diferença é imperceptível lado a lado e está anotada em cada token.
 *
 * A checagem inteira vive em `tokens.test.ts` e falha se alguém reverter.
 */

export const palette = {
  /**
   * Verde Congrega — a cor de **ação**.
   *
   * `#4c8b22` no mockup mede 4,18:1 sobre branco: reprova como texto e, por
   * simetria, também reprova branco escrito sobre ele. Este tom escurece o
   * suficiente para 4,66:1 nas duas direções, que é o que permite ao mesmo
   * token servir de preenchimento de botão e de cor de link.
   */
  green: '#44831A',

  /** Verde escuro — texto sobre a lavagem clara e ícone que precisa de peso. 6,51:1 sobre branco. */
  greenDeep: '#356A18',

  /** Verde diluído — item ativo de navegação, círculo de ícone, chip. */
  greenSoft: '#EDF6DF',

  /**
   * Âmbar — a segunda cor, **categórica** e nunca de ação.
   *
   * `#c27a12` do mockup mede 3,45:1 e reprova como texto. Este mede 5,11:1
   * sobre branco e 4,65:1 sobre a própria lavagem — que é onde ele mais aparece,
   * e onde uma correção calibrada só contra o branco ainda reprovaria.
   * Ver a decisão D10 no documento: âmbar distingue *assunto* (aniversariantes,
   * comemorações), verde distingue *o que se pode fazer*. Um botão âmbar
   * quebraria a regra e faria o usuário procurar a ação no lugar errado.
   */
  amber: '#A45C00',

  /** Âmbar diluído — fundo de chip e de círculo de ícone. */
  amberSoft: '#FFF3DE',

  /** Tinta principal. Todo texto, título e ícone de conteúdo. 15,5:1 sobre o canvas. */
  ink: '#162019',

  /**
   * Cinza de apoio — **um só**, no lugar dos oito do mockup.
   *
   * O mockup tem `#758078`, `#7c857f`, `#7d8780`, `#8a938d`, `#8b948e`,
   * `#8d958f`, `#98a19a` e `#9aa19c` em papéis equivalentes: oito cinzas que
   * diferem por 2% e reprovam de 2,45:1 a 3,90:1. A variação é acidental, não
   * projetada, e esconde que **todos** eram ilegíveis. Este mede 4,68:1 sobre o
   * canvas e 4,92:1 sobre branco.
   */
  slate: '#68716B',

  /** Canvas da página — verde-acinzentado muito claro. */
  canvas: '#F5F7F2',

  /** Superfície de cartão e de barra superior. */
  white: '#FFFFFF',

  /** Superfície interna: linha de lista, campo, cartão dentro de cartão. */
  offWhite: '#FBFCFA',

  /**
   * Fio de borda e divisor.
   *
   * 1,21:1 — e isso está certo. A WCAG 1.4.11 exige 3:1 de **indicadores**
   * (foco, estado, limite de controle); um fio que só separa superfícies não
   * carrega informação, e engrossá-lo até 3:1 transformaria cada cartão numa
   * caixa desenhada a caneta. O que separa cartão de página aqui é a sombra
   * difusa mais a diferença de tom, não o fio.
   */
  line: '#E6EBE3',

  /** Ilha escura rara — dica flutuante, menu sobreposto. */
  charcoal: '#22302A',

  /** Placeholder e traço desabilitado. Não carrega texto de leitura. */
  smoke: '#A9B1AB',

  /**
   * Estado — secundário à paleta, nunca dominante.
   *
   * Mantidos do sistema anterior: já têm contraste verificado, e trocá-los sem
   * mandato custaria acessibilidade testada em troca de harmonia.
   *
   * As lavagens `successWash` e `errorWash` saíram: nenhum token as apontava e
   * nenhuma tela as usava. Tom morto na paleta é convite para alguém acreditar
   * que ele já foi verificado para algum uso.
   */
  successGreen: '#1A8245',
  errorRed: '#D33B2C',
} as const;

export interface ColorScheme {
  /** Canvas da página — verde-acinzentado. */
  readonly background: string;
  /** Cartão, painel, barra superior — branco. */
  readonly surface: string;
  /** Superfície dentro de um cartão: linha de lista, campo, chip. */
  readonly surfaceInner: string;
  /** Verde cheio — preenchimento de ação primária e de estado selecionado. */
  readonly surfaceAccent: string;
  /** Verde diluído — item ativo de navegação, círculo de ícone. */
  readonly surfaceAccentSoft: string;
  /** Âmbar cheio — categórico, nunca ação. */
  readonly surfaceCategory: string;
  /** Âmbar diluído — chip e círculo de ícone de categoria. */
  readonly surfaceCategorySoft: string;
  /** Ilha escura — dica flutuante. */
  readonly surfaceInverse: string;
  readonly hairline: string;
  readonly divider: string;
  readonly text: string;
  readonly textBody: string;
  readonly textMuted: string;
  readonly placeholder: string;
  /** Texto e ícone sobre o verde cheio. Branco: 4,66:1. */
  readonly textOnAccent: string;
  /** Texto e ícone sobre a lavagem verde. */
  readonly textOnAccentSoft: string;
  /** Texto e ícone sobre a lavagem âmbar. */
  readonly textOnCategorySoft: string;
  /** Texto sobre a ilha escura. */
  readonly textOnDark: string;
  readonly success: string;
  readonly danger: string;
  readonly disabled: string;
}

export const colors: ColorScheme = {
  background: palette.canvas,
  surface: palette.white,
  surfaceInner: palette.offWhite,
  surfaceAccent: palette.green,
  surfaceAccentSoft: palette.greenSoft,
  surfaceCategory: palette.amber,
  surfaceCategorySoft: palette.amberSoft,
  surfaceInverse: palette.charcoal,

  hairline: palette.line,
  divider: palette.line,

  text: palette.ink,
  textBody: palette.ink,
  textMuted: palette.slate,
  placeholder: palette.slate,

  textOnAccent: palette.white,
  textOnAccentSoft: palette.greenDeep,
  textOnCategorySoft: palette.amber,
  textOnDark: palette.white,

  success: palette.successGreen,
  /** Borda de campo inválido e delta negativo — nunca texto de leitura corrida. */
  danger: palette.errorRed,
  disabled: palette.smoke,
};

/**
 * Família tipográfica.
 *
 * **Cinco pesos, e não dois.** O sistema Perk proibia peso 600+ e carregava só
 * 400/500; o mockup Grove usa 700 na navegação, 800 no nome da marca e 900 nos
 * rótulos de seção e chips. Sem os pesos carregados, tudo cairia no mais
 * próximo e a hierarquia — que neste desenho é feita por **peso**, não por
 * tamanho — desapareceria.
 *
 * O `app/_layout.tsx` precisa carregar exatamente estes cinco.
 */
export const fonts = {
  regular: 'Inter_400Regular',
  medium: 'Inter_500Medium',
  semibold: 'Inter_600SemiBold',
  bold: 'Inter_700Bold',
  black: 'Inter_800ExtraBold',
} as const;

/**
 * Escala tipográfica.
 *
 * Os nomes das variantes vêm do sistema anterior — renomeá-los obrigaria a
 * tocar toda tela sem ganho. O que mudou foram os pesos: `eyebrow` e `caption`
 * sobem para 800/900 porque no Grove eles são rótulo de seção em caixa alta com
 * tracking largo, e nesse papel o peso é o que os torna legíveis a 10px.
 */
export const type = {
  /** Rótulo de seção em caixa alta — "COMUNIDADE", "PRÓXIMOS DIAS". */
  eyebrow: { fontFamily: fonts.black, fontSize: 10, lineHeight: 14, letterSpacing: 1.4 },
  /** Etiqueta curta — dia da semana no bloco de data, chip de status. */
  caption: { fontFamily: fonts.bold, fontSize: 11, lineHeight: 15, letterSpacing: 0.6 },
  /** Texto de apoio corrido — hora e local, data por extenso. */
  captionBody: { fontFamily: fonts.regular, fontSize: 12, lineHeight: 17 },
  body: { fontFamily: fonts.regular, fontSize: 14, lineHeight: 21 },
  bodyLg: { fontFamily: fonts.regular, fontSize: 16, lineHeight: 24 },
  bodyStrong: { fontFamily: fonts.bold, fontSize: 14, lineHeight: 20 },
  subheading: { fontFamily: fonts.bold, fontSize: 16, lineHeight: 22, letterSpacing: -0.2 },

  headingSm: { fontFamily: fonts.bold, fontSize: 20, lineHeight: 25, letterSpacing: -0.5 },
  heading: { fontFamily: fonts.black, fontSize: 24, lineHeight: 29, letterSpacing: -0.7 },
  headingLg: { fontFamily: fonts.black, fontSize: 32, lineHeight: 35, letterSpacing: -1.2 },
  /** O número grande do cartão de métrica e a saudação do painel. */
  display: { fontFamily: fonts.black, fontSize: 40, lineHeight: 42, letterSpacing: -1.6 },
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
 * Raios nomeados.
 *
 * Menores que os do Perk (cartão de 28px): o Grove compensa com sombra, e
 * arredondamento grande **junto** com sombra difusa lê como bolha, não como
 * cartão.
 */
export const radius = {
  inputs: 12,
  cards: 22,
  /** Superfície interna ao cartão — linha de lista, chip retangular. */
  smallCards: 15,
  images: 15,
  elevatedCards: 22,
  buttons: 999,
  tags: 999,
} as const;

/**
 * Elevação.
 *
 * **Com sombra, ao contrário do Perk**, que a proibia porque a hierarquia vinha
 * do contraste tonal entre canvas branco e cartão pergaminho. No Grove a
 * relação se inverte: o cartão é branco e o canvas é o tom — e branco sobre
 * verde-acinzentado claro tem diferença tonal pequena demais para separar
 * sozinha. A sombra é difusa e quase incolor de propósito (7% de opacidade,
 * 42px de desfoque): ela dá profundidade sem desenhar uma borda escura.
 */
export const elevation = {
  none: {},
  card: {
    shadowColor: '#1C2B18',
    shadowOpacity: 0.07,
    shadowRadius: 42,
    shadowOffset: { width: 0, height: 14 },
    elevation: 3,
  },
  /** Hover de item clicável — mais curta e mais próxima, para ler como "subiu". */
  raised: {
    shadowColor: '#1E3219',
    shadowOpacity: 0.06,
    shadowRadius: 22,
    shadowOffset: { width: 0, height: 8 },
    elevation: 5,
  },
  popover: {
    shadowColor: '#162019',
    shadowOpacity: 0.12,
    shadowRadius: 24,
    shadowOffset: { width: 0, height: 8 },
    elevation: 8,
  },
} as const;

/** Alvos de toque. 44pt é o mínimo praticável em toque. */
export const touch = {
  minTarget: 44,
  comfortable: 48,
} as const;

export const layout = {
  pageMaxWidth: 1440,
  /** Altura da barra superior. Substitui a largura de sidebar do Perk. */
  topbarHeight: 78,
  sidebarWidth: 240,
  sectionGap: 48,
  /** Linha de lista e cartão interno. */
  cardPadding: 18,
  /** Painel e cartão de métrica. */
  panelPadding: 22,
  elementGap: 15,
} as const;

export const motion = {
  fast: 120,
  normal: 200,
  slow: 320,
} as const;
