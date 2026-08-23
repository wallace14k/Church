import { ROLES, type Role } from '@congrega/core/identity';
import { Topbar, type TopbarNavItem } from '@congrega/ui/Topbar';
import { useTheme } from '@congrega/ui/theme';
import { Feather } from '@expo/vector-icons';
import { Redirect, Slot, router, usePathname } from 'expo-router';
import { ScrollView, View } from 'react-native';
import { useSession } from '../../src/session';
import { useTenants } from '../../src/useTenants';

const NOME_DO_PAPEL: Record<Role, string> = {
  [ROLES.churchAdmin]: 'Administração',
  [ROLES.treasurer]: 'Tesouraria',
  [ROLES.cellLeader]: 'Liderança de célula',
  [ROLES.childcareStaff]: 'Ministério infantil',
  [ROLES.member]: 'Membro',
};

/**
 * Casca da aplicação autenticada no navegador: **barra superior**, em vez da
 * sidebar fixa que este layout tinha antes.
 *
 * A troca não é de gosto. A coluna de 240px cobrava um sexto da largura, de
 * forma permanente, para cinco links que ninguém relê — e era ela que espremia
 * a grade de três cartões do painel em telas de 1280px. Horizontal, a navegação
 * ocupa 78px de altura uma vez e devolve a largura inteira ao conteúdo.
 *
 * Resolvido pelo Metro por extensão de plataforma — este arquivo só entra no
 * bundle web; `_layout.tsx` (irmão, sem `.web`) continua servindo iOS e Android
 * com `Tabs`. As duas cascas apontam para as mesmas telas dentro de `(tabs)/`,
 * então nenhuma tela precisa saber qual a envolve.
 *
 * **O ícone do item ativo é verde-escuro, não o verde de ação.** Sobre a
 * lavagem clara do item selecionado, o verde cheio mede pouco; o escuro passa
 * com folga. E o que marca o ativo não é só a cor: é a lavagem atrás do item
 * somada ao peso do rótulo — duas pistas, nenhuma dependendo de percepção de
 * matiz. Ver a nota sobre luminância de verde e âmbar em `tokens.test.ts`.
 */
export default function TabsLayoutWeb() {
  const theme = useTheme();
  const pathname = usePathname();
  const { session, status, sair } = useSession();
  const { tenants, atual, trocar } = useTenants();

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

  const corDoIcone = (ativo: boolean) =>
    ativo ? theme.colors.textOnAccentSoft : theme.colors.textMuted;

  const itens: readonly TopbarNavItem[] = [
    {
      key: 'inicio',
      label: 'Início',
      active: pathname.startsWith('/inicio'),
      icon: (ativo) => <Feather name="home" size={18} color={corDoIcone(ativo)} />,
      onPress: () => router.push('/inicio'),
    },
    {
      key: 'membros',
      label: 'Membros',
      active: pathname.startsWith('/membros'),
      icon: (ativo) => <Feather name="users" size={18} color={corDoIcone(ativo)} />,
      onPress: () => router.push('/membros'),
    },
    {
      key: 'agenda',
      label: 'Agenda',
      active: pathname.startsWith('/agenda'),
      icon: (ativo) => <Feather name="calendar" size={18} color={corDoIcone(ativo)} />,
      onPress: () => router.push('/agenda'),
    },
    ...(podeVerFinanceiro
      ? [
          {
            key: 'financeiro',
            label: 'Financeiro',
            active: pathname.startsWith('/financeiro'),
            icon: (ativo: boolean) => (
              <Feather name="trending-up" size={18} color={corDoIcone(ativo)} />
            ),
            onPress: () => router.push('/financeiro'),
          },
        ]
      : []),
    // Só quem administra a igreja. Quem configura integrações digita a senha
    // da conta de e-mail dela — e essa conta costuma ser a que recupera todas
    // as outras senhas. O servidor exige a permissão de qualquer forma; esconder
    // aqui evita oferecer uma porta que vai devolver 403.
    ...(session.roles.some((papel) => papel === ROLES.churchAdmin)
      ? [
          {
            key: 'configuracoes',
            label: 'Configurações',
            active: pathname.startsWith('/configuracoes'),
            icon: (ativo: boolean) => (
              <Feather name="settings" size={18} color={corDoIcone(ativo)} />
            ),
            onPress: () => router.push('/configuracoes'),
          },
        ]
      : []),
    // Sem condicional de papel, ao contrário do item financeiro: é a assinatura
    // da PESSOA, não da igreja — aparece mesmo para quem não tem vínculo com
    // nenhuma congregação.
    {
      key: 'assinatura',
      label: 'Congrega+',
      active: pathname.startsWith('/assinatura'),
      icon: (ativo) => <Feather name="award" size={18} color={corDoIcone(ativo)} />,
      onPress: () => router.push('/assinatura'),
    },
  ];

  return (
    <View style={{ flex: 1, backgroundColor: theme.colors.background }}>
      <Topbar
        items={itens}
        tenantName={session.tenantId !== null ? (atual?.name ?? 'Sua igreja') : 'Congrega+'}
        tenants={tenants}
        onSelectTenant={(id) => void trocar(id)}
        // `?? null` e não direto: o tipo promete `string`, mas isto é a borda da
        // rede. Uma sessão gravada antes de o campo existir entrega `undefined`,
        // que passa por `!== null` — e a barra tentava tirar iniciais de nada,
        // derrubando a aplicação inteira. Já aconteceu.
        userName={session.fullName ?? null}
        roleLabel={roleLabel}
        onSignOut={() => void sair().then(() => router.replace('/entrar'))}
        tenantIcon={<Feather name="home" size={19} color={theme.colors.textOnAccentSoft} />}
        bellIcon={<Feather name="bell" size={19} color={theme.colors.textMuted} />}
      />

      {/* A rolagem mora AQUI, e não em cada tela.
          Com a barra superior fixa, a área de conteúdo é que rola — e telas que
          traziam o próprio `ScrollView` continuam funcionando, porque um
          contêiner de altura definida em volta não muda o comportamento delas.
          O contrário (deixar cada tela resolver) faria a barra rolar junto em
          metade delas e ficar fixa na outra metade. */}
      <ScrollView
        style={{ flex: 1 }}
        contentContainerStyle={{ flexGrow: 1 }}
        // A barra superior sai do fluxo de rolagem, então o teclado não a
        // empurra; sem isto, o `ScrollView` engoliria o toque em campos.
        keyboardShouldPersistTaps="handled"
      >
        <Slot />
      </ScrollView>
    </View>
  );
}
