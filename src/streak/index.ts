import { dayKey, daysBetween, recentDayKeys } from '../utils/date';
import type { StreakState } from '../types';

/**
 * Mecânica de ofensiva.
 *
 * A ofensiva avança quando o usuário bate a meta diária de cards (não apenas
 * ao abrir o app). Um dia perdido pode ser coberto por um "protetor de
 * ofensiva", que o usuário ganha a cada 7 dias seguidos e também pode receber
 * ao assistir um anúncio ou assinar o plano premium.
 */

export const DEFAULT_DAILY_GOAL = 20;
/** Dias seguidos necessários para ganhar um protetor. */
export const FREEZE_EARN_EVERY = 7;
/** Máximo de protetores acumulados. */
export const MAX_FREEZES = 3;

export function createStreakState(dailyGoal = DEFAULT_DAILY_GOAL): StreakState {
  return {
    current: 0,
    longest: 0,
    lastGoalDay: null,
    lastStudyDay: null,
    freezes: 0,
    dailyGoal,
    history: {},
  };
}

/** Quantos cards foram revisados hoje. */
export function reviewsToday(streak: StreakState, now = Date.now()): number {
  return streak.history[dayKey(now)] ?? 0;
}

export function goalReachedToday(streak: StreakState, now = Date.now()): boolean {
  return reviewsToday(streak, now) >= streak.dailyGoal;
}

/**
 * Reconcilia a ofensiva com o calendário. Deve rodar na abertura do app e
 * antes de qualquer leitura da ofensiva, porque dias podem ter passado com o
 * app fechado.
 *
 * Cada dia perdido consome um protetor; sem protetores, a ofensiva zera.
 */
export function reconcileStreak(streak: StreakState, now = Date.now()): StreakState {
  if (!streak.lastGoalDay || streak.current === 0) return streak;

  const gap = daysBetween(streak.lastGoalDay, dayKey(now));
  // 0 = meta batida hoje, 1 = batida ontem (a ofensiva ainda está viva hoje).
  if (gap <= 1) return streak;

  const missedDays = gap - 1;
  if (missedDays <= streak.freezes) {
    return {
      ...streak,
      freezes: streak.freezes - missedDays,
      // Os protetores seguram a ofensiva; ela continua de onde parou.
      lastGoalDay: dayKey(now - 24 * 60 * 60 * 1000),
    };
  }

  return { ...streak, current: 0, freezes: 0 };
}

export interface StreakUpdate {
  streak: StreakState;
  /** A meta diária foi batida exatamente nesta chamada. */
  goalJustReached: boolean;
  /** Um protetor foi ganho nesta chamada. */
  freezeEarned: boolean;
}

/**
 * Registra revisões concluídas e atualiza a ofensiva.
 * `count` é o número de cards respondidos no treino.
 */
export function registerReviews(
  streak: StreakState,
  count: number,
  now = Date.now()
): StreakUpdate {
  const today = dayKey(now);
  const reconciled = reconcileStreak(streak, now);
  const before = reconciled.history[today] ?? 0;
  const after = before + count;

  const next: StreakState = {
    ...reconciled,
    lastStudyDay: today,
    history: { ...reconciled.history, [today]: after },
  };

  const alreadyCounted = reconciled.lastGoalDay === today;
  const goalJustReached =
    !alreadyCounted && before < next.dailyGoal && after >= next.dailyGoal;

  let freezeEarned = false;
  if (goalJustReached) {
    next.current = reconciled.current + 1;
    next.longest = Math.max(reconciled.longest, next.current);
    next.lastGoalDay = today;

    if (next.current % FREEZE_EARN_EVERY === 0 && next.freezes < MAX_FREEZES) {
      next.freezes = next.freezes + 1;
      freezeEarned = true;
    }
  }

  return { streak: next, goalJustReached, freezeEarned };
}

/** Adiciona protetores (recompensa de anúncio ou benefício premium). */
export function grantFreeze(streak: StreakState, amount = 1): StreakState {
  return { ...streak, freezes: Math.min(MAX_FREEZES, streak.freezes + amount) };
}

/** Série dos últimos `days` dias, para o gráfico de barras das estatísticas. */
export function activitySeries(
  streak: StreakState,
  days = 7,
  now = Date.now()
): { day: string; count: number; goalReached: boolean }[] {
  return recentDayKeys(days, now).map((key) => ({
    day: key,
    count: streak.history[key] ?? 0,
    goalReached: (streak.history[key] ?? 0) >= streak.dailyGoal,
  }));
}
