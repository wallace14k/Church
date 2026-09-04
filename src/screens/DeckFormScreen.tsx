import React, { useState } from 'react';
import { Alert, Pressable, ScrollView, StyleSheet, Text, View } from 'react-native';
import { useNavigation, useRoute, type RouteProp } from '@react-navigation/native';
import type { NativeStackNavigationProp } from '@react-navigation/native-stack';

import { Button } from '../components/Button';
import { Screen } from '../components/Screen';
import { TextField } from '../components/TextField';
import { useApp } from '../store/AppContext';
import { colors, deckColors, deckEmojis, radius, spacing, typography } from '../theme';
import type { RootStackParamList } from '../navigation/types';

type Nav = NativeStackNavigationProp<RootStackParamList, 'DeckForm'>;
type Route = RouteProp<RootStackParamList, 'DeckForm'>;

/** Criação e edição de baralho. */
export function DeckFormScreen() {
  const navigation = useNavigation<Nav>();
  const { params } = useRoute<Route>();
  const { decks, addDeck, editDeck, removeDeck } = useApp();

  const existing = params.deckId ? decks.find((deck) => deck.id === params.deckId) : undefined;

  const [name, setName] = useState(existing?.name ?? '');
  const [description, setDescription] = useState(existing?.description ?? '');
  const [emoji, setEmoji] = useState(existing?.emoji ?? deckEmojis[0]);
  const [color, setColor] = useState<string>(existing?.color ?? deckColors[0]);
  const [newPerDay, setNewPerDay] = useState(String(existing?.newPerDay ?? 15));
  const [saving, setSaving] = useState(false);

  const trimmedName = name.trim();

  const save = async () => {
    if (!trimmedName) {
      Alert.alert('Falta o nome', 'Dê um nome ao baralho para continuar.');
      return;
    }
    setSaving(true);
    try {
      const parsed = Number.parseInt(newPerDay, 10);
      const patch = {
        name: trimmedName,
        description: description.trim(),
        emoji,
        color,
        newPerDay: Number.isFinite(parsed) ? Math.min(200, Math.max(1, parsed)) : 15,
      };

      if (existing) {
        await editDeck(existing.id, patch);
        navigation.goBack();
      } else {
        const deck = await addDeck(patch);
        // Depois de criar, o usuário quer adicionar cards: vai direto ao baralho.
        navigation.replace('DeckDetail', { deckId: deck.id });
      }
    } finally {
      setSaving(false);
    }
  };

  const confirmDelete = () => {
    if (!existing) return;
    Alert.alert(
      'Apagar baralho',
      `"${existing.name}", todos os seus cards e áudios serão apagados deste aparelho. Não dá para desfazer.`,
      [
        { text: 'Cancelar', style: 'cancel' },
        {
          text: 'Apagar',
          style: 'destructive',
          onPress: async () => {
            await removeDeck(existing.id);
            navigation.navigate('Main');
          },
        },
      ]
    );
  };

  return (
    <Screen edges={['bottom']}>
      <ScrollView contentContainerStyle={styles.content} keyboardShouldPersistTaps="handled">
        <TextField
          label="Nome"
          placeholder="Ex.: Espanhol — verbos irregulares"
          value={name}
          onChangeText={setName}
          maxLength={60}
          showCounter
          autoFocus={!existing}
        />

        <TextField
          label="Descrição (opcional)"
          placeholder="Do que trata este baralho?"
          value={description}
          onChangeText={setDescription}
          maxLength={140}
          multiline
        />

        <View style={styles.section}>
          <Text style={styles.sectionLabel}>Ícone</Text>
          <View style={styles.emojiGrid}>
            {deckEmojis.map((item) => (
              <Pressable
                key={item}
                onPress={() => setEmoji(item)}
                style={[styles.emojiOption, emoji === item && styles.emojiSelected]}
                accessibilityRole="button"
                accessibilityState={{ selected: emoji === item }}
              >
                <Text style={styles.emojiText}>{item}</Text>
              </Pressable>
            ))}
          </View>
        </View>

        <View style={styles.section}>
          <Text style={styles.sectionLabel}>Cor</Text>
          <View style={styles.colorRow}>
            {deckColors.map((item) => (
              <Pressable
                key={item}
                onPress={() => setColor(item)}
                style={[
                  styles.colorOption,
                  { backgroundColor: item },
                  color === item && styles.colorSelected,
                ]}
                accessibilityRole="button"
                accessibilityState={{ selected: color === item }}
              />
            ))}
          </View>
        </View>

        <TextField
          label="Cards novos por dia"
          hint="Quantos cards inéditos o app introduz por dia neste baralho. Revisões vencidas não contam."
          value={newPerDay}
          onChangeText={setNewPerDay}
          keyboardType="number-pad"
          maxLength={3}
        />

        <Button
          label={existing ? 'Salvar alterações' : 'Criar baralho'}
          onPress={() => void save()}
          loading={saving}
          size="lg"
          fullWidth
        />

        {existing ? (
          <Button label="Apagar baralho" onPress={confirmDelete} variant="danger" fullWidth />
        ) : null}
      </ScrollView>
    </Screen>
  );
}

const styles = StyleSheet.create({
  content: { padding: spacing.lg, gap: spacing.lg, paddingBottom: spacing.xxl },
  section: { gap: spacing.sm },
  sectionLabel: { ...typography.caption, color: colors.textMuted },
  emojiGrid: { flexDirection: 'row', flexWrap: 'wrap', gap: spacing.sm },
  emojiOption: {
    width: 46,
    height: 46,
    borderRadius: radius.md,
    backgroundColor: colors.surface,
    alignItems: 'center',
    justifyContent: 'center',
    borderWidth: 2,
    borderColor: 'transparent',
  },
  emojiSelected: { borderColor: colors.primary, backgroundColor: colors.primarySoft },
  emojiText: { fontSize: 22 },
  colorRow: { flexDirection: 'row', flexWrap: 'wrap', gap: spacing.md },
  colorOption: { width: 38, height: 38, borderRadius: 19, borderWidth: 3, borderColor: 'transparent' },
  colorSelected: { borderColor: colors.text },
});
