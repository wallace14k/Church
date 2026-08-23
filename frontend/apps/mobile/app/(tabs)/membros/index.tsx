import type { Member, MemberGap } from '@congrega/api-client/members';
import { formatPhone } from '@congrega/core/validation';
import { AsyncContent } from '@congrega/ui/AsyncContent';
import { Button } from '@congrega/ui/Button';
import { Card } from '@congrega/ui/Card';
import { EmptyState } from '@congrega/ui/EmptyState';
import { Screen } from '@congrega/ui/Screen';
import { ScreenLoading } from '@congrega/ui/ScreenLoading';
import { SignatureButton } from '@congrega/ui/SignatureButton';
import { SkeletonListRow } from '@congrega/ui/Skeleton';
import { Text } from '@congrega/ui/Text';
import { TextField } from '@congrega/ui/TextField';
import { useTheme } from '@congrega/ui/theme';
import { Feather } from '@expo/vector-icons';
import { router } from 'expo-router';
import { useMemo, useState } from 'react';
import { Pressable, View, useWindowDimensions } from 'react-native';
import { useMembers } from '../../../src/useMembers';
import { useTenants } from '../../../src/useTenants';

const MESES = [
  'janeiro', 'fevereiro', 'março', 'abril', 'maio', 'junho',
  'julho', 'agosto', 'setembro', 'outubro', 'novembro', 'dezembro',
] as const;

const MES_CURTO = [
  'jan', 'fev', 'mar', 'abr', 'mai', 'jun',
  'jul', 'ago', 'set', 'out', 'nov', 'dez',
] as const;

/** Abaixo disto os cartões de resumo empilham e a linha do membro reflui. */
const LARGURA_PARA_TRES_COLUNAS = 900;

export default function ListaDeMembros() {
  const theme = useTheme();
  const { width } = useWindowDimensions();
  const [busca, setBusca] = useState('');
  const [filtro, setFiltro] = useState<MemberGap | undefined>(undefined);
  const { atual } = useTenants();

  const {
    membros, total, carregando, carregandoMais, erro, temMais, resumo, carregarMais, recarregar,
  } = useMembers(busca, filtro);

  const emTresColunas = width >= LARGURA_PARA_TRES_COLUNAS;
  const mesAtual = useMemo(() => new Date().getMonth(), []);

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
            alignItems: emTresColunas ? 'flex-end' : 'stretch',
            justifyContent: 'space-between',
            gap: theme.space[20],
            marginBottom: theme.space[4],
          }}
        >
          <View style={{ flexShrink: 1 }}>
            <Text variant="eyebrow" tone="muted">
              SUA IGREJA · MEMBROS
            </Text>

            <Text variant="headingLg" style={{ marginTop: theme.space[8] }}>
              Membros
            </Text>

            {/* O número vem de `totalCount`, do servidor — não de
                `membros.length`, que conta só a página carregada e diria "30
                pessoas" numa igreja com 240. */}
            <Text variant="body" tone="muted" style={{ marginTop: theme.space[8] }}>
              {total === 1 ? '1 pessoa cadastrada' : `${total} pessoas cadastradas`}
              {atual?.name === undefined ? '.' : ` na comunidade da ${atual.name}.`}
            </Text>
          </View>

          <View style={{ flexDirection: 'row', gap: theme.space[12] }}>
            <Button
              label="Importar"
              variant="outline"
              onPress={() => router.push('/membros/importar')}
            />
            <SignatureButton
              label="Cadastrar membro"
              onPress={() => router.push('/membros/novo')}
            />
          </View>
        </View>

        {/* ---------------------------------------------- cartões de resumo */}
        {resumo !== null && (
          <View style={{ flexDirection: emTresColunas ? 'row' : 'column', gap: theme.space[12] }}>
            <CartaoDeResumo
              icone="users"
              tom="accent"
              valor={resumo.total}
              rotulo="Membros no total"
            />
            <CartaoDeResumo
              icone="gift"
              tom="category"
              valor={resumo.birthdayThisMonth}
              rotulo={`Aniversariantes de ${MESES[mesAtual]}`}
            />
            <CartaoDeResumo
              icone="alert-triangle"
              tom="danger"
              valor={resumo.incomplete}
              rotulo="Com perfil incompleto"
            />
          </View>
        )}

        {/* ---------------------------------------------------------- painel */}
        <Card style={{ gap: theme.space[16] }}>
          <View
            style={{
              flexDirection: 'row',
              justifyContent: 'space-between',
              alignItems: 'flex-end',
              gap: theme.space[12],
            }}
          >
            <View style={{ flexShrink: 1 }}>
              <Text variant="eyebrow" tone="muted">
                SUA IGREJA
              </Text>
              <Text variant="headingSm" style={{ marginTop: theme.space[4] }}>
                Todos os membros{' '}
                <Text variant="captionBody" tone="muted">
                  · {total}
                </Text>
              </Text>
            </View>

            {/* Alternador Lista/Famílias.
                "Famílias" navega para uma tela que já existe, em vez de ser um
                segundo modo desta. Duas visões dentro do mesmo componente
                exigiriam que ele soubesse carregar duas coleções diferentes —
                e a tela de famílias já resolve a dela. */}
            <View
              style={{
                flexDirection: 'row',
                padding: theme.space[4],
                borderRadius: theme.radius.inputs,
                borderWidth: 1,
                borderColor: theme.colors.hairline,
                backgroundColor: theme.colors.surfaceInner,
              }}
            >
              <AbaDeVisao rotulo="Lista" ativa />
              <AbaDeVisao rotulo="Famílias" onPress={() => router.push('/membros/familias')} />
            </View>
          </View>

          <TextField
            label="Buscar"
            placeholder="Buscar por nome, e-mail ou telefone"
            defaultValue=""
            onValueChange={setBusca}
            autoCapitalize="none"
            autoCorrect={false}
          />

          {/* Os chips só aparecem quando as contagens chegaram. Um "Sem telefone
              0" numa igreja que tem três seria pior do que chip nenhum — e as
              contagens vêm do acervo, não da página. */}
          {resumo !== null && (
            <View
              style={{ flexDirection: 'row', flexWrap: 'wrap', gap: theme.space[8] }}
              accessibilityRole="radiogroup"
            >
              <Chip
                rotulo="Todos"
                quantidade={resumo.total}
                ativo={filtro === undefined}
                onPress={() => setFiltro(undefined)}
              />
              <Chip
                rotulo="Perfil incompleto"
                quantidade={resumo.incomplete}
                ativo={filtro === 'Any'}
                onPress={() => setFiltro('Any')}
              />
              <Chip
                rotulo="Sem telefone"
                quantidade={resumo.withoutPhone}
                ativo={filtro === 'SemTelefone'}
                onPress={() => setFiltro('SemTelefone')}
              />
              <Chip
                rotulo="Sem e-mail"
                quantidade={resumo.withoutEmail}
                ativo={filtro === 'SemEmail'}
                onPress={() => setFiltro('SemEmail')}
              />
            </View>
          )}

          <AsyncContent
            loading={carregando}
            skeleton={
              <View style={{ gap: theme.space[8] }}>
                <SkeletonListRow />
                <SkeletonListRow />
                <SkeletonListRow />
              </View>
            }
            failure={erro}
            errorTitle="Não deu para carregar os membros"
            onRetry={recarregar}
            isEmpty={membros.length === 0}
            empty={
              <EmptyState
                title={
                  busca !== '' || filtro !== undefined
                    ? 'Nenhum membro encontrado'
                    : 'Nenhum membro cadastrado'
                }
                description={
                  busca !== '' || filtro !== undefined
                    ? 'Tente outro termo, ou limpe o filtro para ver todos.'
                    : 'Cadastre a primeira pessoa da comunidade, ou importe a lista que a igreja já tem.'
                }
                action={
                  busca !== '' || filtro !== undefined ? (
                    <Button label="Limpar filtro" variant="outline" onPress={() => setFiltro(undefined)} />
                  ) : (
                    <SignatureButton label="Cadastrar membro" onPress={() => router.push('/membros/novo')} />
                  )
                }
              />
            }
          >
            <View style={{ gap: theme.space[8] }}>
              {membros.map((membro, indice) => (
                <LinhaDeMembro
                  key={membro.id}
                  membro={membro}
                  indice={indice}
                  mesAtual={mesAtual}
                  emLinha={emTresColunas}
                />
              ))}
            </View>

            {/* Paginação por "carregar mais", e não por números de página como
                no mockup. A lista acumula: trocar para páginas numeradas faria
                quem rolou até o fim voltar ao topo a cada avanço, e o padrão de
                acúmulo é o que a tela já usava e que o toque espera. */}
            {temMais && (
              <View
                style={{
                  marginTop: theme.space[16],
                  paddingTop: theme.space[16],
                  borderTopWidth: 1,
                  borderTopColor: theme.colors.hairline,
                  flexDirection: 'row',
                  alignItems: 'center',
                  justifyContent: 'space-between',
                  gap: theme.space[12],
                  flexWrap: 'wrap',
                }}
              >
                <Text variant="captionBody" tone="muted">
                  Mostrando {membros.length} de {total}{' '}
                  {total === 1 ? 'membro' : 'membros'}
                </Text>

                {carregandoMais ? (
                  <ScreenLoading what="mais membros" fill={false} />
                ) : (
                  <Button label="Carregar mais" variant="outline" onPress={carregarMais} />
                )}
              </View>
            )}
          </AsyncContent>
        </Card>
      </View>
    </Screen>
  );
}

function CartaoDeResumo({
  icone,
  tom,
  valor,
  rotulo,
}: {
  readonly icone: keyof typeof Feather.glyphMap;
  readonly tom: 'accent' | 'category' | 'danger';
  readonly valor: number;
  readonly rotulo: string;
}) {
  const theme = useTheme();

  const fundo =
    tom === 'accent' ? theme.colors.surfaceAccentSoft
    : tom === 'category' ? theme.colors.surfaceCategorySoft
    : '#FAEAEA';

  const cor =
    tom === 'accent' ? theme.colors.textOnAccentSoft
    : tom === 'category' ? theme.colors.textOnCategorySoft
    : theme.colors.danger;

  return (
    <Card style={{ flex: 1, flexDirection: 'row', alignItems: 'center', gap: theme.space[12] }}>
      {/* O ícone é reforço, não o portador do sentido: o rótulo escrito ao lado
          é quem diz o que o número conta. Ver a nota sobre luminância de verde
          e âmbar em `tokens.test.ts`. */}
      <View
        accessibilityElementsHidden
        importantForAccessibility="no-hide-descendants"
        style={{
          width: 44,
          height: 44,
          borderRadius: theme.radius.smallCards,
          alignItems: 'center',
          justifyContent: 'center',
          backgroundColor: fundo,
        }}
      >
        <Feather name={icone} size={20} color={cor} />
      </View>

      <View style={{ flex: 1, minWidth: 0 }}>
        <Text variant="headingSm">{valor}</Text>
        <Text variant="captionBody" tone="muted" numberOfLines={2}>
          {rotulo}
        </Text>
      </View>
    </Card>
  );
}

function AbaDeVisao({
  rotulo,
  ativa = false,
  onPress,
}: {
  readonly rotulo: string;
  readonly ativa?: boolean;
  readonly onPress?: () => void;
}) {
  const theme = useTheme();

  return (
    <Pressable
      onPress={onPress}
      disabled={ativa}
      accessibilityRole="tab"
      accessibilityState={{ selected: ativa }}
      accessibilityLabel={rotulo}
      style={({ pressed }) => ({
        paddingVertical: theme.space[8],
        paddingHorizontal: theme.space[16],
        borderRadius: theme.radius.inputs - 3,
        backgroundColor:
          ativa ? theme.colors.surface : pressed ? theme.colors.surfaceAccentSoft : 'transparent',
        ...(ativa ? theme.elevation.raised : {}),
      })}
    >
      <Text variant="caption" tone={ativa ? 'accent' : 'muted'}>
        {rotulo}
      </Text>
    </Pressable>
  );
}

function Chip({
  rotulo,
  quantidade,
  ativo,
  onPress,
}: {
  readonly rotulo: string;
  readonly quantidade: number;
  readonly ativo: boolean;
  readonly onPress: () => void;
}) {
  const theme = useTheme();

  return (
    <Pressable
      onPress={onPress}
      accessibilityRole="radio"
      accessibilityState={{ checked: ativo }}
      // O leitor de tela recebe rótulo e contagem como uma frase; separados,
      // eles seriam anunciados como dois fragmentos sem relação.
      accessibilityLabel={`${rotulo}, ${quantidade}`}
      style={({ pressed }) => ({
        flexDirection: 'row',
        alignItems: 'center',
        gap: theme.space[8],
        minHeight: theme.touch.minTarget,
        paddingHorizontal: theme.space[12],
        borderRadius: theme.radius.tags,
        borderWidth: 1,
        borderColor: ativo ? theme.colors.surfaceAccent : theme.colors.hairline,
        backgroundColor:
          ativo ? theme.colors.surfaceAccentSoft
          : pressed ? theme.colors.surfaceInner
          : theme.colors.surface,
      })}
    >
      <Text variant="caption" tone={ativo ? 'accent' : 'muted'}>
        {rotulo}
      </Text>
      <Text variant="caption" tone={ativo ? 'accent' : 'ink'}>
        {quantidade}
      </Text>
    </Pressable>
  );
}

/**
 * Paleta rotativa do avatar.
 *
 * **Pelo índice na lista, não por hash do nome.** Hash daria a mesma cor à
 * mesma pessoa sempre, o que parece melhor — mas duas pessoas vizinhas podem
 * cair na mesma cor, e aí a cor deixa de separar visualmente uma linha da
 * seguinte, que é a única coisa que ela faz aqui. A cor não carrega
 * significado: é ritmo visual.
 */
const PALETA_DE_AVATAR = [
  { fundo: '#E0EFCA', cor: '#3F6224' },
  { fundo: '#F0E5D7', cor: '#6A5136' },
  { fundo: '#EFEAD8', cor: '#655C3E' },
  { fundo: '#DDE9F0', cor: '#355A6D' },
  { fundo: '#F0DDE8', cor: '#763557' },
  { fundo: '#EEE6D8', cor: '#63533D' },
] as const;

function iniciais(nome: string): string {
  const PARTICULAS = ['de', 'da', 'do', 'das', 'dos', 'e'];

  const partes = nome
    .trim()
    .split(/\s+/)
    .filter((p) => p.length > 0 && !PARTICULAS.includes(p.toLowerCase()));

  if (partes.length === 0) return '?';

  const primeira = partes[0]!.charAt(0);
  const ultima = partes.length > 1 ? partes[partes.length - 1]!.charAt(0) : '';

  return (primeira + ultima).toUpperCase();
}

function LinhaDeMembro({
  membro,
  indice,
  mesAtual,
  emLinha,
}: {
  readonly membro: Member;
  readonly indice: number;
  readonly mesAtual: number;
  readonly emLinha: boolean;
}) {
  const theme = useTheme();
  const cores = PALETA_DE_AVATAR[indice % PALETA_DE_AVATAR.length]!;

  const semTelefone = membro.phone === null || membro.phone === '';
  const semEmail = membro.email === null || membro.email === '';
  const incompleto = semTelefone || semEmail;

  /**
   * Aniversário do mês corrente.
   *
   * `birthDate` é `YYYY-MM-DD` sem hora; fatiar a string evita o `new Date()`
   * interpretá-la como UTC e devolver o dia anterior em fuso negativo — que é
   * exatamente o Brasil.
   */
  const aniversario = useMemo(() => {
    if (membro.birthDate === null) return null;

    const [, mes, dia] = membro.birthDate.split('-').map(Number);
    if (mes === undefined || dia === undefined || mes - 1 !== mesAtual) return null;

    const hoje = new Date();
    return {
      dia,
      mes: mes - 1,
      ehHoje: hoje.getDate() === dia && hoje.getMonth() === mes - 1,
    };
  }, [membro.birthDate, mesAtual]);

  const detalhes = [
    membro.age === null ? null : `${membro.age} anos`,
    membro.familyName,
  ].filter((d): d is string => d !== null);

  return (
    <Pressable
      onPress={() => router.push(`/membros/${membro.id}`)}
      accessibilityRole="button"
      // Uma frase só. Sem isto o leitor de tela anunciaria nome, idade, telefone
      // e situação como fragmentos soltos, e "Sem telefone" ficaria sem dono.
      accessibilityLabel={
        [
          membro.fullName,
          detalhes.join(', '),
          aniversario === null ? null
            : aniversario.ehHoje ? 'faz aniversário hoje'
            : `aniversário em ${aniversario.dia} de ${MESES[aniversario.mes]}`,
          semTelefone ? 'sem telefone' : null,
          semEmail ? 'sem e-mail' : null,
        ]
          .filter((p) => p !== null && p !== '')
          .join('. ')
      }
      style={({ pressed }) => ({
        flexDirection: 'row',
        alignItems: emLinha ? 'center' : 'flex-start',
        gap: theme.space[12],
        padding: theme.space[12],
        borderRadius: theme.radius.smallCards,
        borderWidth: 1,
        borderColor: theme.colors.hairline,
        backgroundColor: pressed ? theme.colors.surfaceInner : theme.colors.surface,
      })}
    >
      <View
        accessibilityElementsHidden
        importantForAccessibility="no-hide-descendants"
        style={{
          width: 44,
          height: 44,
          borderRadius: 22,
          flexShrink: 0,
          alignItems: 'center',
          justifyContent: 'center',
          backgroundColor: cores.fundo,
        }}
      >
        <Text variant="caption" style={{ color: cores.cor }}>
          {iniciais(membro.fullName)}
        </Text>
      </View>

      <View
        style={{
          flex: 1,
          minWidth: 0,
          flexDirection: emLinha ? 'row' : 'column',
          alignItems: emLinha ? 'center' : 'flex-start',
          gap: emLinha ? theme.space[12] : theme.space[8],
        }}
      >
        <View style={{ flex: emLinha ? 1.5 : undefined, minWidth: 0, width: emLinha ? undefined : '100%' }}>
          <View style={{ flexDirection: 'row', alignItems: 'center', gap: theme.space[8], flexWrap: 'wrap' }}>
            <Text variant="bodyStrong" numberOfLines={1} style={{ flexShrink: 1, minWidth: 0 }}>
              {membro.fullName}
            </Text>

            {aniversario !== null && (
              <Etiqueta
                texto={aniversario.ehHoje ? 'Hoje' : `${aniversario.dia} ${MES_CURTO[aniversario.mes]}`}
                tom={aniversario.ehHoje ? 'accent' : 'category'}
              />
            )}

            {/* A situação de vínculo aparece só quando NÃO é "Ativo".
                Um selo "Ativo" em toda linha seria ruído; um membro
                transferido ou falecido passando sem marca, ao contrário,
                esconde o que a secretaria precisa ver. */}
            {membro.status !== 'Ativo' && <Etiqueta texto={membro.status} tom="neutro" />}
          </View>

          {detalhes.length > 0 && (
            <Text variant="captionBody" tone="muted" numberOfLines={1} style={{ marginTop: 3 }}>
              {detalhes.join(' · ')}
            </Text>
          )}
        </View>

        <View
          style={{
            flex: emLinha ? 1.4 : undefined,
            width: emLinha ? undefined : '100%',
            minWidth: 0,
            flexDirection: 'row',
            flexWrap: 'wrap',
            gap: theme.space[12],
          }}
        >
          <Contato
            icone="phone"
            texto={semTelefone ? 'Sem telefone' : formatPhone(membro.phone!)}
            ausente={semTelefone}
          />
          <Contato
            icone="mail"
            texto={semEmail ? 'Sem e-mail' : membro.email!}
            ausente={semEmail}
          />
        </View>

        {incompleto && (
          <View style={{ flexShrink: 0 }}>
            <Etiqueta texto="Incompleto" tom="alerta" />
          </View>
        )}
      </View>

      <Feather name="chevron-right" size={18} color={theme.colors.textMuted} style={{ marginTop: emLinha ? 0 : 12 }} />
    </Pressable>
  );
}

function Contato({
  icone,
  texto,
  ausente,
}: {
  readonly icone: keyof typeof Feather.glyphMap;
  readonly texto: string;
  readonly ausente: boolean;
}) {
  const theme = useTheme();

  // A ausência é dita por PALAVRA ("Sem telefone"), não só pela cor — quem não
  // distingue matiz veria um texto âmbar idêntico a um cinza.
  const cor = ausente ? theme.colors.textOnCategorySoft : theme.colors.textMuted;

  return (
    <View style={{ flexDirection: 'row', alignItems: 'center', gap: theme.space[4], flexShrink: 1, minWidth: 0 }}>
      <Feather name={icone} size={13} color={cor} />
      <Text
        variant="captionBody"
        numberOfLines={1}
        style={{ color: cor, flexShrink: 1, minWidth: 0 }}
      >
        {texto}
      </Text>
    </View>
  );
}

function Etiqueta({
  texto,
  tom,
}: {
  readonly texto: string;
  readonly tom: 'accent' | 'category' | 'alerta' | 'neutro';
}) {
  const theme = useTheme();

  const fundo =
    tom === 'accent' ? theme.colors.surfaceAccentSoft
    : tom === 'category' ? theme.colors.surfaceCategorySoft
    : tom === 'alerta' ? theme.colors.surfaceCategorySoft
    : theme.colors.surfaceInner;

  const cor =
    tom === 'accent' ? theme.colors.textOnAccentSoft
    : tom === 'category' || tom === 'alerta' ? theme.colors.textOnCategorySoft
    : theme.colors.textMuted;

  return (
    <View
      style={{
        paddingVertical: 3,
        paddingHorizontal: theme.space[8],
        borderRadius: theme.radius.tags,
        backgroundColor: fundo,
      }}
    >
      <Text variant="caption" style={{ color: cor }} numberOfLines={1}>
        {texto}
      </Text>
    </View>
  );
}
