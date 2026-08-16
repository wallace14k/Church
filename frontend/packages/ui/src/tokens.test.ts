import { describe, expect, it } from 'vitest';
import { colors, palette, touch, type } from './tokens';

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
const AA_GRANDE = 3;

describe('contraste do texto', () => {
  it.each([
    ['tinta principal sobre canvas', colors.text, colors.background, AA_NORMAL],
    ['corpo sobre canvas', colors.textBody, colors.background, AA_NORMAL],
    ['auxiliar sobre canvas', colors.textMuted, colors.background, AA_NORMAL],
    ['tinta sobre cartão neutro', colors.text, palette.mistGray, AA_NORMAL],
    ['branco sobre índigo (botão primário)', palette.paperWhite, palette.indigo, AA_NORMAL],
    ['índigo profundo sobre canvas (link)', palette.indigoDeep, colors.background, AA_NORMAL],
    ['verde de sucesso sobre canvas', palette.successGreen, colors.background, AA_NORMAL],
  ])('%s atende WCAG AA', (_nome, frente, fundo, minimo) => {
    expect(contraste(frente, fundo)).toBeGreaterThanOrEqual(minimo);
  });

  it('texto auxiliar passa por margem estreita, e isso está registrado', () => {
    const razao = contraste(colors.textMuted, colors.background);
    expect(razao).toBeGreaterThanOrEqual(AA_NORMAL);
    expect(razao).toBeLessThan(5.2);
  });

  it('rótulo terciário (ash gray) atende o mínimo de texto grande, não o de leitura corrida', () => {
    // Reservado a tag/categoria — nunca a texto de corpo, que exige 4.5:1.
    expect(contraste(palette.ashGray, colors.background)).toBeGreaterThanOrEqual(AA_GRANDE);
    expect(contraste(palette.ashGray, colors.background)).toBeLessThan(AA_NORMAL);
  });

  it('o vermelho de erro atende AA — mas continua restrito a traço, por convenção', () => {
    // Passa como texto (4.74:1), mas a mensagem de erro em si vai em tinta
    // principal e o vermelho fica só na borda do campo — consistência com o
    // resto da interface importa mais que a margem de contraste permitir mais.
    expect(contraste(colors.danger, colors.background)).toBeGreaterThanOrEqual(AA_NORMAL);
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

  it('usa uma família só — nenhuma variante escapa para fora de Inter', () => {
    // Sistema de referência não mistura serifada com sans em nenhum momento.
    for (const [nome, estilo] of Object.entries(type)) {
      expect(estilo.fontFamily.startsWith('Inter_'), `${nome} não usa Inter`).toBe(true);
    }
  });
});
