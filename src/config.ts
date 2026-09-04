import Constants from 'expo-constants';

type Extra = {
  googleExpoClientId?: string;
  googleIosClientId?: string;
  googleAndroidClientId?: string;
  googleWebClientId?: string;
  admobInterstitialIosId?: string;
  admobInterstitialAndroidId?: string;
};

const extra = ((Constants.expoConfig?.extra ?? {}) as Extra) || {};

/**
 * Credenciais externas.
 *
 * Os valores reais entram em `app.json` -> `expo.extra` (ou via variáveis de
 * ambiente no build do EAS). Quando não estão preenchidos, o app segue
 * funcionando: o login com Google entra em modo de demonstração e os anúncios
 * usam o placeholder interno.
 */
export const config = {
  google: {
    expoClientId: extra.googleExpoClientId ?? '',
    iosClientId: extra.googleIosClientId ?? '',
    androidClientId: extra.googleAndroidClientId ?? '',
    webClientId: extra.googleWebClientId ?? '',
  },
  admob: {
    interstitialIos: extra.admobInterstitialIosId ?? '',
    interstitialAndroid: extra.admobInterstitialAndroidId ?? '',
  },
};

/** `true` quando existe pelo menos um client ID do Google configurado. */
export const isGoogleConfigured = Boolean(
  config.google.iosClientId ||
    config.google.androidClientId ||
    config.google.webClientId ||
    config.google.expoClientId
);
