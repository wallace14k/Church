import { listGivingEntries, type GivingEntry } from '@congrega/api-client/giving';
import { cents, formatBRL } from '@congrega/core/money';
import { Platform } from 'react-native';
import { apiClient } from './api';
import { ROTULO_DA_FREQUENCIA, ROTULO_DO_METODO } from './rotulosFinanceiros';

/**
 * Exportar está disponível?
 *
 * **Só no navegador.** Baixar arquivo em iOS/Android exige sistema de arquivos
 * mais folha de compartilhamento — outra dependência e outro fluxo. Um botão
 * que aparece no celular e não faz nada seria pior do que não aparecer: ensina
 * o usuário a desconfiar dos outros botões.
 */
export const PODE_EXPORTAR = Platform.OS === 'web';

/**
 * Tamanho da página ao varrer o mês.
 *
 * O teto do servidor é 100. Pedir exatamente o teto minimiza as idas ao banco
 * sem depender de o servidor aceitar mais.
 */
const TAMANHO_DA_PAGINA = 100;

/**
 * Teto de páginas, como rede de segurança.
 *
 * Se o servidor um dia devolver `hasNext` sempre verdadeiro por um bug de
 * paginação, o laço abaixo rodaria para sempre e travaria a aba. 50 páginas são
 * 5.000 lançamentos num mês — muito além do que qualquer igreja lança, e ainda
 * assim finito.
 */
const MAXIMO_DE_PAGINAS = 50;

/**
 * Todos os lançamentos do mês, e não só os que a tela carregou.
 *
 * A listagem já traz 100 por vez, e para quase toda igreja isso é o mês
 * inteiro. **"Quase" não serve aqui:** exportar 100 de 130 lançamentos e
 * chamar o arquivo de "lançamentos de agosto" produz uma prestação de contas
 * incompleta que ninguém tem como perceber olhando o arquivo.
 */
async function buscarTodos(ano: number, mes: number): Promise<readonly GivingEntry[]> {
  const todos: GivingEntry[] = [];

  for (let pagina = 1; pagina <= MAXIMO_DE_PAGINAS; pagina++) {
    const resultado = await listGivingEntries(apiClient, {
      year: ano,
      month: mes,
      page: pagina,
      pageSize: TAMANHO_DA_PAGINA,
    });

    todos.push(...resultado.items);

    if (!resultado.hasNext) break;
  }

  return todos;
}

/**
 * Escapa um campo para CSV.
 *
 * Aspas duplas viram duas aspas, e o campo inteiro é envolvido em aspas. Sem
 * isso, um lançamento com título "Aluguel, 2ª parcela" quebraria a linha em duas
 * colunas — e a planilha do tesoureiro sairia com o valor na coluna errada.
 */
function campo(valor: string | number | null | undefined): string {
  const texto = valor === null || valor === undefined ? '' : String(valor);
  return `"${texto.replace(/"/gu, '""')}"`;
}

const COLUNAS = [
  'Data',
  'Título',
  'Tipo',
  'Categoria',
  'Valor',
  'Forma de pagamento',
  'Conta',
  'Membro',
  'Situação',
  'Recorrência',
  'Observações',
] as const;

function paraLinha(lancamento: GivingEntry): string {
  return [
    campo(lancamento.occurredOn),
    campo(lancamento.description ?? lancamento.categoryName),
    campo(lancamento.kind === 'Saida' ? 'Saída' : 'Entrada'),
    campo(lancamento.categoryName),
    // Com o sinal: uma planilha em que saída e entrada são ambas positivas
    // exige que quem soma saiba de cor qual linha é qual.
    campo(
      (lancamento.kind === 'Saida' ? '-' : '') + formatBRL(cents(lancamento.amountCents)),
    ),
    campo(ROTULO_DO_METODO[lancamento.method] ?? lancamento.method),
    campo(lancamento.accountName),
    campo(lancamento.memberName),
    campo(lancamento.status === 'Previsto' ? 'Previsto' : 'Realizado'),
    campo(
      lancamento.recurrence === null ? '' : ROTULO_DA_FREQUENCIA[lancamento.recurrence],
    ),
    campo(lancamento.notes),
  ].join(';');
}

export interface ResultadoDaExportacao {
  readonly quantidade: number;
  readonly nomeDoArquivo: string;
}

/**
 * Exporta os lançamentos do mês como CSV e entrega o download.
 *
 * <b>Separador `;` e BOM UTF-8</b>, os dois por causa do Excel em português: ele
 * usa ponto-e-vírgula como separador de lista e, sem o BOM, lê o arquivo como
 * ANSI — "Dízimo" vira "DÃ­zimo" na primeira coluna que o tesoureiro abrir.
 */
export async function exportarLancamentosDoMes(
  ano: number,
  mes: number,
): Promise<ResultadoDaExportacao> {
  if (!PODE_EXPORTAR) {
    throw new Error('A exportação está disponível apenas no navegador.');
  }

  const lancamentos = await buscarTodos(ano, mes);

  const csv = [COLUNAS.map(campo).join(';'), ...lancamentos.map(paraLinha)].join('\r\n');

  const nomeDoArquivo = `congrega-financeiro-${ano}-${String(mes).padStart(2, '0')}.csv`;

  const blob = new Blob(['﻿' + csv], { type: 'text/csv;charset=utf-8;' });
  const url = URL.createObjectURL(blob);

  const ancora = document.createElement('a');
  ancora.href = url;
  ancora.download = nomeDoArquivo;
  document.body.appendChild(ancora);
  ancora.click();
  ancora.remove();

  // Sem revogar, cada exportação deixa o arquivo inteiro preso na memória da
  // aba até ela ser fechada.
  URL.revokeObjectURL(url);

  return { quantidade: lancamentos.length, nomeDoArquivo };
}
