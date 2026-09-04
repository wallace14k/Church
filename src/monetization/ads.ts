import { config } from '../config';

/**
 * Anúncios intersticiais.
 *
 * O gancho fica aqui para que a integração com o AdMob
 * (`react-native-google-mobile-ads`, que exige um development build) entre em
 * um lugar só. Sem os IDs configurados, o app mostra o placeholder interno
 * (`<InterstitialAd />`) com a mesma duração e o mesmo fluxo de fechamento,
 * então as telas não mudam quando a rede de anúncios for ligada.
 */

/** Quantos treinos podem terminar sem anúncio antes do primeiro. */
export const SESSIONS_BEFORE_FIRST_AD = 2;
/** A partir daí, um anúncio a cada N treinos. */
export const AD_EVERY_N_SESSIONS = 2;
/** A cada quantos treinos a tela de assinatura aparece. */
export const PAYWALL_EVERY_N_SESSIONS = 6;
/** Segundos até o botão de fechar liberar. */
export const AD_SKIP_AFTER_SECONDS = 5;

export const isAdNetworkConfigured = Boolean(
  config.admob.interstitialIos || config.admob.interstitialAndroid
);

/**
 * Decide se o treino recém-terminado deve exibir um anúncio.
 * `sessionNumber` é a contagem acumulada de treinos concluídos.
 */
export function shouldShowAd(sessionNumber: number, premium: boolean): boolean {
  if (premium) return false;
  if (sessionNumber <= SESSIONS_BEFORE_FIRST_AD) return false;
  return (sessionNumber - SESSIONS_BEFORE_FIRST_AD) % AD_EVERY_N_SESSIONS === 0;
}

/** Decide se a tela de assinatura deve ser oferecida após o treino. */
export function shouldShowPaywall(sessionNumber: number, premium: boolean): boolean {
  if (premium) return false;
  return sessionNumber > 0 && sessionNumber % PAYWALL_EVERY_N_SESSIONS === 0;
}
