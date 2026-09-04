import {
  DEFAULT_DAILY_GOAL,
  FREEZE_EARN_EVERY,
  MAX_FREEZES,
  activitySeries,
  createStreakState,
  goalReachedToday,
  grantFreeze,
  reconcileStreak,
  registerReviews,
  reviewsToday,
} from './index';
import { dayKey } from '../utils/date';
import type { StreakState } from '../types';

const DAY_MS = 24 * 60 * 60 * 1000;
/** Meio-dia local, para os cálculos de dia não escorregarem por fuso. */
const TODAY = new Date(2026, 2, 10, 12, 0, 0).getTime();

function streakWith(overrides: Partial<StreakState>): StreakState {
  return { ...createStreakState(), ...overrides };
}

describe('registerReviews', () => {
  it('acumula as revisões do dia sem subir a ofensiva antes da meta', () => {
    const state = streakWith({ dailyGoal: 20 });
    const { streak, goalJustReached } = registerReviews(state, 8, TODAY);

    expect(reviewsToday(streak, TODAY)).toBe(8);
    expect(goalJustReached).toBe(false);
    expect(streak.current).toBe(0);
  });

  it('sobe a ofensiva no treino que fecha a meta', () => {
    const partial = registerReviews(streakWith({ dailyGoal: 20 }), 12, TODAY).streak;
    const { streak, goalJustReached } = registerReviews(partial, 10, TODAY);

    expect(goalJustReached).toBe(true);
    expect(streak.current).toBe(1);
    expect(streak.longest).toBe(1);
    expect(goalReachedToday(streak, TODAY)).toBe(true);
  });

  it('não conta a ofensiva duas vezes no mesmo dia', () => {
    const first = registerReviews(streakWith({ dailyGoal: 10 }), 10, TODAY);
    const second = registerReviews(first.streak, 10, TODAY);

    expect(second.goalJustReached).toBe(false);
    expect(second.streak.current).toBe(1);
    expect(reviewsToday(second.streak, TODAY)).toBe(20);
  });

  it('continua a ofensiva quando a meta é batida no dia seguinte', () => {
    const yesterday = registerReviews(streakWith({ dailyGoal: 10 }), 10, TODAY - DAY_MS).streak;
    const { streak } = registerReviews(yesterday, 10, TODAY);

    expect(streak.current).toBe(2);
    expect(streak.longest).toBe(2);
  });

  it('dá um protetor a cada semana completa', () => {
    let state = streakWith({
      dailyGoal: 10,
      current: FREEZE_EARN_EVERY - 1,
      lastGoalDay: dayKey(TODAY - DAY_MS),
    });
    const result = registerReviews(state, 10, TODAY);

    expect(result.streak.current).toBe(FREEZE_EARN_EVERY);
    expect(result.freezeEarned).toBe(true);
    expect(result.streak.freezes).toBe(1);
  });
});

describe('reconcileStreak', () => {
  it('mantém a ofensiva de quem estudou ontem', () => {
    const state = streakWith({ current: 5, lastGoalDay: dayKey(TODAY - DAY_MS) });
    expect(reconcileStreak(state, TODAY).current).toBe(5);
  });

  it('zera a ofensiva de quem passou um dia sem estudar', () => {
    const state = streakWith({ current: 5, lastGoalDay: dayKey(TODAY - 2 * DAY_MS) });
    expect(reconcileStreak(state, TODAY).current).toBe(0);
  });

  it('gasta um protetor para cobrir o dia perdido', () => {
    const state = streakWith({
      current: 5,
      freezes: 2,
      lastGoalDay: dayKey(TODAY - 2 * DAY_MS),
    });
    const result = reconcileStreak(state, TODAY);

    expect(result.current).toBe(5);
    expect(result.freezes).toBe(1);
  });

  it('zera quando os protetores não cobrem todos os dias perdidos', () => {
    const state = streakWith({
      current: 9,
      freezes: 1,
      lastGoalDay: dayKey(TODAY - 4 * DAY_MS),
    });
    const result = reconcileStreak(state, TODAY);

    expect(result.current).toBe(0);
    expect(result.freezes).toBe(0);
  });
});

describe('grantFreeze', () => {
  it('não passa do teto de protetores', () => {
    const state = grantFreeze(streakWith({ freezes: MAX_FREEZES }), 2);
    expect(state.freezes).toBe(MAX_FREEZES);
  });
});

describe('activitySeries', () => {
  it('devolve um ponto por dia, terminando em hoje', () => {
    const state = streakWith({
      dailyGoal: DEFAULT_DAILY_GOAL,
      history: { [dayKey(TODAY)]: 25, [dayKey(TODAY - DAY_MS)]: 5 },
    });
    const series = activitySeries(state, 7, TODAY);

    expect(series).toHaveLength(7);
    expect(series[6]).toEqual({ day: dayKey(TODAY), count: 25, goalReached: true });
    expect(series[5]).toEqual({ day: dayKey(TODAY - DAY_MS), count: 5, goalReached: false });
    expect(series[0].count).toBe(0);
  });
});
