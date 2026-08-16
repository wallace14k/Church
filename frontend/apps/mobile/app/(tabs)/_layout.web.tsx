import { ROLES, type Role } from '@congrega/core/identity';
import { Sidebar } from '@congrega/ui/Sidebar';
import { useTheme } from '@congrega/ui/theme';
import { Feather } from '@expo/vector-icons';
import { Redirect, Slot, router, usePathname } from 'expo-router';
import { View } from 'react-native';
import { useSession } from '../../src/session';
import { useSidebarCollapsed } from '../../src/useSidebarCollapsed';
import { useTenants } from '../../src/useTenants';

const NOME_DO_PAPEL: Record<Role, string> = {
  [ROLES.churchAdmin]: 'Administração',
  [ROLES.treasurer]: 'Tesouraria',
  [ROLES.cellLeader]: 'Liderança de célula',
  [ROLES.childcareStaff]: 'Ministério infantil',
  [ROLES.member]: 'Membro',
};

/**
 * Casca da aplicação autenticada no navegador: sidebar fixa à esquerda, no
 * padrão de dashboard SaaS, em vez da barra de abas do celular.
 *
 * Resolvido pelo Metro por extensão de plataforma — este arquivo só entra no
 * bundle web; `_layout.tsx` (irmão, sem `.web`) continua servindo iOS e
 * Android com `Tabs`. As duas casas apontam para as mesmas telas dentro de
 * `(tabs)/`, então nenhuma tela precisa saber qual casca a envolve.
 */
export default function TabsLayoutWeb() {
  const theme = useTheme();
  const pathname = usePathname();
  const { session, status, sair } = useSession();
  const { tenants, atual, trocar } = useTenants();
  const [recolhida, alternarRecolhida] = useSidebarCollapsed();

  if (status === 'anonimo') {
    return <Redirect href="/entrar" />;
  }

  if (session === null) {
    return null;
  }

  const roleLabel =
    session.roles.length > 0
      ? session.roles.map((papel) => NOME_DO_PAPEL[papel as Role] ?? papel).join(' · ')
      : null;

  return (
    <View style={{ flex: 1, flexDirection: 'row', backgroundColor: theme.colors.background }}>
      <Sidebar
        items={[
          {
            key: 'inicio',
            label: 'Início',
            active: pathname.startsWith('/inicio'),
            icon: (
              <Feather
                name="home"
                size={18}
                color={pathname.startsWith('/inicio') ? theme.colors.brand : theme.colors.textMuted}
              />
            ),
            onPress: () => router.push('/inicio'),
          },
          {
            key: 'membros',
            label: 'Membros',
            active: pathname.startsWith('/membros'),
            icon: (
              <Feather
                name="users"
                size={18}
                color={pathname.startsWith('/membros') ? theme.colors.brand : theme.colors.textMuted}
              />
            ),
            onPress: () => router.push('/membros'),
          },
        ]}
        tenantName={session.tenantId !== null ? (atual?.name ?? 'Sua igreja') : 'Congrega+'}
        tenants={tenants}
        onSelectTenant={(id) => void trocar(id)}
        roleLabel={roleLabel}
        onSignOut={() => void sair().then(() => router.replace('/entrar'))}
        collapsed={recolhida}
        onToggleCollapsed={alternarRecolhida}
      />

      <View style={{ flex: 1, overflow: 'hidden' }}>
        <Slot />
      </View>
    </View>
  );
}
