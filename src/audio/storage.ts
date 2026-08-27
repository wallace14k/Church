import { Directory, File, Paths } from 'expo-file-system';

import { createId } from '../utils/id';
import type { CardAudio } from '../types';

/**
 * Arquivos de áudio dos cards.
 *
 * As gravações nascem em um diretório temporário; aqui elas são movidas para
 * `documentDirectory/card-audio/`, que sobrevive ao fechamento do app e é
 * incluído no backup do aparelho.
 */

const AUDIO_DIR_NAME = 'card-audio';

function audioDirectory(): Directory {
  const dir = new Directory(Paths.document, AUDIO_DIR_NAME);
  if (!dir.exists) dir.create({ intermediates: true, idempotent: true });
  return dir;
}

function extensionOf(uri: string): string {
  const clean = uri.split('?')[0];
  const dot = clean.lastIndexOf('.');
  if (dot === -1 || dot < clean.lastIndexOf('/')) return 'm4a';
  return clean.slice(dot + 1);
}

/**
 * Copia um áudio para o armazenamento permanente do app e devolve o
 * `CardAudio` pronto para ser salvo no card.
 */
export async function persistAudio(
  sourceUri: string,
  meta: { durationMs: number; label?: string; source: CardAudio['source'] }
): Promise<CardAudio> {
  const dir = audioDirectory();
  const fileName = `${createId('a')}.${extensionOf(sourceUri)}`;
  const destination = new File(dir, fileName);
  const origin = new File(sourceUri);

  await origin.copy(destination);

  return {
    uri: destination.uri,
    durationMs: Math.max(0, Math.round(meta.durationMs)),
    label: meta.label,
    source: meta.source,
  };
}

/** Apaga o arquivo de um áudio. Silencioso se o arquivo já sumiu. */
export function deleteAudio(audio: CardAudio | null | undefined): void {
  if (!audio) return;
  try {
    const file = new File(audio.uri);
    if (file.exists) file.delete();
  } catch {
    // Um arquivo ausente não é motivo para interromper a edição do card.
  }
}

/** `true` se o arquivo do áudio ainda existe no aparelho. */
export function audioExists(audio: CardAudio | null | undefined): boolean {
  if (!audio) return false;
  try {
    return new File(audio.uri).exists;
  } catch {
    return false;
  }
}

/**
 * Remove arquivos de áudio que nenhum card referencia mais — por exemplo,
 * sobras de cards apagados enquanto o app estava sem espaço em disco.
 */
export function pruneOrphanAudio(referencedUris: Set<string>): number {
  try {
    const dir = audioDirectory();
    let removed = 0;
    for (const entry of dir.list()) {
      if (entry instanceof File && !referencedUris.has(entry.uri)) {
        entry.delete();
        removed += 1;
      }
    }
    return removed;
  } catch {
    return 0;
  }
}

/** Espaço ocupado pelos áudios, em bytes. */
export function audioDiskUsage(): number {
  try {
    return audioDirectory()
      .list()
      .reduce((total, entry) => (entry instanceof File ? total + entry.size : total), 0);
  } catch {
    return 0;
  }
}

export function formatBytes(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(0)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(1).replace('.', ',')} MB`;
}
