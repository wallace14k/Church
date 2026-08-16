import { describe, expect, it } from 'vitest';
import { daysUntil, describeRenewal, formatDate, formatTime } from './datetime';

describe('formatação no fuso de negócio', () => {
  it('formata data e hora em America/Sao_Paulo', () => {
    // 2026-08-15T22:30:00Z = 19:30 em São Paulo (UTC-3).
    const instant = '2026-08-15T22:30:00Z';
    expect(formatDate(instant)).toBe('15/08/2026');
    expect(formatTime(instant)).toBe('19:30');
  });

  it('usa o fuso de negócio, não o do aparelho', () => {
    // 2026-08-16T01:00:00Z já é dia 16 em UTC, mas ainda é 22h do dia 15 no
    // Brasil. Um membro viajando não pode ver o culto de sábado como domingo.
    expect(formatDate('2026-08-16T01:00:00Z')).toBe('15/08/2026');
  });

  it('recusa data inválida em vez de exibir "Invalid Date"', () => {
    expect(() => formatDate('não é data')).toThrow(TypeError);
  });
});

describe('daysUntil', () => {
  it('conta dias-calendário, não intervalos de 24 horas', () => {
    // Às 23h de hoje, amanhã continua sendo "1 dia". A diferença bruta em
    // milissegundos daria 0 e o aviso sairia errado.
    const now = new Date('2026-08-15T02:00:00Z'); // 23h do dia 14 em São Paulo
    expect(daysUntil('2026-08-15T20:00:00Z', now)).toBe(1); // dia 15 em SP
  });

  it('devolve zero para o próprio dia', () => {
    const now = new Date('2026-08-15T12:00:00Z');
    expect(daysUntil('2026-08-15T23:00:00Z', now)).toBe(0);
  });

  it('devolve negativo para data passada', () => {
    const now = new Date('2026-08-15T12:00:00Z');
    expect(daysUntil('2026-08-12T12:00:00Z', now)).toBe(-3);
  });
});

describe('describeRenewal', () => {
  const now = new Date('2026-08-15T12:00:00Z');

  it.each([
    ['2026-08-30T12:00:00Z', 'Vence em 15 dias'],
    ['2026-08-16T12:00:00Z', 'Vence amanhã'],
    ['2026-08-15T23:00:00Z', 'Vence hoje'],
    ['2026-08-12T12:00:00Z', 'Venceu há 3 dias'],
    ['2026-08-14T12:00:00Z', 'Venceu há 1 dia'],
  ])('descreve %s como "%s"', (periodEnd, expected) => {
    expect(describeRenewal(periodEnd, now)).toBe(expected);
  });

  it('concorda com as janelas do motor de retenção', () => {
    // O texto na tela precisa bater com o e-mail que o usuário recebeu. Se o
    // e-mail diz "faltam 7 dias" e o app diz outra coisa, a mensagem perde
    // credibilidade justamente no momento de renovar.
    expect(describeRenewal('2026-08-22T12:00:00Z', now)).toBe('Vence em 7 dias');
  });
});
