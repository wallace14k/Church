import React, {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useRef,
  useState,
} from 'react';

import { deleteAudio } from '../audio/storage';
import { FREE_LIMITS } from '../monetization/products';
import { FREE_ENTITLEMENTS, loadEntitlements, purchase, restore } from '../monetization/purchases';
import { schedule } from '../srs/scheduler';
import { storage, SCHEMA_VERSION } from '../storage';
import {
  createStreakState,
  grantFreeze,
  reconcileStreak,
  registerReviews,
} from '../streak';
import { createId } from '../utils/id';
import type {
  Card,
  Deck,
  Entitlements,
  Grade,
  ReviewLog,
  SessionResult,
  Settings,
  StreakState,
} from '../types';
import { DEFAULT_SETTINGS, createCard, createDeck, createStarterContent } from './defaults';
import { buildQueue, computeDeckStats, forecast, type DeckStats } from './queue';

export interface AnswerOutcome {
  card: Card;
  log: ReviewLog;
}

export interface LimitCheck {
  allowed: boolean;
  /** Motivo a exibir quando `allowed` é falso. */
  reason?: string;
}

interface AppContextValue {
  ready: boolean;
  decks: Deck[];
  cards: Card[];
  logs: ReviewLog[];
  streak: StreakState;
  entitlements: Entitlements;
  settings: Settings;
  sessionCount: number;
  premium: boolean;

  // Baralhos
  addDeck: (input: Parameters<typeof createDeck>[0]) => Promise<Deck>;
  editDeck: (deckId: string, patch: Partial<Deck>) => Promise<void>;
  removeDeck: (deckId: string) => Promise<void>;

  // Cards
  addCard: (input: Parameters<typeof createCard>[0]) => Promise<Card>;
  editCard: (cardId: string, patch: Partial<Card>) => Promise<void>;
  removeCard: (cardId: string) => Promise<void>;
  toggleSuspend: (cardId: string) => Promise<void>;
  resetCardProgress: (cardId: string) => Promise<void>;

  // Treino
  getQueue: (deckId: string) => Card[];
  getDeckStats: (deckId: string) => DeckStats;
  answer: (cardId: string, grade: Grade, elapsedMs: number) => Promise<AnswerOutcome | null>;
  finishSession: (result: Omit<SessionResult, 'streakBefore' | 'streakAfter' | 'goalReached'>) =>
    Promise<SessionResult>;
  getForecast: (days?: number) => number[];

  // Ofensiva e ajustes
  setDailyGoal: (goal: number) => Promise<void>;
  addFreeze: (amount?: number) => Promise<void>;
  updateSettings: (patch: Partial<Settings>) => Promise<void>;

  // Monetização
  buyPremium: (productId: string) => Promise<void>;
  restorePremium: () => Promise<boolean>;
  canAddDeck: () => LimitCheck;
  canAddCard: (deckId: string) => LimitCheck;
  canAttachBothAudios: boolean;

  wipeAllData: () => Promise<void>;
}

const AppContext = createContext<AppContextValue | null>(null);

export function AppProvider({ children }: { children: React.ReactNode }) {
  const [ready, setReady] = useState(false);
  const [decks, setDecks] = useState<Deck[]>([]);
  const [cards, setCards] = useState<Card[]>([]);
  const [logs, setLogs] = useState<ReviewLog[]>([]);
  const [streak, setStreak] = useState<StreakState>(() => createStreakState());
  const [entitlements, setEntitlements] = useState<Entitlements>(FREE_ENTITLEMENTS);
  const [settings, setSettings] = useState<Settings>(DEFAULT_SETTINGS);
  const [sessionCount, setSessionCount] = useState(0);

  // Espelho síncrono do estado: as ações precisam ler o valor mais recente
  // sem depender do ciclo de renderização (várias respostas seguidas no
  // treino chegam mais rápido do que o React re-renderiza).
  const cardsRef = useRef<Card[]>([]);
  const logsRef = useRef<ReviewLog[]>([]);
  const decksRef = useRef<Deck[]>([]);
  const streakRef = useRef<StreakState>(streak);

  const commitCards = useCallback(async (next: Card[]) => {
    cardsRef.current = next;
    setCards(next);
    await storage.saveCards(next);
  }, []);

  const commitDecks = useCallback(async (next: Deck[]) => {
    decksRef.current = next;
    setDecks(next);
    await storage.saveDecks(next);
  }, []);

  const commitLogs = useCallback(async (next: ReviewLog[]) => {
    logsRef.current = next;
    setLogs(next);
    await storage.saveReviewLogs(next);
  }, []);

  const commitStreak = useCallback(async (next: StreakState) => {
    streakRef.current = next;
    setStreak(next);
    await storage.saveStreak(next);
  }, []);

  // Carga inicial.
  useEffect(() => {
    let active = true;
    void (async () => {
      const [
        savedDecks,
        savedCards,
        savedLogs,
        savedStreak,
        savedSettings,
        savedSessions,
        savedEntitlements,
        schemaVersion,
      ] = await Promise.all([
        storage.getDecks(),
        storage.getCards(),
        storage.getReviewLogs(),
        storage.getStreak(),
        storage.getSettings(),
        storage.getSessionCount(),
        loadEntitlements(),
        storage.getSchemaVersion(),
      ]);

      if (!active) return;

      let initialDecks = savedDecks;
      let initialCards = savedCards;

      // Primeiro uso: monta o baralho de exemplo.
      if (schemaVersion === 0 && savedDecks.length === 0) {
        const starter = createStarterContent();
        initialDecks = [starter.deck];
        initialCards = starter.cards;
        await Promise.all([
          storage.saveDecks(initialDecks),
          storage.saveCards(initialCards),
          storage.setSchemaVersion(SCHEMA_VERSION),
        ]);
      } else if (schemaVersion !== SCHEMA_VERSION) {
        await storage.setSchemaVersion(SCHEMA_VERSION);
      }

      // A ofensiva pode ter vencido enquanto o app estava fechado.
      const baseStreak = savedStreak ?? createStreakState();
      const reconciled = reconcileStreak(baseStreak);

      decksRef.current = initialDecks;
      cardsRef.current = initialCards;
      logsRef.current = savedLogs;
      streakRef.current = reconciled;

      setDecks(initialDecks);
      setCards(initialCards);
      setLogs(savedLogs);
      setStreak(reconciled);
      setSettings({ ...DEFAULT_SETTINGS, ...(savedSettings ?? {}) });
      setSessionCount(savedSessions);
      setEntitlements(savedEntitlements);
      setReady(true);

      if (reconciled !== baseStreak) await storage.saveStreak(reconciled);
    })();
    return () => {
      active = false;
    };
  }, []);

  const premium = entitlements.premium;

  const canAddDeck = useCallback((): LimitCheck => {
    if (premium || decksRef.current.length < FREE_LIMITS.decks) return { allowed: true };
    return {
      allowed: false,
      reason: `O plano gratuito permite ${FREE_LIMITS.decks} baralhos. Assine para criar quantos quiser.`,
    };
  }, [premium]);

  const canAddCard = useCallback(
    (deckId: string): LimitCheck => {
      if (premium) return { allowed: true };
      const count = cardsRef.current.filter((card) => card.deckId === deckId).length;
      if (count < FREE_LIMITS.cardsPerDeck) return { allowed: true };
      return {
        allowed: false,
        reason: `O plano gratuito permite ${FREE_LIMITS.cardsPerDeck} cards por baralho. Assine para não ter limite.`,
      };
    },
    [premium]
  );

  const addDeck = useCallback(
    async (input: Parameters<typeof createDeck>[0]) => {
      const deck = createDeck(input);
      await commitDecks([...decksRef.current, deck]);
      return deck;
    },
    [commitDecks]
  );

  const editDeck = useCallback(
    async (deckId: string, patch: Partial<Deck>) => {
      const next = decksRef.current.map((deck) =>
        deck.id === deckId ? { ...deck, ...patch, id: deck.id, updatedAt: Date.now() } : deck
      );
      await commitDecks(next);
    },
    [commitDecks]
  );

  const removeDeck = useCallback(
    async (deckId: string) => {
      // Os áudios dos cards do baralho vão junto, senão ficam ocupando espaço.
      cardsRef.current
        .filter((card) => card.deckId === deckId)
        .forEach((card) => {
          deleteAudio(card.frontAudio);
          deleteAudio(card.backAudio);
        });

      await commitDecks(decksRef.current.filter((deck) => deck.id !== deckId));
      await commitCards(cardsRef.current.filter((card) => card.deckId !== deckId));
      await commitLogs(logsRef.current.filter((log) => log.deckId !== deckId));
    },
    [commitCards, commitDecks, commitLogs]
  );

  const addCard = useCallback(
    async (input: Parameters<typeof createCard>[0]) => {
      const card = createCard(input);
      await commitCards([...cardsRef.current, card]);
      return card;
    },
    [commitCards]
  );

  const editCard = useCallback(
    async (cardId: string, patch: Partial<Card>) => {
      const next = cardsRef.current.map((card) =>
        card.id === cardId ? { ...card, ...patch, id: card.id, updatedAt: Date.now() } : card
      );
      await commitCards(next);
    },
    [commitCards]
  );

  const removeCard = useCallback(
    async (cardId: string) => {
      const card = cardsRef.current.find((item) => item.id === cardId);
      if (card) {
        deleteAudio(card.frontAudio);
        deleteAudio(card.backAudio);
      }
      await commitCards(cardsRef.current.filter((item) => item.id !== cardId));
    },
    [commitCards]
  );

  const toggleSuspend = useCallback(
    async (cardId: string) => {
      const next = cardsRef.current.map((card) =>
        card.id === cardId ? { ...card, suspended: !card.suspended, updatedAt: Date.now() } : card
      );
      await commitCards(next);
    },
    [commitCards]
  );

  const resetCardProgress = useCallback(
    async (cardId: string) => {
      const { createSrsState } = await import('../srs/scheduler');
      const next = cardsRef.current.map((card) =>
        card.id === cardId ? { ...card, srs: createSrsState(), updatedAt: Date.now() } : card
      );
      await commitCards(next);
    },
    [commitCards]
  );

  const getQueue = useCallback((deckId: string): Card[] => {
    const deck = decksRef.current.find((item) => item.id === deckId);
    if (!deck) return [];
    return buildQueue(deck, cardsRef.current, logsRef.current);
  }, []);

  const getDeckStats = useCallback(
    (deckId: string): DeckStats => {
      const deck = decksRef.current.find((item) => item.id === deckId);
      if (!deck) {
        return {
          total: 0,
          newAvailable: 0,
          learningDue: 0,
          reviewDue: 0,
          readyNow: 0,
          scheduled: 0,
          suspended: 0,
        };
      }
      return computeDeckStats(deck, cardsRef.current, logsRef.current);
    },
    // `cards` e `logs` entram na dependência para que a tela recalcule quando
    // o estado muda, mesmo lendo os refs por dentro.
    [cards, logs, decks] // eslint-disable-line react-hooks/exhaustive-deps
  );

  const answer = useCallback(
    async (cardId: string, grade: Grade, elapsedMs: number): Promise<AnswerOutcome | null> => {
      const card = cardsRef.current.find((item) => item.id === cardId);
      if (!card) return null;

      const now = Date.now();
      const nextSrs = schedule(card.srs, grade, { now });
      const updated: Card = { ...card, srs: nextSrs, updatedAt: now };

      const log: ReviewLog = {
        id: createId('r'),
        cardId: card.id,
        deckId: card.deckId,
        grade,
        previousState: card.srs.state,
        intervalDays: nextSrs.intervalDays,
        elapsedMs,
        reviewedAt: now,
      };

      await commitCards(cardsRef.current.map((item) => (item.id === cardId ? updated : item)));
      await commitLogs([...logsRef.current, log]);

      return { card: updated, log };
    },
    [commitCards, commitLogs]
  );

  const finishSession = useCallback(
    async (
      result: Omit<SessionResult, 'streakBefore' | 'streakAfter' | 'goalReached'>
    ): Promise<SessionResult> => {
      const before = streakRef.current;
      const update = registerReviews(before, result.total);
      await commitStreak(update.streak);

      const nextCount = sessionCount + 1;
      setSessionCount(nextCount);
      await storage.setSessionCount(nextCount);

      return {
        ...result,
        streakBefore: before.current,
        streakAfter: update.streak.current,
        goalReached: update.goalJustReached,
      };
    },
    [commitStreak, sessionCount]
  );

  const getForecast = useCallback(
    (days = 7) => forecast(cardsRef.current, days),
    [cards] // eslint-disable-line react-hooks/exhaustive-deps
  );

  const setDailyGoal = useCallback(
    async (goal: number) => {
      await commitStreak({ ...streakRef.current, dailyGoal: Math.max(5, Math.round(goal)) });
    },
    [commitStreak]
  );

  const addFreeze = useCallback(
    async (amount = 1) => {
      await commitStreak(grantFreeze(streakRef.current, amount));
    },
    [commitStreak]
  );

  const updateSettings = useCallback(
    async (patch: Partial<Settings>) => {
      const next = { ...settings, ...patch };
      setSettings(next);
      await storage.saveSettings(next);
    },
    [settings]
  );

  const buyPremium = useCallback(async (productId: string) => {
    setEntitlements(await purchase(productId));
  }, []);

  const restorePremium = useCallback(async () => {
    const restored = await restore();
    setEntitlements(restored);
    return restored.premium;
  }, []);

  const wipeAllData = useCallback(async () => {
    cardsRef.current.forEach((card) => {
      deleteAudio(card.frontAudio);
      deleteAudio(card.backAudio);
    });
    await storage.clearAll();

    const fresh = createStreakState();
    decksRef.current = [];
    cardsRef.current = [];
    logsRef.current = [];
    streakRef.current = fresh;

    setDecks([]);
    setCards([]);
    setLogs([]);
    setStreak(fresh);
    setSessionCount(0);
    setEntitlements(FREE_ENTITLEMENTS);
    setSettings(DEFAULT_SETTINGS);
    await storage.setSchemaVersion(SCHEMA_VERSION);
  }, []);

  const value = useMemo<AppContextValue>(
    () => ({
      ready,
      decks,
      cards,
      logs,
      streak,
      entitlements,
      settings,
      sessionCount,
      premium,
      addDeck,
      editDeck,
      removeDeck,
      addCard,
      editCard,
      removeCard,
      toggleSuspend,
      resetCardProgress,
      getQueue,
      getDeckStats,
      answer,
      finishSession,
      getForecast,
      setDailyGoal,
      addFreeze,
      updateSettings,
      buyPremium,
      restorePremium,
      canAddDeck,
      canAddCard,
      canAttachBothAudios: premium || FREE_LIMITS.audioSides > 1,
      wipeAllData,
    }),
    [
      ready,
      decks,
      cards,
      logs,
      streak,
      entitlements,
      settings,
      sessionCount,
      premium,
      addDeck,
      editDeck,
      removeDeck,
      addCard,
      editCard,
      removeCard,
      toggleSuspend,
      resetCardProgress,
      getQueue,
      getDeckStats,
      answer,
      finishSession,
      getForecast,
      setDailyGoal,
      addFreeze,
      updateSettings,
      buyPremium,
      restorePremium,
      canAddDeck,
      canAddCard,
      wipeAllData,
    ]
  );

  return <AppContext.Provider value={value}>{children}</AppContext.Provider>;
}

export function useApp(): AppContextValue {
  const context = useContext(AppContext);
  if (!context) throw new Error('useApp precisa estar dentro de <AppProvider>.');
  return context;
}
