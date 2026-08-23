import type { ApiClient } from './client';
import type { Paged } from './members';

/**
 * Financeiro — espelha `GivingEndpoints` no backend.
 *
 * Todo valor trafega em **centavos inteiros**, nunca em reais decimais. Ver
 * `@congrega/core/money`: `0.1 + 0.2 !== 0.3`, e num livro-caixa de igreja esse
 * erro vira diferença de fechamento que alguém precisa explicar em assembleia.
 */

/**
 * Entrada, saída — ou, só em categoria, os dois.
 *
 * `Ambos` nunca aparece num lançamento: ele não teria sinal, e o fechamento não
 * saberia se soma ou subtrai. O servidor recusa.
 */
export type GivingKind = 'Entrada' | 'Saida' | 'Ambos';

/**
 * Forma de pagamento.
 *
 * `Cartao` é **legado**: os lançamentos gravados com ele não dizem se era
 * crédito ou débito, e escolher um seria inventar informação financeira. Ele
 * fica para a listagem desenhar; o formulário oferece só os de
 * {@link METODOS_OFERECIDOS}.
 */
export type GivingMethod =
  | 'Dinheiro'
  | 'Pix'
  | 'Cartao'
  | 'Transferencia'
  | 'Cheque'
  | 'Outro'
  | 'CartaoCredito'
  | 'CartaoDebito'
  | 'Boleto';

/** Os que o formulário oferece — sem o `Cartao` legado. */
export const METODOS_OFERECIDOS: readonly GivingMethod[] = [
  'Dinheiro',
  'Pix',
  'Transferencia',
  'CartaoCredito',
  'CartaoDebito',
  'Boleto',
  'Cheque',
  'Outro',
];

export interface GivingCategory {
  readonly id: string;
  readonly name: string;
  /**
   * O que a categoria **aceita**: entrada, saída, ou os dois.
   *
   * Não é o sinal do lançamento — esse mora em `GivingEntry.kind`. A distinção
   * passou a existir quando `Ambos` entrou: uma categoria que serve aos dois
   * não tem sinal para emprestar.
   */
  readonly kind: GivingKind;
  /** `#RRGGBB`, ou `null` para a cor padrão do sistema. */
  readonly colorHex: string | null;
  readonly isActive: boolean;
}

/** Onde o dinheiro fica — caixa físico ou conta bancária. */
export interface FinancialAccount {
  readonly id: string;
  readonly name: string;
  readonly kind: FinancialAccountKind;
  readonly isActive: boolean;
}

export type FinancialAccountKind = 'Caixa' | 'ContaBancaria';

/**
 * O dinheiro já se moveu, ou ainda vai se mover?
 *
 * **Só `Realizado` soma no fechamento.** As parcelas futuras de uma série
 * periódica nascem `Previsto`: elas existem para planejar, e contá-las faria o
 * caixa afirmar que dinheiro que não se moveu já se moveu.
 */
export type GivingEntryStatus = 'Realizado' | 'Previsto';

/** Com que frequência um lançamento recorrente se repete. */
export type GivingRecurrence = 'Semanal' | 'Mensal' | 'Anual';

/**
 * Contagens do mês, para os chips de filtro.
 *
 * **Do período inteiro, não da página.** Contar no cliente sobre os 50 itens
 * carregados diria "2 entradas" num mês com 30 — e o número apareceria ao lado
 * de um filtro que devolve as 30.
 */
export interface GivingSummary {
  readonly total: number;
  readonly entradas: number;
  readonly saidas: number;
  /** Quantos lançamentos por categoria, indexado pelo id da categoria. */
  readonly porCategoria: Readonly<Record<string, number>>;
}

export interface GivingEntry {
  readonly id: string;

  /**
   * Título do lançamento — "Aluguel", "Oferta do culto".
   *
   * `null` nos gravados antes do campo existir. Quem desenha cai no nome da
   * categoria: preencher com ele produziria a repetição ("Dízimo / Categoria:
   * Dízimo") que este campo veio resolver.
   */
  readonly description: string | null;

  readonly categoryId: string;
  readonly categoryName: string;
  readonly categoryColorHex: string | null;

  /**
   * Entrada ou saída — do **lançamento**, não da categoria.
   *
   * Ler o sinal da categoria devolveria "Ambos" para um lançamento que é
   * definidamente uma coisa só.
   */
  readonly kind: GivingKind;

  /** Centavos, sempre positivo. O sinal vem de `kind`. */
  readonly amountCents: number;

  readonly occurredOn: string;
  readonly method: GivingMethod;
  readonly memberName: string | null;
  readonly accountName: string | null;
  readonly isRecurring: boolean;
  readonly recurrence: GivingRecurrence | null;

  /** Realizado ou previsto. Só o realizado soma no fechamento. */
  readonly status: GivingEntryStatus;

  /** Identidade da série periódica, quando pertence a uma. */
  readonly seriesId: string | null;

  /**
   * Quantas parcelas futuras foram criadas junto.
   *
   * Só vem preenchido na **criação** de uma série — é o que permite a tela
   * dizer "e mais 12 parcelas previstas" em vez de o usuário descobrir sozinho
   * ao trocar de mês.
   */
  readonly generatedCount?: number;

  readonly notes: string | null;
}

export interface ClosingLine {
  readonly categoryId: string;
  readonly categoryName: string;
  readonly kind: GivingKind;
  readonly totalCents: number;
  readonly entryCount: number;
}

export interface MonthlyClosing {
  readonly year: number;
  readonly month: number;
  readonly totalIncomeCents: number;
  readonly totalExpenseCents: number;
  /** Entradas menos saídas. Negativo é informação, não erro. */
  readonly balanceCents: number;

  /**
   * Entradas ainda PREVISTAS — dinheiro que não se moveu.
   *
   * Fora de `totalIncomeCents`, e ao lado dele. Somar os dois faria o
   * fechamento afirmar um pagamento que não aconteceu; omitir o previsto faz a
   * tela mostrar R$ 0,00 ao lado de uma lista com lançamentos, e quem lê conclui
   * que o sistema perdeu a conta.
   */
  readonly plannedIncomeCents: number;

  /** Saídas ainda previstas. */
  readonly plannedExpenseCents: number;

  readonly lines: readonly ClosingLine[];
}

export interface ListGivingEntriesInput {
  readonly year?: number;
  readonly month?: number;
  readonly categoryId?: string;
  readonly page?: number;
  readonly pageSize?: number;

  /** `Entrada` ou `Saida`. Ausente traz os dois — é o chip "Todos". */
  readonly kind?: 'Entrada' | 'Saida';

  /** Busca no título e nas observações. */
  readonly search?: string;
}

export interface CreateGivingEntryInput {
  readonly categoryId: string;

  /** `Entrada` ou `Saida`. Obrigatório — o servidor recusa `Ambos`. */
  readonly kind: 'Entrada' | 'Saida';

  readonly amountCents: number;
  readonly occurredOn: string;
  readonly method: GivingMethod;

  /** Título. Opcional. */
  readonly description?: string;

  readonly memberId?: string;

  /** Conta ou caixa. Opcional: igreja com caixa único não precisa cadastrar. */
  readonly accountId?: string;

  /**
   * Frequência. Ausente = não recorrente.
   *
   * Um campo só, e não um par `recorrente` + `frequencia`: o par admite
   * "recorrente sem frequência", que a constraint do banco recusaria com um
   * erro que não explica nada.
   */
  readonly recurrence?: GivingRecurrence;

  readonly notes?: string;
}

export function listGivingCategories(
  client: ApiClient,
  includeInactive = false,
  signal?: AbortSignal,
): Promise<readonly GivingCategory[]> {
  const sufixo = includeInactive ? '?includeInactive=true' : '';
  return client.request<readonly GivingCategory[]>(`/api/v1/giving/categories${sufixo}`, {
    ...(signal ? { signal } : {}),
  });
}

export function createGivingCategory(
  client: ApiClient,
  name: string,
  kind: GivingKind,
): Promise<GivingCategory> {
  return client.request<GivingCategory>('/api/v1/giving/categories', {
    method: 'POST',
    body: { name, kind },
  });
}

export function updateGivingCategory(
  client: ApiClient,
  id: string,
  name: string,
  isActive: boolean,
): Promise<GivingCategory> {
  return client.request<GivingCategory>(`/api/v1/giving/categories/${id}`, {
    method: 'PUT',
    body: { name, isActive },
  });
}

export function listGivingEntries(
  client: ApiClient,
  input: ListGivingEntriesInput = {},
  signal?: AbortSignal,
): Promise<Paged<GivingEntry>> {
  const query = new URLSearchParams();

  if (input.year !== undefined) query.set('year', String(input.year));
  if (input.month !== undefined) query.set('month', String(input.month));
  if (input.categoryId !== undefined) query.set('categoryId', input.categoryId);
  if (input.kind !== undefined) query.set('kind', input.kind);
  if (input.search !== undefined && input.search.trim().length > 0) {
    query.set('search', input.search.trim());
  }
  if (input.page !== undefined) query.set('page', String(input.page));
  if (input.pageSize !== undefined) query.set('pageSize', String(input.pageSize));

  const sufixo = query.size > 0 ? `?${query.toString()}` : '';

  return client.request<Paged<GivingEntry>>(`/api/v1/giving/entries${sufixo}`, {
    ...(signal ? { signal } : {}),
  });
}

export function createGivingEntry(
  client: ApiClient,
  input: CreateGivingEntryInput,
): Promise<GivingEntry> {
  return client.request<GivingEntry>('/api/v1/giving/entries', { method: 'POST', body: input });
}

export function deleteGivingEntry(client: ApiClient, id: string): Promise<void> {
  return client.request<void>(`/api/v1/giving/entries/${id}`, { method: 'DELETE' });
}

export function getMonthlyClosing(
  client: ApiClient,
  year: number,
  month: number,
  signal?: AbortSignal,
): Promise<MonthlyClosing> {
  return client.request<MonthlyClosing>(`/api/v1/giving/closing?year=${year}&month=${month}`, {
    ...(signal ? { signal } : {}),
  });
}

/**
 * Contagens do mês para os chips de filtro.
 *
 * Sem período, o servidor usa o mês corrente **dele**: se o cliente o mandasse,
 * dois usuários em fusos diferentes veriam contagens diferentes para a mesma
 * igreja.
 */
export function getGivingSummary(
  client: ApiClient,
  year?: number,
  month?: number,
  signal?: AbortSignal,
): Promise<GivingSummary> {
  const query = new URLSearchParams();
  if (year !== undefined) query.set('year', String(year));
  if (month !== undefined) query.set('month', String(month));

  const sufixo = query.size > 0 ? `?${query.toString()}` : '';

  return client.request<GivingSummary>(`/api/v1/giving/entries/summary${sufixo}`, {
    ...(signal ? { signal } : {}),
  });
}

export function listFinancialAccounts(
  client: ApiClient,
  includeInactive = false,
  signal?: AbortSignal,
): Promise<readonly FinancialAccount[]> {
  const sufixo = includeInactive ? '?includeInactive=true' : '';

  return client.request<readonly FinancialAccount[]>(`/api/v1/giving/accounts${sufixo}`, {
    ...(signal ? { signal } : {}),
  });
}

export function createFinancialAccount(
  client: ApiClient,
  input: { readonly name: string; readonly kind?: FinancialAccountKind },
): Promise<FinancialAccount> {
  return client.request<FinancialAccount>('/api/v1/giving/accounts', {
    method: 'POST',
    body: input,
  });
}


// ===========================================================================
// Documento fiscal — o comprovante de uma saída
// ===========================================================================

/** Que papel comprova a despesa. */
export type FiscalDocumentType = 'NotaFiscal' | 'NotaDeServico' | 'CupomFiscal' | 'Recibo';

/** Os tipos que o formulário oferece, na ordem em que fazem sentido. */
export const TIPOS_DE_DOCUMENTO: readonly FiscalDocumentType[] = [
  'NotaFiscal',
  'NotaDeServico',
  'CupomFiscal',
  'Recibo',
];

/** O que o navegador sabe exibir sem baixar. O servidor recusa o resto. */
export const TIPOS_DE_ANEXO_ACEITOS: readonly string[] = [
  'application/pdf',
  'image/png',
  'image/jpeg',
];

/** 10 MB — o mesmo teto do CHECK da coluna. */
export const TAMANHO_MAXIMO_DO_ANEXO = 10 * 1024 * 1024;

/** O anexo, sem os bytes. */
export interface FiscalDocumentFileInfo {
  readonly id: string;
  readonly fileName: string;
  readonly contentType: string;
  readonly sizeBytes: number;
}

export interface FiscalDocument {
  readonly id: string;
  readonly documentType: FiscalDocumentType;
  readonly number: string | null;
  readonly series: string | null;

  /**
   * CPF ou CNPJ do emissor, **só dígitos**.
   *
   * A pontuação não é guardada: com ela, "12.345.678/0001-90" e
   * "12345678000190" seriam dois emissores diferentes no mesmo relatório. Quem
   * exibe formata.
   */
  readonly issuerTaxId: string | null;

  readonly accessKey: string | null;

  /** `null` quando o documento foi registrado sem anexo. */
  readonly file: FiscalDocumentFileInfo | null;
}

/** O lançamento com tudo — o que a tela de detalhe lê. */
export interface GivingEntryDetail {
  readonly id: string;
  readonly description: string | null;
  readonly categoryId: string;
  readonly categoryName: string;
  readonly categoryColorHex: string | null;
  readonly kind: 'Entrada' | 'Saida';
  readonly amountCents: number;
  readonly occurredOn: string;
  readonly method: GivingMethod;
  readonly memberName: string | null;
  readonly accountName: string | null;
  readonly isRecurring: boolean;
  readonly recurrence: GivingRecurrence | null;
  readonly status: GivingEntryStatus;
  readonly seriesId: string | null;
  readonly notes: string | null;

  /** Quem digitou. Prestação de contas precisa da autoria. */
  readonly recordedByName: string | null;

  readonly createdAt: string;

  /** Sempre `null` em entrada — a igreja não emite nota ao receber oferta. */
  readonly document: FiscalDocument | null;
}

export interface FiscalDocumentFileContent {
  readonly fileName: string;
  readonly contentType: string;

  /** Base64 puro, sem o prefixo `data:`. Quem exibe monta a URI. */
  readonly contentBase64: string;
}

export interface AttachFiscalDocumentInput {
  readonly documentType: FiscalDocumentType;

  /** Obrigatório, exceto em `Recibo` — recibo à mão costuma não ter número. */
  readonly number?: string;
  readonly series?: string;

  /** Aceita pontuado; o servidor guarda só os dígitos e confere o verificador. */
  readonly issuerTaxId?: string;
  readonly accessKey?: string;

  readonly attachment?: {
    readonly fileName: string;
    readonly contentType: string;
    readonly contentBase64: string;
  };
}

export function getGivingEntry(
  client: ApiClient,
  id: string,
  signal?: AbortSignal,
): Promise<GivingEntryDetail> {
  return client.request<GivingEntryDetail>(`/api/v1/giving/entries/${id}`, {
    ...(signal ? { signal } : {}),
  });
}

/**
 * Anexa o documento fiscal a uma saída já lançada.
 *
 * **Chamada separada de `createGivingEntry` de propósito.** O comprovante chega
 * a 10 MB — 13 MB depois do Base64 — e mandá-lo junto do lançamento faria uma
 * queda de conexão a 90% do envio perder também o lançamento, obrigando o
 * tesoureiro a digitar tudo de novo por causa do anexo.
 *
 * E há o caso comum: **a nota costuma chegar depois**. A despesa é lançada no
 * dia do pagamento e o documento aparece dias mais tarde.
 */
export function attachFiscalDocument(
  client: ApiClient,
  entryId: string,
  input: AttachFiscalDocumentInput,
): Promise<FiscalDocument> {
  return client.request<FiscalDocument>(`/api/v1/giving/entries/${entryId}/document`, {
    method: 'POST',
    body: input,
  });
}

export function getFiscalDocumentFile(
  client: ApiClient,
  entryId: string,
  signal?: AbortSignal,
): Promise<FiscalDocumentFileContent> {
  return client.request<FiscalDocumentFileContent>(
    `/api/v1/giving/entries/${entryId}/document/file`,
    { ...(signal ? { signal } : {}) },
  );
}

// ===========================================================================
// Cofre
// ===========================================================================

/** Para onde o dinheiro foi. */
export type VaultDirection = 'Deposito' | 'Retirada';

export interface VaultMovement {
  readonly id: string;
  readonly sequenceNumber: number;
  readonly direction: VaultDirection;

  /** Centavos, sempre positivo. O sentido vem de `direction`. */
  readonly amountCents: number;

  /** Saldo do cofre depois deste movimento — o extrato confere linha a linha. */
  readonly balanceAfterCents: number;

  readonly occurredAt: string;
  readonly accountName: string | null;
  readonly performedByName: string | null;
  readonly notes: string | null;
}

/**
 * O cofre.
 *
 * **Nada aqui entra no fechamento do mês.** Mover dinheiro entre o caixa e o
 * cofre é a mesma nota mudando de gaveta: não é receita nem despesa, e somá-lo
 * ao resultado faria guardar dinheiro parecer gastá-lo.
 */
export interface Vault {
  readonly balanceCents: number;
  readonly movementCount: number;
  readonly movements: readonly VaultMovement[];
  readonly page: number;
  readonly pageSize: number;
  readonly totalCount: number;
  readonly hasNext: boolean;
}

export function getVault(
  client: ApiClient,
  page = 1,
  pageSize = 20,
  signal?: AbortSignal,
): Promise<Vault> {
  return client.request<Vault>(`/api/v1/giving/vault?page=${page}&pageSize=${pageSize}`, {
    ...(signal ? { signal } : {}),
  });
}

export function createVaultMovement(
  client: ApiClient,
  input: {
    readonly direction: VaultDirection;
    readonly amountCents: number;
    readonly accountId?: string;
    readonly notes?: string;
  },
): Promise<VaultMovement> {
  return client.request<VaultMovement>('/api/v1/giving/vault/movements', {
    method: 'POST',
    body: input,
  });
}

/**
 * Confirma um lançamento previsto: o dinheiro se moveu.
 *
 * **Sem isto uma série periódica é um beco sem saída.** As parcelas nascem
 * `Previsto` para não afirmarem um pagamento que não aconteceu — e, sem uma
 * forma de confirmá-las, nunca entram em fechamento nenhum: o aluguel seria pago
 * no mundo real e continuaria valendo R$ 0,00 no caixa, para sempre.
 *
 * O servidor recusa confirmar data futura, com 409 e o motivo escrito.
 */
export function confirmGivingEntry(
  client: ApiClient,
  id: string,
): Promise<GivingEntryDetail> {
  return client.request<GivingEntryDetail>(`/api/v1/giving/entries/${id}/confirm`, {
    method: 'POST',
  });
}
