import type { GivingKind, GivingMethod, GivingRecurrence } from '@congrega/api-client/giving';

/**
 * Rótulos das formas de pagamento — **todas**, inclusive as que o formulário
 * não oferece.
 *
 * Módulo próprio, e não exportado da tela de lançar: importar de um arquivo de
 * rota do expo-router acopla a listagem ao ciclo de vida de uma tela, e o
 * roteador trata esses arquivos como pontos de entrada, não como bibliotecas.
 *
 * <b>Uma tabela só.</b> Duas divergiriam, e o sintoma seria a listagem
 * mostrando `CartaoCredito` cru no dia em que alguém corrigisse apenas o
 * formulário.
 */
export const ROTULO_DO_METODO: Record<GivingMethod, string> = {
  Dinheiro: 'Dinheiro',
  Pix: 'Pix',
  /** Legado: gravado antes de crédito e débito serem separados. */
  Cartao: 'Cartão',
  Transferencia: 'Transferência',
  Cheque: 'Cheque',
  Outro: 'Outro',
  CartaoCredito: 'Cartão de crédito',
  CartaoDebito: 'Cartão de débito',
  Boleto: 'Boleto',
};

export const ROTULO_DO_TIPO: Record<GivingKind, string> = {
  Entrada: 'Entrada',
  Saida: 'Saída',
  /** Só aparece em categoria: um lançamento nunca é "ambos". */
  Ambos: 'Entrada ou saída',
};

export const ROTULO_DA_FREQUENCIA: Record<GivingRecurrence, string> = {
  Semanal: 'semanal',
  Mensal: 'mensal',
  Anual: 'anual',
};
