import {
  EASE_MIN,
  EASE_START,
  GRADUATING_INTERVAL_DAYS,
  LEARNING_STEPS_MIN,
  createSrsState,
  isDue,
  previewIntervals,
  schedule,
} from './scheduler';
import { DAY_MS, MINUTE_MS } from '../utils/date';
import type { SrsState } from '../types';

const NOW = new Date('2026-03-10T12:00:00Z').getTime();
/** Sem variação aleatória, para os intervalos serem exatos nos testes. */
const options = { now: NOW, random: () => 0.5 };

/** Card já em revisão, com o intervalo e a facilidade informados. */
function reviewCard(intervalDays: number, ease = EASE_START): SrsState {
  return {
    ...createSrsState(NOW),
    state: 'review',
    intervalDays,
    ease,
    reps: 5,
    due: NOW,
  };
}

describe('cards novos', () => {
  it('começa como novo e vencido de imediato', () => {
    const state = createSrsState(NOW);
    expect(state.state).toBe('new');
    expect(isDue(state, NOW)).toBe(true);
  });

  it('"não lembro" devolve o card ao primeiro passo de aprendizado', () => {
    const state = schedule(createSrsState(NOW), 'forgot', options);
    expect(state.state).toBe('learning');
    expect(state.step).toBe(0);
    expect(state.due - NOW).toBe(LEARNING_STEPS_MIN[0] * MINUTE_MS);
  });

  it('"mais ou menos" repete o passo atual com mais folga', () => {
    const state = schedule(createSrsState(NOW), 'partial', options);
    expect(state.state).toBe('learning');
    expect(state.step).toBe(0);
    expect(state.due - NOW).toBe(LEARNING_STEPS_MIN[0] * 1.5 * MINUTE_MS);
  });

  it('"lembro" avança um passo por vez até formar o card', () => {
    const first = schedule(createSrsState(NOW), 'known', options);
    expect(first.state).toBe('learning');
    expect(first.step).toBe(1);
    expect(first.due - NOW).toBe(LEARNING_STEPS_MIN[1] * MINUTE_MS);

    const graduated = schedule(first, 'known', options);
    expect(graduated.state).toBe('review');
    expect(graduated.intervalDays).toBe(GRADUATING_INTERVAL_DAYS);
    expect(graduated.due - NOW).toBe(GRADUATING_INTERVAL_DAYS * DAY_MS);
  });
});

describe('cards em revisão', () => {
  it('"lembro" multiplica o intervalo pela facilidade', () => {
    const state = schedule(reviewCard(10), 'known', options);
    expect(state.intervalDays).toBeCloseTo(25, 5);
    expect(state.ease).toBe(EASE_START);
  });

  it('"mais ou menos" avança devagar e reduz a facilidade', () => {
    const state = schedule(reviewCard(10), 'partial', options);
    expect(state.intervalDays).toBeCloseTo(12, 5);
    expect(state.ease).toBeCloseTo(2.35, 5);
    expect(state.state).toBe('review');
  });

  it('"não lembro" manda o card para reaprendizado e conta o lapso', () => {
    const state = schedule(reviewCard(20), 'forgot', options);
    expect(state.state).toBe('relearning');
    expect(state.lapses).toBe(1);
    expect(state.ease).toBeCloseTo(2.3, 5);
    expect(state.due - NOW).toBe(10 * MINUTE_MS);
    // Metade do intervalo fica guardada para a recuperação.
    expect(state.pendingIntervalDays).toBe(10);
    expect(state.intervalDays).toBe(0);
  });

  it('devolve metade do intervalo quando o card sai do reaprendizado', () => {
    const lapsed = schedule(reviewCard(20), 'forgot', options);
    const recovered = schedule(lapsed, 'known', options);
    expect(recovered.state).toBe('review');
    expect(recovered.intervalDays).toBe(10);
    expect(recovered.pendingIntervalDays).toBe(0);
  });

  it('não deixa a facilidade cair abaixo do piso', () => {
    let state = reviewCard(10, EASE_MIN);
    for (let i = 0; i < 5; i += 1) {
      state = schedule({ ...state, state: 'review' }, 'forgot', options);
    }
    expect(state.ease).toBe(EASE_MIN);
  });

  it('respeita o intervalo mínimo de um dia', () => {
    const state = schedule(reviewCard(1, EASE_MIN), 'partial', options);
    expect(state.intervalDays).toBeGreaterThanOrEqual(1);
  });
});

describe('previewIntervals', () => {
  it('ordena as respostas do intervalo mais curto para o mais longo', () => {
    const preview = previewIntervals(reviewCard(10), NOW);
    expect(preview.forgot).toBeLessThan(preview.partial);
    expect(preview.partial).toBeLessThan(preview.known);
  });

  it('bate com o agendamento real de um card novo', () => {
    const preview = previewIntervals(createSrsState(NOW), NOW);
    expect(preview.forgot).toBe(LEARNING_STEPS_MIN[0] * MINUTE_MS);
  });
});

describe('pureza', () => {
  it('não altera o estado recebido', () => {
    const original = reviewCard(10);
    const snapshot = { ...original };
    schedule(original, 'forgot', options);
    expect(original).toEqual(snapshot);
  });
});
