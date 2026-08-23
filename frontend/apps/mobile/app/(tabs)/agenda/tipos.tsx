import { describeError } from '@congrega/api-client/errors';
import {
  createEventType,
  deleteEventType,
  listEventTypeIcons,
  updateEventType,
  type EventType,
} from '@congrega/api-client/event-types';
import { AsyncContent } from '@congrega/ui/AsyncContent';
import { Button } from '@congrega/ui/Button';
import { Card } from '@congrega/ui/Card';
import { Dropdown } from '@congrega/ui/Dropdown';
import { EmptyState } from '@congrega/ui/EmptyState';
import { EyebrowPill } from '@congrega/ui/EyebrowPill';
import { Screen } from '@congrega/ui/Screen';
import { SignatureButton } from '@congrega/ui/SignatureButton';
import { SkeletonListRow } from '@congrega/ui/Skeleton';
import { Text } from '@congrega/ui/Text';
import { TextField } from '@congrega/ui/TextField';
import { useTheme } from '@congrega/ui/theme';
import { useCarregamentoGlobal } from '@congrega/ui/GlobalLoading';
import { Feather } from '@expo/vector-icons';
import { router } from 'expo-router';
import { useEffect, useState } from 'react';
import { Pressable, ScrollView, View } from 'react-native';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import { apiClient } from '../../../src/api';
import { useEventTypes } from '../../../src/useEventTypes';

/** Converte o nome vindo do servidor no glifo do Feather, degradando ao genérico. */
/**
 * Cores que a igreja pode escolher para um tipo.
 *
 * <b>Paleta fechada, e não um seletor livre.</b> Um campo de cor arbitrária
 * deixa escolher amarelo-claro para a faixa da agenda — que some sobre o cartão
 * branco e faz o tipo desaparecer da lista. Todas as seis foram medidas contra
 * o branco e passam de 3:1, que é o mínimo de 1.4.11 para elemento não textual.
 *
 * São as mesmas seis que a migration usou para dar uma cor inicial aos tipos que
 * já existiam, então trocar não introduz uma cor que o resto do sistema não
 * conhece.
 */
const CORES: readonly { readonly valor: string; readonly nome: string }[] = [
  { valor: '#44831A', nome: 'Verde' },
  { valor: '#4A5FBF', nome: 'Azul' },
  { valor: '#7C5CBF', nome: 'Roxo' },
  { valor: '#A45C00', nome: 'Âmbar' },
  { valor: '#237268', nome: 'Verde-azulado' },
  { valor: '#B3453F', nome: 'Vermelho' },
];

function iconeDe(nome: string): keyof typeof Feather.glyphMap {
  return nome in Feather.glyphMap ? (nome as keyof typeof Feather.glyphMap) : 'calendar';
}

/**
 * Administração do vocabulário da agenda.
 *
 * `includeInactive` é ligado aqui e desligado no formulário de evento: um tipo
 * desativado precisa continuar visível **nesta** tela, senão desativá-lo o faria
 * sumir do único lugar onde se reativa.
 */
export default function TiposDeEvento() {
  const theme = useTheme();
  const insets = useSafeAreaInsets();
  const { tipos, carregando, erro, recarregar } = useEventTypes(true);

  const [icones, setIcones] = useState<readonly string[]>([]);
  const [emEdicao, setEmEdicao] = useState<EventType | null>(null);
  const [criando, setCriando] = useState(false);

  // A lista de ícones vem do servidor para não divergir da validação dele —
  // duas cópias fariam o app oferecer um ícone que a API recusa.
  useEffect(() => {
    let cancelado = false;
    listEventTypeIcons(apiClient)
      .then((lista) => {
        if (!cancelado) setIcones(lista);
      })
      .catch(() => {
        // Silencioso de propósito: sem a lista o formulário cai no ícone padrão,
        // que é degradação aceitável. Barrar o cadastro inteiro por causa da
        // decoração seria desproporcional.
      });
    return () => {
      cancelado = true;
    };
  }, []);

  const editando = emEdicao !== null || criando;

  return (
    <Screen padded={false} wide>
      <View
        style={{
          paddingTop: insets.top + theme.space[16],
          paddingHorizontal: theme.space[24],
          gap: theme.space[16],
          maxWidth: theme.layout.pageMaxWidth,
          width: '100%',
          alignSelf: 'center',
        }}
      >
        <View
          style={{
            flexDirection: 'row',
            alignItems: 'center',
            justifyContent: 'space-between',
            gap: theme.space[16],
          }}
        >
          <View style={{ gap: theme.space[4], flexShrink: 1 }}>
            <Text variant="eyebrow" tone="muted">
              AGENDA
            </Text>
            <Text variant="heading">Tipos de evento</Text>
          </View>

          <Button label="Voltar" variant="ghost" onPress={() => router.back()} />
        </View>

        <Text variant="captionBody" tone="muted">
          O vocabulário da sua agenda. Um tipo desativado sai do formulário de evento e continua
          marcando os eventos que já o usam.
        </Text>
      </View>

      <AsyncContent
        fill
        loading={carregando}
        skeleton={
          <View style={{ paddingHorizontal: theme.space[24], paddingTop: theme.space[16], gap: theme.space[8] }}>
            <SkeletonListRow />
            <SkeletonListRow />
            <SkeletonListRow />
          </View>
        }
        failure={erro}
        errorTitle="Não deu para carregar os tipos"
        onRetry={recarregar}
        isEmpty={tipos.length === 0 && !editando}
        empty={
          <EmptyState
            title="Nenhum tipo cadastrado"
            description="Cultos, ensaios, células, vigílias — cadastre os nomes que a sua igreja usa. Eventos podem ser salvos sem tipo enquanto isso."
            action={<SignatureButton label="Cadastrar tipo" onPress={() => setCriando(true)} />}
          />
        }
      >
        <ScrollView
          contentContainerStyle={{
            paddingHorizontal: theme.space[24],
            paddingTop: theme.space[8],
            paddingBottom: insets.bottom + theme.space[48],
            gap: theme.space[16],
            maxWidth: theme.layout.pageMaxWidth,
            width: '100%',
            alignSelf: 'center',
          }}
        >
          {editando ? (
            <Formulario
              inicial={emEdicao}
              icones={icones}
              onCancelar={() => {
                setEmEdicao(null);
                setCriando(false);
              }}
              onSalvo={() => {
                setEmEdicao(null);
                setCriando(false);
                recarregar();
              }}
            />
          ) : (
            <Button label="Cadastrar tipo" variant="outline" onPress={() => setCriando(true)} />
          )}

          {tipos.length > 0 && (
            <Card style={{ padding: 0, gap: 0, overflow: 'hidden' }}>
              {tipos.map((tipo, indice) => (
                <View key={tipo.id}>
                  {indice > 0 && (
                    <View style={{ height: 1, backgroundColor: theme.colors.hairline }} />
                  )}
                  <Linha
                    tipo={tipo}
                    onEditar={() => {
                      setCriando(false);
                      setEmEdicao(tipo);
                    }}
                    onMudou={recarregar}
                  />
                </View>
              ))}
            </Card>
          )}
        </ScrollView>
      </AsyncContent>
    </Screen>
  );
}

function Linha({
  tipo,
  onEditar,
  onMudou,
}: {
  readonly tipo: EventType;
  readonly onEditar: () => void;
  readonly onMudou: () => void;
}) {
  const theme = useTheme();
  const [ocupado, setOcupado] = useState(false);
  const [erro, setErro] = useState<string | null>(null);

  async function executar(acao: () => Promise<unknown>) {
    setErro(null);
    setOcupado(true);
    try {
      await acao();
      onMudou();
    } catch (causa) {
      // O 409 de "tipo em uso" chega aqui com a explicação que o servidor
      // escreveu. Mostrá-la é melhor do que uma mensagem genérica: ela já diz o
      // que fazer em vez disso (desativar).
      setErro(describeError(causa));
    } finally {
      setOcupado(false);
    }
  }

  return (
    <View
      style={{
        gap: theme.space[8],
        paddingHorizontal: theme.space[24],
        paddingVertical: theme.space[16],
        opacity: ocupado ? 0.6 : 1,
      }}
    >
      <View style={{ flexDirection: 'row', alignItems: 'center', gap: theme.space[12] }}>
        <Feather name={iconeDe(tipo.icon)} size={18} color={theme.colors.surfaceAccent} />

        <View style={{ flex: 1, minWidth: 0, gap: 2 }}>
          <View style={{ flexDirection: 'row', alignItems: 'center', gap: theme.space[8] }}>
            <Text variant="bodyStrong" numberOfLines={1} style={{ flexShrink: 1 }}>
              {tipo.name}
            </Text>
            {!tipo.isActive && <EyebrowPill label="Desativado" tone="badge" />}
          </View>

          <Text variant="captionBody" tone="muted">
            {tipo.eventCount === 0
              ? 'nenhum evento usa este tipo'
              : tipo.eventCount === 1
                ? '1 evento usa este tipo'
                : `${tipo.eventCount} eventos usam este tipo`}
          </Text>
        </View>

        <Pressable
          onPress={onEditar}
          disabled={ocupado}
          accessibilityRole="button"
          accessibilityLabel={`Editar ${tipo.name}`}
          hitSlop={8}
          style={({ pressed }) => ({ opacity: pressed ? 0.6 : 1 })}
        >
          <Feather name="edit-2" size={16} color={theme.colors.textMuted} />
        </Pressable>

        <Pressable
          onPress={() =>
            void executar(() =>
              updateEventType(apiClient, tipo.id, {
                name: tipo.name,
                icon: tipo.icon,
                isActive: !tipo.isActive,
              }),
            )
          }
          disabled={ocupado}
          accessibilityRole="switch"
          accessibilityState={{ checked: tipo.isActive }}
          accessibilityLabel={`${tipo.isActive ? 'Desativar' : 'Reativar'} ${tipo.name}`}
          hitSlop={8}
          style={({ pressed }) => ({ opacity: pressed ? 0.6 : 1 })}
        >
          <Feather
            name={tipo.isActive ? 'eye' : 'eye-off'}
            size={16}
            color={theme.colors.textMuted}
          />
        </Pressable>

        {/* Excluir só aparece quando ninguém usa o tipo.
            Não é a autorização — quem recusa é a chave estrangeira, e o 409 é
            tratado abaixo. É evitar oferecer um botão cujo único resultado
            possível é um erro. */}
        {tipo.eventCount === 0 && (
          <Pressable
            onPress={() => void executar(() => deleteEventType(apiClient, tipo.id))}
            disabled={ocupado}
            accessibilityRole="button"
            accessibilityLabel={`Excluir ${tipo.name}`}
            hitSlop={8}
            style={({ pressed }) => ({ opacity: pressed ? 0.6 : 1 })}
          >
            <Feather name="trash-2" size={16} color={theme.colors.danger} />
          </Pressable>
        )}
      </View>

      {erro !== null && (
        <Text variant="captionBody" style={{ color: theme.colors.danger }}>
          {erro}
        </Text>
      )}
    </View>
  );
}

function Formulario({
  inicial,
  icones,
  onCancelar,
  onSalvo,
}: {
  readonly inicial: EventType | null;
  readonly icones: readonly string[];
  readonly onCancelar: () => void;
  readonly onSalvo: () => void;
}) {
  const theme = useTheme();
  const [nome, setNome] = useState(inicial?.name ?? '');
  const [icone, setIcone] = useState<string | null>(inicial?.icon ?? 'calendar');
  const [cor, setCor] = useState<string>(inicial?.colorHex ?? CORES[0]!.valor);
  const { executar } = useCarregamentoGlobal();
  const [erro, setErro] = useState<string | null>(null);
  const [salvando, setSalvando] = useState(false);

  async function salvar() {
    if (nome.trim().length < 2) {
      setErro('Informe um nome com pelo menos 2 letras.');
      return;
    }

    setErro(null);
    setSalvando(true);

    try {
      const entrada = {
        name: nome.trim(),
        ...(icone === null ? {} : { icon: icone }),
        colorHex: cor,
        ...(inicial === null ? {} : { isActive: inicial.isActive }),
      };

      await executar('Salvando o tipo…', 'Atualizando o vocabulário da agenda.', () =>
        inicial === null
          ? createEventType(apiClient, entrada)
          : updateEventType(apiClient, inicial.id, entrada),
      );

      onSalvo();
    } catch (causa) {
      // Inclui o 409 de nome repetido, que vem da constraint única do banco —
      // e não de uma verificação prévia, que teria janela de corrida.
      setErro(describeError(causa));
    } finally {
      setSalvando(false);
    }
  }

  return (
    <Card style={{ gap: theme.space[16] }}>
      <Text variant="subheading">{inicial === null ? 'Novo tipo' : `Editar ${inicial.name}`}</Text>

      <TextField
        label="Nome"
        placeholder="Vigília"
        defaultValue={inicial?.name ?? ''}
        onValueChange={(v) => {
          setNome(v);
          if (erro !== null) setErro(null);
        }}
        autoCapitalize="sentences"
        autoFocus
      />

      <Dropdown
        label="Ícone"
        value={icone}
        onChange={setIcone}
        options={icones.map((nomeDoIcone) => ({
          value: nomeDoIcone,
          label: nomeDoIcone,
          icon: <Feather name={iconeDe(nomeDoIcone)} size={16} color={theme.colors.surfaceAccent} />,
        }))}
        placeholder="calendar"
        emptyMessage="Não deu para carregar os ícones; o tipo usará o padrão."
      />

      <View style={{ gap: theme.space[8] }}>
        <Text variant="eyebrow" tone="muted">
          COR NA AGENDA
        </Text>

        <View
          style={{ flexDirection: 'row', flexWrap: 'wrap', gap: theme.space[8] }}
          accessibilityRole="radiogroup"
        >
          {CORES.map((opcao) => (
            <Pressable
              key={opcao.valor}
              onPress={() => setCor(opcao.valor)}
              accessibilityRole="radio"
              accessibilityState={{ checked: cor === opcao.valor }}
              // O NOME da cor entra no rótulo: um seletor em que as opções só se
              // distinguem por matiz é inacessível por definição, e "opção 3"
              // não diz qual foi escolhida.
              accessibilityLabel={opcao.nome}
              style={{
                width: theme.touch.minTarget,
                height: theme.touch.minTarget,
                borderRadius: theme.radius.inputs,
                alignItems: 'center',
                justifyContent: 'center',
                backgroundColor: `${opcao.valor}2E`,
                borderWidth: cor === opcao.valor ? 2 : 1,
                borderColor: cor === opcao.valor ? opcao.valor : theme.colors.hairline,
              }}
            >
              {/* O visto marca a escolhida ALÉM da borda: duas cores diferentes
                  com bordas diferentes ainda são duas cores, e quem não
                  distingue matiz não veria qual está ativa. */}
              {cor === opcao.valor ? (
                <Feather name="check" size={16} color={opcao.valor} />
              ) : (
                <View
                  style={{
                    width: 14,
                    height: 14,
                    borderRadius: 7,
                    backgroundColor: opcao.valor,
                  }}
                />
              )}
            </Pressable>
          ))}
        </View>

        <Text variant="captionBody" tone="muted">
          Aparece na faixa e na etiqueta da agenda, sempre ao lado do nome do tipo.
        </Text>
      </View>

      {erro !== null && (
        <Text variant="captionBody" style={{ color: theme.colors.danger }}>
          {erro}
        </Text>
      )}

      <SignatureButton
        label={salvando ? 'Salvando' : 'Salvar tipo'}
        onPress={() => void salvar()}
        disabled={salvando}
      />

      <Button label="Cancelar" variant="ghost" onPress={onCancelar} />
    </Card>
  );
}
