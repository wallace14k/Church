import { describeError } from '@congrega/api-client/errors';
import { listMembers, type Member } from '@congrega/api-client/members';
import { Chip } from '@congrega/ui/Chip';
import { Text } from '@congrega/ui/Text';
import { TextField } from '@congrega/ui/TextField';
import { useTheme } from '@congrega/ui/theme';
import { Feather } from '@expo/vector-icons';
import { useEffect, useRef, useState } from 'react';
import { ActivityIndicator, Pressable, View } from 'react-native';
import { apiClient } from './api';

/** Mínimo de letras antes de ir ao servidor. Uma letra traria meia igreja. */
const MINIMO_PARA_BUSCAR = 2;

/** Teto do que cabe na tela sem virar rolagem dentro de formulário. */
const MAXIMO_DE_SUGESTOES = 5;

const DEBOUNCE_MS = 350;

export interface MembroSelecionado {
  readonly id: string;
  readonly nome: string;
}

export interface SeletorDeMembroProps {
  readonly selecionado: MembroSelecionado | null;
  readonly onSelecionar: (membro: MembroSelecionado | null) => void;
  readonly label: string;
  readonly hint?: string;
}

/**
 * Busca e escolhe um membro, ou nenhum.
 *
 * **Nenhum é um resultado válido e comum**, não um estado incompleto: oferta de
 * gazofilácio não tem doador identificado. Por isso o componente nasce vazio,
 * não busca nada até o usuário digitar, e deixa limpar a escolha com um toque.
 *
 * Só consulta o servidor a partir de {@link MINIMO_PARA_BUSCAR} letras. Sem
 * isso, abrir a tela de lançamento dispararia uma busca de membros no caminho
 * mais comum do formulário — justamente aquele em que ninguém será vinculado.
 */
export function SeletorDeMembro({ selecionado, onSelecionar, label, hint }: SeletorDeMembroProps) {
  const theme = useTheme();

  const [termo, setTermo] = useState('');
  const [sugestoes, setSugestoes] = useState<readonly Member[]>([]);
  const [buscando, setBuscando] = useState(false);
  const [erro, setErro] = useState<string | null>(null);

  // Cancela a busca anterior a cada tecla: sem isso a resposta de "jo" pode
  // chegar depois da de "joão" e substituir a lista pelo resultado errado.
  const emVoo = useRef<AbortController | null>(null);

  useEffect(() => {
    const alvo = termo.trim();

    if (alvo.length < MINIMO_PARA_BUSCAR) {
      emVoo.current?.abort();
      setSugestoes([]);
      setBuscando(false);
      setErro(null);
      return;
    }

    const id = setTimeout(() => {
      emVoo.current?.abort();
      const controlador = new AbortController();
      emVoo.current = controlador;

      setBuscando(true);
      setErro(null);

      listMembers(apiClient, { search: alvo, pageSize: MAXIMO_DE_SUGESTOES }, controlador.signal)
        .then((resultado) => {
          if (controlador.signal.aborted) return;
          setSugestoes(resultado.items);
        })
        .catch((causa: unknown) => {
          // Cancelamento não é falha: é a busca anterior sendo descartada de
          // propósito. Mostrá-lo faria a tela piscar erro a cada tecla.
          if (controlador.signal.aborted) return;
          setErro(describeError(causa));
        })
        .finally(() => {
          if (!controlador.signal.aborted) setBuscando(false);
        });
    }, DEBOUNCE_MS);

    return () => clearTimeout(id);
  }, [termo]);

  useEffect(() => () => emVoo.current?.abort(), []);

  if (selecionado !== null) {
    return (
      <View style={{ gap: theme.space[8] }}>
        <Text variant="eyebrow" tone="muted">
          {label.toUpperCase()}
        </Text>
        {/* O membro escolhido é um chip SELECIONADO — mesmo componente e mesmo
            preenchimento das pílulas de categoria, porque é o mesmo estado.
            O "x" herda a tinta do texto sobre o lima. */}
        <Chip
          label={selecionado.nome}
          selected
          accessibilityLabel={`Remover ${selecionado.nome} do lançamento`}
          onPress={() => {
            onSelecionar(null);
            setTermo('');
          }}
          trailing={<Feather name="x" size={14} color={theme.colors.textOnAccent} />}
        />
      </View>
    );
  }

  return (
    <View style={{ gap: theme.space[8] }}>
      <TextField
        label={label}
        placeholder="Buscar por nome, e-mail ou telefone"
        defaultValue=""
        onValueChange={setTermo}
        autoCapitalize="words"
        autoCorrect={false}
        {...(hint ? { hint } : {})}
        {...(erro ? { error: erro } : {})}
      />

      {buscando && (
        <View style={{ paddingVertical: theme.space[8] }}>
          <ActivityIndicator color={theme.colors.textMuted} />
        </View>
      )}

      {!buscando && termo.trim().length >= MINIMO_PARA_BUSCAR && sugestoes.length === 0 && erro === null && (
        <Text variant="captionBody" tone="muted">
          Ninguém encontrado com esse nome.
        </Text>
      )}

      {sugestoes.map((membro) => (
        <Pressable
          key={membro.id}
          onPress={() => onSelecionar({ id: membro.id, nome: membro.fullName })}
          accessibilityRole="button"
          accessibilityLabel={`Vincular a ${membro.fullName}`}
          style={({ pressed }) => ({
            opacity: pressed ? 0.7 : 1,
            paddingVertical: theme.space[8],
            paddingHorizontal: theme.space[12],
            borderRadius: theme.radius.inputs,
            borderWidth: 1,
            borderColor: theme.colors.hairline,
            backgroundColor: theme.colors.surface,
          })}
        >
          <Text variant="body" numberOfLines={1}>
            {membro.fullName}
          </Text>
          {membro.familyName !== null && (
            <Text variant="captionBody" tone="muted" numberOfLines={1}>
              {membro.familyName}
            </Text>
          )}
        </Pressable>
      ))}
    </View>
  );
}
