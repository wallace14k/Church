import React, { useEffect } from 'react';
import { Pressable, StyleSheet, Text, View } from 'react-native';
import { Ionicons } from '@expo/vector-icons';
import { useAudioPlayer, useAudioPlayerStatus } from 'expo-audio';

import { configureForPlayback } from '../audio/session';
import { colors, radius, spacing, typography } from '../theme';
import { formatDuration } from '../utils/date';
import type { CardAudio } from '../types';

interface Props {
  audio: CardAudio;
  label?: string;
  /** Toca sozinho assim que o componente aparece. */
  autoPlay?: boolean;
  compact?: boolean;
}

/** Botão de reprodução do áudio anexado a um lado do card. */
export function AudioButton({ audio, label, autoPlay, compact }: Props) {
  const player = useAudioPlayer({ uri: audio.uri }, { updateInterval: 120 });
  const status = useAudioPlayerStatus(player);

  useEffect(() => {
    void configureForPlayback();
  }, []);

  useEffect(() => {
    if (!autoPlay || !status.isLoaded) return;
    player.seekTo(0).then(() => player.play()).catch(() => {
      // O arquivo pode ter sumido (backup restaurado, limpeza de espaço);
      // nesse caso o card segue funcionando sem o áudio.
    });
    // Dispara uma única vez, quando o áudio termina de carregar.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [autoPlay, status.isLoaded]);

  const toggle = () => {
    if (status.playing) {
      player.pause();
      return;
    }
    // Depois de terminar, o cursor fica no fim: rebobina antes de tocar.
    const atEnd = status.duration > 0 && status.currentTime >= status.duration - 0.05;
    if (atEnd || status.didJustFinish) {
      void player.seekTo(0).then(() => player.play());
      return;
    }
    player.play();
  };

  const totalMs = status.duration > 0 ? status.duration * 1000 : audio.durationMs;
  const progress = status.duration > 0 ? status.currentTime / status.duration : 0;

  return (
    <Pressable
      onPress={toggle}
      accessibilityRole="button"
      accessibilityLabel={status.playing ? 'Pausar áudio' : 'Tocar áudio'}
      style={({ pressed }) => [styles.wrapper, compact && styles.compact, pressed && styles.pressed]}
    >
      <View style={styles.iconCircle}>
        <Ionicons
          name={status.playing ? 'pause' : 'play'}
          size={compact ? 14 : 18}
          color={colors.primary}
        />
      </View>

      <View style={styles.body}>
        {!compact && label ? <Text style={styles.label}>{label}</Text> : null}
        <View style={styles.trackRow}>
          <View style={styles.track}>
            <View style={[styles.trackFill, { width: `${Math.min(100, progress * 100)}%` }]} />
          </View>
          <Text style={styles.time}>
            {status.playing ? formatDuration(status.currentTime * 1000) : formatDuration(totalMs)}
          </Text>
        </View>
      </View>
    </Pressable>
  );
}

const styles = StyleSheet.create({
  wrapper: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.md,
    backgroundColor: colors.primarySoft,
    borderRadius: radius.md,
    paddingVertical: spacing.md,
    paddingHorizontal: spacing.md,
  },
  compact: { paddingVertical: spacing.sm },
  pressed: { opacity: 0.7 },
  iconCircle: {
    width: 34,
    height: 34,
    borderRadius: 17,
    backgroundColor: colors.bgElevated,
    alignItems: 'center',
    justifyContent: 'center',
  },
  body: { flex: 1, gap: spacing.xs },
  label: { ...typography.tiny, color: colors.primary },
  trackRow: { flexDirection: 'row', alignItems: 'center', gap: spacing.sm },
  track: { flex: 1, height: 4, borderRadius: 2, backgroundColor: colors.surfaceAlt, overflow: 'hidden' },
  trackFill: { height: '100%', backgroundColor: colors.primary },
  time: { ...typography.tiny, color: colors.textMuted, minWidth: 34, textAlign: 'right' },
});
