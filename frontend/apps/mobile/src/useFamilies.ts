import { describeError } from '@congrega/api-client/errors';
import { listFamilies, type Family } from '@congrega/api-client/families';
import { useCallback, useEffect, useRef, useState } from 'react';
import { apiClient } from './api';

interface EstadoFamilias {
  readonly familias: readonly Family[];
  readonly carregando: boolean;
  readonly erro: string | null;
}

const INICIAL: EstadoFamilias = {
  familias: [],
  carregando: true,
  erro: null,
};

/** Carrega a lista de famílias. Sem busca nem paginação: a lista é curta por natureza. */
export function useFamilies(): EstadoFamilias & { recarregar: () => void } {
  const [estado, setEstado] = useState<EstadoFamilias>(INICIAL);
  const emVoo = useRef<AbortController | null>(null);

  const carregar = useCallback(async () => {
    emVoo.current?.abort();
    const controlador = new AbortController();
    emVoo.current = controlador;

    setEstado((anterior) => ({ ...anterior, carregando: true, erro: null }));

    try {
      const familias = await listFamilies(apiClient, controlador.signal);
      setEstado({ familias, carregando: false, erro: null });
    } catch (causa) {
      if (controlador.signal.aborted) return;
      setEstado((anterior) => ({ ...anterior, carregando: false, erro: describeError(causa) }));
    }
  }, []);

  useEffect(() => {
    void carregar();
    return () => emVoo.current?.abort();
  }, [carregar]);

  return { ...estado, recarregar: carregar };
}
