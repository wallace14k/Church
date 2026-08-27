import React, { useLayoutEffect, useMemo } from 'react';
import { Alert, FlatList, Pressable, StyleSheet, Text, View } from 'react-native';
import { Ionicons } from '@expo/vector-icons';
import { useNavigation, useRoute, type RouteProp } from '@react-navigation/native';
import type { NativeStackNavigationProp } from '@react-navigation/native-stack';

import { Button } from '../components/Button';
import { EmptyState } from '../components/EmptyState';
import { Screen } from '../components/Screen';
import { useApp } from '../store/AppContext';
import { colors, radius, spacing, typography } from '../theme';
import { formatInterval } from '../utils/date';
import type { RootStackParamList } from '../navigation/types';
import type { Card } from '../types';

type Nav = NativeStackNavigationProp<RootStackParamList, 'DeckDetail'>;
type Route = RouteProp<RootStackParamList, 'DeckDetail'>;

const STATE_LABEL: Record<Card['srs']['state'], string> = {
  new: 'Novo',
  learning: 'Aprendendo',
  relearning: 'Reaprendendo',
  review: 'Em revisão',
};

const STATE_COLOR: Record<Card['srs']['state'], string> = {
  new: colors.primary,
  learning: colors.partial,
  relearning: colors.forgot,
  review: colors.known,
};

/** Lista os cards de um baralho e dá acesso ao treino e à edição. */
export function DeckDetailScreen() {
  const navigation = useNavigation<Nav>();
  const { params } = useRoute<Route>();
  const { decks, cards, getDeckStats, canAddCard, removeCard, toggleSuspend } = useApp();
  const deck = decks.find((item) => item.id === params.deckId);
  const stats = getDeckStats(params.deckId);

  const deckCards = useMemo(
    () =>
      cards
        .filter((card) => card.deckId === params.deckId)
        .sort((a, b) => b.updatedAt - a.updatedAt),
    [cards, params.deckId]
  );

  useLayoutEffect(() => {
    navigation.setOptions({
      title: deck?.name ?? 'Baralho',
      headerRight: () =>
        deck ? (
          <Pressable
            onPress={() => navigation.navigate('DeckForm', { deckId: deck.id })}
            hitSlop={10}
            accessibilityLabel="Editar baralho"
          >
            <Ionicons name="settings-outline" size={21} color={colors.text} />
          </Pressable>
        ) : null,
    });
  }, [navigation, deck]);

  if (!deck) {
    return (
      <Screen edges={['bottom']}>
        <EmptyState
          icon="alert-circle-outline"
          title="Baralho não encontrado"
          description="Ele pode ter sido apagado em outra tela."
        />
      </Screen>
    );
  }

  const handleNewCard = () => {
    const check = canAddCard(deck.id);
    if (!check.allowed) {
      Alert.alert('Limite do plano gratuito', check.reason, [
        { text: 'Agora não', style: 'cancel' },
        {
          text: 'Ver planos',
          onPress: () => navigation.navigate('Paywall', { source: 'limite-cards' }),
        },
      ]);
      return;
    }
    navigation.navigate('CardForm', { deckId: deck.id });
  };

  const confirmDeleteCard = (card: Card) => {
    Alert.alert('Apagar card', 'O card e os áudios anexados serão apagados.', [
      { text: 'Cancelar', style: 'cancel' },
      { text: 'Apagar', style: 'destructive', onPress: () => void removeCard(card.id) },
    ]);
  };

  return (
    <Screen edges={['bottom']}>
      <FlatList
        data={deckCards}
        keyExtractor={(card) => card.id}
        contentContainerStyle={styles.list}
        ListHeaderComponent={
          <View style={styles.header}>
            {deck.description ? <Text style={styles.description}>{deck.description}</Text> : null}

            <View style={styles.statsRow}>
              <Stat value={stats.newAvailable} label="novos hoje" color={colors.primary} />
              <Stat value={stats.learningDue} label="aprendendo" color={colors.partial} />
              <Stat value={stats.reviewDue} label="revisar" color={colors.known} />
              <Stat value={stats.scheduled} label="agendados" color={colors.textMuted} />
            </View>

            <Button
              label={stats.readyNow > 0 ? `Treinar ${stats.readyNow} cards` : 'Nada vencido agora'}
              icon={stats.readyNow > 0 ? 'play' : 'checkmark-circle-outline'}
              onPress={() => navigation.navigate('Study', { deckId: deck.id })}
              disabled={stats.readyNow === 0}
              size="lg"
              fullWidth
            />

            <Text style={styles.listTitle}>
              {deckCards.length} {deckCards.length === 1 ? 'card' : 'cards'}
            </Text>
          </View>
        }
        renderItem={({ item }) => (
          <CardRow
            card={item}
            onPress={() => navigation.navigate('CardForm', { deckId: deck.id, cardId: item.id })}
            onToggleSuspend={() => void toggleSuspend(item.id)}
            onDelete={() => confirmDeleteCard(item)}
          />
        )}
        ItemSeparatorComponent={() => <View style={styles.separator} />}
        ListEmptyComponent={
          <EmptyState
            icon="documents-outline"
            title="Baralho vazio"
            description="Adicione o primeiro card com a frente, o verso e, se quiser, o áudio do diálogo."
            actionLabel="Adicionar card"
            onAction={handleNewCard}
          />
        }
      />

      <Pressable
        onPress={handleNewCard}
        style={({ pressed }) => [styles.fab, pressed && styles.fabPressed]}
        accessibilityRole="button"
        accessibilityLabel="Adicionar card"
      >
        <Ionicons name="add" size={26} color="#FFFFFF" />
      </Pressable>
    </Screen>
  );
}

function Stat({ value, label, color }: { value: number; label: string; color: string }) {
  return (
    <View style={styles.stat}>
      <Text style={[styles.statValue, { color }]}>{value}</Text>
      <Text style={styles.statLabel}>{label}</Text>
    </View>
  );
}

function CardRow({
  card,
  onPress,
  onToggleSuspend,
  onDelete,
}: {
  card: Card;
  onPress: () => void;
  onToggleSuspend: () => void;
  onDelete: () => void;
}) {
  const hasAudio = Boolean(card.frontAudio || card.backAudio);
  const dueIn = card.srs.due - Date.now();

  return (
    <Pressable
      onPress={onPress}
      onLongPress={onDelete}
      style={({ pressed }) => [styles.card, pressed && styles.cardPressed, card.suspended && styles.cardSuspended]}
      accessibilityRole="button"
    >
      <View style={styles.cardBody}>
        <Text style={styles.cardFront} numberOfLines={1}>
          {card.front}
        </Text>
        <Text style={styles.cardBack} numberOfLines={1}>
          {card.back}
        </Text>

        <View style={styles.cardMeta}>
          <View style={[styles.badge, { backgroundColor: `${STATE_COLOR[card.srs.state]}22` }]}>
            <Text style={[styles.badgeText, { color: STATE_COLOR[card.srs.state] }]}>
              {STATE_LABEL[card.srs.state]}
            </Text>
          </View>
          {card.srs.state !== 'new' ? (
            <Text style={styles.cardDue}>
              {dueIn <= 0 ? 'pronto agora' : `volta em ${formatInterval(dueIn)}`}
            </Text>
          ) : null}
          {hasAudio ? <Ionicons name="volume-medium" size={14} color={colors.textFaint} /> : null}
        </View>
      </View>

      <Pressable
        onPress={onToggleSuspend}
        hitSlop={10}
        accessibilityRole="button"
        accessibilityLabel={card.suspended ? 'Retomar card' : 'Pausar card'}
      >
        <Ionicons
          name={card.suspended ? 'play-circle-outline' : 'pause-circle-outline'}
          size={22}
          color={colors.textFaint}
        />
      </Pressable>
    </Pressable>
  );
}

const styles = StyleSheet.create({
  list: { padding: spacing.lg, paddingBottom: 96 },
  header: { gap: spacing.lg, marginBottom: spacing.lg },
  description: { ...typography.bodyMuted, lineHeight: 21 },
  statsRow: {
    flexDirection: 'row',
    backgroundColor: colors.bgElevated,
    borderRadius: radius.lg,
    borderWidth: 1,
    borderColor: colors.border,
    padding: spacing.lg,
  },
  stat: { flex: 1, alignItems: 'center', gap: 2 },
  statValue: { fontSize: 20, fontWeight: '700' },
  statLabel: { ...typography.tiny, color: colors.textFaint, textAlign: 'center' },
  listTitle: { ...typography.caption, color: colors.textMuted },
  separator: { height: spacing.sm },
  card: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.md,
    backgroundColor: colors.bgElevated,
    borderRadius: radius.md,
    borderWidth: 1,
    borderColor: colors.border,
    padding: spacing.md,
  },
  cardPressed: { opacity: 0.75 },
  cardSuspended: { opacity: 0.55 },
  cardBody: { flex: 1, gap: 2 },
  cardFront: { ...typography.body, fontWeight: '600' },
  cardBack: { ...typography.caption, color: colors.textMuted },
  cardMeta: { flexDirection: 'row', alignItems: 'center', gap: spacing.sm, marginTop: spacing.xs },
  badge: { borderRadius: radius.sm, paddingHorizontal: spacing.sm, paddingVertical: 2 },
  badgeText: { fontSize: 10, fontWeight: '700' },
  cardDue: { ...typography.tiny, color: colors.textFaint },
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
    elevation: 6,
  },
  fabPressed: { opacity: 0.85 },
});
