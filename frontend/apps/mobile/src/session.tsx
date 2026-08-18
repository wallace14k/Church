import { requestOtp, verifyOtp } from '@congrega/api-client/auth';
import type { Session } from '@congrega/api-client/client';
import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from 'react';
import { apiClient, setSessionEndedHandler } from './api';

export type SessionStatus = 'carregando' | 'anonimo' | 'autenticado';

interface SessionContextValue {
  readonly status: SessionStatus;
  readonly session: Session | null;
  readonly pedirCodigo: (email: string) => Promise<void>;
  readonly confirmarCodigo: (email: string, codigo: string) => Promise<Session>;
  readonly sair: () => Promise<void>;
}

const SessionContext = createContext<SessionContextValue | null>(null);

export function SessionProvider({ children }: { readonly children: ReactNode }) {
  const [session, setSession] = useState<Session | null>(null);
  const [status, setStatus] = useState<SessionStatus>('carregando');

  // Hidratação na abertura do app: tenta renovar a partir do refresh token
  // guardado. É o que permite abrir o app já logado depois de dias.
  useEffect(() => {
    let cancelado = false;

    async function hidratar() {
      try {
        // `hydrateSession()` é o que de fato ADOTA a sessão renovada — ver a
        // nota no método. Chamar `request()` direto aqui já causou o app
        // concluir "anônimo" com uma sessão perfeitamente válida no cookie.
        await apiClient.hydrateSession();
      } catch {
        // Sem sessão válida — estado normal de quem nunca entrou ou saiu.
      }

      if (!cancelado) {
        setSession(apiClient.session);
        setStatus(apiClient.session === null ? 'anonimo' : 'autenticado');
      }
    }

    void hidratar();
    return () => {
      cancelado = true;
    };
  }, []);

  // O cliente avisa quando a sessão morre por conta própria — token revogado,
  // reuso detectado, conta bloqueada. Sem isso, a interface continuaria
  // mostrando telas autenticadas até a próxima requisição falhar.
  useEffect(() => {
    setSessionEndedHandler(() => {
      setSession(null);
      setStatus('anonimo');
    });

    return () => setSessionEndedHandler(null);
  }, []);

  const pedirCodigo = useCallback(async (email: string) => {
    await requestOtp(apiClient, { email });
  }, []);

  const confirmarCodigo = useCallback(async (email: string, codigo: string) => {
    const nova = await verifyOtp(apiClient, { email, code: codigo });
    setSession(nova);
    setStatus('autenticado');
    return nova;
  }, []);

  const sair = useCallback(async () => {
    apiClient.setSession(null);
    setSession(null);
    setStatus('anonimo');
  }, []);

  const value = useMemo<SessionContextValue>(
    () => ({ status, session, pedirCodigo, confirmarCodigo, sair }),
    [status, session, pedirCodigo, confirmarCodigo, sair],
  );

  return <SessionContext.Provider value={value}>{children}</SessionContext.Provider>;
}

export function useSession(): SessionContextValue {
  const value = useContext(SessionContext);

  if (value === null) {
    throw new Error('useSession precisa estar dentro de <SessionProvider>.');
  }

  return value;
}
