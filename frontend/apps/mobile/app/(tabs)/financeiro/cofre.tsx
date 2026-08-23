import { describeError, describeFailure, type Failure } from '@congrega/api-client/errors';
import {
  createVaultMovement,
  getVault,
  listFinancialAccounts,
  type FinancialAccount,
  type Vault,
  type VaultDirection,
} from '@congrega/api-client/giving';
import { formatDate, formatTime } from '@congrega/core/datetime';
import { cents, formatBRL, parseBRL } from '@congrega/core/money';
import { AsyncContent } from '@congrega/ui/AsyncContent';
import { Button } from '@congrega/ui/Button';
import { Card } from '@congrega/ui/Card';
import { Chip } from '@congrega/ui/Chip';
import { EmptyState } from '@congrega/ui/EmptyState';
import { EyebrowPill } from '@congrega/ui/EyebrowPill';
import { Screen } from '@congrega/ui/Screen';
import { SignatureButton } from '@congrega/ui/SignatureButton';
import { SkeletonListRow } from '@congrega/ui/Skeleton';
import { Text } from '@congrega/ui/Text';
import { TextField } from '@congrega/ui/TextField';
import { useTheme } from '@congrega/ui/theme';
import { Feather } from '@expo/vector-icons';
import { router } from 'expo-router';
import { useCallback, useEffect, useRef, useState } from 'react';
import { ScrollView, View, useWindowDimensions } from 'react-native';
import { apiClient } from '../../../src/api';

/** Abaixo disto o formulário e o extrato deixam de dividir a linha. */
const LARGURA_PARA_DUAS_COLUNAS = 900;

/**
 * Data e hora do movimento, no fuso de NEGÓCIO.
 *
 * Não no fuso do aparelho: o extrato do cofre é conferido contra o dinheiro
 * físico que alguém contou às 16h em São Paulo, e um tesoureiro consultando de
 * outro fuso precisa ver o mesmo 16h — senão os dois conferem a mesma contagem
 * lendo horas diferentes. É a mesma regra do resto do app.
 *
 * O minuto entra porque movimento de cofre acontece várias vezes no mesmo dia,
 * e só a data não distingue um do outro.
 */
function quando(iso: string): string {
  return `${formatDate(iso)} às ${formatTime(iso)}`;
}

export default function Cofre() {
  const theme = useTheme();
  const { width } = useWindowDimensions();

  const [cofre, setCofre] = useState<Vault | null>(null);
  const [carregando, setCarregando] = useState(true);
  const [erro, setErro] = useState<Failure | null>(null);
  const [contas, setContas] = useState<readonly FinancialAccount[]>([]);

  const emDuasColunas = width >= LARGURA_PARA_DUAS_COLUNAS;

  const carregar = useCallback(() => {
    setCarregando(true);
    setErro(null);

    getVault(apiClient)
      .then((resposta) => {
        setCofre(resposta);
        setCarregando(false);
      })
      .catch((causa: unknown) => {
        setErro(describeFailure(causa));
        setCarregando(false);
      });
  }, []);

  useEffect(carregar, [carregar]);

  // As contas são acessórias: se a consulta falhar, o campo some e o cofre
  // continua utilizável. Barrar a guarda do dinheiro porque uma lista opcional
  // não carregou seria desproporcional.
  useEffect(() => {
    const controlador = new AbortController();

    listFinancialAccounts(apiClient, false, controlador.signal)
      .then(setContas)
      .catch(() => {});

    return () => controlador.abort();
  }, []);

  return (
    <Screen wide>
      <ScrollView
        contentContainerStyle={{
          paddingBottom: theme.space[48],
          gap: theme.space[16],
          maxWidth: 1080,
          width: '100%',
          alignSelf: 'center',
        }}
      >
        <View style={{ gap: theme.space[4] }}>
          <Text variant="eyebrow" tone="muted">
            FINANCEIRO
          </Text>
          <Text variant="heading">Cofre da igreja</Text>
          {/* **A frase mais importante da tela.** Sem ela, alguém confere o
              fechamento do mês, não encontra os R$ 5.000 guardados e conclui
              que sumiram — quando o que aconteceu é que eles nunca foram
              despesa. */}
          <Text variant="captionBody" tone="muted">
            Guardar e retirar do cofre move dinheiro de lugar, não muda o resultado do mês. Nada
            aqui entra no fechamento como entrada ou saída.
          </Text>
        </View>

        <AsyncContent
          loading={carregando}
          skeleton={
            <View style={{ gap: theme.space[8] }}>
              <SkeletonListRow />
              <SkeletonListRow />
            </View>
          }
          failure={erro}
          errorTitle="Não deu para carregar o cofre"
          onRetry={carregar}
        >
          {cofre !== null && (
            <View style={{ gap: theme.space[16] }}>
              <SaldoDoCofre cofre={cofre} />

              <View
                style={{
                  flexDirection: emDuasColunas ? 'row' : 'column',
                  gap: theme.space[16],
                  alignItems: 'flex-start',
                }}
              >
                <View style={{ flex: emDuasColunas ? 1 : undefined, width: '100%', minWidth: 0 }}>
                  <FormularioDoCofre
                    saldoCents={cofre.balanceCents}
                    contas={contas}
                    aoMovimentar={carregar}
                  />
                </View>

                <View style={{ flex: emDuasColunas ? 1.2 : undefined, width: '100%', minWidth: 0 }}>
                  <ExtratoDoCofre cofre={cofre} />
                </View>
              </View>
            </View>
          )}
        </AsyncContent>

        <Button label="Voltar ao caixa" variant="ghost" onPress={() => router.push('/financeiro')} />
      </ScrollView>
    </Screen>
  );
}

function SaldoDoCofre({ cofre }: { readonly cofre: Vault }) {
  const theme = useTheme();

  return (
    <Card style={{ gap: theme.space[8] }}>
      <View style={{ flexDirection: 'row', alignItems: 'center', gap: theme.space[12], minWidth: 0 }}>
        <View
          style={{
            width: 44,
            height: 44,
            borderRadius: theme.radius.smallCards,
            alignItems: 'center',
            justifyContent: 'center',
            backgroundColor: theme.colors.surfaceAccentSoft,
          }}
        >
          <Feather name="lock" size={20} color={theme.colors.textOnAccentSoft} />
        </View>

        <View style={{ flex: 1, minWidth: 0 }}>
          <Text variant="eyebrow" tone="muted">
            GUARDADO NO COFRE
          </Text>
          <Text variant="headingLg">{formatBRL(cents(cofre.balanceCents))}</Text>
        </View>
      </View>

      <Text variant="captionBody" tone="muted">
        {cofre.movementCount === 0
          ? 'Nenhum movimento registrado ainda.'
          : `${cofre.movementCount} ${cofre.movementCount === 1 ? 'movimento' : 'movimentos'} registrados.`}
      </Text>
    </Card>
  );
}

function FormularioDoCofre({
  saldoCents,
  contas,
  aoMovimentar,
}: {
  readonly saldoCents: number;
  readonly contas: readonly FinancialAccount[];
  readonly aoMovimentar: () => void;
}) {
  const theme = useTheme();
  const [direcao, setDirecao] = useState<VaultDirection>('Deposito');
  const [contaId, setContaId] = useState<string | null>(null);
  const [salvando, setSalvando] = useState(false);
  const [erro, setErro] = useState<string | null>(null);
  const [erroDoValor, setErroDoValor] = useState<string | null>(null);

  const valor = useRef('');
  const observacao = useRef('');

  const ehRetirada = direcao === 'Retirada';

  async function salvar() {
    const valorCents = parseBRL(valor.current);

    if (valorCents === null || valorCents <= 0) {
      setErroDoValor('Informe um valor maior que zero.');
      return;
    }

    // A recusa por saldo também existe no servidor, e é lá que ela vale — sob
    // duas abas abertas, só o índice único do banco impede duas retiradas
    // aprovadas contra o mesmo saldo. Verificar aqui apenas poupa a ida à rede
    // no caso óbvio, e diz o número na hora.
    if (ehRetirada && valorCents > saldoCents) {
      setErroDoValor(
        `O cofre tem ${formatBRL(cents(saldoCents))} e a retirada é de ${formatBRL(cents(valorCents))}.`,
      );
      return;
    }

    setErroDoValor(null);
    setErro(null);
    setSalvando(true);

    try {
      await createVaultMovement(apiClient, {
        direction: direcao,
        amountCents: valorCents,
        ...(contaId !== null ? { accountId: contaId } : {}),
        ...(observacao.current.trim() ? { notes: observacao.current.trim() } : {}),
      });

      aoMovimentar();
      valor.current = '';
      observacao.current = '';
    } catch (causa) {
      setErro(describeError(causa));
    } finally {
      setSalvando(false);
    }
  }

  return (
    <Card style={{ gap: theme.space[12] }}>
      <Text variant="subheading">Movimentar o cofre</Text>

      <View style={{ gap: theme.space[8] }}>
        <Text variant="eyebrow" tone="muted">
          O QUE VOCÊ VAI FAZER
        </Text>
        <View
          style={{ flexDirection: 'row', flexWrap: 'wrap', gap: theme.space[8] }}
          accessibilityRole="radiogroup"
        >
          {/* Os rótulos dizem o movimento inteiro — "Guardar no cofre" em vez de
              "Depósito" — porque "depósito" e "retirada" sozinhos não dizem de
              onde para onde, e quem confere dinheiro precisa disso. */}
          <Chip
            label="Guardar no cofre"
            selected={!ehRetirada}
            onPress={() => {
              setDirecao('Deposito');
              setErroDoValor(null);
            }}
          />
          <Chip
            label="Retirar do cofre"
            selected={ehRetirada}
            onPress={() => {
              setDirecao('Retirada');
              setErroDoValor(null);
            }}
          />
        </View>
      </View>

      <TextField
        label="Valor"
        placeholder="0,00"
        defaultValue=""
        onValueChange={(v) => {
          valor.current = v;
          if (erroDoValor !== null) setErroDoValor(null);
        }}
        {...(erroDoValor !== null ? { error: erroDoValor } : {})}
        keyboardType="decimal-pad"
        inputStyle={{ textAlign: 'right' }}
        hint={
          ehRetirada
            ? `Disponível no cofre: ${formatBRL(cents(saldoCents))}`
            : 'Em reais — ex.: 1.250,00'
        }
      />

      {contas.length > 0 && (
        <View style={{ gap: theme.space[8] }}>
          <Text variant="eyebrow" tone="muted">
            {ehRetirada ? 'PARA QUAL CAIXA' : 'DE QUAL CAIXA'}
          </Text>
          <View style={{ flexDirection: 'row', flexWrap: 'wrap', gap: theme.space[8] }}>
            <Chip
              label="Não informar"
              selected={contaId === null}
              onPress={() => setContaId(null)}
            />
            {contas.map((conta) => (
              <Chip
                key={conta.id}
                label={conta.name}
                selected={contaId === conta.id}
                onPress={() => setContaId(conta.id)}
              />
            ))}
          </View>
        </View>
      )}

      <TextField
        label="Observação"
        placeholder="Ex.: oferta do culto de domingo"
        defaultValue=""
        onValueChange={(v) => {
          observacao.current = v;
        }}
        hint="Opcional."
      />

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
        label={salvando ? 'Registrando' : ehRetirada ? 'Retirar do cofre' : 'Guardar no cofre'}
        onPress={() => void salvar()}
        loading={salvando}
      />
    </Card>
  );
}

function ExtratoDoCofre({ cofre }: { readonly cofre: Vault }) {
  const theme = useTheme();

  return (
    <Card style={{ gap: theme.space[12] }}>
      <View style={{ gap: theme.space[4] }}>
        <Text variant="eyebrow" tone="muted">
          EXTRATO
        </Text>
        <Text variant="subheading">Movimentos do cofre</Text>
      </View>

      {cofre.movements.length === 0 ? (
        <EmptyState
          title="O cofre ainda não foi usado"
          description="Quando você guardar o primeiro valor, ele aparece aqui com o saldo resultante."
        />
      ) : (
        <View style={{ gap: theme.space[8] }}>
          {cofre.movements.map((movimento) => {
            const ehRetirada = movimento.direction === 'Retirada';

            return (
              <View
                key={movimento.id}
                style={{
                  flexDirection: 'row',
                  alignItems: 'center',
                  gap: theme.space[12],
                  paddingVertical: theme.space[12],
                  paddingHorizontal: theme.space[16],
                  borderRadius: theme.radius.smallCards,
                  borderWidth: 1,
                  borderColor: theme.colors.hairline,
                  backgroundColor: theme.colors.surfaceInner,
                  minWidth: 0,
                }}
              >
                <View
                  style={{
                    width: 34,
                    height: 34,
                    borderRadius: theme.radius.inputs,
                    alignItems: 'center',
                    justifyContent: 'center',
                    backgroundColor: ehRetirada
                      ? theme.colors.surface
                      : theme.colors.surfaceAccentSoft,
                  }}
                >
                  <Feather
                    name={ehRetirada ? 'arrow-up-right' : 'arrow-down-left'}
                    size={16}
                    color={ehRetirada ? theme.colors.danger : theme.colors.textOnAccentSoft}
                  />
                </View>

                <View style={{ flex: 1, minWidth: 0 }}>
                  <View
                    style={{ flexDirection: 'row', alignItems: 'center', gap: theme.space[8], minWidth: 0 }}
                  >
                    <Text variant="bodyStrong" numberOfLines={1} style={{ flexShrink: 1 }}>
                      {ehRetirada ? 'Retirada do cofre' : 'Guardado no cofre'}
                    </Text>
                    <EyebrowPill label={`#${movimento.sequenceNumber}`} tone="badge" />
                  </View>

                  <Text variant="captionBody" tone="muted" numberOfLines={1}>
                    {[
                      quando(movimento.occurredAt),
                      movimento.performedByName,
                      movimento.accountName,
                      movimento.notes,
                    ]
                      .filter((parte): parte is string => typeof parte === 'string' && parte !== '')
                      .join(' · ')}
                  </Text>
                </View>

                <View style={{ alignItems: 'flex-end' }}>
                  <Text
                    variant="bodyStrong"
                    style={{ color: ehRetirada ? theme.colors.danger : theme.colors.textOnAccentSoft }}
                  >
                    {ehRetirada ? '− ' : '+ '}
                    {formatBRL(cents(movimento.amountCents))}
                  </Text>
                  {/* O saldo resultante em cada linha é o que torna o extrato
                      conferível de cima a baixo, como o de um banco. */}
                  <Text variant="captionBody" tone="muted">
                    saldo {formatBRL(cents(movimento.balanceAfterCents))}
                  </Text>
                </View>
              </View>
            );
          })}

          {cofre.hasNext && (
            <Text variant="captionBody" tone="muted">
              Mostrando os {cofre.movements.length} movimentos mais recentes de {cofre.totalCount}.
            </Text>
          )}
        </View>
      )}
    </Card>
  );
}
