import { describe, expect, it } from 'vitest';
import { brass, colors, palette, touch, type } from './tokens';

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
    ['tinta sobre lavagem azul', colors.text, palette.skyWash, AA_NORMAL],
    ['tinta sobre lavagem menta', colors.text, palette.mintWash, AA_NORMAL],
    ['tinta sobre lavagem pêssego', colors.text, palette.peachWash, AA_NORMAL],
    ['tinta secundária sobre canvas', colors.brandSecondary, colors.background, AA_NORMAL],
  ])('%s atende WCAG AA', (_nome, frente, fundo, minimo) => {
    expect(contraste(frente, fundo)).toBeGreaterThanOrEqual(minimo);
  });

  it('texto auxiliar passa por margem estreita, e isso está registrado', () => {
    // 4.63:1. O DESIGN.md indica #797979 e o valor cumpre o mínimo — mas sem
    // folga. Se este token for usado abaixo de 14px, o requisito não muda e o
    // texto continua conforme; se alguém clarear a cor "para suavizar", quebra.
    const razao = contraste(colors.textMuted, colors.background);
    expect(razao).toBeGreaterThanOrEqual(AA_NORMAL);
    expect(razao).toBeLessThan(5.5);
  });
});

describe('gradiente de assinatura — latão', () => {
  it('é simétrico: escuro nas pontas, claro no meio', () => {
    // Não é enfeite. Metal escovado concentra o brilho no meio; um gradiente
    // linear de escuro para claro leria como degradê de interface, não como
    // latão. A simetria é o que faz a borda parecer material.
    expect(brass).toHaveLength(3);
    expect(brass[0]).toBe(brass[2]);
    expect(luminance(brass[1]!)).toBeGreaterThan(luminance(brass[0]!));
  });

  it('a parada profunda é visível como traço sobre o canvas', () => {
    // A borda do botão primário precisa se destacar do branco. 4.5:1 é folgado
    // para um traço de 1,5px, e garante que a assinatura não suma em tela sob
    // luz forte — que é a condição de uso no salão da igreja.
    expect(contraste(brass[0]!, colors.background)).toBeGreaterThanOrEqual(AA_NORMAL);
  });

  it('a parada clara NÃO serve como cor de texto', () => {
    // Documenta por que o latão é exclusivamente traço e preenchimento
    // decorativo: 1.74:1 sobre branco é ilegível. Se alguém tentar usar
    // `palette.brassLight` em texto, este teste explica o motivo da recusa.
    expect(contraste(palette.brassLight, colors.background)).toBeLessThan(AA_GRANDE);
  });

  it('o latão brilha sobre a tinta principal, para uso invertido', () => {
    // Cenário de marca sobre superfície escura — cabeçalho ou splash. Aqui o
    // latão claro tem contraste de sobra, o oposto do que acontece no branco.
    expect(contraste(palette.brassLight, colors.text)).toBeGreaterThanOrEqual(AA_NORMAL);
  });

  it('o vermelho de erro só serve como traço, não como texto', () => {
    // Por isso `colors.danger` é usado em borda de campo inválido, e a mensagem
    // em si vai em tinta principal.
    expect(contraste(colors.danger, colors.background)).toBeLessThan(AA_NORMAL);
    expect(contraste(colors.danger, colors.background)).toBeGreaterThanOrEqual(AA_GRANDE);
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

  it('separa as duas vozes por tamanho, sem sobreposição', () => {
    // Regra do DESIGN.md: a face de interface manda até 24px, a de display de 24
    // para cima. Misturar as duas no mesmo corpo parece erro de fallback.
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
    // A compressão é a marca: o título trava num bloco escultural em vez de
    // ficar uma pilha solta.
    expect(type.display.letterSpacing).toBeLessThan(type.headingLg.letterSpacing);
    expect(type.headingLg.letterSpacing).toBeLessThan(type.heading.letterSpacing);
    expect(type.heading.letterSpacing).toBeLessThan(type.headingSm.letterSpacing);
  });
});
