import type { ReactNode } from 'react';
import { ActivityIndicator, View } from 'react-native';
import { EmptyState } from './EmptyState';
import { SignatureButton } from './SignatureButton';
import { useTheme } from './theme';

/**
 * Falha classificada, no formato que `describeFailure` produz.
 *
 * Declarada aqui por estrutura, e não importada de `@congrega/api-client`, de
 * propósito: `@congrega/ui` não tem dependência nenhuma e não deve conhecer a
 * camada de rede. A tipagem estrutural do TypeScript liga os dois lados sem
 * inverter o sentido da dependência.
 */
export interface AsyncFailure {
  readonly title: string | null;
  readonly description: string;
  readonly canRetry: boolean;
}

export interface AsyncContentProps {
  readonly loading: boolean;

  /**
   * Esqueleto com a forma do conteudo, para o carregamento.
   *
   * Quando ausente, cai no indicador central — que continua correto para area
   * pequena ou de forma imprevisivel. Onde a forma e conhecida, o esqueleto
   * evita o salto de layout que o indicador provoca ao sumir.
   */
  readonly skeleton?: ReactNode;

  /** A falha, já classificada. `null` quando não houve nenhuma. */
  readonly failure: AsyncFailure | null;

  /**
   * Título contextual da tela — "Não deu para carregar a agenda".
   *
   * Só aparece quando a falha **não** tem título próprio. Um problema de
   * permissão diz "Acesso não autorizado" e este texto não entra: usar o mesmo
   * título para causas diferentes é o que faz o usuário tentar resolver a coisa
   * errada.
   */
  readonly errorTitle: string;

  /**
   * Ação de recuperação.
   *
   * Só é renderizada quando a falha admite retentativa. Passar `onRetry` numa
   * falha de permissão não produz botão — a decisão é da natureza do erro, não
   * de quem chama, porque quem chama não tem como saber qual erro veio.
   */
  readonly onRetry?: () => void;

  /** Retentativa em curso: o botão mostra progresso e para de aceitar cliques. */
  readonly retrying?: boolean;

  readonly isEmpty?: boolean;

  /**
   * O vazio, já montado pelo chamador.
   *
   * Fica fora deste componente de propósito: "nenhum membro cadastrado" e
   * "ninguém faz aniversário este mês" pedem texto e ação próprios, e um
   * componente que tentasse gerá-los acabaria com um vazio genérico em todas as
   * telas. O que se padroniza aqui é **quando** mostrar, não o quê.
   */
  readonly empty?: ReactNode;

  /**
   * A área ocupa a tela inteira (lista principal) em vez de ficar em linha
   * dentro de um contêiner que já tem seu próprio espaçamento.
   *
   * Governa as duas coisas juntas — centralização do indicador e recuo do
   * erro/vazio — porque elas sempre andaram juntas nas telas: quem preenche a
   * tela precisa do próprio recuo, quem está em linha herda o do contêiner.
   */
  readonly fill?: boolean;

  readonly children: ReactNode;
}

/**
 * Decide entre carregando, erro, vazio e conteúdo.
 *
 * A tríade estava copiada em oito telas, com divergências que ninguém escolheu.
 * Reunir aqui também fechou dois "tentar de novo" que não funcionavam.
 *
 * **A falha chega classificada, não como texto.** A versão anterior recebia
 * `error: string` — já passado por `describeError` — e com isso perdia a
 * categoria: um 403 ganhava o título "não deu para carregar" e um botão de
 * retentativa que ia falhar idêntico. Categoria é decisão de apresentação, e
 * jogá-la fora antes de chegar aqui era o erro de desenho.
 */
export function AsyncContent({
  loading,
  skeleton,
  failure,
  errorTitle,
  onRetry,
  retrying = false,
  isEmpty = false,
  empty,
  fill = false,
  children,
}: AsyncContentProps) {
  const theme = useTheme();

  if (loading) {
    if (skeleton !== undefined) {
      return (
        <View accessibilityRole="progressbar" accessibilityLabel="Carregando">
          {skeleton}
        </View>
      );
    }

    return (
      <View
        accessibilityRole="progressbar"
        accessibilityLabel="Carregando"
        style={
          fill
            ? { flex: 1, alignItems: 'center', justifyContent: 'center' }
            : { paddingVertical: theme.space[32], alignItems: 'center' }
        }
      >
        <ActivityIndicator color={theme.colors.text} />
      </View>
    );
  }

  if (failure !== null) {
    const podeTentar = failure.canRetry && onRetry !== undefined;

    return (
      <Moldura fill={fill}>
        <EmptyState
          title={failure.title ?? errorTitle}
          description={failure.description}
          {...(podeTentar
            ? {
                action: (
                  <SignatureButton
                    label={retrying ? 'Carregando' : 'Tentar de novo'}
                    loading={retrying}
                    onPress={onRetry!}
                  />
                ),
              }
            : {})}
        />
      </Moldura>
    );
  }

  if (isEmpty && empty !== undefined) {
    return <Moldura fill={fill}>{empty}</Moldura>;
  }

  return <>{children}</>;
}

/** Recuo só quando a área é a tela; em linha, o contêiner já espaça. */
function Moldura({ fill, children }: { readonly fill: boolean; readonly children: ReactNode }) {
  const theme = useTheme();

  if (!fill) {
    return <>{children}</>;
  }

  return (
    <View style={{ paddingHorizontal: theme.space[24], paddingTop: theme.space[32] }}>
      {children}
    </View>
  );
}
