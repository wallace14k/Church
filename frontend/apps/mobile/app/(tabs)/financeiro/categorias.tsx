import { describeError } from '@congrega/api-client/errors';
import {
  createGivingCategory,
  updateGivingCategory,
  type GivingCategory,
  type GivingKind,
} from '@congrega/api-client/giving';
import { Button } from '@congrega/ui/Button';
import { Card } from '@congrega/ui/Card';
import { Chip } from '@congrega/ui/Chip';
import { EmptyState } from '@congrega/ui/EmptyState';
import { EyebrowPill } from '@congrega/ui/EyebrowPill';
import { Screen } from '@congrega/ui/Screen';
import { SignatureButton } from '@congrega/ui/SignatureButton';
import { Text } from '@congrega/ui/Text';
import { TextField } from '@congrega/ui/TextField';
import { useTheme } from '@congrega/ui/theme';
import { Feather } from '@expo/vector-icons';
import { router } from 'expo-router';
import { useRef, useState } from 'react';
import { ActivityIndicator, Pressable, ScrollView, View } from 'react-native';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import { apiClient } from '../../../src/api';
import { useGivingCategories } from '../../../src/useGiving';

export default function Categorias() {
  const theme = useTheme();
  const insets = useSafeAreaInsets();

  // Inclui inativas: esta é a tela onde se reativa uma categoria, e uma
  // categoria que some ao ser desativada não teria como voltar.
  const { categorias, carregando, erro, recarregar } = useGivingCategories(true);

  const nome = useRef('');
  const [tipo, setTipo] = useState<GivingKind>('Entrada');
  const [erroForm, setErroForm] = useState<string | null>(null);
  const [salvando, setSalvando] = useState(false);
  const [alterandoId, setAlterandoId] = useState<string | null>(null);

  async function criar() {
    if (nome.current.trim().length < 2) {
      setErroForm('Informe um nome para a categoria.');
      return;
    }

    setErroForm(null);
    setSalvando(true);

    try {
      await createGivingCategory(apiClient, nome.current.trim(), tipo);
      nome.current = '';
      recarregar();
    } catch (causa) {
      setErroForm(describeError(causa));
    } finally {
      setSalvando(false);
    }
  }

  async function alternarAtiva(categoria: GivingCategory) {
    setAlterandoId(categoria.id);
    try {
      await updateGivingCategory(apiClient, categoria.id, categoria.name, !categoria.isActive);
      recarregar();
    } catch (causa) {
      setErroForm(describeError(causa));
    } finally {
      setAlterandoId(null);
    }
  }

  const entradas = categorias.filter((c) => c.kind === 'Entrada');
  const saidas = categorias.filter((c) => c.kind === 'Saida');

  const voltar = (
    <Pressable
      onPress={() => router.back()}
      accessibilityRole="button"
      accessibilityLabel="Voltar"
      hitSlop={8}
      style={({ pressed }) => ({
        width: theme.touch.minTarget,
        height: theme.touch.minTarget,
        alignItems: 'flex-start',
        justifyContent: 'center',
        opacity: pressed ? 0.6 : 1,
      })}
    >
      <Feather name="chevron-left" size={26} color={theme.colors.text} />
    </Pressable>
  );

  return (
    <Screen padded={false} wide>
      <ScrollView
        contentContainerStyle={{
          paddingTop: insets.top + theme.space[8],
          paddingHorizontal: theme.space[24],
          paddingBottom: insets.bottom + theme.space[48],
          gap: theme.space[20],
          maxWidth: 640,
          width: '100%',
          alignSelf: 'center',
        }}
        keyboardShouldPersistTaps="handled"
      >
        {voltar}

        <View style={{ gap: theme.space[4] }}>
          <Text variant="eyebrow" tone="muted">
            CAIXA
          </Text>
          <Text variant="heading">Categorias</Text>
          <Text variant="body" tone="muted">
            É a categoria que diz se o dinheiro entrou ou saiu. O tipo não muda depois de criada —
            trocá-lo inverteria o sinal de todo lançamento já feito nela.
          </Text>
        </View>

        <Card>
          <View style={{ gap: theme.space[12] }}>
            <TextField
              label="Nome da categoria"
              placeholder="Dízimo, Oferta, Aluguel…"
              defaultValue=""
              onValueChange={(v) => {
                nome.current = v;
                if (erroForm) setErroForm(null);
              }}
              autoCapitalize="words"
            />

            <View style={{ flexDirection: 'row', gap: theme.space[8] }}>
              <Chip label="Entrada" selected={tipo === 'Entrada'} onPress={() => setTipo('Entrada')} />
              <Chip label="Saída" selected={tipo === 'Saida'} onPress={() => setTipo('Saida')} />
            </View>

            {erroForm !== null && (
              <Text variant="captionBody" style={{ color: theme.colors.danger }}>
                {erroForm}
              </Text>
            )}

            <SignatureButton
              label={salvando ? 'Salvando' : 'Adicionar categoria'}
              onPress={() => void criar()}
              loading={salvando}
            />
          </View>
        </Card>

        {carregando ? (
          <View style={{ paddingVertical: theme.space[32], alignItems: 'center' }}>
            <ActivityIndicator color={theme.colors.text} />
          </View>
        ) : erro !== null ? (
          <EmptyState
            title="Não deu para carregar as categorias"
            description={erro}
            action={<SignatureButton label="Tentar de novo" onPress={recarregar} />}
          />
        ) : categorias.length === 0 ? (
          <EmptyState
            title="Nenhuma categoria ainda"
            description="Comece por Dízimo e Oferta — depois acrescente as despesas que a igreja tem todo mês."
          />
        ) : (
          <>
            {entradas.length > 0 && (
              <Grupo
                titulo="ENTRADAS"
                categorias={entradas}
                alterandoId={alterandoId}
                onAlternar={(c) => void alternarAtiva(c)}
              />
            )}
            {saidas.length > 0 && (
              <Grupo
                titulo="SAÍDAS"
                categorias={saidas}
                alterandoId={alterandoId}
                onAlternar={(c) => void alternarAtiva(c)}
              />
            )}
          </>
        )}
      </ScrollView>
    </Screen>
  );
}

function Grupo({
  titulo,
  categorias,
  alterandoId,
  onAlternar,
}: {
  readonly titulo: string;
  readonly categorias: readonly GivingCategory[];
  readonly alterandoId: string | null;
  readonly onAlternar: (categoria: GivingCategory) => void;
}) {
  const theme = useTheme();

  return (
    <View style={{ gap: theme.space[8] }}>
      <Text variant="eyebrow" tone="muted">
        {titulo}
      </Text>
      {categorias.map((categoria) => (
        <Card key={categoria.id}>
          <View style={{ flexDirection: 'row', alignItems: 'center', gap: theme.space[12] }}>
            <View style={{ flex: 1, flexDirection: 'row', alignItems: 'center', gap: theme.space[8] }}>
              <Text
                variant="body"
                style={{ flexShrink: 1, opacity: categoria.isActive ? 1 : 0.5 }}
                numberOfLines={1}
              >
                {categoria.name}
              </Text>
              {!categoria.isActive && <EyebrowPill label="Inativa" tone="badge" />}
            </View>
            <Button
              label={categoria.isActive ? 'Desativar' : 'Reativar'}
              onPress={() => onAlternar(categoria)}
              loading={alterandoId === categoria.id}
            />
          </View>
        </Card>
      ))}
    </View>
  );
}
