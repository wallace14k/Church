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
 *
 * O ícone do item ativo é TINTA, não o lima: o acento do sistema tem 1,19:1
 * sobre superfície clara, e um ícone de 18px nessa cor desapareceria. O que
 * marca o ativo é a lavagem lima atrás do item (`Sidebar`) somada ao contraste
 * do ícone contra o grafite dos inativos.
 */
export default function TabsLayoutWeb() {
  const theme = useTheme();
  const pathname = usePathname();
  const { session, status, sair } = useSession();
  const { tenants, atual, trocar } = useTenants();
  const { recolhida, podeAlternar, alternar } = useSidebarCollapsed();

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

  // Espelha o seed: `ChurchAdmin` recebe `giving.read` e `Treasurer` recebe
  // read + write. Se o seed mudar, isto precisa mudar junto — por isso é dica
  // de interface e não controle: o servidor continua sendo a autoridade.
  const podeVerFinanceiro = session.roles.some(
    (papel) => papel === ROLES.churchAdmin || papel === ROLES.treasurer,
  );

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
                color={pathname.startsWith('/inicio') ? theme.colors.text : theme.colors.textMuted}
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
                color={pathname.startsWith('/membros') ? theme.colors.text : theme.colors.textMuted}
              />
            ),
            onPress: () => router.push('/membros'),
          },
          // Sem gate de papel: a agenda é da congregação inteira, e o backend
          // a expõe pela policy `Tenant.Member`, sem exigir permissão.
          {
            key: 'agenda',
            label: 'Agenda',
            active: pathname.startsWith('/agenda'),
            icon: (
              <Feather
                name="calendar"
                size={18}
                color={pathname.startsWith('/agenda') ? theme.colors.text : theme.colors.textMuted}
              />
            ),
            onPress: () => router.push('/agenda'),
          },
          // Dica de interface, não autorização: quem não tem `giving.read`
          // recebe 403 do servidor de qualquer forma. Esconder o item apenas
          // evita oferecer uma porta que não abre — a mesma disciplina que
          // o `CLAUDE.md` aplica a `subscription_tier`.
          ...(podeVerFinanceiro
            ? [
                {
                  key: 'financeiro',
                  label: 'Financeiro',
                  active: pathname.startsWith('/financeiro'),
                  icon: (
                    <Feather
                      name="trending-up"
                      size={18}
                      color={
                        pathname.startsWith('/financeiro')
                          ? theme.colors.text
                          : theme.colors.textMuted
                      }
                    />
                  ),
                  onPress: () => router.push('/financeiro'),
                },
              ]
            : []),
          // Sem condicional de papel, ao contrário do item financeiro: é a
          // assinatura da PESSOA, não da igreja — aparece mesmo para quem não
          // tem vínculo com nenhuma congregação.
          {
            key: 'assinatura',
            label: 'Congrega+',
            active: pathname.startsWith('/assinatura'),
            icon: (
              <Feather
                name="award"
                size={18}
                color={pathname.startsWith('/assinatura') ? theme.colors.text : theme.colors.textMuted}
              />
            ),
            onPress: () => router.push('/assinatura'),
          },
        ]}
        tenantName={session.tenantId !== null ? (atual?.name ?? 'Sua igreja') : 'Congrega+'}
        tenants={tenants}
        onSelectTenant={(id) => void trocar(id)}
        roleLabel={roleLabel}
        onSignOut={() => void sair().then(() => router.replace('/entrar'))}
        collapsed={recolhida}
        onToggleCollapsed={alternar}
        canToggleCollapsed={podeAlternar}
      />

      <View style={{ flex: 1, overflow: 'hidden' }}>
        <Slot />
      </View>
    </View>
  );
}
