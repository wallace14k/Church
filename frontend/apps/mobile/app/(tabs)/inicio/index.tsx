import type { CalendarEvent } from '@congrega/api-client/events';
import type { Member } from '@congrega/api-client/members';
import { ROLES, type Role } from '@congrega/core/identity';
import { AsyncContent } from '@congrega/ui/AsyncContent';
import { Card } from '@congrega/ui/Card';
import { Screen } from '@congrega/ui/Screen';
import { SkeletonListRow } from '@congrega/ui/Skeleton';
import { Text } from '@congrega/ui/Text';
import { useTheme } from '@congrega/ui/theme';
import { Feather } from '@expo/vector-icons';
import { Redirect, router } from 'expo-router';
import { useMemo } from 'react';
import { Pressable, View, useWindowDimensions } from 'react-native';
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

/**
 * Fuso fixo na igreja, não no aparelho.
 *
 * A data que o painel anuncia é a do lugar onde a congregação se reúne. Um
 * membro viajando não deve ver o culto de domingo cair no sábado.
 */
const FUSO = 'America/Sao_Paulo';
const FORMATO_SEMANA = new Intl.DateTimeFormat('pt-BR', { timeZone: FUSO, weekday: 'long' });
const FORMATO_DIA = new Intl.DateTimeFormat('pt-BR', { timeZone: FUSO, day: '2-digit' });
const FORMATO_MES_CURTO = new Intl.DateTimeFormat('pt-BR', { timeZone: FUSO, month: 'short' });
const FORMATO_HORA = new Intl.DateTimeFormat('pt-BR', {
  timeZone: FUSO,
  hour: '2-digit',
  minute: '2-digit',
});

function capitalizar(valor: string): string {
  const limpo = valor.replace('.', '');
  return limpo.charAt(0).toUpperCase() + limpo.slice(1);
}

/** Abaixo disto os três cartões de métrica empilham. */
const LARGURA_PARA_TRES_COLUNAS = 900;
/** Abaixo disto o painel de aniversariantes e a agenda deixam de dividir a linha. */
const LARGURA_PARA_DUAS_COLUNAS = 1180;

export default function Inicio() {
  const theme = useTheme();
  const { width } = useWindowDimensions();
  const { session, status } = useSession();
  const temIgreja = session !== null && session.tenantId !== null;
  const dashboard = useDashboard(temIgreja);
  const { atual } = useTenants();

  const emTresColunas = width >= LARGURA_PARA_TRES_COLUNAS;
  const emDuasColunas = width >= LARGURA_PARA_DUAS_COLUNAS;

  const agora = useMemo(() => new Date(), []);

  const papeis = useMemo(() => {
    if (session === null || session.roles.length === 0) return null;
    return session.roles.map((p) => NOME_DO_PAPEL[p as Role] ?? p).join(' · ');
  }, [session]);

  /**
   * Resumo dos aniversariantes.
   *
   * O mockup mostra "Próximo · 28 de agosto" no rodapé do cartão. É derivável:
   * o primeiro aniversário do mês que ainda não passou. Quando todos já
   * passaram, o rodapé some — em vez de anunciar um "próximo" que ficou para
   * trás.
   */
  const proximoAniversario = useMemo(() => {
    const hoje = Number(FORMATO_DIA.format(agora));

    return dashboard.aniversariantes
      .filter((m) => m.birthDate !== null && diaDoMes(m.birthDate) >= hoje)
      .sort((a, b) => diaDoMes(a.birthDate!) - diaDoMes(b.birthDate!))[0];
  }, [dashboard.aniversariantes, agora]);

  /**
   * Divisão dos próximos eventos por tipo.
   *
   * O mockup traz "2 cultos + 1 reunião". Sai de `evento.type`, que existe
   * desde que o tipo virou dado da igreja — não de adivinhação pelo título,
   * que erraria em "Culto de Oração".
   */
  const eventosPorTipo = useMemo(() => {
    const contagens = new Map<string, number>();

    for (const evento of dashboard.proximosEventos) {
      const nome = evento.type?.name ?? 'sem tipo';
      contagens.set(nome, (contagens.get(nome) ?? 0) + 1);
    }

    return [...contagens.entries()]
      .sort((a, b) => b[1] - a[1] || a[0].localeCompare(b[0], 'pt-BR'))
      .map(([nome, quantidade]) => `${quantidade} ${quantidade === 1 ? nome : nome + 's'}`.toLowerCase());
  }, [dashboard.proximosEventos]);

  if (status === 'anonimo') {
    return <Redirect href="/entrar" />;
  }

  if (session === null) {
    return null;
  }

  const nomeDaIgreja = temIgreja ? (atual?.name ?? 'sua igreja') : 'Congrega+';

  return (
    <Screen padded={false} wide>
      <View
        style={{
          width: '100%',
          maxWidth: theme.layout.pageMaxWidth,
          alignSelf: 'center',
          paddingHorizontal: theme.space[20],
          paddingTop: theme.space[32],
          paddingBottom: theme.space[28],
          gap: theme.space[16],
        }}
      >
        {/* ---------------------------------------------------------- herói */}
        <View
          style={{
            flexDirection: emTresColunas ? 'row' : 'column',
            alignItems: emTresColunas ? 'flex-end' : 'flex-start',
            justifyContent: 'space-between',
            gap: theme.space[24],
            marginBottom: theme.space[8],
          }}
        >
          <View style={{ flexShrink: 1 }}>
            {papeis !== null && (
              <View
                style={{
                  flexDirection: 'row',
                  alignItems: 'center',
                  gap: theme.space[8],
                  marginBottom: theme.space[12],
                }}
              >
                {/* Ponto de "sessão viva". Decorativo: repete o que a frase ao
                    lado já diz, e sozinho não comunicaria nada a quem não
                    distingue cor. */}
                <View
                  accessibilityElementsHidden
                  importantForAccessibility="no-hide-descendants"
                  style={{
                    width: 8,
                    height: 8,
                    borderRadius: 4,
                    backgroundColor: theme.colors.surfaceAccent,
                  }}
                />
                <Text variant="caption" tone="muted">
                  Atuando como {papeis}
                </Text>
              </View>
            )}

            <Text variant="headingLg">
              {saudacao(agora.getHours())}, <Text variant="headingLg" tone="accent">{nomeDaIgreja}</Text>
            </Text>

            <Text variant="body" tone="muted" style={{ marginTop: theme.space[8] }}>
              Uma visão rápida de tudo que está acontecendo hoje.
            </Text>
          </View>

          <Card style={{ minWidth: 176, gap: theme.space[4] }}>
            <Text variant="eyebrow" tone="muted">
              {capitalizar(FORMATO_SEMANA.format(agora)).toUpperCase()}
            </Text>
            <Text variant="subheading">
              {FORMATO_DIA.format(agora)} de {MESES[agora.getMonth()]}
            </Text>
            <Text variant="captionBody" tone="muted">
              {agora.getFullYear()}
            </Text>
          </Card>
        </View>

        <AsyncContent
          loading={dashboard.carregando}
          skeleton={
            <View style={{ gap: theme.space[12] }}>
              <SkeletonListRow />
              <SkeletonListRow />
              <SkeletonListRow />
            </View>
          }
          failure={dashboard.erro}
          errorTitle="Não deu para carregar o painel"
          onRetry={dashboard.recarregar}
        >
          <View style={{ gap: theme.space[16] }}>
            {/* ------------------------------------------ cartões de métrica */}
            <View
              style={{
                flexDirection: emTresColunas ? 'row' : 'column',
                gap: theme.space[16],
              }}
            >
              <CartaoDeMetrica
                categoria="COMUNIDADE"
                icone="users"
                tom="accent"
                valor={String(dashboard.totalMembros ?? 0)}
                titulo="Membros cadastrados"
                descricao="Total de membros na igreja"
              />

              <CartaoDeMetrica
                categoria="COMEMORAÇÕES"
                icone="gift"
                tom="category"
                valor={String(dashboard.aniversariantes.length)}
                titulo="Aniversariantes este mês"
                descricao={`Em ${MESES[agora.getMonth()]}`}
                {...(proximoAniversario?.birthDate
                  ? {
                      rodape: {
                        etiqueta: 'Próximo',
                        texto: `${diaDoMes(proximoAniversario.birthDate)} de ${MESES[agora.getMonth()]}`,
                      },
                    }
                  : {})}
              />

              <CartaoDeMetrica
                categoria="AGENDA"
                icone="calendar"
                tom="accent"
                valor={String(dashboard.proximosEventos.length)}
                titulo="Eventos próximos"
                descricao="Nos próximos 7 dias"
                {...(eventosPorTipo.length > 0
                  ? { rodape: { etiqueta: eventosPorTipo[0]!, texto: eventosPorTipo.slice(1).join(' · ') } }
                  : {})}
              />
            </View>

            {/* ------------------------------------------------- dois painéis */}
            <View
              style={{
                flexDirection: emDuasColunas ? 'row' : 'column',
                gap: theme.space[16],
                alignItems: 'flex-start',
              }}
            >
              <Card style={{ flex: emDuasColunas ? 1.75 : undefined, width: emDuasColunas ? undefined : '100%' }}>
                <CabecalhoDePainel
                  categoria="CALENDÁRIO DA COMUNIDADE"
                  titulo={`Aniversariantes de ${MESES[agora.getMonth()]}`}
                  acao={{
                    rotulo: 'Ver todos',
                    onPress: () => router.push('/inicio/aniversariantes'),
                  }}
                />

                {dashboard.aniversariantes.length === 0 ? (
                  <Text variant="captionBody" tone="muted">
                    Ninguém faz aniversário em {MESES[agora.getMonth()]}.
                  </Text>
                ) : (
                  <View style={{ gap: theme.space[8] }}>
                    {dashboard.aniversariantes.map((membro) => (
                      <LinhaDeAniversariante key={membro.id} membro={membro} hoje={Number(FORMATO_DIA.format(agora))} />
                    ))}
                  </View>
                )}
              </Card>

              <Card
                style={{
                  flex: emDuasColunas ? 0.95 : undefined,
                  width: emDuasColunas ? undefined : '100%',
                }}
              >
                <CabecalhoDePainel categoria="PRÓXIMOS DIAS" titulo="Agenda" />

                {dashboard.proximosEventos.length === 0 ? (
                  <Text variant="captionBody" tone="muted">
                    Nada marcado para os próximos 7 dias.
                  </Text>
                ) : (
                  <View style={{ gap: theme.space[8] }}>
                    {dashboard.proximosEventos.slice(0, 4).map((evento) => (
                      <LinhaDeEvento key={evento.id} evento={evento} />
                    ))}
                  </View>
                )}

                <Pressable
                  onPress={() => router.push('/agenda')}
                  accessibilityRole="link"
                  accessibilityLabel="Abrir agenda completa"
                  style={({ pressed }) => ({
                    marginTop: theme.space[12],
                    opacity: pressed ? 0.6 : 1,
                    flexDirection: 'row',
                    alignItems: 'center',
                    gap: theme.space[8],
                  })}
                >
                  <Text variant="caption" tone="accent">
                    Abrir agenda completa
                  </Text>
                  <Feather name="arrow-right" size={13} color={theme.colors.textOnAccentSoft} />
                </Pressable>
              </Card>
            </View>

            {/* ---------------------------------------------- ações rápidas */}
            <Card style={{ gap: theme.space[16] }}>
              <View
                style={{
                  flexDirection: emTresColunas ? 'row' : 'column',
                  justifyContent: 'space-between',
                  alignItems: emTresColunas ? 'flex-end' : 'flex-start',
                  gap: theme.space[8],
                }}
              >
                <View>
                  <Text variant="eyebrow" tone="muted">
                    ATALHOS
                  </Text>
                  <Text variant="headingSm" style={{ marginTop: theme.space[4] }}>
                    Ações rápidas
                  </Text>
                </View>
                <Text variant="captionBody" tone="muted">
                  Resolva as tarefas mais comuns em poucos cliques.
                </Text>
              </View>

              <View style={{ flexDirection: emTresColunas ? 'row' : 'column', gap: theme.space[12] }}>
                <Atalho
                  destacado
                  icone="user-plus"
                  titulo="Cadastrar membro"
                  descricao="Adicione uma pessoa à comunidade"
                  onPress={() => router.push('/membros/novo')}
                />
                <Atalho
                  icone="calendar"
                  titulo="Agendar evento"
                  descricao="Organize a programação da igreja"
                  onPress={() => router.push('/agenda/novo')}
                />
                <Atalho
                  icone="trending-up"
                  titulo="Ver financeiro"
                  descricao="Acompanhe entradas e saídas"
                  onPress={() => router.push('/financeiro')}
                />
              </View>
            </Card>
          </View>
        </AsyncContent>

        <View
          style={{
            flexDirection: emTresColunas ? 'row' : 'column',
            justifyContent: 'space-between',
            gap: theme.space[4],
            paddingTop: theme.space[8],
          }}
        >
          <Text variant="captionBody" tone="muted">
            Congrega · Gestão simples para uma comunidade viva
          </Text>
        </View>
      </View>
    </Screen>
  );
}

/** Dia do mês de uma data ISO, no fuso da igreja. */
function diaDoMes(iso: string): number {
  return Number(FORMATO_DIA.format(new Date(iso)));
}

function CartaoDeMetrica({
  categoria,
  icone,
  tom,
  valor,
  titulo,
  descricao,
  rodape,
}: {
  readonly categoria: string;
  readonly icone: keyof typeof Feather.glyphMap;
  readonly tom: 'accent' | 'category';
  readonly valor: string;
  readonly titulo: string;
  readonly descricao: string;
  readonly rodape?: { readonly etiqueta: string; readonly texto: string };
}) {
  const theme = useTheme();

  const fundoDoIcone = tom === 'accent' ? theme.colors.surfaceAccentSoft : theme.colors.surfaceCategorySoft;
  const corDoIcone = tom === 'accent' ? theme.colors.textOnAccentSoft : theme.colors.textOnCategorySoft;

  return (
    <Card style={{ flex: 1, gap: theme.space[16], minHeight: 178 }}>
      <View style={{ flexDirection: 'row', gap: theme.space[16] }}>
        {/* O ícone é reforço, nunca o portador do sentido: verde e âmbar têm
            luminância quase idêntica (1,01:1), então quem não distingue matiz
            vê os dois círculos iguais. Quem separa as categorias é o rótulo
            escrito acima do número. Ver `tokens.test.ts`. */}
        <View
          accessibilityElementsHidden
          importantForAccessibility="no-hide-descendants"
          style={{
            width: 54,
            height: 54,
            borderRadius: 27,
            alignItems: 'center',
            justifyContent: 'center',
            backgroundColor: fundoDoIcone,
          }}
        >
          <Feather name={icone} size={24} color={corDoIcone} />
        </View>

        <View style={{ flex: 1, minWidth: 0 }}>
          <Text variant="eyebrow" tone="muted">
            {categoria}
          </Text>
          <Text variant="display" style={{ marginTop: theme.space[4] }}>
            {valor}
          </Text>
          <Text variant="subheading" style={{ marginTop: theme.space[8] }}>
            {titulo}
          </Text>
          <Text variant="captionBody" tone="muted" style={{ marginTop: 2 }}>
            {descricao}
          </Text>
        </View>
      </View>

      {/* O mockup traz "↑ 12,5% vs. mês anterior" no primeiro cartão. Não está
          aqui porque não existe: nada guarda a contagem de membros de meses
          passados, e inventar uma variação seria dar ao usuário um número em
          que ele confiaria para decidir. O rodapé só aparece onde os dados o
          sustentam — próximo aniversário e divisão por tipo de evento. */}
      {rodape !== undefined && (
        <View
          style={{
            flexDirection: 'row',
            alignItems: 'center',
            gap: theme.space[8],
            marginTop: 'auto',
            flexWrap: 'wrap',
          }}
        >
          <View
            style={{
              paddingVertical: theme.space[4],
              paddingHorizontal: theme.space[8],
              borderRadius: theme.radius.tags,
              backgroundColor: fundoDoIcone,
            }}
          >
            <Text variant="caption" style={{ color: corDoIcone }}>
              {rodape.etiqueta}
            </Text>
          </View>

          {rodape.texto !== '' && (
            <Text variant="captionBody" tone="muted" style={{ flexShrink: 1 }}>
              {rodape.texto}
            </Text>
          )}
        </View>
      )}
    </Card>
  );
}

function CabecalhoDePainel({
  categoria,
  titulo,
  acao,
}: {
  readonly categoria: string;
  readonly titulo: string;
  readonly acao?: { readonly rotulo: string; readonly onPress: () => void };
}) {
  const theme = useTheme();

  return (
    <View
      style={{
        flexDirection: 'row',
        justifyContent: 'space-between',
        alignItems: 'flex-end',
        gap: theme.space[12],
        marginBottom: theme.space[16],
      }}
    >
      <View style={{ flexShrink: 1 }}>
        <Text variant="eyebrow" tone="muted">
          {categoria}
        </Text>
        <Text variant="headingSm" style={{ marginTop: theme.space[4] }}>
          {titulo}
        </Text>
      </View>

      {acao !== undefined && (
        <Pressable
          onPress={acao.onPress}
          accessibilityRole="link"
          accessibilityLabel={acao.rotulo}
          style={({ pressed }) => ({
            flexDirection: 'row',
            alignItems: 'center',
            gap: theme.space[8],
            opacity: pressed ? 0.6 : 1,
          })}
        >
          <Text variant="caption" tone="accent">
            {acao.rotulo}
          </Text>
          <Feather name="arrow-right" size={13} color={theme.colors.textOnAccentSoft} />
        </Pressable>
      )}
    </View>
  );
}

function LinhaDeAniversariante({
  membro,
  hoje,
}: {
  readonly membro: Member;
  readonly hoje: number;
}) {
  const theme = useTheme();

  if (membro.birthDate === null) return null;

  const dia = diaDoMes(membro.birthDate);
  const data = new Date(membro.birthDate);

  const situacao =
    dia === hoje ? { rotulo: 'Hoje', destaque: true }
    : dia < hoje ? { rotulo: 'Celebrado', destaque: false }
    : { rotulo: 'Em breve', destaque: false };

  return (
    <Pressable
      onPress={() => router.push(`/membros/${membro.id}`)}
      accessibilityRole="button"
      // O rótulo carrega o que a linha comunica visualmente. Sem isto o leitor
      // de tela anunciaria o nome e o chip de status como fragmentos soltos.
      accessibilityLabel={`${membro.fullName}, aniversário em ${dia} de ${MESES[data.getMonth()]}, ${situacao.rotulo}`}
      style={({ pressed }) => ({
        flexDirection: 'row',
        alignItems: 'center',
        gap: theme.space[12],
        padding: theme.space[8],
        borderRadius: theme.radius.smallCards,
        borderWidth: 1,
        borderColor: situacao.destaque ? theme.colors.surfaceAccentSoft : theme.colors.hairline,
        backgroundColor:
          pressed ? theme.colors.surfaceInner
          : situacao.destaque ? theme.colors.surfaceAccentSoft
          : theme.colors.surface,
      })}
    >
      <View
        style={{
          minWidth: 48,
          alignItems: 'center',
          paddingVertical: theme.space[8],
          paddingHorizontal: theme.space[4],
          borderRadius: theme.radius.inputs,
          backgroundColor: situacao.destaque ? theme.colors.surface : theme.colors.surfaceInner,
        }}
      >
        <Text variant="subheading">{String(dia).padStart(2, '0')}</Text>
        <Text variant="eyebrow" tone="muted">
          {capitalizar(FORMATO_MES_CURTO.format(data)).toUpperCase()}
        </Text>
      </View>

      <View style={{ flex: 1, minWidth: 0 }}>
        <Text variant="bodyStrong" numberOfLines={1}>
          {membro.fullName}
        </Text>
        <Text variant="captionBody" tone="muted" numberOfLines={1}>
          {dia} de {MESES[data.getMonth()]}
        </Text>
      </View>

      <View
        style={{
          paddingVertical: theme.space[4],
          paddingHorizontal: theme.space[8],
          borderRadius: theme.radius.tags,
          backgroundColor: situacao.destaque ? theme.colors.surface : theme.colors.surfaceInner,
        }}
      >
        <Text variant="caption" tone={situacao.destaque ? 'accent' : 'muted'}>
          {situacao.rotulo}
        </Text>
      </View>
    </Pressable>
  );
}

function LinhaDeEvento({ evento }: { readonly evento: CalendarEvent }) {
  const theme = useTheme();
  const inicio = new Date(evento.startsAt);

  const local = evento.location ?? evento.address?.localidade ?? null;

  return (
    <Pressable
      onPress={() => router.push(`/agenda/${evento.id}`)}
      accessibilityRole="button"
      accessibilityLabel={
        `${evento.type?.name ?? 'Evento'}: ${evento.title}, ` +
        `${diaDoMes(evento.startsAt)} de ${MESES[inicio.getMonth()]} às ${FORMATO_HORA.format(inicio)}`
      }
      style={({ pressed }) => ({
        flexDirection: 'row',
        alignItems: 'center',
        gap: theme.space[12],
        padding: theme.space[8],
        borderRadius: theme.radius.inputs,
        borderWidth: 1,
        borderColor: theme.colors.hairline,
        backgroundColor: pressed ? theme.colors.surface : theme.colors.surfaceInner,
      })}
    >
      <View
        style={{
          minWidth: 44,
          alignItems: 'center',
          paddingVertical: theme.space[4],
          borderRadius: theme.radius.inputs,
          backgroundColor: theme.colors.surfaceAccentSoft,
        }}
      >
        <Text variant="subheading" tone="accent">
          {String(diaDoMes(evento.startsAt)).padStart(2, '0')}
        </Text>
        <Text variant="eyebrow" tone="accent">
          {capitalizar(FORMATO_MES_CURTO.format(inicio)).toUpperCase()}
        </Text>
      </View>

      <View style={{ flex: 1, minWidth: 0 }}>
        <Text variant="caption" numberOfLines={1}>
          {evento.title}
        </Text>
        <Text variant="captionBody" tone="muted" numberOfLines={1}>
          {FORMATO_HORA.format(inicio)}
          {local === null ? '' : ` · ${local}`}
        </Text>
      </View>

      {/* Etiqueta de tipo em uma cor só, ao contrário do mockup — que pinta
          culto de verde, reunião de âmbar e estudo de roxo. Aqui os tipos são
          cadastrados pela igreja: ela pode ter dois ou quinze, e uma paleta
          fixa de três teria de repetir cores ou inventar tons a cada tipo novo.
          O nome escrito distingue sem depender de cor. */}
      {evento.type !== null && (
        <View
          style={{
            paddingVertical: theme.space[4],
            paddingHorizontal: theme.space[8],
            borderRadius: theme.radius.tags,
            backgroundColor: theme.colors.surfaceAccentSoft,
          }}
        >
          <Text variant="caption" tone="accent" numberOfLines={1}>
            {evento.type.name}
          </Text>
        </View>
      )}
    </Pressable>
  );
}

function Atalho({
  icone,
  titulo,
  descricao,
  onPress,
  destacado = false,
}: {
  readonly icone: keyof typeof Feather.glyphMap;
  readonly titulo: string;
  readonly descricao: string;
  readonly onPress: () => void;
  readonly destacado?: boolean;
}) {
  const theme = useTheme();

  return (
    <Pressable
      onPress={onPress}
      accessibilityRole="button"
      accessibilityLabel={`${titulo}. ${descricao}`}
      style={({ pressed }) => ({
        flex: 1,
        flexDirection: 'row',
        alignItems: 'center',
        gap: theme.space[12],
        padding: theme.space[12],
        borderRadius: theme.radius.smallCards,
        borderWidth: 1,
        borderColor: destacado ? theme.colors.surfaceAccentSoft : theme.colors.hairline,
        backgroundColor:
          pressed ? theme.colors.surface
          : destacado ? theme.colors.surfaceAccentSoft
          : theme.colors.surfaceInner,
        ...(pressed ? theme.elevation.raised : {}),
      })}
    >
      <View
        style={{
          width: 42,
          height: 42,
          borderRadius: theme.radius.inputs,
          alignItems: 'center',
          justifyContent: 'center',
          backgroundColor: destacado ? theme.colors.surfaceAccent : theme.colors.surfaceAccentSoft,
        }}
      >
        <Feather
          name={icone}
          size={19}
          color={destacado ? theme.colors.textOnAccent : theme.colors.textOnAccentSoft}
        />
      </View>

      <View style={{ flex: 1, minWidth: 0 }}>
        <Text variant="caption" numberOfLines={1}>
          {titulo}
        </Text>
        <Text variant="captionBody" tone="muted" numberOfLines={2}>
          {descricao}
        </Text>
      </View>

      <Feather name="arrow-right" size={16} color={theme.colors.textOnAccentSoft} />
    </Pressable>
  );
}
