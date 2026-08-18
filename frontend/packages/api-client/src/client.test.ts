import { afterEach, describe, expect, it, vi } from 'vitest';
import { ApiClient, type Session } from './client';
import { ApiError, NetworkError } from './errors';
import { InMemoryTokenStorage } from './token-storage';

function session(overrides: Partial<Session> = {}): Session {
  return {
    accessToken: 'access-1',
    // Já vencido: força a renovação no primeiro uso.
    expiresAt: new Date(Date.now() - 1000).toISOString(),
    refreshToken: 'refresh-1',
    userId: 'user-1',
    tenantId: 'tenant-1',
    roles: ['Member'],
    ...overrides,
  };
}

function jsonResponse(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'content-type': 'application/json' },
  });
}

function problemResponse(status: number, detail: string): Response {
  return new Response(JSON.stringify({ status, detail, correlationId: 'abc123' }), {
    status,
    headers: { 'content-type': 'application/problem+json' },
  });
}

async function buildClient(options: { onSessionEnded?: () => void } = {}) {
  const storage = new InMemoryTokenStorage();
  await storage.write('refresh-1');

  const client = new ApiClient({
    baseUrl: 'https://api.congrega.test',
    storage,
    clientKind: 'mobile',
    ...(options.onSessionEnded ? { onSessionEnded: options.onSessionEnded } : {}),
  });

  return { client, storage };
}

afterEach(() => {
  vi.restoreAllMocks();
});

describe('renovação em voo única', () => {
  it('cinco requisições simultâneas disparam UMA renovação', async () => {
    const fetchMock = vi.fn(async (input: string | URL | Request) => {
      const url = String(input);
      if (url.endsWith('/auth/refresh')) {
        // Latência real: sem ela, a primeira renovação terminaria antes de as
        // outras chamadas começarem e o teste passaria por acidente.
        await new Promise((resolve) => setTimeout(resolve, 20));
        return jsonResponse(session({ accessToken: 'access-2', expiresAt: future() }));
      }
      return jsonResponse({ ok: true });
    });
    vi.stubGlobal('fetch', fetchMock);

    const { client } = await buildClient();
    client.setSession(session());

    await Promise.all([
      client.request('/api/v1/members'),
      client.request('/api/v1/members'),
      client.request('/api/v1/events'),
      client.request('/api/v1/giving'),
      client.request('/api/v1/me'),
    ]);

    const refreshCalls = fetchMock.mock.calls.filter(([url]) => String(url).endsWith('/auth/refresh'));

    // ESTE é o ponto. Sem a coordenação, seriam 5 chamadas — e como o refresh
    // ROTACIONA o token, quatro apresentariam um valor já consumido. O servidor
    // leria isso como reuso, revogaria a family inteira e derrubaria o usuário.
    // O sintoma seria "o app me desloga sozinho às vezes", praticamente
    // impossível de reproduzir sob demanda.
    expect(refreshCalls).toHaveLength(1);
  });

  it('renova de novo quando o token vence outra vez', async () => {
    let refreshes = 0;
    vi.stubGlobal(
      'fetch',
      vi.fn(async (input: string | URL | Request) => {
        if (String(input).endsWith('/auth/refresh')) {
          refreshes += 1;
          // Devolve token já vencido: obriga nova renovação na próxima chamada.
          return jsonResponse(session({ accessToken: `access-${refreshes}` }));
        }
        return jsonResponse({ ok: true });
      }),
    );

    const { client } = await buildClient();
    client.setSession(session());

    await client.request('/api/v1/members');
    await client.request('/api/v1/members');

    // A trava é por renovação em curso, não permanente: liberá-la ao terminar é
    // o que permite renovar de novo quando o novo token também vencer.
    expect(refreshes).toBe(2);
  });
});

describe('hidratação na abertura do app', () => {
  it('adota a sessão devolvida pela renovação — não só recebe 200 e ignora', async () => {
    // Este é o bug real: `request()` sozinho não chama `#adoptSession`. Um
    // chamador que discar `request('/auth/refresh', {anonymous:true})` direto
    // e depois ler `client.session` sempre encontra `null`, mesmo com o
    // servidor respondendo 200 — a app conclui "anônimo" com sessão válida no
    // cookie/storage. `hydrateSession()` existe para nunca mais reintroduzir
    // esse caminho.
    vi.stubGlobal(
      'fetch',
      vi.fn(async (input: string | URL | Request) => {
        if (String(input).endsWith('/auth/refresh')) {
          return jsonResponse(session({ accessToken: 'access-hidratado', expiresAt: future() }));
        }
        return jsonResponse({ ok: true });
      }),
    );

    // Sem sessão em memória — exatamente o estado de um app recém-aberto,
    // antes de qualquer login nesta execução.
    const storage = new InMemoryTokenStorage();
    await storage.write('refresh-1');
    const client = new ApiClient({ baseUrl: 'https://api.congrega.test', storage, clientKind: 'mobile' });

    expect(client.session).toBeNull();

    const hidratada = await client.hydrateSession();

    expect(hidratada).not.toBeNull();
    expect(client.session?.accessToken).toBe('access-hidratado');
  });

  it('devolve null sem lançar quando não há sessão para renovar', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => problemResponse(401, 'Refresh token ausente.')));

    const storage = new InMemoryTokenStorage();
    // Nunca logou nesta instalação: sem refresh token guardado.
    const client = new ApiClient({ baseUrl: 'https://api.congrega.test', storage, clientKind: 'mobile' });

    await expect(client.hydrateSession()).resolves.toBeNull();
    expect(client.session).toBeNull();
  });

  it('hidratação concorrente com uma chamada autenticada compartilha a MESMA renovação', async () => {
    // A app real dispara `hydrateSession()` no boot e, quase ao mesmo tempo,
    // uma tela já pode montar e chamar `request()` autenticado. Sem os dois
    // caírem no mesmo `#refreshInFlight`, seriam duas renovações — e como o
    // refresh ROTACIONA o token, a segunda leria a primeira como reuso.
    const fetchMock = vi.fn(async (input: string | URL | Request) => {
      if (String(input).endsWith('/auth/refresh')) {
        await new Promise((resolve) => setTimeout(resolve, 20));
        return jsonResponse(session({ accessToken: 'access-2', expiresAt: future() }));
      }
      return jsonResponse({ ok: true });
    });
    vi.stubGlobal('fetch', fetchMock);

    const storage = new InMemoryTokenStorage();
    await storage.write('refresh-1');
    const client = new ApiClient({ baseUrl: 'https://api.congrega.test', storage, clientKind: 'mobile' });

    await Promise.all([client.hydrateSession(), client.request('/api/v1/members')]);

    const refreshCalls = fetchMock.mock.calls.filter(([url]) => String(url).endsWith('/auth/refresh'));
    expect(refreshCalls).toHaveLength(1);
  });
});

describe('fim de sessão', () => {
  it('401 após renovar encerra a sessão e limpa o token', async () => {
    const onSessionEnded = vi.fn();
    vi.stubGlobal(
      'fetch',
      vi.fn(async (input: string | URL | Request) => {
        if (String(input).endsWith('/auth/refresh')) {
          return jsonResponse(session({ expiresAt: future() }));
        }
        return problemResponse(401, 'Sessão expirada.');
      }),
    );

    const { client, storage } = await buildClient({ onSessionEnded });
    client.setSession(session());

    await expect(client.request('/api/v1/members')).rejects.toBeInstanceOf(ApiError);

    expect(client.session).toBeNull();
    expect(await storage.read()).toBeNull();
    expect(onSessionEnded).toHaveBeenCalledOnce();
  });

  it('falha de rede NÃO derruba a sessão', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async (input: string | URL | Request) => {
        if (String(input).endsWith('/auth/refresh')) {
          return jsonResponse(session({ expiresAt: future() }));
        }
        throw new TypeError('Failed to fetch');
      }),
    );

    const { client, storage } = await buildClient();
    client.setSession(session());

    await expect(client.request('/api/v1/members')).rejects.toBeInstanceOf(NetworkError);

    // O metrô entrou num túnel — o token continua válido. Deslogar aqui seria
    // hostil e desnecessário.
    expect(await storage.read()).toBe('refresh-1');
  });
});

describe('cabeçalhos', () => {
  it('envia identificação de plataforma e correlação', async () => {
    const fetchMock = vi.fn(async (_input: string | URL | Request, _init?: RequestInit) =>
      jsonResponse({ ok: true }),
    );
    vi.stubGlobal('fetch', fetchMock);

    const { client } = await buildClient();
    client.setSession(session({ expiresAt: future() }));

    await client.request('/api/v1/me');

    const init = fetchMock.mock.calls[0]?.[1] as RequestInit;
    const headers = init.headers as Record<string, string>;

    // "mobile" faz o servidor devolver o refresh no corpo em vez de cookie.
    expect(headers['X-Congrega-Client']).toBe('mobile');
    expect(headers.Authorization).toBe('Bearer access-1');
    expect(headers['X-Correlation-Id']).toMatch(/^[a-z0-9]+$/u);
  });

  it('não envia Authorization em chamada anônima', async () => {
    const fetchMock = vi.fn(async (_input: string | URL | Request, _init?: RequestInit) =>
      jsonResponse({ ok: true }),
    );
    vi.stubGlobal('fetch', fetchMock);

    const { client } = await buildClient();
    client.setSession(session({ expiresAt: future() }));

    await client.request('/api/v1/auth/otp/request', { method: 'POST', body: {}, anonymous: true });

    const init = fetchMock.mock.calls[0]?.[1] as RequestInit;
    expect((init.headers as Record<string, string>).Authorization).toBeUndefined();
  });

  it('gera correlação diferente a cada requisição', async () => {
    const fetchMock = vi.fn(async (_input: string | URL | Request, _init?: RequestInit) =>
      jsonResponse({ ok: true }),
    );
    vi.stubGlobal('fetch', fetchMock);

    const { client } = await buildClient();
    client.setSession(session({ expiresAt: future() }));

    await client.request('/api/v1/me');
    await client.request('/api/v1/me');

    const first = (fetchMock.mock.calls[0]?.[1] as RequestInit).headers as Record<string, string>;
    const second = (fetchMock.mock.calls[1]?.[1] as RequestInit).headers as Record<string, string>;
    expect(first['X-Correlation-Id']).not.toBe(second['X-Correlation-Id']);
  });
});

describe('erros', () => {
  it('extrai o identificador de correlação do Problem Details', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => problemResponse(403, 'Sem permissão.')));

    const { client } = await buildClient();
    client.setSession(session({ expiresAt: future() }));

    const error = await client.request('/api/v1/giving').catch((e: unknown) => e);

    expect(error).toBeInstanceOf(ApiError);
    // É o código que o usuário cita ao suporte e que liga o relato ao log.
    expect((error as ApiError).correlationId).toBe('abc123');
    expect((error as ApiError).isForbidden).toBe(true);
    expect((error as ApiError).isRetryable).toBe(false);
  });
});

function future(): string {
  return new Date(Date.now() + 15 * 60_000).toISOString();
}
