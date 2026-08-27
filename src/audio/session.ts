import { setAudioModeAsync } from 'expo-audio';

/**
 * Configuração da sessão de áudio do sistema.
 *
 * O modo precisa mudar entre gravar e reproduzir: no iOS, gravar exige
 * `allowsRecording`, e mantê-lo ligado durante a reprodução derruba o volume
 * para o do fone de ouvido.
 */

/** Reprodução: toca mesmo com o aparelho no silencioso (essencial para idiomas). */
export async function configureForPlayback(): Promise<void> {
  try {
    await setAudioModeAsync({
      allowsRecording: false,
      playsInSilentMode: true,
      shouldPlayInBackground: false,
      interruptionMode: 'mixWithOthers',
    });
  } catch {
    // Sem permissão ou plataforma sem suporte: a reprodução ainda tenta seguir.
  }
}

/** Gravação: libera o microfone. */
export async function configureForRecording(): Promise<void> {
  await setAudioModeAsync({
    allowsRecording: true,
    playsInSilentMode: true,
  });
}
