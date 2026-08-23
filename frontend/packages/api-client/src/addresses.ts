import type { ApiClient } from './client';

/**
 * Tipo de moradia.
 *
 * `Sitio` e `Outro` entraram depois dos dois primeiros: uma igreja no interior
 * cadastra sítio o tempo todo, e sem o valor essas pessoas caíam em `Casa` —
 * que não é falso o bastante para alguém notar, nem verdadeiro o bastante para
 * servir a nada.
 */
export type ResidenceType = 'Casa' | 'Apartamento' | 'Sitio' | 'Outro';

/** O endereço como ele volta do servidor. */
export interface Address {
  readonly id: string;
  /** Formatado, `01001-000`. */
  readonly cep: string | null;
  readonly logradouro: string | null;
  readonly bairro: string | null;
  readonly localidade: string | null;
  readonly estado: string | null;
  readonly residenceType: ResidenceType;
  readonly numero: string | null;
  readonly andar: string | null;
}

/**
 * O endereço como ele vai para o servidor.
 *
 * **Ausente significa "não mexa"**, e um objeto com todos os campos em branco
 * significa "apague". A distinção existe para que quem edita só o telefone de
 * um membro não perca o endereço dele por omissão.
 */
export interface AddressPayload {
  readonly cep?: string;
  readonly logradouro?: string;
  readonly bairro?: string;
  readonly localidade?: string;
  readonly estado?: string;
  readonly residenceType?: ResidenceType;
  readonly numero?: string;
  readonly andar?: string;
}

/** O endereço postal de um CEP. */
export interface PostalCodeResult {
  readonly cep: string;
  readonly logradouro: string;
  readonly bairro: string;
  readonly localidade: string;
  readonly estado: string;
  /**
   * De onde veio: `cache` (banco) ou `viacep`.
   *
   * Não é enfeite de diagnóstico — é o que permite verificar, de fora, que a
   * busca no banco acontece antes da chamada externa. Sem isso, um cache
   * quebrado seria indistinguível de um funcionando, porque as duas respostas
   * têm o mesmo conteúdo.
   */
  readonly source: 'cache' | 'viacep';
}

/** Reduz um CEP a oito dígitos, ou devolve `null` se ainda não for um. */
export function normalizeCep(valor: string | null | undefined): string | null {
  if (valor === null || valor === undefined) return null;
  const digitos = valor.replace(/\D/gu, '');
  return digitos.length === 8 ? digitos : null;
}

/** `01001000` → `01001-000`. Aplica parcialmente, para a máscara enquanto digita. */
export function formatCep(valor: string): string {
  const d = valor.replace(/\D/gu, '').slice(0, 8);
  return d.length <= 5 ? d : `${d.slice(0, 5)}-${d.slice(5)}`;
}

/**
 * Busca o endereço de um CEP.
 *
 * O servidor consulta o banco primeiro e só chama a ViaCEP se não achar. Um
 * **404** significa "não localizamos" e cobre dois casos de propósito — o CEP
 * não existe, ou a ViaCEP não respondeu — porque os dois levam ao mesmo lugar:
 * o preenchimento manual.
 */
export function findPostalCode(client: ApiClient, cep: string): Promise<PostalCodeResult> {
  const normalizado = normalizeCep(cep);

  if (normalizado === null) {
    // Rejeita antes de sair pela rede: um CEP incompleto é o estado normal de
    // quem está digitando, e não vale uma requisição que já se sabe inútil.
    return Promise.reject(new Error('CEP precisa ter 8 dígitos.'));
  }

  return client.request<PostalCodeResult>(`/api/v1/postal-codes/${normalizado}`);
}
