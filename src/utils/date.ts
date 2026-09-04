export const DAY_MS = 24 * 60 * 60 * 1000;
export const MINUTE_MS = 60 * 1000;

/** Chave de dia no fuso local, no formato YYYY-MM-DD. */
export function dayKey(date: Date | number = Date.now()): string {
  const d = typeof date === 'number' ? new Date(date) : date;
  const month = `${d.getMonth() + 1}`.padStart(2, '0');
  const day = `${d.getDate()}`.padStart(2, '0');
  return `${d.getFullYear()}-${month}-${day}`;
}

/** Converte uma chave YYYY-MM-DD de volta para meia-noite local. */
export function dayKeyToDate(key: string): Date {
  const [y, m, d] = key.split('-').map(Number);
  return new Date(y, m - 1, d);
}

/** Diferença em dias inteiros entre duas chaves de dia. */
export function daysBetween(fromKey: string, toKey: string): number {
  const from = dayKeyToDate(fromKey).getTime();
  const to = dayKeyToDate(toKey).getTime();
  return Math.round((to - from) / DAY_MS);
}

/** Início do dia local em epoch ms. */
export function startOfDay(date: Date | number = Date.now()): number {
  const d = typeof date === 'number' ? new Date(date) : new Date(date.getTime());
  d.setHours(0, 0, 0, 0);
  return d.getTime();
}

/** Fim do dia local em epoch ms — usado como limite do "vence hoje". */
export function endOfDay(date: Date | number = Date.now()): number {
  return startOfDay(date) + DAY_MS - 1;
}

/** As últimas `count` chaves de dia, da mais antiga para a mais recente. */
export function recentDayKeys(count: number, from: Date | number = Date.now()): string[] {
  const base = startOfDay(from);
  const keys: string[] = [];
  for (let i = count - 1; i >= 0; i -= 1) {
    keys.push(dayKey(base - i * DAY_MS));
  }
  return keys;
}

/** Formata uma duração em ms como "1:05". */
export function formatDuration(ms: number): string {
  const total = Math.max(0, Math.round(ms / 1000));
  const minutes = Math.floor(total / 60);
  const seconds = total % 60;
  return `${minutes}:${`${seconds}`.padStart(2, '0')}`;
}

/** Descreve um intervalo em linguagem natural: "10 min", "3 dias", "1,2 mês". */
export function formatInterval(ms: number): string {
  if (ms < MINUTE_MS) return '<1 min';
  if (ms < 60 * MINUTE_MS) return `${Math.round(ms / MINUTE_MS)} min`;
  if (ms < DAY_MS) return `${Math.round(ms / (60 * MINUTE_MS))} h`;
  const days = ms / DAY_MS;
  if (days < 30) return days < 1.5 ? '1 dia' : `${Math.round(days)} dias`;
  const months = days / 30;
  if (months < 12) return `${months.toFixed(months < 10 ? 1 : 0).replace('.', ',')} mês`;
  return `${(days / 365).toFixed(1).replace('.', ',')} ano`;
}
