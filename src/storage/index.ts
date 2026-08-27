import AsyncStorage from '@react-native-async-storage/async-storage';

import type {
  AuthUser,
  Card,
  Deck,
  Entitlements,
  ReviewLog,
  Settings,
  StreakState,
} from '../types';

/**
 * Camada de persistência local.
 *
 * Um valor por chave no AsyncStorage, serializado em JSON. É suficiente para
 * a primeira versão (tudo offline, no aparelho) e mantém a porta aberta para
 * trocar por SQLite ou por uma API remota sem tocar no restante do app: basta
 * reimplementar as funções exportadas aqui.
 */

const KEYS = {
  schemaVersion: '@linguacards/schema-version',
  decks: '@linguacards/decks',
  cards: '@linguacards/cards',
  logs: '@linguacards/review-logs',
  streak: '@linguacards/streak',
  entitlements: '@linguacards/entitlements',
  user: '@linguacards/user',
  settings: '@linguacards/settings',
  sessionCount: '@linguacards/session-count',
} as const;

export const SCHEMA_VERSION = 1;

/** Quantos registros de revisão são mantidos no aparelho. */
const MAX_REVIEW_LOGS = 5000;

async function readJson<T>(key: string, fallback: T): Promise<T> {
  try {
    const raw = await AsyncStorage.getItem(key);
    if (raw == null) return fallback;
    return JSON.parse(raw) as T;
  } catch {
    // Um valor corrompido não deve derrubar o app: volta ao padrão.
    return fallback;
  }
}

async function writeJson(key: string, value: unknown): Promise<void> {
  await AsyncStorage.setItem(key, JSON.stringify(value));
}

export const storage = {
  async getSchemaVersion(): Promise<number> {
    return readJson<number>(KEYS.schemaVersion, 0);
  },
  async setSchemaVersion(version: number): Promise<void> {
    await writeJson(KEYS.schemaVersion, version);
  },

  async getDecks(): Promise<Deck[]> {
    return readJson<Deck[]>(KEYS.decks, []);
  },
  async saveDecks(decks: Deck[]): Promise<void> {
    await writeJson(KEYS.decks, decks);
  },

  async getCards(): Promise<Card[]> {
    return readJson<Card[]>(KEYS.cards, []);
  },
  async saveCards(cards: Card[]): Promise<void> {
    await writeJson(KEYS.cards, cards);
  },

  async getReviewLogs(): Promise<ReviewLog[]> {
    return readJson<ReviewLog[]>(KEYS.logs, []);
  },
  async saveReviewLogs(logs: ReviewLog[]): Promise<void> {
    // Mantém apenas os mais recentes para o arquivo não crescer sem limite.
    const trimmed = logs.length > MAX_REVIEW_LOGS ? logs.slice(-MAX_REVIEW_LOGS) : logs;
    await writeJson(KEYS.logs, trimmed);
  },

  async getStreak(): Promise<StreakState | null> {
    return readJson<StreakState | null>(KEYS.streak, null);
  },
  async saveStreak(streak: StreakState): Promise<void> {
    await writeJson(KEYS.streak, streak);
  },

  async getEntitlements(): Promise<Entitlements | null> {
    return readJson<Entitlements | null>(KEYS.entitlements, null);
  },
  async saveEntitlements(entitlements: Entitlements): Promise<void> {
    await writeJson(KEYS.entitlements, entitlements);
  },

  async getUser(): Promise<AuthUser | null> {
    return readJson<AuthUser | null>(KEYS.user, null);
  },
  async saveUser(user: AuthUser | null): Promise<void> {
    if (user == null) {
      await AsyncStorage.removeItem(KEYS.user);
      return;
    }
    await writeJson(KEYS.user, user);
  },

  async getSettings(): Promise<Settings | null> {
    return readJson<Settings | null>(KEYS.settings, null);
  },
  async saveSettings(settings: Settings): Promise<void> {
    await writeJson(KEYS.settings, settings);
  },

  async getSessionCount(): Promise<number> {
    return readJson<number>(KEYS.sessionCount, 0);
  },
  async setSessionCount(count: number): Promise<void> {
    await writeJson(KEYS.sessionCount, count);
  },

  /** Apaga os dados de estudo. Usado em "Apagar todos os dados". */
  async clearAll(): Promise<void> {
    await AsyncStorage.multiRemove(Object.values(KEYS));
  },
};
