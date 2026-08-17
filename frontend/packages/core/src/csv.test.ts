import { describe, expect, it } from 'vitest';
import { parseCsv } from './csv';

describe('parseCsv', () => {
  it('separa cabeçalho e linhas por vírgula', () => {
    const resultado = parseCsv('nome,email\nMaria,maria@teste.com\nJoão,joao@teste.com');

    expect(resultado.headers).toEqual(['nome', 'email']);
    expect(resultado.rows).toEqual([
      ['Maria', 'maria@teste.com'],
      ['João', 'joao@teste.com'],
    ]);
  });

  it('detecta ponto e vírgula quando é o separador dominante', () => {
    const resultado = parseCsv('nome;email\nMaria;maria@teste.com');

    expect(resultado.headers).toEqual(['nome', 'email']);
    expect(resultado.rows).toEqual([['Maria', 'maria@teste.com']]);
  });

  it('respeita vírgula dentro de campo entre aspas', () => {
    const resultado = parseCsv('nome,cidade\n"Silva, Maria","São Paulo, SP"');

    expect(resultado.rows).toEqual([['Silva, Maria', 'São Paulo, SP']]);
  });

  it('respeita aspas escapadas (duplicadas) dentro de campo entre aspas', () => {
    const resultado = parseCsv('nome,apelido\n"Maria","""Mari"""');

    expect(resultado.rows).toEqual([['Maria', '"Mari"']]);
  });

  it('respeita quebra de linha dentro de campo entre aspas', () => {
    const resultado = parseCsv('nome,notas\nMaria,"linha um\nlinha dois"');

    expect(resultado.rows).toEqual([['Maria', 'linha um\nlinha dois']]);
  });

  it('descarta linhas em branco', () => {
    const resultado = parseCsv('nome,email\nMaria,maria@teste.com\n\n\nJoão,joao@teste.com\n');

    expect(resultado.rows).toHaveLength(2);
  });

  it('remove BOM no início do arquivo', () => {
    const resultado = parseCsv('﻿nome,email\nMaria,maria@teste.com');

    expect(resultado.headers).toEqual(['nome', 'email']);
  });

  it('aparam espaços em volta de cada campo', () => {
    const resultado = parseCsv('nome, email\n Maria , maria@teste.com ');

    expect(resultado.headers).toEqual(['nome', 'email']);
    expect(resultado.rows).toEqual([['Maria', 'maria@teste.com']]);
  });

  it('devolve vazio para texto vazio', () => {
    const resultado = parseCsv('');

    expect(resultado.headers).toEqual([]);
    expect(resultado.rows).toEqual([]);
  });

  it('lida com CRLF', () => {
    const resultado = parseCsv('nome,email\r\nMaria,maria@teste.com\r\n');

    expect(resultado.rows).toEqual([['Maria', 'maria@teste.com']]);
  });
});
