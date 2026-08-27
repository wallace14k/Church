import { useCallback, useEffect, useRef, useState } from 'react';
import { Alert } from 'react-native';
import * as DocumentPicker from 'expo-document-picker';
import {
  AudioModule,
  RecordingPresets,
  createAudioPlayer,
  useAudioRecorder,
  useAudioRecorderState,
} from 'expo-audio';

import { configureForPlayback, configureForRecording } from './session';
import { deleteAudio, persistAudio } from './storage';
import type { CardAudio } from '../types';

/** Duração máxima de uma gravação, em ms. */
export const MAX_RECORDING_MS = 60_000;

/**
 * Estado de anexo de áudio de um lado do card (frente ou verso): gravar pelo
 * microfone, importar um arquivo existente e remover o anexo.
 */
export function useAudioAttachment(initial: CardAudio | null | undefined) {
  const recorder = useAudioRecorder(RecordingPresets.HIGH_QUALITY);
  const recorderState = useAudioRecorderState(recorder, 200);

  const [audio, setAudio] = useState<CardAudio | null>(initial ?? null);
  const [busy, setBusy] = useState(false);
  /** Áudios substituídos/removidos nesta edição, apagados só ao salvar. */
  const discarded = useRef<CardAudio[]>([]);
  const startedAt = useRef(0);

  const stopRecording = useCallback(async (): Promise<void> => {
    if (!recorder.isRecording) return;
    setBusy(true);
    try {
      await recorder.stop();
      const uri = recorder.uri;
      if (!uri) return;
      const durationMs = Math.max(500, Date.now() - startedAt.current);
      const saved = await persistAudio(uri, { durationMs, source: 'recording' });
      setAudio((current) => {
        if (current) discarded.current.push(current);
        return saved;
      });
    } catch (error) {
      Alert.alert('Não deu para salvar', 'A gravação falhou. Tente de novo.');
    } finally {
      await configureForPlayback();
      setBusy(false);
    }
  }, [recorder]);

  // Corta gravações longas demais sozinho, para não estourar o armazenamento.
  useEffect(() => {
    if (!recorderState.isRecording) return;
    if (recorderState.durationMillis < MAX_RECORDING_MS) return;
    void stopRecording();
  }, [recorderState.isRecording, recorderState.durationMillis, stopRecording]);

  const startRecording = useCallback(async (): Promise<void> => {
    const permission = await AudioModule.requestRecordingPermissionsAsync();
    if (!permission.granted) {
      Alert.alert(
        'Microfone bloqueado',
        'Libere o acesso ao microfone nos ajustes do aparelho para gravar o diálogo do card.'
      );
      return;
    }
    setBusy(true);
    try {
      await configureForRecording();
      await recorder.prepareToRecordAsync();
      startedAt.current = Date.now();
      recorder.record();
    } catch {
      Alert.alert('Não deu para gravar', 'Não conseguimos acessar o microfone agora.');
    } finally {
      setBusy(false);
    }
  }, [recorder]);

  const importFile = useCallback(async (): Promise<void> => {
    const result = await DocumentPicker.getDocumentAsync({
      type: 'audio/*',
      copyToCacheDirectory: true,
      multiple: false,
    });
    if (result.canceled || !result.assets?.length) return;

    const asset = result.assets[0];
    setBusy(true);
    try {
      const durationMs = await measureDuration(asset.uri);
      const saved = await persistAudio(asset.uri, {
        durationMs,
        label: asset.name,
        source: 'import',
      });
      setAudio((current) => {
        if (current) discarded.current.push(current);
        return saved;
      });
    } catch {
      Alert.alert('Arquivo não suportado', 'Escolha um arquivo de áudio comum (m4a, mp3, wav).');
    } finally {
      setBusy(false);
    }
  }, []);

  const remove = useCallback((): void => {
    setAudio((current) => {
      if (current) discarded.current.push(current);
      return null;
    });
  }, []);

  /** Confirma a edição: apaga de vez os áudios substituídos. */
  const commit = useCallback((): CardAudio | null => {
    discarded.current.forEach(deleteAudio);
    discarded.current = [];
    return audio;
  }, [audio]);

  /** Cancela a edição: apaga os áudios criados agora e restaura o original. */
  const rollback = useCallback((): void => {
    const originalUri = initial?.uri;
    if (audio && audio.uri !== originalUri) deleteAudio(audio);
    discarded.current = [];
    setAudio(initial ?? null);
  }, [audio, initial]);

  return {
    audio,
    isRecording: recorderState.isRecording,
    recordingMs: recorderState.durationMillis,
    busy,
    startRecording,
    stopRecording,
    importFile,
    remove,
    commit,
    rollback,
  };
}

/** Lê a duração de um arquivo carregando-o brevemente em um player. */
async function measureDuration(uri: string): Promise<number> {
  const player = createAudioPlayer({ uri });
  try {
    // O player carrega de forma assíncrona; damos alguns ciclos para a
    // duração aparecer antes de desistir.
    for (let attempt = 0; attempt < 20; attempt += 1) {
      if (player.duration > 0) return Math.round(player.duration * 1000);
      await new Promise((resolve) => setTimeout(resolve, 50));
    }
    return 0;
  } finally {
    player.release();
  }
}
