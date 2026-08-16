/**
 * Dinheiro no Congrega.
 *
 * O backend persiste valores em `BIGINT` de centavos, e o frontend precisa
 * respeitar isso ponta a ponta. `0.1 + 0.2 !== 0.3` em ponto flutuante, e num
 * módulo de dízimos esse erro vira diferença de fechamento que alguém vai ter de
 * explicar para a assembleia da igreja.
 */

/**
 * Centavos, com marca de tipo.
 *
 * A marca existe para o compilador recusar `formatBRL(49.9)` — passar reais onde
 * se espera centavos é o erro mais fácil de cometer e o mais difícil de notar,
 * porque o número aparece na tela cem vezes menor sem nenhum aviso.
 */
export type Cents = number & { readonly __brand: unique symbol };

/** Constrói um valor em centavos a partir de um inteiro. */
export function cents(value: number): Cents {
  if (!Number.isInteger(value)) {
    throw new TypeError(`Centavos precisam ser inteiros. Recebido: ${value}`);
  }
  return value as Cents;
}

/** Converte reais para centavos, arredondando meio para cima. */
export function fromReais(reais: number): Cents {
  // Math.round(x * 100) sozinho erra em casos como 1.005 por representação
  // binária. O toFixed intermediário resolve a família de casos que aparece em
  // valores de dízimo digitados à mão.
  return cents(Math.round(Number.parseFloat((reais * 100).toFixed(4))));
}

const BRL_FORMATTER = new Intl.NumberFormat('pt-BR', {
  style: 'currency',
  currency: 'BRL',
  minimumFractionDigits: 2,
  maximumFractionDigits: 2,
});

/**
 * Formata centavos como moeda brasileira.
 *
 * Depende de `Intl` com dados de locale. O Hermes precisa ser construído com
 * suporte a Intl — em Android antigo sem esse suporte, o formato cai para
 * en-US silenciosamente e o usuário vê "R$1,234.56". Vale um teste de fumaça no
 * dispositivo mais antigo suportado, e não apenas no simulador.
 */
export function formatBRL(value: Cents): string {
  return BRL_FORMATTER.format(value / 100);
}

/**
 * Formata sem o símbolo, para colunas de tabela onde o "R$" repetido só ocupa
 * espaço e atrapalha o alinhamento dos numerais.
 */
export function formatAmount(value: Cents): string {
  return BRL_FORMATTER.format(value / 100).replace(/^R\$\s*/u, '');
}

/**
 * Lê um valor digitado pelo usuário em pt-BR.
 *
 * Aceita "1.234,56", "1234,56", "1234.56" e "1234". Devolve `null` quando não
 * dá para interpretar — o chamador decide o que dizer, porque a mensagem de erro
 * depende do campo.
 */
export function parseBRL(input: string): Cents | null {
  const trimmed = input.trim().replace(/^R\$\s*/u, '');
  if (trimmed.length === 0) return null;

  // Separador decimal é o ÚLTIMO ponto ou vírgula seguido de 1 ou 2 dígitos.
  // Tratar toda vírgula como decimal quebraria "1.234" (mil duzentos e trinta e
  // quatro) e tratar todo ponto como milhar quebraria "1234.56".
  const match = /^(-?)([\d.,]*?)([.,](\d{1,2}))?$/u.exec(trimmed);
  if (!match) return null;

  const [, sign = '', integerPart = '', , decimalPart] = match;
  const digits = integerPart.replace(/[.,]/gu, '');
  if (digits.length === 0 && decimalPart === undefined) return null;
  if (!/^\d*$/u.test(digits)) return null;

  const whole = digits === '' ? 0 : Number.parseInt(digits, 10);
  const fraction = decimalPart === undefined ? 0 : Number.parseInt(decimalPart.padEnd(2, '0'), 10);
  const total = whole * 100 + fraction;

  return cents(sign === '-' ? -total : total);
}

/** Soma valores em centavos sem sair do domínio inteiro. */
export function sumCents(values: readonly Cents[]): Cents {
  return cents(values.reduce<number>((total, value) => total + value, 0));
}
