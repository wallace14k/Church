import React, { useEffect, useLayoutEffect, useRef, useState } from 'react';
import {
  Alert,
  KeyboardAvoidingView,
  Platform,
  Pressable,
  ScrollView,
  StyleSheet,
  Text,
  View,
} from 'react-native';
import { Ionicons } from '@expo/vector-icons';
import { useNavigation, useRoute, type RouteProp } from '@react-navigation/native';
import type { NativeStackNavigationProp } from '@react-navigation/native-stack';

import { AudioAttachmentField } from '../components/AudioAttachmentField';
import { Button } from '../components/Button';
import { Screen } from '../components/Screen';
import { TextField } from '../components/TextField';
import { useAudioAttachment } from '../audio/useAudioAttachment';
import { useApp } from '../store/AppContext';
import { colors, radius, spacing, typography } from '../theme';
import type { RootStackParamList } from '../navigation/types';

type Nav = NativeStackNavigationProp<RootStackParamList, 'CardForm'>;
type Route = RouteProp<RootStackParamList, 'CardForm'>;

/**
 * Criação e edição de card, incluindo os anexos de áudio de cada lado —
 * frente (pronúncia) e verso (diálogo).
 */
export function CardFormScreen() {
  const navigation = useNavigation<Nav>();
  const { params } = useRoute<Route>();
  const { cards, addCard, editCard, removeCard, resetCardProgress, canAttachBothAudios } = useApp();

  const existing = params.cardId ? cards.find((card) => card.id === params.cardId) : undefined;

  const [front, setFront] = useState(existing?.front ?? '');
  const [back, setBack] = useState(existing?.back ?? '');
  const [hint, setHint] = useState(existing?.hint ?? '');
  const [example, setExample] = useState(existing?.example ?? '');
  const [showExtras, setShowExtras] = useState(Boolean(existing?.hint || existing?.example));
  const [saving, setSaving] = useState(false);

  const frontAudio = useAudioAttachment(existing?.frontAudio);
  const backAudio = useAudioAttachment(existing?.backAudio);

  // Sair sem salvar não pode deixar as gravações desta edição no disco.
  const saved = useRef(false);
  const rollbackRef = useRef({ front: frontAudio.rollback, back: backAudio.rollback });
  rollbackRef.current = { front: frontAudio.rollback, back: backAudio.rollback };

  useEffect(
    () => () => {
      if (saved.current) return;
      rollbackRef.current.front();
      rollbackRef.current.back();
    },
    []
  );

  useLayoutEffect(() => {
    navigation.setOptions({ title: existing ? 'Editar card' : 'Novo card' });
  }, [navigation, existing]);

  const save = async () => {
    const trimmedFront = front.trim();
    const trimmedBack = back.trim();

    if (!trimmedFront || !trimmedBack) {
      Alert.alert('Card incompleto', 'Preencha a frente e o verso do card.');
      return;
    }

    setSaving(true);
    try {
      const payload = {
        front: trimmedFront,
        back: trimmedBack,
        hint: hint.trim(),
        example: example.trim(),
        frontAudio: frontAudio.commit(),
        backAudio: canAttachBothAudios ? backAudio.commit() : (existing?.backAudio ?? null),
      };

      if (existing) {
        await editCard(existing.id, payload);
      } else {
        await addCard({ deckId: params.deckId, ...payload });
      }
      saved.current = true;
      navigation.goBack();
    } finally {
      setSaving(false);
    }
  };

  const confirmDelete = () => {
    if (!existing) return;
    Alert.alert('Apagar card', 'O card e os áudios anexados serão apagados.', [
      { text: 'Cancelar', style: 'cancel' },
      {
        text: 'Apagar',
        style: 'destructive',
        onPress: async () => {
          // O card já leva os áudios embora; não há o que desfazer na saída.
          saved.current = true;
          await removeCard(existing.id);
          navigation.goBack();
        },
      },
    ]);
  };

  const confirmReset = () => {
    if (!existing) return;
    Alert.alert(
      'Recomeçar este card',
      'O card volta a ser tratado como novo e reentra no ciclo de aprendizado desde o início.',
      [
        { text: 'Cancelar', style: 'cancel' },
        { text: 'Recomeçar', onPress: () => void resetCardProgress(existing.id) },
      ]
    );
  };

  return (
    <Screen edges={['bottom']}>
      <KeyboardAvoidingView
        style={styles.flex}
        behavior={Platform.OS === 'ios' ? 'padding' : undefined}
        keyboardVerticalOffset={90}
      >
        <ScrollView contentContainerStyle={styles.content} keyboardShouldPersistTaps="handled">
          <TextField
            label="Frente"
            placeholder="A palavra, frase ou pergunta"
            value={front}
            onChangeText={setFront}
            multiline
            maxLength={300}
            showCounter
            autoFocus={!existing}
          />

          <AudioAttachmentField
            title="Áudio da frente"
            description="Grave a pronúncia do termo para treinar o ouvido antes de ver a resposta."
            attachment={frontAudio}
          />

          <TextField
            label="Verso"
            placeholder="A tradução ou resposta"
            value={back}
            onChangeText={setBack}
            multiline
            maxLength={300}
            showCounter
          />

          <AudioAttachmentField
            title="Áudio do verso"
            description="Anexe o diálogo completo — é aqui que entra a fala em contexto do card de idioma."
            attachment={backAudio}
            locked={!canAttachBothAudios}
            onLockedPress={() => navigation.navigate('Paywall', { source: 'audio' })}
          />

          <Pressable onPress={() => setShowExtras((current) => !current)} style={styles.toggle}>
            <Ionicons
              name={showExtras ? 'chevron-down' : 'chevron-forward'}
              size={16}
              color={colors.textMuted}
            />
            <Text style={styles.toggleLabel}>Dica e exemplo (opcionais)</Text>
          </Pressable>

          {showExtras ? (
            <View style={styles.extras}>
              <TextField
                label="Dica"
                placeholder="Aparece antes da resposta, quando você trava"
                value={hint}
                onChangeText={setHint}
                maxLength={140}
              />
              <TextField
                label="Exemplo / diálogo"
                placeholder={'— How have you been?\n— Pretty good, thanks.'}
                value={example}
                onChangeText={setExample}
                multiline
                maxLength={500}
              />
            </View>
          ) : null}

          <Button
            label={existing ? 'Salvar card' : 'Adicionar card'}
            onPress={() => void save()}
            loading={saving}
            size="lg"
            fullWidth
          />

          {existing ? (
            <View style={styles.dangerZone}>
              <Button label="Recomeçar progresso" onPress={confirmReset} variant="secondary" fullWidth />
              <Button label="Apagar card" onPress={confirmDelete} variant="danger" fullWidth />
            </View>
          ) : null}
        </ScrollView>
      </KeyboardAvoidingView>
    </Screen>
  );
}

const styles = StyleSheet.create({
  flex: { flex: 1 },
  content: { padding: spacing.lg, gap: spacing.lg, paddingBottom: spacing.xxl },
  toggle: { flexDirection: 'row', alignItems: 'center', gap: spacing.sm, paddingVertical: spacing.xs },
  toggleLabel: { ...typography.caption, color: colors.textMuted },
  extras: {
    gap: spacing.lg,
    backgroundColor: colors.bgElevated,
    borderRadius: radius.md,
    padding: spacing.md,
  },
  dangerZone: { gap: spacing.sm, marginTop: spacing.sm },
});
