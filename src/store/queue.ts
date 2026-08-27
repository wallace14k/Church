import { dayKey } from '../utils/date';
import type { Card, Deck, ReviewLog } from '../types';

/** Números exibidos no cartão do baralho e no cabeçalho do treino. */
export interface DeckStats {
  total: number;
  /** Cards nunca vistos e ainda dentro da cota de novos do dia. */
  newAvailable: number;
  /** Cards em aprendizado/reaprendizado prontos agora. */
  learningDue: number;
  /** Cards em revisão prontos agora. */
  reviewDue: number;
  /** Total pronto para estudar agora, já respeitando os limites diários. */
  readyNow: number;
  /** Cards que ainda não venceram. */
  scheduled: number;
  suspended: number;
}

function isLearningState(card: Card): boolean {
  return card.srs.state === 'learning' || card.srs.state === 'relearning';
}

/** Quantos cards novos e quantas revisões já foram feitos hoje neste baralho. */
export function todayCounts(logs: ReviewLog[], deckId: string, now = Date.now()) {
  const today = dayKey(now);
  let newIntroduced = 0;
  let reviews = 0;
  for (const log of logs) {
    if (log.deckId !== deckId) continue;
    if (dayKey(log.reviewedAt) !== today) continue;
    if (log.previousState === 'new') newIntroduced += 1;
    else reviews += 1;
  }
  return { newIntroduced, reviews };
}

export function computeDeckStats(
  deck: Deck,
  cards: Card[],
  logs: ReviewLog[],
  now = Date.now()
): DeckStats {
  const deckCards = cards.filter((card) => card.deckId === deck.id);
  const active = deckCards.filter((card) => !card.suspended);
  const { newIntroduced, reviews } = todayCounts(logs, deck.id, now);

  const newCards = active.filter((card) => card.srs.state === 'new');
  const learningDue = active.filter((card) => isLearningState(card) && card.srs.due <= now);
  const reviewDue = active.filter((card) => card.srs.state === 'review' && card.srs.due <= now);

  const newQuota = Math.max(0, deck.newPerDay - newIntroduced);
  const reviewQuota =
    deck.reviewsPerDay > 0 ? Math.max(0, deck.reviewsPerDay - reviews) : Number.MAX_SAFE_INTEGER;

  const newAvailable = Math.min(newCards.length, newQuota);
  // Cards em aprendizado não consomem a cota: eles já foram introduzidos hoje.
  const reviewAllowed = Math.min(reviewDue.length, reviewQuota);

  return {
    total: deckCards.length,
    newAvailable,
    learningDue: learningDue.length,
    reviewDue: reviewAllowed,
    readyNow: newAvailable + learningDue.length + reviewAllowed,
    scheduled: active.filter((card) => card.srs.due > now && card.srs.state !== 'new').length,
    suspended: deckCards.length - active.length,
  };
}

/**
 * Monta a fila de um treino.
 *
 * Ordem: primeiro o que está em aprendizado (o usuário acabou de errar e
 * precisa revisitar), depois as revisões vencidas, com os cards novos
 * intercalados em vez de empilhados no fim — assim o treino não vira um
 * bloco de conteúdo inédito seguido de outro de revisão.
 */
export function buildQueue(
  deck: Deck,
  cards: Card[],
  logs: ReviewLog[],
  now = Date.now()
): Card[] {
  const active = cards.filter((card) => card.deckId === deck.id && !card.suspended);
  const { newIntroduced, reviews } = todayCounts(logs, deck.id, now);

  const newQuota = Math.max(0, deck.newPerDay - newIntroduced);
  const reviewQuota =
    deck.reviewsPerDay > 0 ? Math.max(0, deck.reviewsPerDay - reviews) : Number.MAX_SAFE_INTEGER;

  const learning = active
    .filter((card) => isLearningState(card) && card.srs.due <= now)
    .sort((a, b) => a.srs.due - b.srs.due);

  const review = active
    .filter((card) => card.srs.state === 'review' && card.srs.due <= now)
    .sort((a, b) => a.srs.due - b.srs.due)
    .slice(0, reviewQuota);

  const fresh = active
    .filter((card) => card.srs.state === 'new')
    .sort((a, b) => a.createdAt - b.createdAt)
    .slice(0, newQuota);

  return [...learning, ...interleave(review, fresh)];
}

/**
 * Distribui os cards novos uniformemente entre as revisões.
 * Sem revisões, devolve apenas os novos.
 */
function interleave(review: Card[], fresh: Card[]): Card[] {
  if (fresh.length === 0) return review;
  if (review.length === 0) return fresh;

  const result: Card[] = [];
  const gap = review.length / fresh.length;
  let nextNewAt = gap;
  let freshIndex = 0;

  review.forEach((card, index) => {
    result.push(card);
    while (freshIndex < fresh.length && index + 1 >= nextNewAt) {
      result.push(fresh[freshIndex]);
      freshIndex += 1;
      nextNewAt += gap;
    }
  });

  // Sobras (arredondamento) vão para o fim.
  return [...result, ...fresh.slice(freshIndex)];
}

/**
 * Previsão de quantos cards vencem em cada um dos próximos `days` dias.
 * Alimenta o gráfico da tela de estatísticas.
 */
export function forecast(cards: Card[], days = 7, now = Date.now()): number[] {
  const DAY = 24 * 60 * 60 * 1000;
  const start = new Date(now);
  start.setHours(0, 0, 0, 0);
  const base = start.getTime();

  const buckets = new Array<number>(days).fill(0);
  for (const card of cards) {
    if (card.suspended || card.srs.state === 'new') continue;
    const index = Math.floor((card.srs.due - base) / DAY);
    if (index < 0) buckets[0] += 1;
    else if (index < days) buckets[index] += 1;
  }
  return buckets;
}
