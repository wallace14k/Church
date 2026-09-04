import React from 'react';
import { Pressable, StyleSheet, Text, View } from 'react-native';
import { Ionicons } from '@expo/vector-icons';

import { MAX_RECORDING_MS, useAudioAttachment } from '../audio/useAudioAttachment';
import { colors, radius, spacing, typography } from '../theme';
import { formatDuration } from '../utils/date';
import { AudioButton } from './AudioButton';

type Attachment = ReturnType<typeof useAudioAttachment>;

interface Props {
  title: string;
  description: string;
  attachment: Attachment;
  /** Quando bloqueado, o campo vira um convite para assinar. */
  locked?: boolean;
  onLockedPress?: () => void;
}

/**
 * Campo de anexo de áudio de um lado do card: grava pelo microfone ou importa
 * um arquivo. É a peça central dos cards de idioma, onde o verso costuma
 * carregar o diálogo completo.
 */
export function AudioAttachmentField({
  title,
  description,
  attachment,
  locked,
  onLockedPress,
}: Props) {
  if (locked) {
    return (
      <Pressable onPress={onLockedPress} style={styles.locked}>
        <Ionicons name="lock-closed-outline" size={18} color={colors.premium} />
        <View style={styles.lockedBody}>
          <Text style={styles.lockedTitle}>{title}</Text>
          <Text style={styles.lockedText}>
            Áudio nos dois lados do card faz parte do plano premium.
          </Text>
        </View>
        <Ionicons name="chevron-forward" size={18} color={colors.textFaint} />
      </Pressable>
    );
  }

  const { audio, isRecording, recordingMs, busy } = attachment;
  const remaining = Math.max(0, MAX_RECORDING_MS - recordingMs);

  return (
    <View style={styles.wrapper}>
      <View style={styles.header}>
        <Text style={styles.title}>{title}</Text>
        {audio ? (
          <Pressable
            onPress={attachment.remove}
            hitSlop={8}
            accessibilityRole="button"
            accessibilityLabel="Remover áudio"
          >
            <Text style={styles.remove}>Remover</Text>
          </Pressable>
        ) : null}
      </View>

      {audio ? (
        <>
          <AudioButton audio={audio} compact />
          {audio.label ? (
            <Text style={styles.fileName} numberOfLines={1}>
              {audio.label}
            </Text>
          ) : null}
        </>
      ) : (
        <Text style={styles.description}>{description}</Text>
      )}

      {isRecording ? (
        <Pressable onPress={attachment.stopRecording} style={[styles.action, styles.recording]}>
          <View style={styles.recordingDot} />
          <Text style={styles.recordingText}>
            Gravando · {formatDuration(recordingMs)}
            {remaining < 10_000 ? ` (${Math.ceil(remaining / 1000)}s restantes)` : ''}
          </Text>
          <Text style={styles.recordingStop}>Parar</Text>
        </Pressable>
      ) : (
        <View style={styles.actions}>
          <Pressable
            onPress={attachment.startRecording}
            disabled={busy}
            style={({ pressed }) => [styles.action, pressed && styles.pressed, busy && styles.busy]}
          >
            <Ionicons name="mic-outline" size={17} color={colors.text} />
            <Text style={styles.actionLabel}>{audio ? 'Regravar' : 'Gravar'}</Text>
          </Pressable>
          <Pressable
            onPress={attachment.importFile}
            disabled={busy}
            style={({ pressed }) => [styles.action, pressed && styles.pressed, busy && styles.busy]}
          >
            <Ionicons name="folder-open-outline" size={17} color={colors.text} />
            <Text style={styles.actionLabel}>Importar</Text>
          </Pressable>
        </View>
      )}
    </View>
  );
}

const styles = StyleSheet.create({
  wrapper: {
    backgroundColor: colors.bgElevated,
    borderRadius: radius.md,
    borderWidth: 1,
    borderColor: colors.border,
    padding: spacing.md,
    gap: spacing.sm,
  },
  header: { flexDirection: 'row', justifyContent: 'space-between', alignItems: 'center' },
  title: { ...typography.h3, fontSize: 15 },
  remove: { ...typography.caption, color: colors.forgot },
  description: { ...typography.caption, color: colors.textFaint, lineHeight: 18 },
  fileName: { ...typography.tiny, color: colors.textFaint },
  actions: { flexDirection: 'row', gap: spacing.sm },
  action: {
    flex: 1,
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'center',
    gap: spacing.sm,
    backgroundColor: colors.surface,
    borderRadius: radius.sm,
    paddingVertical: spacing.md,
  },
  actionLabel: { ...typography.caption, color: colors.text },
  pressed: { opacity: 0.7 },
  busy: { opacity: 0.5 },
  recording: { backgroundColor: colors.forgotSoft, justifyContent: 'space-between', paddingHorizontal: spacing.md },
  recordingDot: { width: 10, height: 10, borderRadius: 5, backgroundColor: colors.forgot },
  recordingText: { ...typography.caption, color: colors.text, flex: 1 },
  recordingStop: { ...typography.caption, color: colors.forgot, fontWeight: '700' },
  locked: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.md,
    backgroundColor: colors.premiumSoft,
    borderRadius: radius.md,
    padding: spacing.md,
  },
  lockedBody: { flex: 1, gap: 2 },
  lockedTitle: { ...typography.h3, fontSize: 15 },
  lockedText: { ...typography.tiny, color: colors.textMuted },
});
