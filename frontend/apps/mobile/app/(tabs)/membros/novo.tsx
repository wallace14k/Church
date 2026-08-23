import { describeError } from '@congrega/api-client/errors';
import { createMember } from '@congrega/api-client/members';
import { isProbablyEmail } from '@congrega/core/validation';
import { Button } from '@congrega/ui/Button';
import { Card } from '@congrega/ui/Card';
import { Screen } from '@congrega/ui/Screen';
import { SignatureButton } from '@congrega/ui/SignatureButton';
import { Text } from '@congrega/ui/Text';
import { TextField } from '@congrega/ui/TextField';
import { useTheme } from '@congrega/ui/theme';
import { Feather } from '@expo/vector-icons';
import { router } from 'expo-router';
import { useMemo, useState } from 'react';
import { KeyboardAvoidingView, Platform, Pressable, ScrollView, View, useWindowDimensions } from 'react-native';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import {
  CampoDeEndereco,
  enderecoVazio,
  paraPayload,
  temEndereco,
  type EnderecoEditavel,
} from '../../../src/CampoDeEndereco';
import { apiClient } from '../../../src/api';

/** Só dígitos, no máximo 11 — o backend guarda sem formatação. */
function apenasDigitos(valor: string): string {
  return valor.replace(/\D/gu, '').slice(0, 11);
}

/** Aceita `31/12/1980` e devolve `1980-12-31`, que é o formato do contrato. */
function paraIso(dataBr: string): string | undefined {
  const partes = /^(\d{2})\/(\d{2})\/(\d{4})$/u.exec(dataBr.trim());
  if (partes === null) return undefined;

  const [, dia, mes, ano] = partes;
  return `${ano}-${mes}-${dia}`;
}

/** Máscara progressiva de data enquanto se digita. */
function mascaraData(valor: string): string {
  const d = valor.replace(/\D/gu, '').slice(0, 8);
  if (d.length <= 2) return d;
  if (d.length <= 4) return `${d.slice(0, 2)}/${d.slice(2)}`;
  return `${d.slice(0, 2)}/${d.slice(2, 4)}/${d.slice(4)}`;
}

/** Abaixo disto o resumo lateral desce para baixo do formulário. */
const LARGURA_PARA_DUAS_COLUNAS = 900;

/**
 * Paleta do avatar do resumo, escolhida por **hash do nome**.
 *
 * Aqui o hash é o certo, ao contrário da listagem — que usa o índice da linha.
 * Lá o objetivo é separar uma linha da seguinte, e duas pessoas vizinhas podem
 * colidir no mesmo tom. Aqui há uma pessoa só, e a cor estável enquanto se
 * digita evita o avatar piscando de cor a cada letra do sobrenome.
 */
const PALETA = [
  { fundo: '#E0EFCA', cor: '#3F6224' },
  { fundo: '#F0E5D7', cor: '#6A5136' },
  { fundo: '#EFEAD8', cor: '#655C3E' },
  { fundo: '#DDE9F0', cor: '#355A6D' },
  { fundo: '#F0DDE8', cor: '#763557' },
] as const;

function corDoNome(nome: string) {
  let h = 0;
  for (const c of nome) h = c.charCodeAt(0) + ((h << 5) - h);
  return PALETA[Math.abs(h) % PALETA.length]!;
}

function iniciais(nome: string): string {
  const PARTICULAS = ['de', 'da', 'do', 'das', 'dos', 'e'];

  const partes = nome
    .trim()
    .split(/\s+/)
    .filter((p) => p.length > 0 && !PARTICULAS.includes(p.toLowerCase()));

  if (partes.length === 0) return '?';
  if (partes.length === 1) return partes[0]!.slice(0, 2).toUpperCase();

  return (partes[0]!.charAt(0) + partes[partes.length - 1]!.charAt(0)).toUpperCase();
}

export default function NovoMembro() {
  const theme = useTheme();
  const insets = useSafeAreaInsets();
  const { width } = useWindowDimensions();

  /**
   * Os campos passaram de `ref` para estado — e isso **não** os torna
   * controlados.
   *
   * O `TextField` continua não controlado: ele guarda o próprio valor e só
   * notifica por `onValueChange`. O que mudou é que a tela agora espelha esse
   * valor em estado para desenhar o resumo lateral, que precisa acompanhar a
   * digitação. O anti-padrão que o componente evita é devolver o valor ao
   * nativo a cada render (`value={...}`), e isso continua sem acontecer: o
   * cursor não salta porque nada é escrito de volta no input.
   */
  const [nome, setNome] = useState('');
  const [email, setEmail] = useState('');
  const [telefone, setTelefone] = useState('');
  const [nascimento, setNascimento] = useState('');
  const [endereco, setEndereco] = useState<EnderecoEditavel>(enderecoVazio);

  const [erros, setErros] = useState<Record<string, string>>({});
  const [erroGeral, setErroGeral] = useState<string | null>(null);
  const [salvando, setSalvando] = useState(false);

  const emDuasColunas = width >= LARGURA_PARA_DUAS_COLUNAS;

  const enderecoPreenchido = temEndereco(endereco);

  const conferencia = useMemo(
    () => [
      { rotulo: 'Nome completo', feito: nome.trim() !== '', obrigatorio: true },
      { rotulo: 'E-mail', feito: email.trim() !== '', obrigatorio: false },
      { rotulo: 'Telefone', feito: telefone.trim() !== '', obrigatorio: false },
      { rotulo: 'Data de nascimento', feito: nascimento.trim() !== '', obrigatorio: false },
      { rotulo: 'Endereço', feito: enderecoPreenchido, obrigatorio: false },
    ],
    [nome, email, telefone, nascimento, enderecoPreenchido],
  );

  const preenchidos = conferencia.filter((c) => c.feito).length;
  const percentual = Math.round((preenchidos / conferencia.length) * 100);

  /**
   * A frase da barra inferior.
   *
   * Ela existe para responder "posso salvar já?" sem a pessoa precisar rolar
   * até o topo para reler que só o nome é obrigatório.
   */
  const situacao =
    nome.trim() === '' ? 'Preenchendo dados pessoais'
    : preenchidos === 1 ? 'Só o nome já é suficiente para salvar'
    : 'Tudo certo, pode salvar quando quiser';

  async function salvar() {
    const problemas: Record<string, string> = {};

    if (nome.trim().length < 2) {
      problemas['nome'] = 'Informe o nome completo.';
    }

    if (email.trim().length > 0 && !isProbablyEmail(email)) {
      problemas['email'] = 'Esse e-mail não parece válido.';
    }

    const dataIso = nascimento.trim().length > 0 ? paraIso(nascimento) : undefined;
    if (nascimento.trim().length > 0 && dataIso === undefined) {
      problemas['nascimento'] = 'Use o formato dia/mês/ano.';
    }

    setErros(problemas);
    if (Object.keys(problemas).length > 0) return;

    setErroGeral(null);
    setSalvando(true);

    try {
      await createMember(apiClient, {
        fullName: nome.trim(),
        ...(email.trim() ? { email: email.trim() } : {}),
        ...(telefone ? { phone: telefone } : {}),
        ...(dataIso ? { birthDate: dataIso } : {}),
        // Só manda o endereço se houver algo nele. Um objeto todo em branco
        // significaria "apague o endereço" para o servidor, o que num cadastro
        // novo não faz sentido — e criaria uma linha vazia no banco.
        ...(enderecoPreenchido ? { address: paraPayload(endereco) } : {}),
      });

      // `replace` e não `push`: voltar para o formulário depois de salvar
      // convidaria a cadastrar a mesma pessoa duas vezes.
      router.replace('/membros');
    } catch (causa) {
      setErroGeral(describeError(causa));
    } finally {
      setSalvando(false);
    }
  }

  return (
    <Screen padded={false} wide>
      <KeyboardAvoidingView behavior={Platform.OS === 'ios' ? 'padding' : undefined} style={{ flex: 1 }}>
        <ScrollView
          contentContainerStyle={{
            width: '100%',
            maxWidth: 1080,
            alignSelf: 'center',
            paddingHorizontal: theme.space[20],
            paddingTop: theme.space[28],
            // Espaço para a barra fixa não cobrir o último campo.
            paddingBottom: insets.bottom + theme.space[96],
            gap: theme.space[20],
          }}
          keyboardShouldPersistTaps="handled"
        >
          {/* ------------------------------------------------------- trilha */}
          <View style={{ flexDirection: 'row', alignItems: 'center', gap: theme.space[8], flexWrap: 'wrap' }}>
            <Pressable
              onPress={() => router.push('/membros')}
              accessibilityRole="link"
              accessibilityLabel="Voltar para membros"
              style={({ pressed }) => ({ opacity: pressed ? 0.6 : 1 })}
            >
              <Text variant="caption" tone="muted">
                Membros
              </Text>
            </Pressable>
            <Text variant="caption" tone="muted">
              /
            </Text>
            <Text variant="caption" tone="accent">
              Novo membro
            </Text>
          </View>

          {/* ---------------------------------------------------- cabeçalho */}
          <View
            style={{
              flexDirection: emDuasColunas ? 'row' : 'column',
              alignItems: emDuasColunas ? 'flex-end' : 'flex-start',
              justifyContent: 'space-between',
              gap: theme.space[16],
            }}
          >
            <View style={{ flexShrink: 1 }}>
              <Text variant="headingLg">Cadastrar membro</Text>
              <Text variant="body" tone="muted" style={{ marginTop: theme.space[8] }}>
                Preencha os dados abaixo. Apenas o nome completo é obrigatório.
              </Text>
            </View>

            <Button
              label="Voltar para membros"
              variant="outline"
              onPress={() => router.push('/membros')}
            />
          </View>

          {/* -------------------------------------------- formulário + resumo */}
          <View
            style={{
              flexDirection: emDuasColunas ? 'row' : 'column',
              gap: theme.space[20],
              alignItems: 'flex-start',
            }}
          >
            <View style={{ flex: emDuasColunas ? 1.5 : undefined, width: emDuasColunas ? undefined : '100%', gap: theme.space[20] }}>
              <Card style={{ gap: theme.space[16] }}>
                <TituloDePainel
                  icone="user"
                  titulo="Dados pessoais"
                  descricao="Informações básicas de identificação"
                />

                <TextField
                  label="Nome completo"
                  placeholder="Maria Aparecida da Silva"
                  defaultValue=""
                  onValueChange={(v) => {
                    setNome(v);
                    if (erros['nome']) setErros((e) => ({ ...e, nome: '' }));
                  }}
                  {...(erros['nome'] ? { error: erros['nome'] } : {})}
                  autoCapitalize="words"
                  autoFocus
                />

                <View style={{ flexDirection: emDuasColunas ? 'row' : 'column', gap: theme.space[12] }}>
                  <View style={{ flex: 1 }}>
                    <TextField
                      label="E-mail · opcional"
                      placeholder="nome@email.com"
                      defaultValue=""
                      onValueChange={(v) => {
                        setEmail(v);
                        if (erros['email']) setErros((e) => ({ ...e, email: '' }));
                      }}
                      {...(erros['email'] ? { error: erros['email'] } : {})}
                      keyboardType="email-address"
                      autoCapitalize="none"
                      autoCorrect={false}
                    />
                  </View>

                  <View style={{ flex: 1 }}>
                    <TextField
                      label="Telefone · opcional"
                      placeholder="(31) 90000-0000"
                      defaultValue=""
                      transform={apenasDigitos}
                      onValueChange={setTelefone}
                      keyboardType="phone-pad"
                      hint="Só números, com DDD"
                    />
                  </View>
                </View>

                <TextField
                  label="Data de nascimento · opcional"
                  placeholder="dia/mês/ano"
                  defaultValue=""
                  transform={mascaraData}
                  onValueChange={(v) => {
                    setNascimento(v);
                    if (erros['nascimento']) setErros((e) => ({ ...e, nascimento: '' }));
                  }}
                  {...(erros['nascimento'] ? { error: erros['nascimento'] } : {})}
                  keyboardType="number-pad"
                  hint="Usada no relatório de aniversariantes"
                />
              </Card>

              <Card style={{ gap: theme.space[16] }}>
                <TituloDePainel
                  icone="map-pin"
                  titulo="Endereço"
                  descricao="Opcional — ajuda a organizar visitas por região"
                />

                {/* Sem rótulo próprio: o título do painel logo acima já diz "Endereço",
                    e dois iguais em sequência leem como duas seções. */}
                <CampoDeEndereco valor={endereco} onChange={setEndereco} titulo={null} />
              </Card>

              {erroGeral !== null && (
                <View
                  style={{
                    padding: theme.space[12],
                    borderRadius: theme.radius.inputs,
                    borderWidth: 1,
                    borderColor: theme.colors.danger,
                    backgroundColor: theme.colors.surface,
                  }}
                >
                  <Text variant="captionBody">{erroGeral}</Text>
                </View>
              )}
            </View>

            {/* -------------------------------------------------- resumo */}
            <View style={{ flex: emDuasColunas ? 0.95 : undefined, width: emDuasColunas ? undefined : '100%', gap: theme.space[12] }}>
              <Card style={{ gap: theme.space[16], alignItems: 'stretch' }}>
                <View style={{ alignItems: 'center', gap: theme.space[4] }}>
                  <View
                    accessibilityElementsHidden
                    importantForAccessibility="no-hide-descendants"
                    style={{
                      width: 74,
                      height: 74,
                      borderRadius: 37,
                      alignItems: 'center',
                      justifyContent: 'center',
                      marginBottom: theme.space[8],
                      backgroundColor:
                        nome.trim() === '' ? theme.colors.surfaceAccentSoft : corDoNome(nome).fundo,
                    }}
                  >
                    <Text
                      variant="heading"
                      style={{
                        color: nome.trim() === '' ? theme.colors.textOnAccentSoft : corDoNome(nome).cor,
                      }}
                    >
                      {nome.trim() === '' ? '?' : iniciais(nome)}
                    </Text>
                  </View>

                  <Text
                    variant="subheading"
                    tone={nome.trim() === '' ? 'muted' : 'ink'}
                    numberOfLines={2}
                    style={{ textAlign: 'center' }}
                  >
                    {nome.trim() === '' ? 'Nome do membro' : nome.trim()}
                  </Text>

                  {/* O subtítulo mostra a cidade quando ela existe; senão, diz o
                      que a prévia é. Inventar "45 anos" a partir de nada seria
                      preencher a tela com o que ainda não foi digitado. */}
                  <Text variant="captionBody" tone="muted" numberOfLines={1}>
                    {endereco.localidade.trim() === ''
                      ? 'Aparecerá assim na lista'
                      : endereco.localidade.trim() +
                        (endereco.estado.trim() === '' ? '' : ` · ${endereco.estado.trim()}`)}
                  </Text>

                  {nascimento.trim() !== '' && (
                    <View
                      style={{
                        flexDirection: 'row',
                        alignItems: 'center',
                        gap: theme.space[4],
                        marginTop: theme.space[8],
                        paddingVertical: theme.space[4],
                        paddingHorizontal: theme.space[8],
                        borderRadius: theme.radius.tags,
                        backgroundColor: theme.colors.surfaceCategorySoft,
                      }}
                    >
                      <Feather name="gift" size={12} color={theme.colors.textOnCategorySoft} />
                      <Text variant="caption" style={{ color: theme.colors.textOnCategorySoft }}>
                        {nascimento.trim()}
                      </Text>
                    </View>
                  )}
                </View>

                <View
                  style={{
                    paddingTop: theme.space[16],
                    borderTopWidth: 1,
                    borderTopColor: theme.colors.hairline,
                    gap: theme.space[12],
                  }}
                >
                  {conferencia.map((item) => (
                    <ItemDeConferencia key={item.rotulo} {...item} />
                  ))}
                </View>

                <View style={{ gap: theme.space[8] }}>
                  {/* Barra de progresso.
                      `accessibilityValue` com `now`/`max` é o que faz o leitor
                      de tela anunciar "3 de 5"; uma barra sem isso é só um
                      retângulo que muda de largura em silêncio. */}
                  <View
                    accessibilityRole="progressbar"
                    accessibilityLabel="Preenchimento do perfil"
                    accessibilityValue={{ now: preenchidos, min: 0, max: conferencia.length }}
                    style={{
                      height: 7,
                      borderRadius: theme.radius.tags,
                      overflow: 'hidden',
                      backgroundColor: theme.colors.surfaceInner,
                      borderWidth: 1,
                      borderColor: theme.colors.hairline,
                    }}
                  >
                    <View
                      style={{
                        height: '100%',
                        width: `${percentual}%`,
                        borderRadius: theme.radius.tags,
                        backgroundColor: theme.colors.surfaceAccent,
                      }}
                    />
                  </View>

                  <View style={{ flexDirection: 'row', justifyContent: 'space-between' }}>
                    <Text variant="captionBody" tone="muted">
                      Perfil
                    </Text>
                    <Text variant="captionBody" tone="muted">
                      {percentual}% completo
                    </Text>
                  </View>
                </View>
              </Card>

              <Card style={{ flexDirection: 'row', gap: theme.space[12] }}>
                <Feather name="info" size={17} color={theme.colors.surfaceAccent} style={{ marginTop: 2 }} />
                <Text variant="captionBody" tone="muted" style={{ flex: 1 }}>
                  Só o nome é obrigatório. Você pode completar e-mail, telefone e endereço depois, a
                  qualquer momento.
                </Text>
              </Card>
            </View>
          </View>
        </ScrollView>

        {/* ------------------------------------------------- barra de ações */}
        <View
          style={{
            borderTopWidth: 1,
            borderTopColor: theme.colors.hairline,
            backgroundColor: theme.colors.surface,
            paddingBottom: insets.bottom,
          }}
        >
          <View
            style={{
              width: '100%',
              maxWidth: 1080,
              alignSelf: 'center',
              flexDirection: 'row',
              alignItems: 'center',
              justifyContent: 'flex-end',
              gap: theme.space[12],
              paddingHorizontal: theme.space[20],
              paddingVertical: theme.space[16],
              flexWrap: 'wrap',
            }}
          >
            {emDuasColunas && (
              <Text
                variant="captionBody"
                tone="muted"
                style={{ marginRight: 'auto', flexShrink: 1 }}
                // A frase muda enquanto se digita; sem isto o leitor de tela
                // nunca saberia que ela mudou.
                accessibilityLiveRegion="polite"
              >
                {situacao}
              </Text>
            )}

            <Button label="Cancelar" variant="outline" onPress={() => router.back()} />
            <SignatureButton
              label={salvando ? 'Salvando' : 'Salvar membro'}
              onPress={() => void salvar()}
              disabled={salvando}
            />
          </View>
        </View>
      </KeyboardAvoidingView>
    </Screen>
  );
}

function TituloDePainel({
  icone,
  titulo,
  descricao,
}: {
  readonly icone: keyof typeof Feather.glyphMap;
  readonly titulo: string;
  readonly descricao: string;
}) {
  const theme = useTheme();

  return (
    <View style={{ flexDirection: 'row', alignItems: 'center', gap: theme.space[12] }}>
      <View
        accessibilityElementsHidden
        importantForAccessibility="no-hide-descendants"
        style={{
          width: 36,
          height: 36,
          borderRadius: theme.radius.inputs,
          alignItems: 'center',
          justifyContent: 'center',
          backgroundColor: theme.colors.surfaceAccentSoft,
        }}
      >
        <Feather name={icone} size={18} color={theme.colors.textOnAccentSoft} />
      </View>

      <View style={{ flex: 1, minWidth: 0 }}>
        <Text variant="subheading">{titulo}</Text>
        <Text variant="captionBody" tone="muted" numberOfLines={2}>
          {descricao}
        </Text>
      </View>
    </View>
  );
}

function ItemDeConferencia({
  rotulo,
  feito,
  obrigatorio,
}: {
  readonly rotulo: string;
  readonly feito: boolean;
  readonly obrigatorio: boolean;
}) {
  const theme = useTheme();

  return (
    <View
      style={{ flexDirection: 'row', alignItems: 'center', gap: theme.space[12] }}
      accessible
      // Uma frase só. Separados, o rótulo, o estado e o "opcional" seriam três
      // fragmentos que o leitor de tela anuncia sem relação entre si.
      accessibilityLabel={`${rotulo}: ${feito ? 'preenchido' : 'em branco'}${obrigatorio ? '' : ', opcional'}`}
    >
      <View
        style={{
          width: 19,
          height: 19,
          borderRadius: 10,
          alignItems: 'center',
          justifyContent: 'center',
          borderWidth: feito ? 0 : 1.5,
          borderColor: theme.colors.hairline,
          backgroundColor: feito ? theme.colors.surfaceAccent : 'transparent',
        }}
      >
        {/* A marca de conferido é um ✓, não só o preenchimento verde: verde e
            âmbar têm luminância quase idêntica neste sistema, e um estado
            comunicado só por cor some para quem não distingue matiz. */}
        {feito && <Feather name="check" size={11} color={theme.colors.textOnAccent} />}
      </View>

      <Text variant="captionBody" tone={feito ? 'ink' : 'muted'} style={{ flex: 1 }}>
        {rotulo}
      </Text>

      {!obrigatorio && (
        <Text variant="eyebrow" tone="muted">
          OPCIONAL
        </Text>
      )}
    </View>
  );
}
