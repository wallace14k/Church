/**
 * Modelo de dados do LinguaCards.
 *
 * Tudo é persistido localmente (AsyncStorage + arquivos de áudio no
 * diretório de documentos do app). A modelagem já foi pensada para uma
 * futura sincronização: todo registro tem `id`, `createdAt` e `updatedAt`.
 */

/** Como o usuário avaliou o card. É o que alimenta o agendamento. */
export type Grade = 'forgot' | 'partial' | 'known';

/** Estágio do card dentro do fluxo de repetição espaçada. */
export type CardState = 'new' | 'learning' | 'review' | 'relearning';

export interface SrsState {
  state: CardState;
  /** Momento (epoch ms) em que o card deve reaparecer. */
  due: number;
  /** Intervalo atual em dias (0 enquanto o card está em aprendizado). */
  intervalDays: number;
  /** Fator de facilidade, estilo SM-2. Entre EASE_MIN e EASE_MAX. */
  ease: number;
  /** Índice do passo de aprendizado/reaprendizado atual. */
  step: number;
  /** Total de revisões feitas. */
  reps: number;
  /** Quantas vezes o usuário esqueceu um card que já estava em revisão. */
  lapses: number;
  /** Intervalo guardado para quando o card sair do reaprendizado. */
  pendingIntervalDays: number;
  lastReviewedAt: number | null;
}

export interface CardAudio {
  /** URI local (file://) dentro do diretório de áudios do app. */
  uri: string;
  durationMs: number;
  /** Nome amigável, útil quando o áudio veio de um arquivo importado. */
  label?: string;
  source: 'recording' | 'import';
}

export interface Card {
  id: string;
  deckId: string;
  /** Frente: a pergunta / palavra / expressão. */
  front: string;
  /** Verso: a resposta / tradução. */
  back: string;
  /** Dica opcional exibida antes da resposta. */
  hint?: string;
  /** Exemplo de diálogo/contexto, exibido junto da resposta. */
  example?: string;
  /** Áudio da frente — normalmente a pronúncia do termo. */
  frontAudio?: CardAudio | null;
  /** Áudio do verso — normalmente o diálogo completo. */
  backAudio?: CardAudio | null;
  tags: string[];
  suspended: boolean;
  srs: SrsState;
  createdAt: number;
  updatedAt: number;
}

export interface Deck {
  id: string;
  name: string;
  description: string;
  emoji: string;
  color: string;
  /** Limite de cards novos introduzidos por dia. */
  newPerDay: number;
  /** Limite de revisões por dia (0 = sem limite). */
  reviewsPerDay: number;
  createdAt: number;
  updatedAt: number;
}

/** Uma resposta individual registrada durante um treino. */
export interface ReviewLog {
  id: string;
  cardId: string;
  deckId: string;
  grade: Grade;
  /** Estado do card antes da resposta. */
  previousState: CardState;
  /** Intervalo aplicado após a resposta, em dias. */
  intervalDays: number;
  /** Tempo gasto no card, em ms. */
  elapsedMs: number;
  reviewedAt: number;
}

export interface StreakState {
  /** Ofensiva atual, em dias. */
  current: number;
  /** Maior ofensiva já alcançada. */
  longest: number;
  /** Último dia (YYYY-MM-DD local) em que a meta diária foi batida. */
  lastGoalDay: string | null;
  /** Último dia em que houve qualquer revisão. */
  lastStudyDay: string | null;
  /** Protetores de ofensiva disponíveis. */
  freezes: number;
  /** Meta diária de cards. */
  dailyGoal: number;
  /** Revisões por dia: 'YYYY-MM-DD' -> quantidade. */
  history: Record<string, number>;
}

export interface Entitlements {
  premium: boolean;
  /** Identificador do produto comprado, quando houver. */
  productId: string | null;
  /** Epoch ms. `null` para compra vitalícia. */
  expiresAt: number | null;
  purchasedAt: number | null;
}

export interface AuthUser {
  id: string;
  provider: 'google' | 'apple' | 'guest';
  name: string | null;
  email: string | null;
  avatarUrl: string | null;
  createdAt: number;
}

export interface Settings {
  /** Toca o áudio da frente automaticamente ao abrir o card. */
  autoPlayFrontAudio: boolean;
  /** Toca o áudio do verso automaticamente ao revelar a resposta. */
  autoPlayBackAudio: boolean;
  hapticsEnabled: boolean;
  /** Lembrete diário de estudo. */
  reminderEnabled: boolean;
  /** Horário do lembrete no formato 'HH:mm'. */
  reminderTime: string;
  /** Mostra o intervalo previsto em cada botão de resposta. */
  showNextInterval: boolean;
}

/** Resultado consolidado de um treino, usado na tela de resumo. */
export interface SessionResult {
  deckId: string;
  deckName: string;
  startedAt: number;
  finishedAt: number;
  total: number;
  known: number;
  partial: number;
  forgot: number;
  /** Cards que foram vistos pela primeira vez. */
  newCards: number;
  streakBefore: number;
  streakAfter: number;
  goalReached: boolean;
}
