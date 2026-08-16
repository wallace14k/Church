import { describe, expect, it } from 'vitest';
import {
  formatCpf,
  isCompleteOtp,
  isProbablyEmail,
  isValidCpf,
  normalizeEmail,
  sanitizeOtpInput,
} from './validation';

describe('normalizeEmail', () => {
  it('normaliza igual ao backend', () => {
    // Precisa bater com User.NormalizeEmail. Se divergir, o app manda
    // "Joao@Igreja.com", o backend guarda "joao@igreja.com", e o usuário jura
    // que já tem cadastro enquanto o sistema cria uma segunda conta.
    expect(normalizeEmail('  JOAO@Igreja.COM  ')).toBe('joao@igreja.com');
  });
});

describe('isProbablyEmail', () => {
  it.each(['joao@igreja.com', 'ana.paula@congrega.app', 'a@b.co'])('aceita %s', (value) => {
    expect(isProbablyEmail(value)).toBe(true);
  });

  it.each(['', 'joao', 'joao@', '@igreja.com', 'joao igreja.com', 'joao@igreja'])(
    'rejeita %s',
    (value) => {
      expect(isProbablyEmail(value)).toBe(false);
    },
  );
});

describe('sanitizeOtpInput', () => {
  it('mantém apenas dígitos e corta no tamanho do código', () => {
    // Colar "123 456" do e-mail é o caminho mais comum, e precisa funcionar.
    expect(sanitizeOtpInput('123 456')).toBe('123456');
    expect(sanitizeOtpInput('12a3b4')).toBe('1234');
    expect(sanitizeOtpInput('1234567890')).toBe('123456');
  });
});

describe('isCompleteOtp', () => {
  it('exige exatamente seis dígitos', () => {
    expect(isCompleteOtp('123456')).toBe(true);
    expect(isCompleteOtp('12345')).toBe(false);
    expect(isCompleteOtp('1234567')).toBe(false);
  });

  it('aceita código começando com zero', () => {
    // O backend gera com PadLeft; tratar como número perderia o zero à esquerda
    // e travaria 10% dos códigos.
    expect(isCompleteOtp('000123')).toBe(true);
  });
});

describe('isValidCpf', () => {
  it('aceita CPF com dígitos verificadores corretos', () => {
    expect(isValidCpf('529.982.247-25')).toBe(true);
    expect(isValidCpf('52998224725')).toBe(true);
  });

  it('rejeita dígito verificador errado', () => {
    expect(isValidCpf('529.982.247-26')).toBe(false);
  });

  it('rejeita sequências repetidas', () => {
    // Passam no cálculo dos dígitos mas não existem como documento. É o caso
    // que quase toda implementação esquece.
    expect(isValidCpf('111.111.111-11')).toBe(false);
    expect(isValidCpf('00000000000')).toBe(false);
  });

  it('rejeita tamanho errado', () => {
    expect(isValidCpf('123')).toBe(false);
  });
});

describe('formatCpf', () => {
  it('formata progressivamente enquanto o usuário digita', () => {
    expect(formatCpf('529')).toBe('529');
    expect(formatCpf('529982')).toBe('529.982');
    expect(formatCpf('529982247')).toBe('529.982.247');
    expect(formatCpf('52998224725')).toBe('529.982.247-25');
  });
});
