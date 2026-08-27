import { storage } from '../storage';
import type { Entitlements } from '../types';
import { PRODUCTS } from './products';

/**
 * Compras dentro do app.
 *
 * A primeira versão registra a assinatura apenas no aparelho. A troca por uma
 * biblioteca de IAP real (`react-native-iap` ou `expo-in-app-purchases`) fica
 * contida neste arquivo: as telas só conhecem `purchase`, `restore` e
 * `loadEntitlements`.
 */

export const FREE_ENTITLEMENTS: Entitlements = {
  premium: false,
  productId: null,
  expiresAt: null,
  purchasedAt: null,
};

const DAY_MS = 24 * 60 * 60 * 1000;

function expiryFor(productId: string, now: number): number | null {
  const product = PRODUCTS.find((item) => item.id === productId);
  if (!product) return null;
  if (product.period === 'mensal') return now + 30 * DAY_MS;
  if (product.period === 'anual') return now + 365 * DAY_MS;
  return null; // vitalício
}

/** `true` se a assinatura está ativa (vitalícia ou dentro da validade). */
export function isActive(entitlements: Entitlements, now = Date.now()): boolean {
  if (!entitlements.premium) return false;
  if (entitlements.expiresAt == null) return true;
  return entitlements.expiresAt > now;
}

export async function loadEntitlements(): Promise<Entitlements> {
  const saved = await storage.getEntitlements();
  if (!saved) return FREE_ENTITLEMENTS;
  // Assinatura vencida volta a ser gratuita na leitura.
  return isActive(saved) ? saved : FREE_ENTITLEMENTS;
}

export async function purchase(productId: string): Promise<Entitlements> {
  // Aqui entra a chamada real à loja. O fluxo, o resultado e o tratamento de
  // erro são os mesmos; só a origem do recibo muda.
  const now = Date.now();
  const entitlements: Entitlements = {
    premium: true,
    productId,
    expiresAt: expiryFor(productId, now),
    purchasedAt: now,
  };
  await storage.saveEntitlements(entitlements);
  return entitlements;
}

/**
 * Restaura uma compra anterior. Com a loja ligada, consulta os recibos da
 * conta; localmente, apenas relê o que está salvo.
 */
export async function restore(): Promise<Entitlements> {
  return loadEntitlements();
}
