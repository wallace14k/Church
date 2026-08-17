/**
 * Leitura de CSV.
 *
 * Escrito à mão em vez de importar uma biblioteca: o formato usado aqui é
 * puramente para a importação de membros, e a única parte que uma lib
 * economizaria — campo entre aspas com vírgula, aspas ou quebra de linha
 * dentro — é pequena o bastante para caber em uma função testada.
 */

export interface ParsedCsv {
  readonly headers: readonly string[];
  readonly rows: readonly (readonly string[])[];
}

/**
 * Separa por vírgula ou ponto e vírgula, o que aparecer mais na primeira
 * linha. Planilha exportada por Excel em `pt-BR` costuma usar `;` porque `,`
 * já é o separador decimal daquele locale — sem isso, toda linha viraria uma
 * coluna só.
 */
function detectDelimiter(firstLine: string): ',' | ';' {
  const commas = (firstLine.match(/,/gu) ?? []).length;
  const semicolons = (firstLine.match(/;/gu) ?? []).length;
  return semicolons > commas ? ';' : ',';
}

/**
 * Parser de uma linha lógica de CSV (RFC 4180: aspas duplicadas escapam
 * aspas dentro de um campo entre aspas).
 */
function splitRow(line: string, delimiter: string): string[] {
  const fields: string[] = [];
  let current = '';
  let insideQuotes = false;

  for (let i = 0; i < line.length; i += 1) {
    const char = line[i];

    if (insideQuotes) {
      if (char === '"') {
        if (line[i + 1] === '"') {
          current += '"';
          i += 1;
        } else {
          insideQuotes = false;
        }
      } else {
        current += char;
      }
      continue;
    }

    if (char === '"') {
      insideQuotes = true;
    } else if (char === delimiter) {
      fields.push(current);
      current = '';
    } else {
      current += char;
    }
  }

  fields.push(current);
  return fields;
}

/**
 * Divide o texto inteiro em linhas lógicas, respeitando quebra de linha
 * dentro de um campo entre aspas — sem isso, um endereço com `\n` num campo
 * quebraria a contagem de linhas do arquivo inteiro.
 */
function splitLogicalLines(text: string): string[] {
  const lines: string[] = [];
  let current = '';
  let insideQuotes = false;

  const normalized = text.replace(/\r\n/gu, '\n').replace(/\r/gu, '\n');

  for (const char of normalized) {
    if (char === '"') {
      insideQuotes = !insideQuotes;
      current += char;
    } else if (char === '\n' && !insideQuotes) {
      lines.push(current);
      current = '';
    } else {
      current += char;
    }
  }

  if (current.length > 0) {
    lines.push(current);
  }

  return lines;
}

/**
 * Lê um CSV em memória: primeira linha é cabeçalho, o resto são dados.
 * Linhas totalmente vazias (comuns no fim do arquivo) são descartadas.
 */
export function parseCsv(text: string): ParsedCsv {
  const logicalLines = splitLogicalLines(text.replace(/^﻿/u, '')).filter(
    (line) => line.trim().length > 0,
  );

  if (logicalLines.length === 0) {
    return { headers: [], rows: [] };
  }

  const delimiter = detectDelimiter(logicalLines[0]!);
  const [headerLine, ...dataLines] = logicalLines;

  const headers = splitRow(headerLine!, delimiter).map((h) => h.trim());
  const rows = dataLines.map((line) => splitRow(line, delimiter).map((f) => f.trim()));

  return { headers, rows };
}
