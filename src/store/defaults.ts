import { deckColors, deckEmojis } from '../theme';
import { createSrsState } from '../srs/scheduler';
import { createId } from '../utils/id';
import type { Card, Deck, Settings } from '../types';

export const DEFAULT_SETTINGS: Settings = {
  autoPlayFrontAudio: false,
  autoPlayBackAudio: true,
  hapticsEnabled: true,
  reminderEnabled: false,
  reminderTime: '20:00',
  showNextInterval: true,
};

export const DEFAULT_NEW_PER_DAY = 15;
export const DEFAULT_REVIEWS_PER_DAY = 120;

export function createDeck(input: Partial<Deck> & { name: string }): Deck {
  const now = Date.now();
  return {
    id: createId('d'),
    name: input.name,
    description: input.description ?? '',
    emoji: input.emoji ?? deckEmojis[0],
    color: input.color ?? deckColors[0],
    newPerDay: input.newPerDay ?? DEFAULT_NEW_PER_DAY,
    reviewsPerDay: input.reviewsPerDay ?? DEFAULT_REVIEWS_PER_DAY,
    createdAt: now,
    updatedAt: now,
  };
}

export function createCard(input: Partial<Card> & { deckId: string; front: string; back: string }): Card {
  const now = Date.now();
  return {
    id: createId('c'),
    deckId: input.deckId,
    front: input.front,
    back: input.back,
    hint: input.hint ?? '',
    example: input.example ?? '',
    frontAudio: input.frontAudio ?? null,
    backAudio: input.backAudio ?? null,
    tags: input.tags ?? [],
    suspended: false,
    srs: createSrsState(now),
    createdAt: now,
    updatedAt: now,
  };
}

/**
 * Baralho de exemplo criado no primeiro uso, para que a tela inicial já
 * mostre como um card fica montado.
 */
export function createStarterContent(): { deck: Deck; cards: Card[] } {
  const deck = createDeck({
    name: 'Inglês — dia a dia',
    description: 'Baralho de exemplo. Edite, apague ou use como modelo.',
    emoji: '🇺🇸',
    color: deckColors[0],
  });

  const samples: { front: string; back: string; example?: string; hint?: string }[] = [
    {
      front: 'How have you been?',
      back: 'Como você tem passado?',
      example: '— Hey, long time no see! How have you been?\n— Pretty good, thanks for asking.',
      hint: 'Cumprimento para quem você não vê há um tempo',
    },
    {
      front: 'I am looking forward to it',
      back: 'Estou ansioso por isso',
      example: '— The trip is next week.\n— I know, I am looking forward to it.',
    },
    {
      front: 'It is up to you',
      back: 'É você quem decide / fica a seu critério',
      example: '— Should we eat out or cook?\n— It is up to you.',
    },
    {
      front: 'Do you mind if I...?',
      back: 'Você se importa se eu...?',
      example: '— Do you mind if I open the window?\n— Not at all, go ahead.',
      hint: 'Repare que "Not at all" quer dizer "pode abrir"',
    },
    {
      front: 'I could not agree more',
      back: 'Concordo plenamente',
      example: '— This place is way too expensive.\n— I could not agree more.',
    },
  ];

  return {
    deck,
    cards: samples.map((sample) =>
      createCard({
        deckId: deck.id,
        front: sample.front,
        back: sample.back,
        example: sample.example,
        hint: sample.hint,
      })
    ),
  };
}
