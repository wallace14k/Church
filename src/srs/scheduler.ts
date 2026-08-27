import { DAY_MS, MINUTE_MS } from '../utils/date';
import type { Grade, SrsState } from '../types';

/**
 * Agendador de repetição espaçada.
 *
 * Variante do SM-2 com apenas três respostas — "Não lembro", "Mais ou menos"
 * e "Lembro" — e com passos de aprendizado no estilo Anki. As diferenças em
 * relação ao Anki clássico:
 *
 * - "Mais ou menos" não reinicia o card em revisão: ele avança devagar
 *   (intervalo x HARD_FACTOR) em vez de voltar ao início.
 * - Um lapso não zera o intervalo: metade dele é guardado em
 *   `pendingIntervalDays` e devolvido quando o card sai do reaprendizado.
 * - Todo intervalo acima de dois dias recebe uma variação aleatória de ±5%
 *   para evitar que grandes lotes de cards vençam sempre no mesmo dia.
 */

/** Passos de aprendizado de um card novo, em minutos. */
export const LEARNING_STEPS_MIN = [1, 10] as const;
/** Passos de reaprendizado após um lapso, em minutos. */
export const RELEARNING_STEPS_MIN = [10] as const;

/** Intervalo aplicado quando o card sai do aprendizado, em dias. */
export const GRADUATING_INTERVAL_DAYS = 1;
/** Intervalo mínimo de um card em revisão, em dias. */
export const MIN_REVIEW_INTERVAL_DAYS = 1;
/** Teto de intervalo, em dias (~5,5 anos). */
export const MAX_INTERVAL_DAYS = 2000;

export const EASE_START = 2.5;
export const EASE_MIN = 1.3;
export const EASE_MAX = 3.0;
/** Penalidade de facilidade ao esquecer um card. */
export const EASE_PENALTY_FORGOT = 0.2;
/** Penalidade de facilidade ao lembrar parcialmente. */
export const EASE_PENALTY_PARTIAL = 0.15;
/** Multiplicador de intervalo do "mais ou menos". */
export const HARD_FACTOR = 1.2;
/** Fração do intervalo preservada após um lapso. */
export const LAPSE_RETENTION = 0.5;

/** Estado inicial de um card recém-criado. */
export function createSrsState(now = Date.now()): SrsState {
  return {
    state: 'new',
    due: now,
    intervalDays: 0,
    ease: EASE_START,
    step: 0,
    reps: 0,
    lapses: 0,
    pendingIntervalDays: 0,
    lastReviewedAt: null,
  };
}

function clampEase(ease: number): number {
  return Math.min(EASE_MAX, Math.max(EASE_MIN, Number(ease.toFixed(3))));
}

function clampInterval(days: number): number {
  return Math.min(MAX_INTERVAL_DAYS, Math.max(MIN_REVIEW_INTERVAL_DAYS, days));
}

/**
 * Espalha intervalos longos em ±5% para que cards criados juntos não voltem
 * todos no mesmo dia. Intervalos curtos ficam intactos.
 */
function fuzz(days: number, random: () => number): number {
  if (days <= 2) return days;
  const spread = days * 0.05;
  return days + (random() * 2 - 1) * spread;
}

export interface ScheduleOptions {
  now?: number;
  /** Injetável para tornar o resultado determinístico em testes. */
  random?: () => number;
}

/**
 * Aplica uma resposta a um card e devolve o novo estado de agendamento.
 * A função é pura: não altera o estado recebido.
 */
export function schedule(state: SrsState, grade: Grade, options: ScheduleOptions = {}): SrsState {
  const now = options.now ?? Date.now();
  const random = options.random ?? Math.random;

  const next: SrsState = {
    ...state,
    reps: state.reps + 1,
    lastReviewedAt: now,
  };

  const isLearning = state.state === 'new' || state.state === 'learning';
  const isRelearning = state.state === 'relearning';

  if (isLearning || isRelearning) {
    const steps = isRelearning ? RELEARNING_STEPS_MIN : LEARNING_STEPS_MIN;

    if (grade === 'forgot') {
      next.state = isRelearning ? 'relearning' : 'learning';
      next.step = 0;
      next.due = now + steps[0] * MINUTE_MS;
      return next;
    }

    if (grade === 'partial') {
      // Repete o passo atual, com um intervalo 50% maior para dar mais tempo.
      const stepIndex = Math.min(state.step, steps.length - 1);
      next.state = isRelearning ? 'relearning' : 'learning';
      next.step = stepIndex;
      next.due = now + steps[stepIndex] * 1.5 * MINUTE_MS;
      return next;
    }

    // grade === 'known'
    const nextStep = state.step + 1;
    if (nextStep < steps.length) {
      next.state = isRelearning ? 'relearning' : 'learning';
      next.step = nextStep;
      next.due = now + steps[nextStep] * MINUTE_MS;
      return next;
    }

    // Formatura: o card entra (ou volta) para o ciclo de revisão.
    const graduatedInterval = isRelearning
      ? clampInterval(state.pendingIntervalDays || GRADUATING_INTERVAL_DAYS)
      : GRADUATING_INTERVAL_DAYS;
    next.state = 'review';
    next.step = 0;
    next.pendingIntervalDays = 0;
    next.intervalDays = clampInterval(fuzz(graduatedInterval, random));
    next.due = now + next.intervalDays * DAY_MS;
    return next;
  }

  // Card em revisão.
  if (grade === 'forgot') {
    next.state = 'relearning';
    next.step = 0;
    next.lapses = state.lapses + 1;
    next.ease = clampEase(state.ease - EASE_PENALTY_FORGOT);
    // Guarda metade do intervalo para devolver quando o card se recuperar.
    next.pendingIntervalDays = clampInterval(state.intervalDays * LAPSE_RETENTION);
    next.intervalDays = 0;
    next.due = now + RELEARNING_STEPS_MIN[0] * MINUTE_MS;
    return next;
  }

  const base = Math.max(state.intervalDays, MIN_REVIEW_INTERVAL_DAYS);
  if (grade === 'partial') {
    next.ease = clampEase(state.ease - EASE_PENALTY_PARTIAL);
    next.intervalDays = clampInterval(fuzz(base * HARD_FACTOR, random));
  } else {
    next.ease = state.ease;
    next.intervalDays = clampInterval(fuzz(base * state.ease, random));
  }
  next.state = 'review';
  next.step = 0;
  next.due = now + next.intervalDays * DAY_MS;
  return next;
}

/**
 * Prévia do tempo até o card voltar, para cada resposta. Alimenta os rótulos
 * dos botões ("Não lembro · 1 min", "Lembro · 4 dias").
 */
export function previewIntervals(state: SrsState, now = Date.now()): Record<Grade, number> {
  // random fixo em 0.5 => fuzz neutro, então a prévia bate com a média real.
  const stable = { now, random: () => 0.5 };
  const grades: Grade[] = ['forgot', 'partial', 'known'];
  const result = {} as Record<Grade, number>;
  for (const grade of grades) {
    result[grade] = Math.max(0, schedule(state, grade, stable).due - now);
  }
  return result;
}

/** Um card está pronto quando seu vencimento já passou. */
export function isDue(state: SrsState, now = Date.now()): boolean {
  return state.due <= now;
}
