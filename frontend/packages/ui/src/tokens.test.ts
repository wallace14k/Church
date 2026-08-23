import { describe, expect, it } from 'vitest';
import { colors, fonts, palette, radius, touch, type } from './tokens';

/** Luminância relativa conforme WCAG 2.1. */
function luminance(hex: string): number {
  const canais = [1, 3, 5].map((offset) => {
    const valor = Number.parseInt(hex.slice(offset, offset + 2), 16) / 255;
    return valor <= 0.03928 ? valor / 12.92 : ((valor + 0.055) / 1.055) ** 2.4;
  }) as [number, number, number];

  return 0.2126 * canais[0] + 0.7152 * canais[1] + 0.0722 * canais[2];
}

function contraste(frente: string, fundo: string): number {
  const a = luminance(frente);
  const b = luminance(fundo);
  const [claro, escuro] = a > b ? [a, b] : [b, a];
  return (claro + 0.05) / (escuro + 0.05);
}

const AA_NORMAL = 4.5;
/** WCAG 1.4.11 — componente não textual (traço de estado, borda de seleção). */
const AA_NAO_TEXTUAL = 3;

describe('contraste do texto', () => {
  it.each([
    ['tinta sobre canvas', colors.text, colors.background],
    ['tinta sobre cartão branco', colors.text, colors.surface],
    ['tinta sobre superfície interna', colors.text, colors.surfaceInner],
    ['corpo sobre canvas', colors.textBody, colors.background],
    ['auxiliar sobre canvas', colors.textMuted, colors.background],
    ['auxiliar sobre cartão', colors.textMuted, colors.surface],
    ['auxiliar sobre superfície interna', colors.textMuted, colors.surfaceInner],
    ['texto sobre acento cheio', colors.textOnAccent, colors.surfaceAccent],
    ['texto sobre lavagem verde', colors.textOnAccentSoft, colors.surfaceAccentSoft],
    ['texto sobre lavagem âmbar', colors.textOnCategorySoft, colors.surfaceCategorySoft],
    ['texto sobre ilha escura', colors.textOnDark, colors.surfaceInverse],
  ])('%s passa em 4,5:1', (_nome, frente, fundo) => {
    expect(contraste(frente, fundo)).toBeGreaterThanOrEqual(AA_NORMAL);
  });
});

describe('as correções que o mockup exigiu', () => {
  /**
   * O verde do mockup falha nas DUAS direções, e é a mesma medida.
   *
   * `#4c8b22` sobre branco mede 4,18:1 — reprova como texto — e por simetria
   * branco sobre ele mede o mesmo 4,18:1, reprovando dentro do botão primário.
   * Um token que servisse só de preenchimento poderia viver com isso; este
   * serve de preenchimento **e** de cor de link, então precisa passar dos dois
   * lados.
   */
  it('o acento serve como texto E como fundo de texto branco', () => {
    expect(contraste(colors.surfaceAccent, colors.surface)).toBeGreaterThanOrEqual(AA_NORMAL);
    expect(contraste(colors.textOnAccent, colors.surfaceAccent)).toBeGreaterThanOrEqual(AA_NORMAL);
  });

  /**
   * Sem esta, o verde do mockup voltaria sem ninguém notar: a diferença entre
   * `#4c8b22` e `#44831A` é invisível a olho, e só a medida a denuncia.
   */
  it('o acento é mais escuro que o do mockup, e o suficiente', () => {
    expect(contraste('#4c8b22', colors.surface)).toBeLessThan(AA_NORMAL);
    expect(contraste(colors.surfaceAccent, colors.surface)).toBeGreaterThanOrEqual(AA_NORMAL);
  });

  /**
   * O âmbar é categórico e aparece como texto em chip. `#c27a12` mede 3,45:1.
   */
  it('o âmbar passa como texto, ao contrário do tom do mockup', () => {
    expect(contraste('#c27a12', colors.surface)).toBeLessThan(AA_NORMAL);
    expect(contraste(colors.surfaceCategory, colors.surface)).toBeGreaterThanOrEqual(AA_NORMAL);
  });

  /**
   * O mockup tem oito cinzas de apoio entre 2,45:1 e 3,90:1 — todos reprovam, e
   * a variação de 2% entre eles esconde exatamente isso. Um só, legível.
   */
  it('há UM cinza de apoio, e ele passa nas três superfícies', () => {
    expect(colors.textMuted).toBe(colors.placeholder);

    for (const fundo of [colors.background, colors.surface, colors.surfaceInner]) {
      expect(contraste(colors.textMuted, fundo)).toBeGreaterThanOrEqual(AA_NORMAL);
    }
  });

  /**
   * O rótulo de seção do mockup é 9px em `#98a19a` — 2,66:1. Tamanho pequeno
   * com contraste baixo é a pior combinação possível, e aqui ele carrega a
   * orientação da tela ("COMUNIDADE", "PRÓXIMOS DIAS").
   */
  it('o rótulo de seção não é minúsculo nem apagado', () => {
    expect(type.eyebrow.fontSize).toBeGreaterThanOrEqual(10);
    expect(contraste(colors.textMuted, colors.surface)).toBeGreaterThanOrEqual(AA_NORMAL);
  });
});

describe('invariantes que sobrevivem a troca de paleta', () => {
  it('o texto do acento nunca some no próprio acento', () => {
    expect(colors.textOnAccent.toUpperCase()).not.toBe(colors.surfaceAccent.toUpperCase());
    expect(colors.textOnCategorySoft.toUpperCase()).not.toBe(colors.surfaceCategorySoft.toUpperCase());
  });

  /**
   * WCAG 1.4.11: o foco é indicador NÃO textual e precisa de 3:1. É por isso
   * que o anel usa tinta e não `hairline` — o fio de borda mede 1,2:1 e serve
   * para dividir superfície, não para dizer onde o teclado está.
   */
  it('o anel de foco é perceptível nas três superfícies', () => {
    for (const fundo of [colors.background, colors.surface, colors.surfaceInner]) {
      expect(contraste(colors.text, fundo)).toBeGreaterThanOrEqual(AA_NAO_TEXTUAL);
    }
  });

  /**
   * O fio NÃO precisa de 3:1, e a asserção fixa isso de propósito.
   *
   * Um fio que só separa superfícies não carrega informação; engrossá-lo até
   * 3:1 desenharia uma caixa a caneta em volta de cada cartão. Quem separa
   * cartão de página no Grove é a sombra mais a diferença de tom. Se alguém um
   * dia usar `hairline` como indicador de estado, é aqui que a decisão está
   * escrita.
   */
  it('o fio de borda é decorativo, e por isso pode ser tênue', () => {
    expect(contraste(colors.hairline, colors.surface)).toBeLessThan(AA_NAO_TEXTUAL);
    expect(colors.hairline).toBe(colors.divider);
  });

  /**
   * **Verde e âmbar têm a mesma luminância.** 1,01:1 entre eles — quem não
   * distingue matiz (acromatopsia, ou uma tela em escala de cinza) vê os dois
   * como o mesmo tom.
   *
   * Isto não é defeito a corrigir: separá-los em luminância exigiria clarear o
   * âmbar até o amarelo ou escurecer o verde até o musgo, e o desenho do mockup
   * se perderia. É **restrição a respeitar**, e o teste existe para deixá-la
   * visível: enquanto esta asserção passar, categoria não pode ser comunicada
   * só por cor. Todo cartão categórico carrega ícone e rótulo escrito, e é isso
   * que faz a distinção — a cor é reforço.
   *
   * Se um dia alguém trocar a paleta e os dois se separarem, este teste falha e
   * obriga a reler a regra antes de relaxá-la.
   */
  it('verde e âmbar são indistinguíveis sem matiz — por isso a cor nunca informa sozinha', () => {
    expect(contraste(colors.surfaceAccent, colors.surfaceCategory)).toBeLessThan(1.3);
  });
});

describe('tipografia', () => {
  /**
   * A hierarquia do Grove é feita por PESO, não por tamanho: o nome da marca e
   * o rótulo de seção têm quase o mesmo corpo e se distinguem por 800 contra
   * 400. Sem os pesos carregados em `app/_layout.tsx`, tudo cai no mais próximo
   * e a hierarquia desaparece sem nenhum erro aparecer.
   */
  it('a escala usa apenas famílias declaradas em `fonts`', () => {
    const declaradas = new Set(Object.values(fonts));

    for (const [nome, variante] of Object.entries(type)) {
      expect(declaradas.has(variante.fontFamily as never), `${nome} usa fonte não declarada`).toBe(true);
    }
  });

  it('o rótulo de seção é mais pesado que o corpo', () => {
    expect(type.eyebrow.fontFamily).toBe(fonts.black);
    expect(type.body.fontFamily).toBe(fonts.regular);
  });

  it('a escala cresce sem saltos invertidos', () => {
    const ordem = [
      type.eyebrow,
      type.caption,
      type.captionBody,
      type.body,
      type.bodyLg,
      type.headingSm,
      type.heading,
      type.headingLg,
      type.display,
    ];

    for (let i = 1; i < ordem.length; i++) {
      expect(ordem[i]!.fontSize).toBeGreaterThanOrEqual(ordem[i - 1]!.fontSize);
    }
  });

  it('toda variante tem entrelinha maior que o corpo', () => {
    for (const [nome, variante] of Object.entries(type)) {
      expect(variante.lineHeight, `${nome}`).toBeGreaterThan(variante.fontSize);
    }
  });
});

describe('forma e alvo', () => {
  it('o alvo de toque mínimo é 44pt', () => {
    expect(touch.minTarget).toBeGreaterThanOrEqual(44);
    expect(touch.comfortable).toBeGreaterThanOrEqual(touch.minTarget);
  });

  /**
   * Raio menor que o do Perk (28px) porque o Grove tem sombra: arredondamento
   * grande junto com sombra difusa lê como bolha, não como cartão.
   */
  it('o cartão é mais arredondado que a superfície interna', () => {
    expect(radius.cards).toBeGreaterThan(radius.smallCards);
    expect(radius.smallCards).toBeGreaterThan(radius.inputs);
  });

  it('botão e etiqueta são pílula', () => {
    expect(radius.buttons).toBeGreaterThanOrEqual(999);
    expect(radius.tags).toBeGreaterThanOrEqual(999);
  });

  it('a paleta não guarda tom morto', () => {
    const usados = new Set(Object.values(colors).map((c) => c.toUpperCase()));

    for (const [nome, tom] of Object.entries(palette)) {
      expect(usados.has(tom.toUpperCase()), `palette.${nome} não é usado por nenhum token`).toBe(true);
    }
  });
});
