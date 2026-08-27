import React, { createContext, useCallback, useContext, useEffect, useMemo, useState } from 'react';
import { Platform } from 'react-native';
import * as AppleAuthentication from 'expo-apple-authentication';
import * as Google from 'expo-auth-session/providers/google';
import * as WebBrowser from 'expo-web-browser';

import { config, isGoogleConfigured } from '../config';
import { storage } from '../storage';
import { createId } from '../utils/id';
import type { AuthUser } from '../types';

// Fecha a aba do navegador assim que o provedor devolve o usuário.
WebBrowser.maybeCompleteAuthSession();

export class AuthError extends Error {}

interface AuthContextValue {
  user: AuthUser | null;
  loading: boolean;
  /** Autenticação em andamento (botão em estado de espera). */
  pending: 'google' | 'apple' | 'guest' | null;
  appleAvailable: boolean;
  googleAvailable: boolean;
  signInWithGoogle: () => Promise<void>;
  signInWithApple: () => Promise<void>;
  continueAsGuest: () => Promise<void>;
  signOut: () => Promise<void>;
}

const AuthContext = createContext<AuthContextValue | null>(null);

export function AuthProvider({ children }: { children: React.ReactNode }) {
  const [user, setUser] = useState<AuthUser | null>(null);
  const [loading, setLoading] = useState(true);
  const [pending, setPending] = useState<AuthContextValue['pending']>(null);
  const [appleAvailable, setAppleAvailable] = useState(false);

  const [, googleResponse, promptGoogle] = Google.useAuthRequest({
    iosClientId: config.google.iosClientId || undefined,
    androidClientId: config.google.androidClientId || undefined,
    webClientId: config.google.webClientId || undefined,
    clientId: config.google.expoClientId || config.google.webClientId || undefined,
    scopes: ['profile', 'email'],
  });

  // Restaura a sessão salva no aparelho.
  useEffect(() => {
    let active = true;
    void (async () => {
      const saved = await storage.getUser();
      if (active) {
        setUser(saved);
        setLoading(false);
      }
    })();
    return () => {
      active = false;
    };
  }, []);

  useEffect(() => {
    if (Platform.OS !== 'ios') return;
    void AppleAuthentication.isAvailableAsync().then(setAppleAvailable);
  }, []);

  const persist = useCallback(async (next: AuthUser) => {
    await storage.saveUser(next);
    setUser(next);
  }, []);

  // O fluxo do Google devolve o resultado de forma assíncrona, fora do clique.
  useEffect(() => {
    if (!googleResponse) return;
    if (googleResponse.type !== 'success') {
      setPending(null);
      return;
    }

    const accessToken = googleResponse.authentication?.accessToken;
    void (async () => {
      try {
        const profile = accessToken ? await fetchGoogleProfile(accessToken) : null;
        await persist({
          id: profile?.sub ?? createId('google-'),
          provider: 'google',
          name: profile?.name ?? null,
          email: profile?.email ?? null,
          avatarUrl: profile?.picture ?? null,
          createdAt: Date.now(),
        });
      } finally {
        setPending(null);
      }
    })();
  }, [googleResponse, persist]);

  const signInWithGoogle = useCallback(async () => {
    if (!isGoogleConfigured) {
      throw new AuthError(
        'O login com Google ainda não foi configurado nesta build. Preencha os client IDs em app.json (expo.extra).'
      );
    }
    setPending('google');
    const result = await promptGoogle();
    // Cancelou ou fechou a aba: libera o botão na hora.
    if (result?.type !== 'success') setPending(null);
  }, [promptGoogle]);

  const signInWithApple = useCallback(async () => {
    setPending('apple');
    try {
      const credential = await AppleAuthentication.signInAsync({
        requestedScopes: [
          AppleAuthentication.AppleAuthenticationScope.FULL_NAME,
          AppleAuthentication.AppleAuthenticationScope.EMAIL,
        ],
      });

      // A Apple só envia nome e e-mail no primeiro login de cada aparelho;
      // depois disso vem apenas o `user`. Guardamos o que chegar.
      const fullName = [credential.fullName?.givenName, credential.fullName?.familyName]
        .filter(Boolean)
        .join(' ');

      await persist({
        id: credential.user,
        provider: 'apple',
        name: fullName || null,
        email: credential.email ?? null,
        avatarUrl: null,
        createdAt: Date.now(),
      });
    } catch (error) {
      const code = (error as { code?: string }).code;
      if (code !== 'ERR_REQUEST_CANCELED') {
        throw new AuthError('Não foi possível entrar com a conta Apple.');
      }
    } finally {
      setPending(null);
    }
  }, [persist]);

  const continueAsGuest = useCallback(async () => {
    setPending('guest');
    try {
      await persist({
        id: createId('guest-'),
        provider: 'guest',
        name: null,
        email: null,
        avatarUrl: null,
        createdAt: Date.now(),
      });
    } finally {
      setPending(null);
    }
  }, [persist]);

  const signOut = useCallback(async () => {
    // Os baralhos e cards continuam no aparelho: sair é só encerrar a sessão.
    await storage.saveUser(null);
    setUser(null);
  }, []);

  const value = useMemo<AuthContextValue>(
    () => ({
      user,
      loading,
      pending,
      appleAvailable,
      googleAvailable: isGoogleConfigured,
      signInWithGoogle,
      signInWithApple,
      continueAsGuest,
      signOut,
    }),
    [
      user,
      loading,
      pending,
      appleAvailable,
      signInWithGoogle,
      signInWithApple,
      continueAsGuest,
      signOut,
    ]
  );

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth(): AuthContextValue {
  const context = useContext(AuthContext);
  if (!context) throw new Error('useAuth precisa estar dentro de <AuthProvider>.');
  return context;
}

interface GoogleProfile {
  sub: string;
  name?: string;
  email?: string;
  picture?: string;
}

async function fetchGoogleProfile(accessToken: string): Promise<GoogleProfile | null> {
  try {
    const response = await fetch('https://www.googleapis.com/oauth2/v3/userinfo', {
      headers: { Authorization: `Bearer ${accessToken}` },
    });
    if (!response.ok) return null;
    return (await response.json()) as GoogleProfile;
  } catch {
    // Sem rede: seguimos com uma sessão local sem os dados do perfil.
    return null;
  }
}
