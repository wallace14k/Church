import type { ReactNode } from 'react';
import { useState } from 'react';
import { Pressable, View, useWindowDimensions } from 'react-native';
import { Brandmark } from './Brandmark';
import { Text } from './Text';
import { useTheme } from './theme';

export interface TopbarNavItem {
  readonly key: string;
  readonly label: string;
  readonly icon: (ativo: boolean) => ReactNode;
  readonly active: boolean;
  readonly onPress: () => void;
}

export interface TopbarTenant {
  readonly id: string;
  readonly name: string;
}

export interface TopbarProps {
  readonly items: readonly TopbarNavItem[];
  readonly tenantName: string | null;
  readonly tenants: readonly TopbarTenant[];
  readonly onSelectTenant: (tenantId: string) => void;
  readonly userName: string | null;
  readonly roleLabel: string | null;
  readonly onSignOut: () => void;
  /** Ícone da igreja, à esquerda do nome. */
  readonly tenantIcon: ReactNode;
  /** Sino decorativo. Ver a nota sobre por que ele não é clicável. */
  readonly bellIcon: ReactNode;
}

/**
 * Abaixo disso o rótulo da navegação some e sobra só o ícone.
 *
 * Não é breakpoint de dispositivo: é a largura em que marca + seletor de igreja
 * + cinco rótulos + perfil param de caber numa linha. Medido com o conteúdo
 * real, não escolhido de uma tabela.
 */
const LARGURA_PARA_ROTULOS = 1180;

/** Abaixo disso o seletor de igreja também sai — o nome dela já está no painel. */
const LARGURA_PARA_SELETOR = 1180;

/** Abaixo disso o nome e o papel do usuário saem, e fica só o avatar. */
const LARGURA_PARA_PERFIL = 900;

/**
 * Abaixo disso a navegação desce para uma segunda linha.
 *
 * É a largura em que marca + cinco ícones + sino + avatar param de caber numa
 * linha só. Medido, não escolhido: em 390px o conteúdo chegava a 469px e o
 * avatar saía pela borda.
 */
const LARGURA_PARA_UMA_LINHA = 760;

/**
 * Barra de navegação superior.
 *
 * **Substitui a `Sidebar` no navegador.** A troca não é de gosto: numa tela de
 * painel, a coluna de 240px à esquerda cobrava um sexto da largura permanente
 * para cinco links que ninguém relê, e era ela que espremia a grade de cartões
 * em telas de 1280px. Horizontal, a navegação ocupa 78px de altura uma vez e
 * devolve a largura inteira para o conteúdo.
 *
 * Só existe no navegador — no celular a navegação continua em barra de abas,
 * que é o padrão nativo esperado em iOS/Android. Ver `(tabs)/_layout.web.tsx`.
 *
 * O seletor de igreja é real: a mesma pessoa pode ter vínculo com mais de uma
 * congregação, e a troca já existe na API (`/auth/tenants` + `/auth/refresh`
 * com `switchToTenantId`).
 */
export function Topbar({
  items,
  tenantName,
  tenants,
  onSelectTenant,
  userName,
  roleLabel,
  onSignOut,
  tenantIcon,
  bellIcon,
}: TopbarProps) {
  const theme = useTheme();
  const { width } = useWindowDimensions();
  const [seletorAberto, setSeletorAberto] = useState(false);
  const [menuAberto, setMenuAberto] = useState(false);

  const comRotulos = width >= LARGURA_PARA_ROTULOS;
  const comSeletor = width >= LARGURA_PARA_SELETOR && tenants.length > 0;
  const comPerfil = width >= LARGURA_PARA_PERFIL;
  const empilhado = width < LARGURA_PARA_UMA_LINHA;

  return (
    <View
      style={{
        backgroundColor: theme.colors.surface,
        borderBottomWidth: 1,
        borderBottomColor: theme.colors.hairline,
        zIndex: 20,
      }}
    >
      <View
        style={{
          width: '100%',
          maxWidth: theme.layout.pageMaxWidth,
          alignSelf: 'center',
          minHeight: theme.layout.topbarHeight,
          flexDirection: 'row',
          alignItems: 'center',
          gap: theme.space[16],
          paddingHorizontal: theme.space[20],
          // Em tela estreita a navegação desce para uma segunda linha.
          //
          // Sem isto, marca + cinco ícones + sino + avatar somam mais de 400px
          // e o avatar sai pela borda direita — cortado mas ainda clicável, que
          // é a pior combinação possível. Medido: em 390px o conteúdo chegava a
          // 469px.
          flexWrap: empilhado ? 'wrap' : 'nowrap',
          paddingVertical: empilhado ? theme.space[8] : 0,
        }}
      >
        <View style={{ flexDirection: 'row', alignItems: 'center', gap: theme.space[8] }}>
          <View
            style={{
              width: 36,
              height: 36,
              borderRadius: theme.radius.inputs,
              alignItems: 'center',
              justifyContent: 'center',
              backgroundColor: theme.colors.surfaceAccentSoft,
            }}
          >
            <Brandmark size={22} color={theme.colors.textOnAccentSoft} />
          </View>
          <Text variant="headingSm" style={{ letterSpacing: -0.6 }}>
            Congrega
          </Text>
        </View>

        {comSeletor && (
          <View>
            <Pressable
              onPress={() => setSeletorAberto((a) => !a)}
              disabled={tenants.length <= 1}
              accessibilityRole="button"
              accessibilityLabel={tenants.length > 1 ? 'Trocar de igreja' : 'Igreja ativa'}
              accessibilityState={{ expanded: seletorAberto }}
              style={({ pressed }) => ({
                flexDirection: 'row',
                alignItems: 'center',
                gap: theme.space[8],
                paddingVertical: theme.space[8],
                paddingHorizontal: theme.space[12],
                borderRadius: theme.radius.smallCards,
                borderWidth: 1,
                borderColor: theme.colors.hairline,
                backgroundColor:
                  pressed || seletorAberto ? theme.colors.surfaceAccentSoft : theme.colors.surfaceInner,
              })}
            >
              <View
                style={{
                  width: 34,
                  height: 34,
                  borderRadius: theme.radius.inputs,
                  alignItems: 'center',
                  justifyContent: 'center',
                  backgroundColor: theme.colors.surfaceAccentSoft,
                }}
              >
                {tenantIcon}
              </View>

              <View>
                <Text variant="eyebrow" tone="muted">
                  IGREJA ATIVA
                </Text>
                <Text variant="caption" numberOfLines={1} style={{ maxWidth: 160 }}>
                  {tenantName ?? 'Selecionar igreja'}
                </Text>
              </View>
            </Pressable>

            {seletorAberto && tenants.length > 1 && (
              <View
                style={{
                  position: 'absolute',
                  top: '100%',
                  left: 0,
                  marginTop: theme.space[4],
                  minWidth: 220,
                  borderRadius: theme.radius.smallCards,
                  backgroundColor: theme.colors.surface,
                  borderWidth: 1,
                  borderColor: theme.colors.hairline,
                  overflow: 'hidden',
                  ...theme.elevation.popover,
                }}
              >
                {tenants.map((tenant) => (
                  <Pressable
                    key={tenant.id}
                    onPress={() => {
                      setSeletorAberto(false);
                      onSelectTenant(tenant.id);
                    }}
                    accessibilityRole="button"
                    style={({ pressed }) => ({
                      paddingVertical: theme.space[12],
                      paddingHorizontal: theme.space[16],
                      backgroundColor: pressed ? theme.colors.surfaceAccentSoft : 'transparent',
                    })}
                  >
                    <Text variant="captionBody" numberOfLines={1}>
                      {tenant.name}
                    </Text>
                  </Pressable>
                ))}
              </View>
            )}
          </View>
        )}

        <View
          style={{
            flexDirection: 'row',
            gap: theme.space[4],
            alignItems: 'center',
            // Empilhada, ocupa a linha inteira e distribui os itens; em linha
            // única, é empurrada para a direita pela margem automática.
            ...(empilhado
              ? { order: 3, width: '100%', justifyContent: 'space-between' }
              : { marginLeft: 'auto' }),
          }}
        >
          {items.map((item) => (
            <ItemDeNavegacao key={item.key} item={item} comRotulo={comRotulos} />
          ))}
        </View>

        <View
          style={{
            flexDirection: 'row',
            alignItems: 'center',
            gap: theme.space[8],
            ...(empilhado ? { marginLeft: 'auto' } : {}),
          }}
        >
          {/* Sino decorativo, e a ausência de `Pressable` é a decisão.
              Notificações não existem: não há tabela, endpoint nem push. Um
              sino que abre nada — ou uma lista vazia permanente — ensina o
              usuário a ignorar o canto onde os avisos reais vão aparecer.
              Inerte e fora do alcance do teclado, ele reserva o lugar sem
              prometer o que o sistema não faz. */}
          <View
            accessibilityElementsHidden
            importantForAccessibility="no-hide-descendants"
            focusable={false}
            style={{
              width: 40,
              height: 40,
              borderRadius: theme.radius.inputs,
              borderWidth: 1,
              borderColor: theme.colors.hairline,
              alignItems: 'center',
              justifyContent: 'center',
              backgroundColor: theme.colors.surface,
            }}
          >
            {bellIcon}
          </View>

          {userName !== null && (
            <View>
              <Pressable
                onPress={() => setMenuAberto((a) => !a)}
                accessibilityRole="button"
                accessibilityLabel={roleLabel === null ? userName : `${userName}, ${roleLabel}`}
                accessibilityState={{ expanded: menuAberto }}
                style={({ pressed }) => ({
                  flexDirection: 'row',
                  alignItems: 'center',
                  gap: theme.space[8],
                  paddingVertical: theme.space[4],
                  paddingHorizontal: theme.space[4],
                  borderRadius: theme.radius.smallCards,
                  backgroundColor: pressed || menuAberto ? theme.colors.surfaceInner : 'transparent',
                })}
              >
                <Avatar nome={userName} />

                {comPerfil && (
                  <>
                    <View style={{ maxWidth: 150 }}>
                      <Text variant="caption" numberOfLines={1}>
                        {userName}
                      </Text>
                      {roleLabel !== null && (
                        <Text variant="captionBody" tone="muted" numberOfLines={1}>
                          {roleLabel}
                        </Text>
                      )}
                    </View>
                    <Text variant="captionBody" tone="muted">
                      ⌄
                    </Text>
                  </>
                )}
              </Pressable>

              {menuAberto && (
                <View
                  style={{
                    position: 'absolute',
                    top: '100%',
                    right: 0,
                    marginTop: theme.space[4],
                    minWidth: 180,
                    borderRadius: theme.radius.smallCards,
                    backgroundColor: theme.colors.surface,
                    borderWidth: 1,
                    borderColor: theme.colors.hairline,
                    overflow: 'hidden',
                    ...theme.elevation.popover,
                  }}
                >
                  <Pressable
                    onPress={() => {
                      setMenuAberto(false);
                      onSignOut();
                    }}
                    accessibilityRole="button"
                    accessibilityLabel="Sair da conta"
                    style={({ pressed }) => ({
                      paddingVertical: theme.space[12],
                      paddingHorizontal: theme.space[16],
                      backgroundColor: pressed ? theme.colors.surfaceInner : 'transparent',
                    })}
                  >
                    <Text variant="captionBody">Sair da conta</Text>
                  </Pressable>
                </View>
              )}
            </View>
          )}
        </View>
      </View>
    </View>
  );
}

/**
 * Um item da navegação.
 *
 * Componente próprio, e não inline no `.map()`, porque o hover é estado de cada
 * item — misturar num único estado no `Topbar` faria passar o mouse por um
 * re-renderizar os outros à toa.
 */
function ItemDeNavegacao({
  item,
  comRotulo,
}: {
  readonly item: TopbarNavItem;
  readonly comRotulo: boolean;
}) {
  const theme = useTheme();
  const [emHover, setEmHover] = useState(false);

  const destacado = item.active || emHover;

  return (
    <Pressable
      onPress={item.onPress}
      onHoverIn={() => setEmHover(true)}
      onHoverOut={() => setEmHover(false)}
      accessibilityRole="link"
      accessibilityLabel={item.label}
      accessibilityState={{ selected: item.active }}
      style={{
        flexDirection: 'row',
        alignItems: 'center',
        gap: theme.space[8],
        minHeight: theme.touch.minTarget,
        paddingHorizontal: comRotulo ? theme.space[12] : theme.space[8],
        borderRadius: theme.radius.inputs,
        backgroundColor: destacado ? theme.colors.surfaceAccentSoft : 'transparent',
      }}
    >
      {item.icon(item.active)}

      {/* O rótulo some por FALTA DE ESPAÇO, e o `accessibilityLabel` acima
          continua carregando o nome — quem usa leitor de tela não perde nada
          quando a janela encolhe. */}
      {comRotulo && (
        <Text variant="caption" tone={item.active ? 'accent' : 'muted'}>
          {item.label}
        </Text>
      )}
    </Pressable>
  );
}

/**
 * Iniciais do nome — no máximo duas, primeira e última palavra.
 *
 * Ignora as partículas ("de", "da", "dos") porque em nome brasileiro elas caem
 * quase sempre no meio: "Ana Paula **de** Souza" precisa render "AS", e pegar
 * as duas primeiras palavras daria "AP" — o nome próprio duplicado, não a
 * identificação da pessoa.
 */
function iniciais(nome: string): string {
  const PARTICULAS = ['de', 'da', 'do', 'das', 'dos', 'e'];

  const partes = nome
    .trim()
    .split(/\s+/)
    .filter((parte) => parte.length > 0 && !PARTICULAS.includes(parte.toLowerCase()));

  if (partes.length === 0) return '?';

  const primeira = partes[0]!.charAt(0);
  const ultima = partes.length > 1 ? partes[partes.length - 1]!.charAt(0) : '';

  return (primeira + ultima).toUpperCase();
}

function Avatar({ nome }: { readonly nome: string }) {
  const theme = useTheme();

  return (
    <View
      style={{
        width: 40,
        height: 40,
        borderRadius: 20,
        flexShrink: 0,
        alignItems: 'center',
        justifyContent: 'center',
        backgroundColor: theme.colors.surfaceAccentSoft,
      }}
      // Decorativo: as iniciais repetem o nome que está ao lado, e no estado
      // estreito o nome vai no `accessibilityLabel` do botão que envolve isto.
      accessibilityElementsHidden
      importantForAccessibility="no-hide-descendants"
    >
      <Text variant="caption" style={{ color: theme.colors.textOnAccentSoft }}>
        {iniciais(nome)}
      </Text>
    </View>
  );
}
