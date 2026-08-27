/**
 * Catálogo de produtos.
 *
 * Os IDs precisam ser cadastrados na App Store Connect e no Google Play
 * Console com exatamente estes valores. Enquanto a loja não está ligada,
 * `purchases.ts` opera em modo local e concede o acesso na hora.
 */

export interface Product {
  id: string;
  title: string;
  /** Preço de vitrine. Em produção vem da loja, já localizado. */
  price: string;
  period: 'mensal' | 'anual' | 'vitalicio';
  /** Texto de apoio abaixo do preço. */
  note?: string;
  highlight?: boolean;
}

export const PRODUCTS: Product[] = [
  {
    id: 'premium_monthly',
    title: 'Mensal',
    price: 'R$ 14,90',
    period: 'mensal',
    note: 'Cancele quando quiser',
  },
  {
    id: 'premium_yearly',
    title: 'Anual',
    price: 'R$ 89,90',
    period: 'anual',
    note: 'Equivale a R$ 7,49/mês — economia de 50%',
    highlight: true,
  },
  {
    id: 'premium_lifetime',
    title: 'Vitalício',
    price: 'R$ 249,90',
    period: 'vitalicio',
    note: 'Pagamento único, para sempre',
  },
];

/** Benefícios listados na tela de assinatura. */
export const PREMIUM_BENEFITS = [
  { icon: 'close-circle-outline', label: 'Sem anúncios entre os treinos' },
  { icon: 'albums-outline', label: 'Baralhos e cards ilimitados' },
  { icon: 'mic-outline', label: 'Áudio na frente e no verso de todos os cards' },
  { icon: 'snow-outline', label: 'Protetores de ofensiva todo mês' },
  { icon: 'stats-chart-outline', label: 'Estatísticas completas e previsão de revisões' },
  { icon: 'options-outline', label: 'Meta diária e limites personalizados' },
] as const;

/** Limites da versão gratuita. */
export const FREE_LIMITS = {
  decks: 3,
  cardsPerDeck: 60,
  /** Só a frente pode ter áudio no plano gratuito. */
  audioSides: 1,
} as const;
