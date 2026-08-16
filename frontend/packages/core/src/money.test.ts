import { describe, expect, it } from 'vitest';
import { cents, formatAmount, formatBRL, fromReais, parseBRL, sumCents } from './money';

describe('formatBRL', () => {
  it('formata centavos como moeda brasileira', () => {
    //   é o espaço não-separável que o Intl insere depois de R$. Escrever o
    // espaço comum aqui faria o teste falhar por um caractere invisível.
    expect(formatBRL(cents(4990))).toBe('R$ 4.990,00'.replace('4.990,00', '49,90'));
    expect(formatBRL(cents(123_456))).toContain('1.234,56');
  });

  it('formata zero e negativo', () => {
    expect(formatBRL(cents(0))).toContain('0,00');
    expect(formatBRL(cents(-500))).toContain('5,00');
  });

  it('omite o símbolo em colunas de tabela', () => {
    expect(formatAmount(cents(123_456))).toBe('1.234,56');
  });
});

describe('cents', () => {
  it('recusa valor não inteiro', () => {
    // A marca de tipo pega em compilação; esta guarda pega o que vem de JSON,
    // que o compilador não vê.
    expect(() => cents(49.9)).toThrow(TypeError);
  });
});

describe('fromReais', () => {
  it('converte reais para centavos', () => {
    expect(fromReais(49.9)).toBe(4990);
    expect(fromReais(0.1)).toBe(10);
  });

  it('não perde centavo em valores que quebram ponto flutuante', () => {
    // 1.005 * 100 dá 100.49999999999999 em IEEE 754. Sem o passo de
    // normalização, o dízimo de R$ 1,005 viraria R$ 1,00 — e a diferença
    // aparece no fechamento do mês.
    expect(fromReais(1.005)).toBe(101);
    expect(fromReais(8.115)).toBe(812);
  });
});

describe('parseBRL', () => {
  it.each([
    ['1.234,56', 123_456],
    ['1234,56', 123_456],
    ['1234.56', 123_456],
    ['49,90', 4990],
    ['1234', 123_400],
    ['R$ 1.234,56', 123_456],
    ['  99,00  ', 9900],
    ['-50,00', -5000],
  ])('lê %s como %i centavos', (input, expected) => {
    expect(parseBRL(input)).toBe(expected);
  });

  it('distingue separador de milhar de separador decimal', () => {
    // O caso que quebra a implementação ingênua: tratar toda vírgula como
    // decimal transformaria mil duzentos e trinta e quatro em doze reais.
    expect(parseBRL('1.234')).toBe(123_400);
    expect(parseBRL('12,34')).toBe(1234);
  });

  it('devolve null quando não dá para interpretar', () => {
    expect(parseBRL('')).toBeNull();
    expect(parseBRL('abc')).toBeNull();
    expect(parseBRL('   ')).toBeNull();
  });
});

describe('sumCents', () => {
  it('soma sem sair do domínio inteiro', () => {
    // Em ponto flutuante, 0.1 + 0.2 + 0.3 !== 0.6. Em centavos, sempre fecha.
    const total = sumCents([fromReais(0.1), fromReais(0.2), fromReais(0.3)]);
    expect(total).toBe(60);
    expect(formatAmount(total)).toBe('0,60');
  });

  it('soma lista vazia como zero', () => {
    expect(sumCents([])).toBe(0);
  });
});
