import { describe, expect, it } from 'vitest';
import { colors, palette, radius, touch, type } from './tokens';

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
    ['tinta sobre canvas', colors.text, colors.background, AA_NORMAL],
    ['tinta sobre cartão pergaminho', colors.text, colors.surface, AA_NORMAL],
    ['corpo sobre canvas', colors.textBody, colors.background, AA_NORMAL],
    ['auxiliar sobre canvas', colors.textMuted, colors.background, AA_NORMAL],
    ['auxiliar sobre cartão pergaminho', colors.textMuted, colors.surface, AA_NORMAL],
    ['placeholder sobre campo branco', colors.placeholder, colors.surfaceInner, AA_NORMAL],
    ['tinta sobre lima (botão primário)', colors.textOnAccent, colors.surfaceAccent, AA_NORMAL],
    ['tinta sobre lima diluído (item ativo)', colors.text, colors.surfaceAccentSoft, AA_NORMAL],
    ['texto sobre a ilha escura', colors.textOnDark, colors.surfaceInverse, AA_NORMAL],
    ['verde de sucesso sobre canvas', palette.successGreen, colors.background, AA_NORMAL],
    ['vermelho de erro sobre canvas', colors.danger, colors.background, AA_NORMAL],
  ])('%s atende WCAG AA', (_nome, frente, fundo, minimo) => {
    expect(contraste(frente, fundo)).toBeGreaterThanOrEqual(minimo);
  });

  it('o auxiliar sobre pergaminho passa por margem estreita, e isso está registrado', () => {
    // O pergaminho é mais escuro que o canvas, então é ele que define o limite
    // do cinza auxiliar. Escurecer o texto mais que isso o aproximaria demais
    // da tinta principal e apagaria a hierarquia; clarear reprovaria.
    const razao = contraste(colors.textMuted, colors.surface);
    expect(razao).toBeGreaterThanOrEqual(AA_NORMAL);
    expect(razao).toBeLessThan(5);
  });
});

describe('o lima é superfície, nunca texto (D1)', () => {
  it('lima sobre canvas reprova até para componente não textual', () => {
    // É este número que justifica a renomeação de `brand` para `surfaceAccent`.
    // Como cor de texto seria ilegível; como borda de estado de seleção,
    // reprovaria a WCAG 1.4.11 — por isso seleção usa preenchimento (D6).
    expect(contraste(colors.surfaceAccent, colors.background)).toBeLessThan(AA_NAO_TEXTUAL);
  });

  it('nenhum token de texto recebe o lima', () => {
    const tokensDeTexto = [
      colors.text,
      colors.textBody,
      colors.textMuted,
      colors.placeholder,
      colors.textOnAccent,
      colors.textOnDark,
    ];

    for (const token of tokensDeTexto) {
      expect(token.toUpperCase()).not.toBe(palette.electricLime);
    }
  });

  it('o texto sobre lima é tinta, não branco', () => {
    // Branco sobre lima mede ~1,4:1. O erro é fácil de cometer porque todo
    // botão primário do sistema anterior tinha texto branco.
    expect(contraste(palette.pureWhite, colors.surfaceAccent)).toBeLessThan(AA_NAO_TEXTUAL);
    expect(contraste(colors.textOnAccent, colors.surfaceAccent)).toBeGreaterThanOrEqual(AA_NORMAL);
  });
});

describe('separação de superfície sem sombra', () => {
  it('cartão e canvas se distinguem por tom', () => {
    // A §6 proíbe sombra: se cartão e canvas tivessem a mesma cor, sobraria
    // uma borda de 1px como única separação. Ver D4.
    expect(colors.surface).not.toBe(colors.background);
  });

  it('a borda tem contraste suficiente para ser um traço, não um fantasma', () => {
    expect(contraste(colors.hairline, colors.background)).toBeGreaterThan(1.2);
  });
});

describe('alvos de toque', () => {
  it('respeita o mínimo praticável', () => {
    expect(touch.minTarget).toBeGreaterThanOrEqual(44);
    expect(touch.comfortable).toBeGreaterThanOrEqual(touch.minTarget);
  });
});

describe('escala tipográfica', () => {
  it('define lineHeight em todas as variantes', () => {
    for (const [nome, estilo] of Object.entries(type)) {
      expect(estilo.lineHeight, `${nome} sem lineHeight`).toBeGreaterThan(0);
    }
  });

  it('a hierarquia de heading nunca é menor que a de corpo', () => {
    const interfaceMax = Math.max(
      type.body.fontSize,
      type.bodyLg.fontSize,
      type.bodyStrong.fontSize,
      type.subheading.fontSize,
    );
    const displayMin = Math.min(
      type.headingSm.fontSize,
      type.heading.fontSize,
      type.headingLg.fontSize,
      type.display.fontSize,
    );

    expect(displayMin).toBeGreaterThanOrEqual(interfaceMax);
  });

  it('comprime o tracking conforme o corpo cresce', () => {
    expect(type.display.letterSpacing).toBeLessThan(type.headingLg.letterSpacing);
    expect(type.headingLg.letterSpacing).toBeLessThan(type.heading.letterSpacing);
    expect(type.heading.letterSpacing).toBeLessThan(type.headingSm.letterSpacing);
  });

  it('abre o tracking dos micro-rótulos em caixa alta', () => {
    // §3: rótulo em caixa alta com tracking de ~0.1em.
    expect(type.eyebrow.letterSpacing).toBeCloseTo(type.eyebrow.fontSize * 0.1, 1);
  });

  it('usa uma família só — nenhuma variante escapa para fora de Inter', () => {
    for (const [nome, estilo] of Object.entries(type)) {
      expect(estilo.fontFamily.startsWith('Inter_'), `${nome} não usa Inter`).toBe(true);
    }
  });

  it('nenhuma variante usa peso 600 ou 700 (D2)', () => {
    // A §3 admite 400 e 500 apenas. O peso 600 também foi removido do
    // carregamento em `app/_layout.tsx` — se voltar aqui, a fonte não existe
    // e o React Native cai na do sistema em silêncio.
    for (const [nome, estilo] of Object.entries(type)) {
      expect(estilo.fontFamily, `${nome} usa peso proibido`).not.toMatch(/600|700/u);
    }
  });

  it('o título de página fica na faixa que a §3 pede para dashboard', () => {
    expect(type.display.fontSize).toBeLessThanOrEqual(40);
    expect(type.heading.fontSize).toBeGreaterThanOrEqual(28);
  });
});

describe('raios', () => {
  it('a superfície interna é menos arredondada que o cartão que a contém', () => {
    // Raio interno maior que o externo faz o encaixe parecer errado mesmo que
    // ninguém saiba dizer por quê.
    expect(radius.smallCards).toBeLessThan(radius.cards);
    expect(radius.inputs).toBeLessThan(radius.smallCards);
  });

  it('cartão e campo seguem a tabela da §5', () => {
    expect(radius.cards).toBe(28);
    expect(radius.inputs).toBe(8);
  });
});
