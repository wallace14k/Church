import React, { useCallback, useEffect, useLayoutEffect, useMemo, useRef, useState } from 'react';
import { Alert, Pressable, ScrollView, StyleSheet, Text, View } from 'react-native';
import { Ionicons } from '@expo/vector-icons';
import * as Haptics from 'expo-haptics';
import { useNavigation, useRoute, type RouteProp } from '@react-navigation/native';
import type { NativeStackNavigationProp } from '@react-navigation/native-stack';

import { AudioButton } from '../components/AudioButton';
import { Button } from '../components/Button';
import { EmptyState } from '../components/EmptyState';
import { GradeButtons } from '../components/GradeButtons';
import { ProgressBar } from '../components/ProgressBar';
import { Screen } from '../components/Screen';
import { previewIntervals } from '../srs/scheduler';
import { useApp } from '../store/AppContext';
import { colors, radius, spacing, typography } from '../theme';
import { MINUTE_MS } from '../utils/date';
import type { RootStackParamList } from '../navigation/types';
import type { Card, Grade } from '../types';

type Nav = NativeStackNavigationProp<RootStackParamList, 'Study'>;
type Route = RouteProp<RootStackParamList, 'Study'>;

/**
 * Um card respondido volta ainda no mesmo treino se o agendador o marcou
 * para daqui a pouco — é o que faz "Não lembro" ter efeito imediato.
 */
const REQUEUE_WINDOW_MS = 20 * MINUTE_MS;

interface Tally {
  total: number;
  known: number;
  partial: number;
  forgot: number;
  newCards: number;
}

const EMPTY_TALLY: Tally = { total: 0, known: 0, partial: 0, forgot: 0, newCards: 0 };

export function StudyScreen() {
  const navigation = useNavigation<Nav>();
  const { params } = useRoute<Route>();
  const { decks, cards, getQueue, answer, finishSession, settings } = useApp();

  const deck = decks.find((item) => item.id === params.deckId);

  // A fila é montada uma vez, na abertura do treino, e guarda apenas ids —
  // assim cada card é sempre lido em seu estado mais recente.
  const [queue, setQueue] = useState<string[]>(() => getQueue(params.deckId).map((card) => card.id));
  const [revealed, setRevealed] = useState(false);
  const [showHint, setShowHint] = useState(false);
  const [tally, setTally] = useState<Tally>(EMPTY_TALLY);
  const [answering, setAnswering] = useState(false);

  const startedAt = useRef(Date.now());
  const cardShownAt = useRef(Date.now());
  const initialCount = useRef(queue.length);
  const finished = useRef(false);

  const currentId = queue[0];
  const current = useMemo(
    () => cards.find((card) => card.id === currentId),
    [cards, currentId]
  );

  const intervals = useMemo(
    () => (current ? previewIntervals(current.srs) : null),
    [current]
  );

  const buzz = useCallback(
    (style: Haptics.ImpactFeedbackStyle) => {
      if (!settings.hapticsEnabled) return;
      void Haptics.impactAsync(style);
    },
    [settings.hapticsEnabled]
  );

  useLayoutEffect(() => {
    navigation.setOptions({ title: deck?.name ?? 'Treino' });
  }, [navigation, deck]);

  useEffect(() => {
    cardShownAt.current = Date.now();
    setRevealed(false);
    setShowHint(false);
  }, [currentId]);

  const complete = useCallback(
    async (result: Tally) => {
      if (finished.current) return;
      finished.current = true;

      const summary = await finishSession({
        deckId: params.deckId,
        deckName: deck?.name ?? 'Baralho',
        startedAt: startedAt.current,
        finishedAt: Date.now(),
        ...result,
      });
      navigation.replace('SessionSummary', { result: summary });
    },
    [deck?.name, finishSession, navigation, params.deckId]
  );

  const handleGrade = useCallback(
    async (grade: Grade) => {
      if (!current || answering) return;
      setAnswering(true);

      buzz(
        grade === 'forgot'
          ? Haptics.ImpactFeedbackStyle.Heavy
          : Haptics.ImpactFeedbackStyle.Light
      );

      try {
        const elapsed = Date.now() - cardShownAt.current;
        const outcome = await answer(current.id, grade, elapsed);
        if (!outcome) return;

        const nextTally: Tally = {
          total: tally.total + 1,
          known: tally.known + (grade === 'known' ? 1 : 0),
          partial: tally.partial + (grade === 'partial' ? 1 : 0),
          forgot: tally.forgot + (grade === 'forgot' ? 1 : 0),
          newCards: tally.newCards + (current.srs.state === 'new' ? 1 : 0),
        };
        setTally(nextTally);

        const comesBackSoon = outcome.card.srs.due - Date.now() <= REQUEUE_WINDOW_MS;
        const rest = queue.slice(1);
        const nextQueue = comesBackSoon ? requeue(rest, current.id) : rest;
        setQueue(nextQueue);

        if (nextQueue.length === 0) {
          await complete(nextTally);
        }
      } finally {
        setAnswering(false);
      }
    },
    [answer, answering, buzz, complete, current, queue, tally]
  );

  const confirmExit = useCallback(() => {
    if (tally.total === 0) {
      navigation.goBack();
      return;
    }
    Alert.alert('Encerrar treino?', 'As respostas já dadas ficam salvas.', [
      { text: 'Continuar treinando', style: 'cancel' },
      { text: 'Encerrar', onPress: () => void complete(tally) },
    ]);
  }, [complete, navigation, tally]);

  useLayoutEffect(() => {
    navigation.setOptions({
      headerLeft: () => (
        <Pressable onPress={confirmExit} hitSlop={10} accessibilityLabel="Encerrar treino">
          <Ionicons name="close" size={24} color={colors.text} />
        </Pressable>
      ),
    });
  }, [navigation, confirmExit]);

  if (!current) {
    return (
      <Screen edges={['bottom']}>
        <EmptyState
          icon="checkmark-done-outline"
          title="Nada para revisar agora"
          description="Todos os cards deste baralho estão em dia. Volte quando os próximos vencerem."
          actionLabel="Voltar"
          onAction={() => navigation.goBack()}
        />
      </Screen>
    );
  }

  const done = initialCount.current - queue.length;
  const progress = initialCount.current > 0 ? done / initialCount.current : 0;

  return (
    <Screen edges={['bottom']}>
      <View style={styles.progressArea}>
        <ProgressBar progress={progress} />
        <View style={styles.progressRow}>
          <Text style={styles.progressText}>
            {queue.length} {queue.length === 1 ? 'card restante' : 'cards restantes'}
          </Text>
          <View style={styles.tallyRow}>
            <TallyDot color={colors.forgot} value={tally.forgot} />
            <TallyDot color={colors.partial} value={tally.partial} />
            <TallyDot color={colors.known} value={tally.known} />
          </View>
        </View>
      </View>

      <ScrollView contentContainerStyle={styles.cardArea}>
        <View style={styles.faceCard}>
          {current.srs.state === 'new' ? (
            <View style={styles.newBadge}>
              <Text style={styles.newBadgeText}>NOVO</Text>
            </View>
          ) : null}

          <Text style={styles.front}>{current.front}</Text>

          {current.frontAudio ? (
            <AudioButton
              audio={current.frontAudio}
              label="Pronúncia"
              autoPlay={settings.autoPlayFrontAudio}
            />
          ) : null}

          {!revealed && current.hint ? (
            showHint ? (
              <View style={styles.hintBox}>
                <Ionicons name="bulb-outline" size={15} color={colors.partial} />
                <Text style={styles.hintText}>{current.hint}</Text>
              </View>
            ) : (
              <Pressable onPress={() => setShowHint(true)} style={styles.hintToggle}>
                <Ionicons name="bulb-outline" size={15} color={colors.textMuted} />
                <Text style={styles.hintToggleText}>Ver dica</Text>
              </Pressable>
            )
          ) : null}
        </View>

        {revealed ? (
          <View style={styles.answerCard}>
            <Text style={styles.answerLabel}>Resposta</Text>
            <Text style={styles.back}>{current.back}</Text>

            {current.backAudio ? (
              <AudioButton
                audio={current.backAudio}
                label="Diálogo"
                autoPlay={settings.autoPlayBackAudio}
              />
            ) : null}

            {current.example ? (
              <View style={styles.exampleBox}>
                <Text style={styles.exampleText}>{current.example}</Text>
              </View>
            ) : null}
          </View>
        ) : null}
      </ScrollView>

      <View style={styles.footer}>
        {revealed && intervals ? (
          <GradeButtons
            intervals={intervals}
            onGrade={(grade) => void handleGrade(grade)}
            showIntervals={settings.showNextInterval}
            disabled={answering}
          />
        ) : (
          <Button
            label="Mostrar resposta"
            onPress={() => {
              buzz(Haptics.ImpactFeedbackStyle.Light);
              setRevealed(true);
            }}
            size="lg"
            fullWidth
          />
        )}
      </View>
    </Screen>
  );
}

function TallyDot({ color, value }: { color: string; value: number }) {
  return (
    <View style={styles.tallyItem}>
      <View style={[styles.dot, { backgroundColor: color }]} />
      <Text style={styles.tallyValue}>{value}</Text>
    </View>
  );
}

/**
 * Reinsere um card na fila algumas posições à frente, para que ele volte
 * dentro do treino sem aparecer imediatamente depois de si mesmo.
 */
function requeue(rest: string[], cardId: string): string[] {
  const position = Math.min(rest.length, 3);
  return [...rest.slice(0, position), cardId, ...rest.slice(position)];
}

const styles = StyleSheet.create({
  progressArea: { paddingHorizontal: spacing.lg, paddingTop: spacing.sm, gap: spacing.sm },
  progressRow: { flexDirection: 'row', justifyContent: 'space-between', alignItems: 'center' },
  progressText: { ...typography.tiny, color: colors.textFaint },
  tallyRow: { flexDirection: 'row', gap: spacing.md },
  tallyItem: { flexDirection: 'row', alignItems: 'center', gap: spacing.xs },
  dot: { width: 7, height: 7, borderRadius: 4 },
  tallyValue: { ...typography.tiny, color: colors.textMuted },

  cardArea: { padding: spacing.lg, gap: spacing.md, flexGrow: 1, justifyContent: 'center' },
  faceCard: {
    backgroundColor: colors.bgElevated,
    borderRadius: radius.xl,
    borderWidth: 1,
    borderColor: colors.border,
    padding: spacing.xl,
    gap: spacing.lg,
  },
  newBadge: {
    alignSelf: 'flex-start',
    backgroundColor: colors.primarySoft,
    borderRadius: radius.sm,
    paddingHorizontal: spacing.sm,
    paddingVertical: 2,
  },
  newBadgeText: { fontSize: 10, fontWeight: '800', color: colors.primary, letterSpacing: 0.5 },
  front: { fontSize: 24, fontWeight: '600', color: colors.text, lineHeight: 32 },
  hintToggle: { flexDirection: 'row', alignItems: 'center', gap: spacing.sm, alignSelf: 'flex-start' },
  hintToggleText: { ...typography.caption, color: colors.textMuted },
  hintBox: {
    flexDirection: 'row',
    gap: spacing.sm,
    backgroundColor: colors.partialSoft,
    borderRadius: radius.md,
    padding: spacing.md,
  },
  hintText: { ...typography.caption, color: colors.text, flex: 1, lineHeight: 19 },

  answerCard: {
    backgroundColor: colors.surface,
    borderRadius: radius.xl,
    borderWidth: 1,
    borderColor: colors.borderStrong,
    padding: spacing.xl,
    gap: spacing.md,
  },
  answerLabel: { ...typography.tiny, color: colors.textFaint, letterSpacing: 0.8 },
  back: { fontSize: 21, fontWeight: '600', color: colors.text, lineHeight: 29 },
  exampleBox: {
    backgroundColor: colors.bg,
    borderRadius: radius.md,
    padding: spacing.md,
    borderLeftWidth: 3,
    borderLeftColor: colors.primary,
  },
  exampleText: { ...typography.body, color: colors.textMuted, lineHeight: 22 },

  footer: { padding: spacing.lg, paddingTop: spacing.sm, gap: spacing.sm },
});
