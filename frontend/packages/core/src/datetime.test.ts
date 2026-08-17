import { describe, expect, it } from 'vitest';
import {
  daysUntil,
  describeRenewal,
  formatBirthday,
  formatDate,
  formatTime,
  monthName,
  shiftMonth,
  businessMonthRange,
} from './datetime';

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

describe('formatBirthday', () => {
  it('lê dia e mês direto da string, sem passar por Date', () => {
    // Se isto passasse por `new Date('...')`, a meia-noite viraria UTC e um
    // fuso atrás de UTC devolveria o dia anterior — exatamente o bug que a
    // implementação evita não usando Date aqui.
    expect(formatBirthday('1980-12-31')).toBe('31 de dezembro');
    expect(formatBirthday('2000-01-01')).toBe('1 de janeiro');
  });

  it('recusa string que não é data ISO', () => {
    expect(() => formatBirthday('31/12/1980')).toThrow(TypeError);
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

describe('shiftMonth', () => {
  it('anda para frente dentro do mesmo ano', () => {
    expect(shiftMonth({ year: 2026, month: 3 }, 2)).toEqual({ year: 2026, month: 5 });
  });

  it('anda para trás dentro do mesmo ano', () => {
    expect(shiftMonth({ year: 2026, month: 8 }, -3)).toEqual({ year: 2026, month: 5 });
  });

  it('vira o ano para trás em janeiro', () => {
    expect(shiftMonth({ year: 2026, month: 1 }, -1)).toEqual({ year: 2025, month: 12 });
  });

  it('vira o ano para frente em dezembro', () => {
    expect(shiftMonth({ year: 2026, month: 12 }, 1)).toEqual({ year: 2027, month: 1 });
  });

  it('atravessa o ano com passo maior que um mês', () => {
    // O caso que a versão "soma no campo month e corrige com if" erra.
    expect(shiftMonth({ year: 2026, month: 2 }, -5)).toEqual({ year: 2025, month: 9 });
    expect(shiftMonth({ year: 2026, month: 11 }, 5)).toEqual({ year: 2027, month: 4 });
  });

  it('atravessa mais de um ano', () => {
    expect(shiftMonth({ year: 2026, month: 6 }, 25)).toEqual({ year: 2028, month: 7 });
    expect(shiftMonth({ year: 2026, month: 6 }, -25)).toEqual({ year: 2024, month: 5 });
  });

  it('passo zero devolve o mesmo período', () => {
    expect(shiftMonth({ year: 2026, month: 8 }, 0)).toEqual({ year: 2026, month: 8 });
  });
});

describe('monthName', () => {
  it('nomeia os extremos', () => {
    expect(monthName(1)).toBe('janeiro');
    expect(monthName(12)).toBe('dezembro');
  });

  it('recusa mês fora do intervalo', () => {
    expect(() => monthName(0)).toThrow(RangeError);
    expect(() => monthName(13)).toThrow(RangeError);
  });
});

describe('businessMonthRange', () => {
  it('abre o mês na meia-noite de São Paulo, não de UTC', () => {
    // Meia-noite de 1º de agosto em São Paulo (-03:00) é 03:00 UTC do mesmo
    // dia. Usar Date.UTC direto começaria a janela três horas cedo demais e
    // traria eventos do dia 31 do mês anterior.
    const { from } = businessMonthRange({ year: 2026, month: 8 });
    expect(from).toBe('2026-08-01T03:00:00.000Z');
  });

  it('fecha no primeiro instante do mês seguinte', () => {
    const { to } = businessMonthRange({ year: 2026, month: 8 });
    expect(to).toBe('2026-09-01T03:00:00.000Z');
  });

  it('vira o ano em dezembro', () => {
    const { from, to } = businessMonthRange({ year: 2026, month: 12 });
    expect(from).toBe('2026-12-01T03:00:00.000Z');
    expect(to).toBe('2027-01-01T03:00:00.000Z');
  });

  it('a janela de um mês encosta na do seguinte, sem buraco nem sobreposição', () => {
    // Semiaberto: o fim de agosto é exatamente o começo de setembro. Um evento
    // não pode cair entre as duas janelas nem aparecer nas duas.
    const agosto = businessMonthRange({ year: 2026, month: 8 });
    const setembro = businessMonthRange({ year: 2026, month: 9 });
    expect(agosto.to).toBe(setembro.from);
  });

  it('cobre fevereiro bissexto até o fim', () => {
    const { to } = businessMonthRange({ year: 2028, month: 2 });
    expect(to).toBe('2028-03-01T03:00:00.000Z');
  });
});
