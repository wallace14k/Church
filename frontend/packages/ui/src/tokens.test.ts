import { describe, expect, it } from 'vitest';
import { darkColors, lightColors, touch, type, type ColorScheme } from './tokens';

/**
 * Luminância relativa conforme WCAG 2.1.
 * https://www.w3.org/TR/WCAG21/#dfn-relative-luminance
 */
function luminance(hex: string): number {
  const channels = [1, 3, 5].map((offset) => {
    const value = Number.parseInt(hex.slice(offset, offset + 2), 16) / 255;
    return value <= 0.03928 ? value / 12.92 : ((value + 0.055) / 1.055) ** 2.4;
  }) as [number, number, number];

  return 0.2126 * channels[0] + 0.7152 * channels[1] + 0.0722 * channels[2];
}

function contrast(foreground: string, background: string): number {
  const a = luminance(foreground);
  const b = luminance(background);
  const [lighter, darker] = a > b ? [a, b] : [b, a];
  return (lighter + 0.05) / (darker + 0.05);
}

/**
 * Pares que precisam ser legíveis.
 *
 * Contraste não é preferência estética — é o que decide se a secretária de 58
 * anos consegue ler a tela sob a luz do salão da igreja. Testar aqui, sobre os
 * tokens, pega a regressão no commit em que a cor muda, e não meses depois num
 * relato de usuário que ninguém consegue reproduzir.
 */
const AA_TEXTO_NORMAL = 4.5;
const AA_TEXTO_GRANDE = 3;

function paresDeTexto(scheme: ColorScheme) {
  return [
    ['texto sobre fundo', scheme.text, scheme.background, AA_TEXTO_NORMAL],
    ['texto sobre superfície', scheme.text, scheme.surface, AA_TEXTO_NORMAL],
    ['texto secundário sobre fundo', scheme.textMuted, scheme.background, AA_TEXTO_NORMAL],
    ['texto sobre a marca', scheme.textOnBrand, scheme.brand, AA_TEXTO_NORMAL],
    ['erro sobre fundo', scheme.danger, scheme.background, AA_TEXTO_NORMAL],
    ['acento sobre fundo', scheme.accent, scheme.background, AA_TEXTO_GRANDE],
  ] as const;
}

describe('contraste no tema claro', () => {
  it.each(paresDeTexto(lightColors))('%s atende WCAG AA', (_nome, fg, bg, minimo) => {
    expect(contrast(fg, bg)).toBeGreaterThanOrEqual(minimo);
  });
});

describe('contraste no tema escuro', () => {
  it.each(paresDeTexto(darkColors))('%s atende WCAG AA', (_nome, fg, bg, minimo) => {
    expect(contrast(fg, bg)).toBeGreaterThanOrEqual(minimo);
  });
});

describe('alvos de toque', () => {
  it('respeita o mínimo praticável', () => {
    // 44pt não é sugestão: o check-in acontece com o pai segurando a criança no
    // colo, de pé, minutos antes do culto.
    expect(touch.minTarget).toBeGreaterThanOrEqual(44);
    expect(touch.comfortable).toBeGreaterThanOrEqual(touch.minTarget);
  });
});

describe('escala tipográfica', () => {
  it('define lineHeight em todas as variantes', () => {
    // O padrão do RN varia entre plataformas. Texto que muda de altura entre
    // iOS e Android é a diferença que ninguém vê no simulador e todo mundo vê
    // em produção.
    for (const [nome, estilo] of Object.entries(type)) {
      expect(estilo.lineHeight, `${nome} sem lineHeight`).toBeGreaterThan(0);
    }
  });

  it('mantém proporção legível entre corpo e entrelinha', () => {
    for (const [nome, estilo] of Object.entries(type)) {
      const proporcao = estilo.lineHeight / estilo.fontSize;
      expect(proporcao, `${nome} com entrelinha apertada`).toBeGreaterThanOrEqual(1.1);
    }
  });
});
