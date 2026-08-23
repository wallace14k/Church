import type { ReactNode } from 'react';
import { useState } from 'react';
import { Pressable, View } from 'react-native';
import { Brandmark } from './Brandmark';
import { Text } from './Text';
import { useTheme } from './theme';

export interface SidebarNavItem {
  readonly key: string;
  readonly label: string;
  readonly icon: ReactNode;
  readonly active: boolean;
  readonly onPress: () => void;
}

export interface SidebarTenant {
  readonly id: string;
  readonly name: string;
}

export interface SidebarProps {
  readonly items: readonly SidebarNavItem[];
  readonly tenantName: string | null;
  readonly tenants: readonly SidebarTenant[];
  readonly onSelectTenant: (tenantId: string) => void;
  /**
   * Nome da pessoa logada.
   *
   * A sessão não carregava isto: até então o cliente só recebia claims de
   * autorização, e a sidebar identificava o usuário pela função. `fullName`
   * passou a viajar em `SessionResponse` justamente para este bloco — sem ele,
   * a alternativa seria imprimir o e-mail ou o UUID, e nenhum dos dois é como
   * uma pessoa se reconhece.
   */
  readonly userName: string | null;

  /**
   * Papéis do usuário na igreja atual, já formatados ("Administração ·
   * Tesouraria"). Fica **sob** o nome: junto, o par responde "quem sou eu e o
   * que posso fazer aqui", que é a pergunta de quem participa de mais de uma
   * igreja com papéis diferentes em cada.
   */
  readonly roleLabel: string | null;
  readonly onSignOut: () => void;
  /** Estado controlado pelo chamador — persiste entre sessões via `localStorage`. */
  readonly collapsed: boolean;
  readonly onToggleCollapsed: () => void;

  /**
   * O usuario pode alternar?
   *
   * Falso quando a largura da janela forca o recolhimento: ali o botao nao teria
   * efeito nenhum, e um controle que nao faz nada ensina o usuario a desconfiar
   * dos outros.
   */
  readonly canToggleCollapsed?: boolean;
}

/** Largura no estado recolhido: só ícone, sem rótulo. */
const LARGURA_RECOLHIDA = 68;

/**
 * Sidebar fixa à esquerda — o padrão de mercado para dashboard web, no
 * tratamento que a §10 pede: superfície pergaminho, item ativo com lavagem
 * lima **sutil**, hover em branco, fio de 1px como único traço de separação e
 * nenhuma sombra.
 *
 * O item ativo não usa lima cheio, e o rótulo não fica na cor de acento: lima
 * sobre pergaminho como texto é ilegível, e lima cheio aqui competiria com o
 * botão primário da tela. O que marca o item ativo é a superfície diluída mais
 * a tinta no rótulo, contra o grafite dos inativos — duas pistas, nenhuma
 * dependendo de percepção de cor.
 *
 * Só existe no navegador — no celular a navegação continua em barra de abas,
 * que é o padrão nativo esperado em iOS/Android. Ver `(tabs)/_layout.web.tsx`.
 *
 * O seletor de igreja no topo troca de lugar com o "workspace switcher" que
 * dashboards de referência costumam ter: aqui ele é real — a mesma pessoa
 * pode ter vínculo com mais de uma igreja, e a troca já existe na API
 * (`/auth/tenants` + `/auth/refresh` com `switchToTenantId`).
 *
 * **Recolhe para só ícones.** O estado em si mora no chamador (`collapsed` é
 * controlado) porque persistência é decisão de app, não de componente de
 * design system — `Sidebar` não deveria saber que `localStorage` existe.
 */
export function Sidebar({
  items,
  tenantName,
  tenants,
  onSelectTenant,
  userName,
  roleLabel,
  onSignOut,
  collapsed,
  onToggleCollapsed,
  canToggleCollapsed = true,
}: SidebarProps) {
  const theme = useTheme();
  const [seletorAberto, setSeletorAberto] = useState(false);

  return (
    <View
      style={{
        width: collapsed ? LARGURA_RECOLHIDA : theme.layout.sidebarWidth,
        flexShrink: 0,
        height: '100%',
        backgroundColor: theme.colors.surface,
        borderRightWidth: 1,
        borderRightColor: theme.colors.hairline,
        paddingVertical: theme.space[20],
        paddingHorizontal: collapsed ? theme.space[8] : theme.space[12],
      }}
    >
      <View style={{ paddingHorizontal: theme.space[8], gap: theme.space[16] }}>
        <View
          style={{
            flexDirection: 'row',
            alignItems: 'center',
            justifyContent: collapsed ? 'center' : 'space-between',
            gap: theme.space[8],
          }}
        >
          <View style={{ flexDirection: 'row', alignItems: 'center', gap: theme.space[8] }}>
            <Brandmark size={22} />
            {!collapsed && <Text variant="bodyStrong">Congrega</Text>}
          </View>

          {!collapsed && canToggleCollapsed && (
            <Pressable
              onPress={onToggleCollapsed}
              accessibilityRole="button"
              accessibilityLabel="Recolher menu"
              hitSlop={8}
              style={({ pressed }) => ({ opacity: pressed ? 0.6 : 1 })}
            >
              <Text variant="body" tone="muted">
                ‹
              </Text>
            </Pressable>
          )}
        </View>

        {collapsed && canToggleCollapsed && (
          <Pressable
            onPress={onToggleCollapsed}
            accessibilityRole="button"
            accessibilityLabel="Expandir menu"
            hitSlop={8}
            style={({ pressed }) => ({ alignSelf: 'center', opacity: pressed ? 0.6 : 1 })}
          >
            <Text variant="body" tone="muted">
              ›
            </Text>
          </Pressable>
        )}

        {!collapsed && tenants.length > 0 && (
          <View>
            <Pressable
              onPress={() => setSeletorAberto((aberto) => !aberto)}
              accessibilityRole="button"
              accessibilityLabel="Trocar de igreja"
              style={({ pressed }) => ({
                flexDirection: 'row',
                alignItems: 'center',
                justifyContent: 'space-between',
                paddingVertical: theme.space[8],
                paddingHorizontal: theme.space[8],
                borderRadius: theme.radius.smallCards,
                backgroundColor: pressed || seletorAberto ? theme.colors.surfaceInner : 'transparent',
              })}
            >
              <Text variant="bodyStrong" numberOfLines={1} style={{ flexShrink: 1 }}>
                {tenantName ?? 'Selecionar igreja'}
              </Text>
              {tenants.length > 1 && (
                <Text variant="captionBody" tone="muted">
                  ⌄
                </Text>
              )}
            </Pressable>

            {seletorAberto && tenants.length > 1 && (
              <View
                style={{
                  marginTop: theme.space[4],
                  borderRadius: theme.radius.smallCards,
                  backgroundColor: theme.colors.surfaceInner,
                  borderWidth: 1,
                  borderColor: theme.colors.hairline,
                  overflow: 'hidden',
                  // A exceção que a §6 abre: menu flutuante precisa se separar
                  // do que está por baixo, e aqui não há diferença de tom para
                  // fazer esse trabalho — o menu cobre a própria sidebar.
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
                    accessibilityLabel={`Trocar para ${tenant.name}`}
                    style={({ pressed }) => ({
                      paddingVertical: theme.space[8],
                      paddingHorizontal: theme.space[12],
                      backgroundColor: pressed ? theme.colors.surfaceInner : 'transparent',
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
      </View>

      <View style={{ marginTop: theme.space[24], gap: 2 }}>
        {/* View simples, não ScrollView: só dois itens hoje, e um ScrollView
            recorta o próprio conteúdo no eixo transversal — a dica flutuante
            do estado recolhido, que precisa extrapolar a largura da barra,
            ficaria cortada na borda em vez de aparecer sobre a área de
            conteúdo. Se a navegação crescer a ponto de precisar rolar, essa é
            a hora de reconsiderar. */}
        {items.map((item) => (
          <NavItemRow key={item.key} item={item} collapsed={collapsed} />
        ))}
      </View>

      <View
        style={{
          marginTop: 'auto',
          paddingTop: theme.space[12],
          borderTopWidth: 1,
          borderTopColor: theme.colors.hairline,
          gap: theme.space[8],
          paddingHorizontal: theme.space[8],
          alignItems: collapsed ? 'center' : 'stretch',
        }}
      >
        {/* Bloco de identidade: avatar de iniciais, nome e papel. Recolhida, só
            o avatar sobrevive — é o que ainda identifica em 68px de largura, e
            some com menos ambiguidade do que um nome truncado em duas letras. */}
        {userName !== null && (
          <View
            style={{
              flexDirection: 'row',
              alignItems: 'center',
              gap: theme.space[8],
              paddingBottom: theme.space[4],
            }}
            // Recolhida, o nome não está escrito em lugar nenhum e as iniciais
            // são decorativas — sem este rótulo o bloco ficaria mudo. Expandida
            // o texto ao lado já diz tudo, e um rótulo aqui faria o leitor de
            // tela anunciar o nome duas vezes.
            {...(collapsed
              ? {
                  accessible: true,
                  accessibilityLabel:
                    roleLabel === null ? userName : `${userName}, ${roleLabel}`,
                }
              : {})}
          >
            <Avatar nome={userName} />

            {!collapsed && (
              <View style={{ flex: 1 }}>
                <Text variant="captionBody" numberOfLines={1}>
                  {userName}
                </Text>
                {roleLabel !== null && (
                  <Text variant="caption" tone="muted" numberOfLines={2}>
                    {roleLabel}
                  </Text>
                )}
              </View>
            )}
          </View>
        )}

        {/* Sem nome — sessão antiga, ou papel sem identidade carregada — o papel
            volta a ser o identificador, que era o comportamento anterior. */}
        {userName === null && !collapsed && roleLabel !== null && (
          <Text variant="captionBody" tone="muted" numberOfLines={1}>
            {roleLabel}
          </Text>
        )}

        <Pressable onPress={onSignOut} accessibilityRole="button" accessibilityLabel="Sair da conta">
          <Text variant="captionBody">{collapsed ? '⏻' : 'Sair da conta'}</Text>
        </Pressable>
      </View>
    </View>
  );
}

/**
 * Iniciais do nome — no máximo duas, primeira e última palavra.
 *
 * Ignora as partículas ("de", "da", "dos") porque em nome brasileiro elas caem
 * quase sempre no meio: "Ana Paula **de** Souza" precisa render "AS", e um
 * `split(' ')` ingênuo pegando as duas primeiras palavras daria "AP" — que é o
 * nome próprio duplicado, não a identificação da pessoa.
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

/**
 * Avatar de iniciais.
 *
 * **Preenchido com o acento**, o que só passou a ser possível com o verde: o
 * lima media 1,19:1 contra branco e 1,4:1 contra a tinta — nenhuma letra ficava
 * legível dentro dele, e o avatar teria de ser um círculo cinza. Ver D1 em
 * `docs/07-design-system.md`.
 *
 * Sem foto por enquanto: `users` não guarda avatar, e "Fornecedor de mídia" é
 * decisão pendente. Iniciais identificam de verdade; um ícone genérico de
 * silhueta identificaria todo mundo igual.
 */
function Avatar({ nome }: { readonly nome: string }) {
  const theme = useTheme();

  return (
    <View
      style={{
        width: 32,
        height: 32,
        borderRadius: 16,
        flexShrink: 0,
        alignItems: 'center',
        justifyContent: 'center',
        backgroundColor: theme.colors.surfaceAccent,
      }}
      // Decorativo: as iniciais repetem o nome que está escrito ao lado, e no
      // estado recolhido o nome vai no `accessibilityLabel` do bloco.
      accessibilityElementsHidden
      importantForAccessibility="no-hide-descendants"
    >
      <Text variant="caption" tone="onAccent" style={{ letterSpacing: 0 }}>
        {iniciais(nome)}
      </Text>
    </View>
  );
}

/**
 * Uma linha de navegação, com a dica flutuante do estado recolhido.
 *
 * Componente próprio, e não inline no `.map()`, porque o hover é estado de
 * cada linha — misturar isso num único estado no `Sidebar` faria hover num
 * item re-renderizar os outros à toa.
 *
 * `onHoverIn`/`onHoverOut` só disparam com mouse (web); em toque, o item
 * simplesmente não mostra dica — comportamento correto, já que não há como
 * "passar por cima" sem tocar.
 */
function NavItemRow({ item, collapsed }: { readonly item: SidebarNavItem; readonly collapsed: boolean }) {
  const theme = useTheme();
  const [emHover, setEmHover] = useState(false);

  return (
    <View style={{ position: 'relative' }}>
      <Pressable
        onPress={item.onPress}
        onHoverIn={() => setEmHover(true)}
        onHoverOut={() => setEmHover(false)}
        accessibilityRole="button"
        accessibilityLabel={item.label}
        accessibilityState={{ selected: item.active }}
        style={({ pressed }) => ({
          flexDirection: 'row',
          alignItems: 'center',
          justifyContent: collapsed ? 'center' : 'flex-start',
          gap: theme.space[8],
          paddingVertical: theme.space[8],
          paddingHorizontal: theme.space[8],
          borderRadius: theme.radius.smallCards,
          backgroundColor: item.active
            ? theme.colors.surfaceAccentSoft
            : pressed
              ? theme.colors.surfaceInner
              : 'transparent',
        })}
      >
        {item.icon}
        {!collapsed && (
          <Text
            variant="bodyStrong"
            style={{ color: item.active ? theme.colors.text : theme.colors.textMuted }}
          >
            {item.label}
          </Text>
        )}
      </Pressable>

      {collapsed && emHover && (
        <View
          pointerEvents="none"
          style={{
            position: 'absolute',
            left: '100%',
            top: '50%',
            transform: [{ translateY: -14 }],
            marginLeft: theme.space[8],
            paddingVertical: theme.space[4],
            paddingHorizontal: theme.space[8],
            borderRadius: theme.radius.inputs,
            backgroundColor: theme.colors.surfaceInverse,
            zIndex: 50,
            elevation: 8,
          }}
        >
          <Text variant="captionBody" tone="onDark" numberOfLines={1}>
            {item.label}
          </Text>
        </View>
      )}
    </View>
  );
}
