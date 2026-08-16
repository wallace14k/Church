/**
 * Datas no fuso de negócio.
 *
 * O backend persiste tudo em UTC (`TIMESTAMPTZ`) e converte na borda. O frontend
 * faz a mesma coisa, e pelo mesmo motivo: um culto às 19h em São Paulo precisa
 * aparecer como 19h para quem está lá, independentemente do fuso do aparelho —
 * um membro viajando não pode ver o horário do culto deslocado.
 */

/** Fuso de negócio. Espelha `RetentionOptions.BusinessTimeZone`. */
export const BUSINESS_TIME_ZONE = 'America/Sao_Paulo';

const DATE_FORMATTER = new Intl.DateTimeFormat('pt-BR', {
  timeZone: BUSINESS_TIME_ZONE,
  day: '2-digit',
  month: '2-digit',
  year: 'numeric',
});

const TIME_FORMATTER = new Intl.DateTimeFormat('pt-BR', {
  timeZone: BUSINESS_TIME_ZONE,
  hour: '2-digit',
  minute: '2-digit',
});

const WEEKDAY_FORMATTER = new Intl.DateTimeFormat('pt-BR', {
  timeZone: BUSINESS_TIME_ZONE,
  weekday: 'long',
  day: '2-digit',
  month: 'long',
});

/** `15/08/2026` */
export function formatDate(value: Date | string): string {
  return DATE_FORMATTER.format(toDate(value));
}

/** `19:30` */
export function formatTime(value: Date | string): string {
  return TIME_FORMATTER.format(toDate(value));
}

/** `sábado, 15 de agosto` — cabeçalho de agenda, onde o ano é ruído. */
export function formatWeekday(value: Date | string): string {
  return WEEKDAY_FORMATTER.format(toDate(value));
}

/**
 * Dias inteiros restantes até a data, contados no fuso de negócio.
 *
 * Compara **dias-calendário**, não intervalo de 24 horas. "Vence amanhã"
 * precisa continuar sendo amanhã às 23h de hoje; a diferença bruta em
 * milissegundos diria "vence em 0 dias" e o aviso sairia errado.
 */
export function daysUntil(value: Date | string, now: Date = new Date()): number {
  const target = startOfBusinessDay(toDate(value));
  const today = startOfBusinessDay(now);
  return Math.round((target.getTime() - today.getTime()) / 86_400_000);
}

/**
 * Texto de vencimento para a tela de assinatura.
 *
 * Espelha as janelas do motor de retenção (`RetentionWindowCalculator`): o que o
 * usuário lê no app precisa concordar com o e-mail que ele recebeu, senão a
 * mensagem perde credibilidade justamente no momento de renovar.
 */
export function describeRenewal(periodEnd: Date | string, now: Date = new Date()): string {
  const days = daysUntil(periodEnd, now);

  if (days < 0) return `Venceu há ${Math.abs(days)} ${plural(Math.abs(days), 'dia', 'dias')}`;
  if (days === 0) return 'Vence hoje';
  if (days === 1) return 'Vence amanhã';
  return `Vence em ${days} dias`;
}

function plural(count: number, singular: string, plural_: string): string {
  return count === 1 ? singular : plural_;
}

const MESES = [
  'janeiro', 'fevereiro', 'março', 'abril', 'maio', 'junho',
  'julho', 'agosto', 'setembro', 'outubro', 'novembro', 'dezembro',
] as const;

/**
 * `31 de dezembro` — dia e mês de um aniversário, sem ano.
 *
 * Recebe a data ISO (`DateOnly` do backend, ex. `"1980-12-31"`) como texto e lê
 * os dígitos direto da string, sem passar por `Date`. `birthDate` não carrega
 * horário nem fuso — construir um `Date` a partir dele interpretaria a
 * meia-noite como UTC, e formatar num fuso atrás de UTC devolveria o dia
 * anterior. Um aniversariante do dia 1º apareceria no card do dia 31 do mês
 * passado.
 */
export function formatBirthday(isoDate: string): string {
  const partes = /^(\d{4})-(\d{2})-(\d{2})/u.exec(isoDate);
  if (partes === null) {
    throw new TypeError(`Data inválida: ${isoDate}`);
  }

  const [, , mes, dia] = partes;
  return `${Number(dia)} de ${MESES[Number(mes) - 1]}`;
}

function toDate(value: Date | string): Date {
  const date = typeof value === 'string' ? new Date(value) : value;
  if (Number.isNaN(date.getTime())) {
    throw new TypeError(`Data inválida: ${String(value)}`);
  }
  return date;
}

/**
 * Meia-noite do dia, no fuso de negócio.
 *
 * Usa `Intl` com `en-CA` porque esse locale formata como `YYYY-MM-DD`, o que
 * evita ter de montar a data a partir de partes numéricas e errar o mês
 * zero-based. É um truque conhecido, e mais confiável que a alternativa.
 */
function startOfBusinessDay(value: Date): Date {
  const isoDate = new Intl.DateTimeFormat('en-CA', {
    timeZone: BUSINESS_TIME_ZONE,
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
  }).format(value);

  return new Date(`${isoDate}T00:00:00Z`);
}
