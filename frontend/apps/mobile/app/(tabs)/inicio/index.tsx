import { ROLES, type Role } from '@congrega/core/identity';
import { formatBirthday } from '@congrega/core/datetime';
import { Avatar } from '@congrega/ui/Avatar';
import { Card } from '@congrega/ui/Card';
import { SignatureButton } from '@congrega/ui/SignatureButton';
import { StatCard } from '@congrega/ui/StatCard';
import { Screen } from '@congrega/ui/Screen';
import { Text } from '@congrega/ui/Text';
import { TextLink } from '@congrega/ui/TextLink';
import { useTheme } from '@congrega/ui/theme';
import { Redirect, router } from 'expo-router';
import { useEffect, useRef } from 'react';
import { Animated, Easing, Pressable, View } from 'react-native';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import { useDashboard } from '../../../src/useDashboard';
import { useSession } from '../../../src/session';
import { useTenants } from '../../../src/useTenants';

const NOME_DO_PAPEL: Record<Role, string> = {
  [ROLES.churchAdmin]: 'Administração',
  [ROLES.treasurer]: 'Tesouraria',
  [ROLES.cellLeader]: 'Liderança de célula',
  [ROLES.childcareStaff]: 'Ministério infantil',
  [ROLES.member]: 'Membro',
};

const MESES = [
  'janeiro', 'fevereiro', 'março', 'abril', 'maio', 'junho',
  'julho', 'agosto', 'setembro', 'outubro', 'novembro', 'dezembro',
] as const;

function saudacao(hora: number): string {
  if (hora < 12) return 'Bom dia';
  if (hora < 18) return 'Boa tarde';
  return 'Boa noite';
}

export default function Inicio() {
  const theme = useTheme();
  const insets = useSafeAreaInsets();
  const { session, status, sair } = useSession();
  const temIgreja = session !== null && session.tenantId !== null;
  const dashboard = useDashboard(temIgreja);
  const { atual } = useTenants();

  // Entrada única e discreta: o conteúdo sobe 12px enquanto ganha opacidade.
  // Um gesto orquestrado só, não uma animação por cartão — várias animações
  // pequenas ao mesmo tempo lêem como "gerado", não como intencional.
  const entrada = useRef(new Animated.Value(0)).current;
  useEffect(() => {
    Animated.timing(entrada, {
      toValue: 1,
      duration: 320,
      easing: Easing.out(Easing.cubic),
      useNativeDriver: true,
    }).start();
  }, [entrada]);

  if (status === 'anonimo') {
    return <Redirect href="/entrar" />;
  }

  if (session === null) {
    return null;
  }

  const agora = new Date();

  return (
    <Screen padded={false} wide>
      <Animated.ScrollView
        contentContainerStyle={{
          paddingTop: insets.top + theme.space[24],
          paddingBottom: insets.bottom + theme.space[48],
          paddingHorizontal: theme.space[24],
          gap: theme.space[24],
          maxWidth: theme.layout.pageMaxWidth,
          width: '100%',
          alignSelf: 'center',
        }}
        style={{
          opacity: entrada,
          transform: [{ translateY: entrada.interpolate({ inputRange: [0, 1], outputRange: [12, 0] }) }],
        }}
      >
        <Text variant="headingLg">
          {saudacao(agora.getHours())}, {temIgreja ? (atual?.name ?? 'sua igreja') : 'Congrega+'}
        </Text>

        {temIgreja ? (
          <>
            <View style={{ flexDirection: 'row', gap: theme.space[12] }}>
              <StatCard
                value={dashboard.totalMembros !== null ? String(dashboard.totalMembros) : '—'}
                label={dashboard.totalMembros === 1 ? 'membro cadastrado' : 'membros cadastrados'}
              />
              <StatCard
                value={String(dashboard.aniversariantes.length)}
                label={
                  dashboard.aniversariantes.length === 1
                    ? 'aniversariante este mês'
                    : 'aniversariantes este mês'
                }
              />
            </View>

            {session.roles.length > 0 && (
              <Text variant="captionBody" tone="muted">
                Atuando como{' '}
                {session.roles.map((papel) => NOME_DO_PAPEL[papel as Role] ?? papel).join(' · ')}
              </Text>
            )}

            {dashboard.aniversariantes.length > 0 && (
              <View style={{ gap: theme.space[12] }}>
                <View style={{ flexDirection: 'row', alignItems: 'center', justifyContent: 'space-between' }}>
                  <Text variant="subheading">Aniversariantes de {MESES[agora.getMonth()]}</Text>
                  <TextLink
                    label="Ver todos"
                    accessibilityLabel="Ver todos os aniversariantes do mês"
                    onPress={() => router.push('/inicio/aniversariantes')}
                  />
                </View>

                <View style={{ gap: theme.space[8] }}>
                  {dashboard.aniversariantes.map((membro) => (
                    <Pressable
                      key={membro.id}
                      onPress={() => router.push(`/membros/${membro.id}`)}
                      accessibilityRole="button"
                      accessibilityLabel={`Abrir ficha de ${membro.fullName}`}
                      style={({ pressed }) => ({ opacity: pressed ? 0.75 : 1 })}
                    >
                      <View
                        style={{
                          flexDirection: 'row',
                          alignItems: 'center',
                          gap: theme.space[12],
                          padding: theme.space[12],
                          borderRadius: theme.radius.smallCards,
                          // Superfície interna: branca sobre o canvas, no mesmo
                          // tratamento do chip de valor da referência.
                          backgroundColor: theme.colors.surfaceInner,
                          borderWidth: 1,
                          borderColor: theme.colors.hairline,
                        }}
                      >
                        <Avatar name={membro.fullName} size={40} />
                        <View style={{ flex: 1, gap: 2 }}>
                          <Text variant="bodyStrong" numberOfLines={1}>
                            {membro.fullName}
                          </Text>
                          {membro.birthDate !== null && (
                            <Text variant="captionBody" tone="muted">
                              {formatBirthday(membro.birthDate)}
                            </Text>
                          )}
                        </View>
                      </View>
                    </Pressable>
                  ))}
                </View>
              </View>
            )}

            <SignatureButton label="Cadastrar membro" onPress={() => router.push('/membros/novo')} />
          </>
        ) : (
          <Card>
            <Text variant="subheading">Sua conta não está vinculada a uma igreja</Text>
            {/* Estado válido, não erro: o assinante Congrega+ é cidadão de
                primeira classe. O texto explica o que ele TEM, não o que falta. */}
            <Text variant="body">
              Você tem acesso ao conteúdo do Congrega+. Se sua igreja usa o Congrega, peça um
              convite à secretaria para acompanhar também a vida da comunidade.
            </Text>
          </Card>
        )}

        <Pressable
          onPress={() => void sair().then(() => router.replace('/entrar'))}
          accessibilityRole="button"
          accessibilityLabel="Sair da conta"
          style={({ pressed }) => ({
            alignSelf: 'center',
            minHeight: theme.touch.minTarget,
            justifyContent: 'center',
            opacity: pressed ? 0.6 : 1,
          })}
        >
          <Text variant="captionBody" tone="muted">
            Sair da conta
          </Text>
        </Pressable>
      </Animated.ScrollView>
    </Screen>
  );
}
