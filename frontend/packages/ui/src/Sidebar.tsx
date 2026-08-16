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
   * Papéis do usuário na igreja atual, já formatados ("Administração ·
   * Tesouraria"). Não é o nome da pessoa — a sessão não carrega nome nem
   * e-mail, só claims de autorização — então a sidebar identifica pela
   * função, não por uma identidade que o cliente não tem.
   */
  readonly roleLabel: string | null;
  readonly onSignOut: () => void;
  /** Estado controlado pelo chamador — persiste entre sessões via `localStorage`. */
  readonly collapsed: boolean;
  readonly onToggleCollapsed: () => void;
}

/** Largura no estado recolhido: só ícone, sem rótulo. */
const LARGURA_RECOLHIDA = 68;

/**
 * Sidebar fixa à esquerda — o padrão de mercado para dashboard web, no mesmo
 * tratamento do produto de referência: fundo branco (não cinza), item ativo
 * com lavagem índigo suave e ícone/texto na cor de marca, hover em
 * cinza-névoa. Hairline como único traço de separação, sem sombra.
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
  roleLabel,
  onSignOut,
  collapsed,
  onToggleCollapsed,
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

          {!collapsed && (
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

        {collapsed && (
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
                backgroundColor: pressed || seletorAberto ? theme.colors.surfaceNeutral : 'transparent',
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
                  backgroundColor: theme.colors.surface,
                  borderWidth: 1,
                  borderColor: theme.colors.hairline,
                  overflow: 'hidden',
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
                      backgroundColor: pressed ? theme.colors.surfaceNeutral : 'transparent',
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
        {!collapsed && roleLabel !== null && (
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
            ? theme.colors.surfaceTinted
            : pressed
              ? theme.colors.surfaceNeutral
              : 'transparent',
        })}
      >
        {item.icon}
        {!collapsed && (
          <Text
            variant="bodyStrong"
            style={{ color: item.active ? theme.colors.brand : theme.colors.textMuted }}
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
            borderRadius: theme.radius.smallCards / 2,
            backgroundColor: theme.colors.text,
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
