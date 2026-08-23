import { describeError, describeFailure, type Failure } from '@congrega/api-client/errors';
import {
  deleteConnector,
  listConnectors,
  saveConnector,
  testConnector,
  type Connector,
  type ConnectorKind,
} from '@congrega/api-client/connectors';
import { formatDate, formatTime } from '@congrega/core/datetime';
import { AsyncContent } from '@congrega/ui/AsyncContent';
import { Button } from '@congrega/ui/Button';
import { Card } from '@congrega/ui/Card';
import { Chip } from '@congrega/ui/Chip';
import { EyebrowPill } from '@congrega/ui/EyebrowPill';
import { useCarregamentoGlobal } from '@congrega/ui/GlobalLoading';
import { Screen } from '@congrega/ui/Screen';
import { SignatureButton } from '@congrega/ui/SignatureButton';
import { SkeletonListRow } from '@congrega/ui/Skeleton';
import { Text } from '@congrega/ui/Text';
import { TextField } from '@congrega/ui/TextField';
import { useTheme } from '@congrega/ui/theme';
import { Feather } from '@expo/vector-icons';
import { useCallback, useEffect, useRef, useState } from 'react';
import { Platform, Pressable, ScrollView, View, useWindowDimensions } from 'react-native';
import { apiClient } from '../../../src/api';
import { CONECTORES, type DefinicaoDeConector } from '../../../src/conectores';

/** Abaixo disto os cards deixam de dividir a linha. */
const LARGURA_PARA_DUAS_COLUNAS = 1080;

export default function Configuracoes() {
  const theme = useTheme();
  const { width } = useWindowDimensions();

  const [conectores, setConectores] = useState<readonly Connector[]>([]);
  const [carregando, setCarregando] = useState(true);
  const [erro, setErro] = useState<Failure | null>(null);

  const emDuasColunas = width >= LARGURA_PARA_DUAS_COLUNAS;

  const carregar = useCallback(() => {
    setCarregando(true);
    setErro(null);

    listConnectors(apiClient)
      .then((resposta) => {
        setConectores(resposta);
        setCarregando(false);
      })
      .catch((causa: unknown) => {
        setErro(describeFailure(causa));
        setCarregando(false);
      });
  }, []);

  useEffect(carregar, [carregar]);

  return (
    <Screen wide>
      <ScrollView
        contentContainerStyle={{
          paddingBottom: theme.space[48],
          gap: theme.space[16],
          maxWidth: 1200,
          width: '100%',
          alignSelf: 'center',
        }}
      >
        <View style={{ gap: theme.space[4] }}>
          <Text variant="eyebrow" tone="muted">
            SUA IGREJA
          </Text>
          <Text variant="heading">Configurações</Text>
          <Text variant="captionBody" tone="muted">
            As credenciais ficam guardadas cifradas e nunca voltam para esta tela — nem para você,
            nem para quem tiver acesso ao banco.
          </Text>
        </View>

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
          errorTitle="Não deu para carregar as integrações"
          onRetry={carregar}
        >
          <View
            style={{
              flexDirection: emDuasColunas ? 'row' : 'column',
              flexWrap: 'wrap',
              gap: theme.space[16],
              alignItems: 'flex-start',
            }}
          >
            {CONECTORES.map((definicao) => (
              <View
                key={definicao.kind}
                style={{
                  width: emDuasColunas ? '48.5%' : '100%',
                  minWidth: 0,
                }}
              >
                <CartaoDeConector
                  definicao={definicao}
                  conector={conectores.find((c) => c.kind === definicao.kind) ?? null}
                  aoMudar={carregar}
                />
              </View>
            ))}
          </View>
        </AsyncContent>
      </ScrollView>
    </Screen>
  );
}

function CartaoDeConector({
  definicao,
  conector,
  aoMudar,
}: {
  readonly definicao: DefinicaoDeConector;
  readonly conector: Connector | null;
  readonly aoMudar: () => void;
}) {
  const theme = useTheme();
  const [aberto, setAberto] = useState(false);

  const configurado = conector !== null;

  return (
    <Card style={{ gap: theme.space[12] }}>
      <View style={{ flexDirection: 'row', alignItems: 'flex-start', gap: theme.space[12], minWidth: 0 }}>
        <View
          style={{
            width: 40,
            height: 40,
            borderRadius: theme.radius.smallCards,
            flexShrink: 0,
            alignItems: 'center',
            justifyContent: 'center',
            backgroundColor: theme.colors.surfaceAccentSoft,
          }}
        >
          <Feather name={definicao.icone} size={19} color={theme.colors.textOnAccentSoft} />
        </View>

        <View style={{ flex: 1, minWidth: 0, gap: theme.space[4] }}>
          <View
            style={{ flexDirection: 'row', alignItems: 'center', gap: theme.space[8], flexWrap: 'wrap' }}
          >
            <Text variant="subheading">{definicao.nome}</Text>
            <SeloDeSituacao conector={conector} />
          </View>

          <Text variant="captionBody" tone="muted">
            {definicao.resumo}
          </Text>

          {/* Dois dos três conectores ainda não têm consumidor. Apresentá-los
              como equivalentes prometeria um funcionamento que não existe. */}
          <Text variant="captionBody" tone="muted">
            {definicao.uso}
          </Text>
        </View>
      </View>

      {configurado && conector.settings[definicao.resumoDoCampo] !== undefined && (
        <Text variant="captionBody" numberOfLines={1}>
          {conector.settings[definicao.resumoDoCampo]}
        </Text>
      )}

      {conector?.lastTestMessage != null && (
        <View
          style={{
            padding: theme.space[12],
            borderRadius: theme.radius.inputs,
            borderWidth: 1,
            borderColor:
              conector.lastTestSucceeded === true ? theme.colors.hairline : theme.colors.danger,
            backgroundColor: theme.colors.surfaceInner,
            gap: theme.space[4],
          }}
        >
          <Text variant="captionBody">{conector.lastTestMessage}</Text>
          {conector.lastTestedAt !== null && (
            <Text variant="captionBody" tone="muted">
              Testado em {formatDate(conector.lastTestedAt)} às {formatTime(conector.lastTestedAt)}
            </Text>
          )}
        </View>
      )}

      <View style={{ flexDirection: 'row', flexWrap: 'wrap', gap: theme.space[8] }}>
        <Button
          label={aberto ? 'Fechar' : configurado ? 'Editar' : 'Configurar'}
          variant="ghost"
          onPress={() => setAberto((a) => !a)}
        />
        {configurado && <BotaoDeTeste kind={definicao.kind} aoTestar={aoMudar} />}
      </View>

      {aberto && (
        <FormularioDeConector
          definicao={definicao}
          conector={conector}
          aoSalvar={() => {
            setAberto(false);
            aoMudar();
          }}
          aoRemover={() => {
            setAberto(false);
            aoMudar();
          }}
        />
      )}
    </Card>
  );
}

/**
 * O selo de situação.
 *
 * <b>Quatro estados, e cada um diz algo diferente.</b> Reduzi-los a
 * "ligado/desligado" apagaria a distinção que mais importa: um conector nunca
 * testado e um que falhou no último teste parecem iguais numa lista, e só o
 * segundo exige ação agora.
 */
function SeloDeSituacao({ conector }: { readonly conector: Connector | null }) {
  if (conector === null) {
    return <EyebrowPill label="Não configurado" tone="neutral" />;
  }

  if (!conector.isEnabled) {
    return <EyebrowPill label="Desligado" tone="neutral" />;
  }

  if (conector.lastTestSucceeded === null) {
    return <EyebrowPill label="Nunca testado" tone="neutral" />;
  }

  return conector.lastTestSucceeded ? (
    <EyebrowPill label="Testado e funcionando" tone="badge" />
  ) : (
    <EyebrowPill label="Último teste falhou" tone="neutral" />
  );
}

function BotaoDeTeste({
  kind,
  aoTestar,
}: {
  readonly kind: ConnectorKind;
  readonly aoTestar: () => void;
}) {
  const [testando, setTestando] = useState(false);
  const { executar } = useCarregamentoGlobal();

  async function testar() {
    setTestando(true);

    try {
      // O resultado não é lido aqui: ele é gravado no conector pelo servidor, e
      // a recarga o traz junto do resto. Mostrar um alerta volátil faria o
      // diagnóstico sumir na primeira mudança de página — e a pergunta que
      // importa dias depois é "isto chegou a funcionar?".
      await executar(
        'Testando a conexão…',
        'Falando com o serviço de verdade. Pode levar até 20 segundos.',
        () => testConnector(apiClient, kind),
      );
    } catch {
      // Falha de rede ao testar. O erro do PRÓPRIO teste vem com 200 e já foi
      // gravado; aqui só sobra o caso de a chamada não completar, e a recarga
      // mostra o estado real.
    } finally {
      setTestando(false);
      aoTestar();
    }
  }

  return (
    <Button
      label={testando ? 'Testando…' : 'Testar conexão'}
      variant="ghost"
      onPress={() => void testar()}
      disabled={testando}
    />
  );
}

function FormularioDeConector({
  definicao,
  conector,
  aoSalvar,
  aoRemover,
}: {
  readonly definicao: DefinicaoDeConector;
  readonly conector: Connector | null;
  readonly aoSalvar: () => void;
  readonly aoRemover: () => void;
}) {
  const theme = useTheme();

  const valores = useRef<Record<string, string>>({});
  const [escolhas, setEscolhas] = useState<Record<string, string>>(() => {
    const inicial: Record<string, string> = {};

    for (const campo of definicao.campos) {
      if (campo.opcoes !== undefined) {
        inicial[campo.chave] =
          conector?.settings[campo.chave] ?? campo.opcoes[0]?.valor ?? '';
      }
    }

    return inicial;
  });

  const [ligado, setLigado] = useState(conector?.isEnabled ?? true);
  const [salvando, setSalvando] = useState(false);
  const [erro, setErro] = useState<string | null>(null);
  const { executar } = useCarregamentoGlobal();

  async function salvar() {
    setErro(null);
    setSalvando(true);

    const settings: Record<string, string> = {};
    let segredo: string | undefined;

    for (const campo of definicao.campos) {
      const digitado = valores.current[campo.chave] ?? '';

      if (campo.segredo === true) {
        // Vazio significa "mantenha o que já está guardado". É o que permite
        // corrigir a porta sem apagar a senha — a tela nunca recebeu a senha de
        // volta, logo não tem como reenviá-la.
        if (digitado.trim() !== '') {
          segredo = digitado;
        }
        continue;
      }

      const valor =
        campo.opcoes !== undefined
          ? (escolhas[campo.chave] ?? '')
          : digitado !== ''
            ? digitado
            : (conector?.settings[campo.chave] ?? '');

      if (valor !== '') {
        settings[campo.chave] = valor;
      }
    }

    try {
      await executar('Salvando a integração…', 'Guardando a credencial cifrada.', () =>
        saveConnector(apiClient, definicao.kind, {
        settings,
        ...(segredo !== undefined ? { secret: segredo } : {}),
        isEnabled: ligado,
        }),
      );

      aoSalvar();
    } catch (causa) {
      setErro(describeError(causa));
    } finally {
      setSalvando(false);
    }
  }

  async function remover() {
    const confirmado =
      Platform.OS !== 'web' ||
      // eslint-disable-next-line no-alert
      globalThis.confirm(
        `Remover a integração ${definicao.nome}? A credencial guardada será apagada e precisará ser digitada de novo.`,
      );

    if (!confirmado) return;

    setSalvando(true);

    try {
      await executar('Removendo a integração…', 'Apagando a credencial guardada.', () =>
        deleteConnector(apiClient, definicao.kind),
      );
      aoRemover();
    } catch (causa) {
      setErro(describeError(causa));
    } finally {
      setSalvando(false);
    }
  }

  return (
    <View
      style={{
        gap: theme.space[12],
        paddingTop: theme.space[12],
        borderTopWidth: 1,
        borderTopColor: theme.colors.hairline,
      }}
    >
      {definicao.campos.map((campo) => {
        if (campo.opcoes !== undefined) {
          return (
            <View key={campo.chave} style={{ gap: theme.space[8] }}>
              <Text variant="eyebrow" tone="muted">
                {campo.rotulo.toUpperCase()}
              </Text>
              <View
                style={{ flexDirection: 'row', flexWrap: 'wrap', gap: theme.space[8] }}
                accessibilityRole="radiogroup"
              >
                {campo.opcoes.map((opcao) => (
                  <Chip
                    key={opcao.valor}
                    label={opcao.rotulo}
                    selected={escolhas[campo.chave] === opcao.valor}
                    onPress={() => setEscolhas((e) => ({ ...e, [campo.chave]: opcao.valor }))}
                  />
                ))}
              </View>
            </View>
          );
        }

        const jaGuardado = campo.segredo === true && conector?.hasSecret === true;

        return (
          <TextField
            key={campo.chave}
            label={campo.rotulo}
            {...(campo.placeholder !== undefined ? { placeholder: campo.placeholder } : {})}
            // O valor de um campo secreto NUNCA é pré-preenchido: ele não vem do
            // servidor. O que a tela mostra é que já existe um guardado.
            defaultValue={campo.segredo === true ? '' : (conector?.settings[campo.chave] ?? '')}
            onValueChange={(v) => {
              valores.current[campo.chave] = v;
            }}
            {...(campo.segredo === true ? { secureTextEntry: true } : {})}
            {...(campo.multilinha === true ? { multiline: true, numberOfLines: 4 } : {})}
            {...(campo.teclado !== undefined ? { keyboardType: campo.teclado } : {})}
            autoCapitalize="none"
            {...(jaGuardado
              ? {
                  hint: `Já configurado. Deixe em branco para manter. ${campo.ajuda ?? ''}`.trim(),
                }
              : campo.ajuda !== undefined
                ? { hint: campo.ajuda }
                : {})}
          />
        );
      })}

      <View style={{ gap: theme.space[8] }}>
        <Text variant="eyebrow" tone="muted">
          SITUAÇÃO
        </Text>
        <View
          style={{ flexDirection: 'row', gap: theme.space[8] }}
          accessibilityRole="radiogroup"
        >
          <Chip label="Ligado" selected={ligado} onPress={() => setLigado(true)} />
          {/* Desligar mantém tudo digitado, inclusive a credencial: é a saída
              para parar os envios quando o provedor bloqueia a conta, sem
              perder o que ninguém consegue redigitar de memória. */}
          <Chip label="Desligado" selected={!ligado} onPress={() => setLigado(false)} />
        </View>
      </View>

      {erro !== null && (
        <View
          style={{
            padding: theme.space[12],
            borderRadius: theme.radius.inputs,
            borderWidth: 1,
            borderColor: theme.colors.danger,
          }}
        >
          <Text variant="captionBody">{erro}</Text>
        </View>
      )}

      <SignatureButton
        label={salvando ? 'Salvando' : 'Salvar integração'}
        onPress={() => void salvar()}
        loading={salvando}
      />

      {conector !== null && (
        <Pressable
          onPress={() => void remover()}
          accessibilityRole="button"
          accessibilityLabel={`Remover a integração ${definicao.nome}`}
          style={{ alignSelf: 'center', paddingVertical: theme.space[8] }}
        >
          <Text variant="caption" style={{ color: theme.colors.danger }}>
            Remover integração
          </Text>
        </Pressable>
      )}
    </View>
  );
}
