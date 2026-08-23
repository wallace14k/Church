import { describeFailure, type Failure } from '@congrega/api-client/errors';
import { listEventTypes, type EventType } from '@congrega/api-client/event-types';
import { useCallback, useEffect, useState } from 'react';
import { apiClient } from './api';

export interface UseEventTypes {
  readonly tipos: readonly EventType[];
  readonly carregando: boolean;
  readonly erro: Failure | null;
  readonly recarregar: () => void;
}

/**
 * Tipos de evento da igreja.
 *
 * `includeInactive` decide o público: o formulário de evento pede só os ativos
 * — oferecer um tipo desativado desfaria o propósito de desativá-lo — e a tela
 * de administração pede todos, porque um tipo que sumisse ao ser desativado
 * nunca mais poderia ser reativado.
 */
export function useEventTypes(includeInactive = false): UseEventTypes {
  const [tipos, setTipos] = useState<readonly EventType[]>([]);
  const [carregando, setCarregando] = useState(true);
  const [erro, setErro] = useState<Failure | null>(null);
  const [versao, setVersao] = useState(0);

  const recarregar = useCallback(() => setVersao((v) => v + 1), []);

  useEffect(() => {
    let cancelado = false;

    setCarregando(true);
    setErro(null);

    listEventTypes(apiClient, includeInactive)
      .then((lista) => {
        if (!cancelado) setTipos(lista);
      })
      .catch((causa: unknown) => {
        // Guarda a falha categorizada, não a mensagem: é o que permite ao
        // `AsyncContent` distinguir "sem conexão" de "sem permissão" e decidir
        // se oferece "tentar de novo".
        if (!cancelado) setErro(describeFailure(causa));
      })
      .finally(() => {
        if (!cancelado) setCarregando(false);
      });

    return () => {
      cancelado = true;
    };
  }, [includeInactive, versao]);

  return { tipos, carregando, erro, recarregar };
}
