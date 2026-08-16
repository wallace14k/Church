import { ROLES, type Role } from '@congrega/core/identity';
import { Button } from '@congrega/ui/Button';
import { SignatureButton } from '@congrega/ui/SignatureButton';
import { Screen } from '@congrega/ui/Screen';
import { Text } from '@congrega/ui/Text';
import { useTheme } from '@congrega/ui/theme';
import { Redirect, router } from 'expo-router';
import { ScrollView, View } from 'react-native';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import { useSession } from '../src/session';

const NOME_DO_PAPEL: Record<Role, string> = {
  [ROLES.churchAdmin]: 'Administração',
  [ROLES.treasurer]: 'Tesouraria',
  [ROLES.cellLeader]: 'Liderança de célula',
  [ROLES.childcareStaff]: 'Ministério infantil',
  [ROLES.member]: 'Membro',
};

export default function Inicio() {
  const theme = useTheme();
  const insets = useSafeAreaInsets();
  const { session, status, sair } = useSession();

  if (status === 'anonimo') {
    return <Redirect href="/entrar" />;
  }

  if (session === null) {
    return null;
  }

  const temIgreja = session.tenantId !== null;

  return (
    <Screen padded={false}>
      <ScrollView
        contentContainerStyle={{
          paddingTop: insets.top + theme.space[24],
          paddingBottom: insets.bottom + theme.space[32],
          paddingHorizontal: theme.space[24],
          gap: theme.space[24],
        }}
      >
        <View style={{ gap: theme.space[4] }}>
          <Text variant="eyebrow" tone="muted">
            {temIgreja ? 'SUA IGREJA' : 'CONGREGA+'}
          </Text>
          <Text variant="headingLg">Início</Text>
        </View>

        {temIgreja ? (
          <View style={{ gap: theme.space[8] }}>
            <Text variant="subheading">Você está atuando em uma igreja</Text>
            <Text variant="body" tone="muted">
              {session.roles.length > 0
                ? session.roles.map((papel) => NOME_DO_PAPEL[papel as Role] ?? papel).join(' · ')
                : 'Sem papéis atribuídos'}
            </Text>
          </View>
        ) : (
          <View style={{ gap: theme.space[8] }}>
            <Text variant="subheading">Sua conta não está vinculada a uma igreja</Text>
            {/* Estado válido, não erro: o assinante Congrega+ é cidadão de
                primeira classe. O texto explica o que ele TEM, não o que falta. */}
            <Text variant="body" tone="muted">
              Você tem acesso ao conteúdo do Congrega+. Se sua igreja usa o Congrega, peça um
              convite à secretaria para acompanhar também a vida da comunidade.
            </Text>
          </View>
        )}

        <View
          style={{
            gap: theme.space[12],
            padding: theme.space[16],
            borderRadius: theme.radius.cards,
            borderWidth: 1,
            borderColor: theme.colors.hairline,
            backgroundColor: theme.colors.surface,
          }}
        >
          <Text variant="eyebrow" tone="muted">
            EM CONSTRUÇÃO
          </Text>
          <Text variant="body" tone="muted">
            Cadastro de membros, contribuições e check-in infantil entram nas próximas etapas.
            Esta tela existe hoje para confirmar que a autenticação funciona ponta a ponta.
          </Text>
        </View>

        {temIgreja && (
          <SignatureButton label="Ver membros" onPress={() => router.push('/membros')} />
        )}

        <Button
          label="Sair da conta"
          variant="outline"
          onPress={() => {
            void sair().then(() => router.replace('/entrar'));
          }}
        />
      </ScrollView>
    </Screen>
  );
}
