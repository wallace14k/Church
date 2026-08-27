import type { SessionResult } from '../types';

export type RootStackParamList = {
  Main: undefined;
  DeckForm: { deckId?: string };
  DeckDetail: { deckId: string };
  CardForm: { deckId: string; cardId?: string };
  Study: { deckId: string };
  SessionSummary: { result: SessionResult };
  Paywall: { source?: 'limite-baralhos' | 'limite-cards' | 'audio' | 'resumo' | 'perfil' };
};

export type MainTabParamList = {
  Decks: undefined;
  Stats: undefined;
  Profile: undefined;
};

declare global {
  // eslint-disable-next-line @typescript-eslint/no-namespace
  namespace ReactNavigation {
    interface RootParamList extends RootStackParamList {}
  }
}
