import React, { useMemo } from 'react';
import { Alert, FlatList, Pressable, StyleSheet, Text, View } from 'react-native';
import { Ionicons } from '@expo/vector-icons';
import { useNavigation } from '@react-navigation/native';
import type { NativeStackNavigationProp } from '@react-navigation/native-stack';

import { DeckCard } from '../components/DeckCard';
import { EmptyState } from '../components/EmptyState';
import { ProgressBar } from '../components/ProgressBar';
import { Screen } from '../components/Screen';
import { StreakBadge } from '../components/StreakBadge';
import { useApp } from '../store/AppContext';
import { goalReachedToday, reviewsToday } from '../streak';
import { colors, radius, spacing, typography } from '../theme';
import type { RootStackParamList } from '../navigation/types';

type Nav = NativeStackNavigationProp<RootStackParamList>;

/** Tela inicial: ofensiva, meta do dia e a lista de baralhos. */
export function DecksScreen() {
  const navigation = useNavigation<Nav>();
  const { decks, streak, getDeckStats, canAddDeck } = useApp();

  const today = reviewsToday(streak);
  const goalDone = goalReachedToday(streak);

  const totalReady = useMemo(
    () => decks.reduce((sum, deck) => sum + getDeckStats(deck.id).readyNow, 0),
    [decks, getDeckStats]
  );

  const handleNewDeck = () => {
    const check = canAddDeck();
    if (!check.allowed) {
      Alert.alert('Limite do plano gratuito', check.reason, [
        { text: 'Agora não', style: 'cancel' },
        {
          text: 'Ver planos',
          onPress: () => navigation.navigate('Paywall', { source: 'limite-baralhos' }),
        },
      ]);
      return;
    }
    navigation.navigate('DeckForm', {});
  };

  return (
    <Screen>
      <FlatList
        data={decks}
        keyExtractor={(deck) => deck.id}
        contentContainerStyle={styles.list}
        ListHeaderComponent={
          <View style={styles.header}>
            <View style={styles.topRow}>
              <View>
                <Text style={styles.greeting}>Seus baralhos</Text>
                <Text style={styles.subtitle}>
                  {totalReady > 0
                    ? `${totalReady} ${totalReady === 1 ? 'card espera' : 'cards esperam'} por você`
                    : 'Nada vencido agora — volte mais tarde'}
                </Text>
              </View>
              <StreakBadge
                days={streak.current}
                activeToday={goalDone}
                freezes={streak.freezes}
                onPress={() => navigation.navigate('Main')}
              />
            </View>

            <View style={styles.goalCard}>
              <View style={styles.goalRow}>
                <Text style={styles.goalLabel}>Meta de hoje</Text>
                <Text style={styles.goalValue}>
                  {Math.min(today, streak.dailyGoal)}/{streak.dailyGoal}
                </Text>
              </View>
              <ProgressBar
                progress={today / streak.dailyGoal}
                color={goalDone ? colors.known : colors.streak}
              />
              <Text style={styles.goalHint}>
                {goalDone
                  ? streak.current > 0
                    ? `Meta batida! Ofensiva de ${streak.current} ${streak.current === 1 ? 'dia' : 'dias'}.`
                    : 'Meta batida! A ofensiva começa hoje.'
                  : `Faltam ${streak.dailyGoal - today} cards para manter a ofensiva.`}
              </Text>
            </View>
          </View>
        }
        renderItem={({ item }) => (
          <DeckCard
            deck={item}
            stats={getDeckStats(item.id)}
            onPress={() => navigation.navigate('DeckDetail', { deckId: item.id })}
            onStudy={() => navigation.navigate('Study', { deckId: item.id })}
          />
        )}
        ItemSeparatorComponent={() => <View style={styles.separator} />}
        ListEmptyComponent={
          <EmptyState
            icon="albums-outline"
            title="Nenhum baralho ainda"
            description="Crie um baralho por tema ou idioma e comece a montar seus cards."
            actionLabel="Criar baralho"
            onAction={handleNewDeck}
          />
        }
      />

      <Pressable
        onPress={handleNewDeck}
        style={({ pressed }) => [styles.fab, pressed && styles.fabPressed]}
        accessibilityRole="button"
        accessibilityLabel="Criar baralho"
      >
        <Ionicons name="add" size={26} color="#FFFFFF" />
      </Pressable>
    </Screen>
  );
}

const styles = StyleSheet.create({
  list: { padding: spacing.lg, paddingBottom: 96, gap: 0 },
  header: { gap: spacing.lg, marginBottom: spacing.lg },
  topRow: { flexDirection: 'row', justifyContent: 'space-between', alignItems: 'flex-start', gap: spacing.md },
  greeting: { ...typography.h1 },
  subtitle: { ...typography.caption, color: colors.textMuted, marginTop: 2 },
  goalCard: {
    backgroundColor: colors.bgElevated,
    borderRadius: radius.lg,
    borderWidth: 1,
    borderColor: colors.border,
    padding: spacing.lg,
    gap: spacing.sm,
  },
  goalRow: { flexDirection: 'row', justifyContent: 'space-between', alignItems: 'baseline' },
  goalLabel: { ...typography.h3, fontSize: 15 },
  goalValue: { ...typography.h3, fontSize: 15, color: colors.textMuted },
  goalHint: { ...typography.tiny, color: colors.textFaint },
  separator: { height: spacing.md },
  fab: {
    position: 'absolute',
    right: spacing.lg,
    bottom: spacing.lg,
    width: 56,
    height: 56,
    borderRadius: 28,
    backgroundColor: colors.primary,
    alignItems: 'center',
    justifyContent: 'center',
    shadowColor: '#000',
    shadowOpacity: 0.35,
    shadowRadius: 12,
    shadowOffset: { width: 0, height: 6 },
    elevation: 6,
  },
  fabPressed: { opacity: 0.85 },
});
