/**
 * Validação de entrada.
 *
 * Tudo aqui é validação de **usabilidade**: avisar cedo, antes da ida ao
 * servidor. O servidor revalida sempre — validação de cliente é conveniência,
 * jamais controle de segurança.
 */

/** Tamanho do código OTP. Precisa bater com `OtpGenerator` no backend. */
export const OTP_LENGTH = 6;

/**
 * Verificação pragmática de e-mail.
 *
 * Deliberadamente permissiva: a regex "completa" da RFC 5322 rejeita endereços
 * válidos e aceita inválidos, e o único teste que realmente vale é o código
 * chegar na caixa. Aqui só se pega erro de digitação óbvio.
 */
export function isProbablyEmail(value: string): boolean {
  const trimmed = value.trim();
  return trimmed.length >= 5 && trimmed.length <= 254 && /^[^\s@]+@[^\s@]+\.[^\s@]{2,}$/u.test(trimmed);
}

/** Normaliza como o backend normaliza — `User.NormalizeEmail`. */
export function normalizeEmail(value: string): string {
  return value.trim().toLowerCase();
}

/** Mantém apenas dígitos e corta no tamanho do código. */
export function sanitizeOtpInput(value: string): string {
  return value.replace(/\D/gu, '').slice(0, OTP_LENGTH);
}

export function isCompleteOtp(value: string): boolean {
  return new RegExp(`^\\d{${OTP_LENGTH}}$`, 'u').test(value);
}

/**
 * Valida CPF pelos dígitos verificadores.
 *
 * Rejeita as sequências repetidas (`111.111.111-11`), que passam no cálculo mas
 * não existem como documento — é o caso que quase toda implementação esquece.
 */
export function isValidCpf(input: string): boolean {
  const digits = input.replace(/\D/gu, '');
  if (digits.length !== 11) return false;
  if (/^(\d)\1{10}$/u.test(digits)) return false;

  const checkDigit = (length: number): number => {
    let sum = 0;
    for (let index = 0; index < length; index += 1) {
      sum += Number(digits[index]) * (length + 1 - index);
    }
    const remainder = (sum * 10) % 11;
    return remainder === 10 ? 0 : remainder;
  };

  return checkDigit(9) === Number(digits[9]) && checkDigit(10) === Number(digits[10]);
}

/** Formata CPF progressivamente, para máscara de campo enquanto se digita. */
export function formatCpf(input: string): string {
  const digits = input.replace(/\D/gu, '').slice(0, 11);
  const parts = [digits.slice(0, 3), digits.slice(3, 6), digits.slice(6, 9)].filter(Boolean);
  const verifier = digits.slice(9, 11);

  const base = parts.join('.');
  return verifier.length > 0 ? `${base}-${verifier}` : base;
}
