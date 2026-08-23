import { useState } from 'react';
import { Modal, Pressable, ScrollView, View, type ViewStyle } from 'react-native';
import { Text } from './Text';
import { useTheme } from './theme';

export interface DropdownOption {
  readonly value: string;
  readonly label: string;
  /** Desenhado à esquerda do rótulo, na lista e no gatilho. */
  readonly icon?: React.ReactNode;
}

export interface DropdownProps {
  readonly label: string;
  readonly value: string | null;
  readonly options: readonly DropdownOption[];
  readonly onChange: (value: string | null) => void;

  /** Texto do gatilho quando nada está escolhido. */
  readonly placeholder?: string;

  /**
   * Permite voltar a "nenhum".
   *
   * Quando ligado, a lista ganha uma primeira opção que limpa a escolha. Sem
   * ela, quem classificou um evento por engano não teria como desfazer — só
   * trocar por outro rótulo igualmente errado.
   */
  readonly clearable?: boolean;

  /** Rótulo da opção que limpa. Só aparece com `clearable`. */
  readonly clearLabel?: string;

  /** Mensagem sob o campo quando não há nenhuma opção. */
  readonly emptyMessage?: string;

  readonly error?: string;
  readonly disabled?: boolean;
  readonly style?: ViewStyle;
}

/**
 * Lista suspensa de escolha única.
 *
 * **Existe porque o vocabulário deixou de ser fixo.** Enquanto os tipos de
 * evento eram cinco valores conhecidos, chips lado a lado mostravam todos de
 * uma vez — que é o controle certo para uma lista curta e imutável. Uma igreja
 * que cadastra os próprios tipos pode ter vinte; vinte chips empurrariam o
 * resto do formulário para fora da tela.
 *
 * **`Modal`, e não um painel absoluto.** Em React Native não existe `z-index`
 * confiável entre irmãos de árvores diferentes: um painel posicionado ficaria
 * atrás do campo seguinte, ou seria recortado pelo `ScrollView` do formulário.
 * O `Modal` sai da hierarquia de recorte, e é o mesmo motivo pelo qual o menu
 * de igrejas da sidebar usa elevação em vez de fluxo normal.
 *
 * **Acessibilidade:** o gatilho é `button` com `accessibilityValue` — o leitor
 * de tela anuncia o rótulo e o que está escolhido. Cada opção é `radio` com
 * `checked`, o que informa que escolher uma desmarca a outra; `menuitem` não
 * diria isso.
 */
export function Dropdown({
  label,
  value,
  options,
  onChange,
  placeholder = 'Selecione',
  clearable = false,
  clearLabel = 'Nenhum',
  emptyMessage,
  error,
  disabled = false,
  style,
}: DropdownProps) {
  const theme = useTheme();
  const [aberto, setAberto] = useState(false);

  const escolhida = options.find((o) => o.value === value) ?? null;
  const vazio = options.length === 0;
  const bloqueado = disabled || vazio;

  return (
    <View style={[{ gap: theme.space[8] }, style]}>
      <Text variant="caption" tone="muted">
        {label.toUpperCase()}
      </Text>

      <Pressable
        onPress={() => setAberto(true)}
        disabled={bloqueado}
        accessibilityRole="button"
        accessibilityLabel={label}
        accessibilityValue={{ text: escolhida?.label ?? placeholder }}
        accessibilityState={{ disabled: bloqueado, expanded: aberto }}
        style={({ pressed }) => ({
          flexDirection: 'row',
          alignItems: 'center',
          gap: theme.space[8],
          minHeight: theme.touch.comfortable,
          paddingHorizontal: theme.space[16],
          borderRadius: theme.radius.inputs,
          borderWidth: 1,
          // Borda de erro em `danger`; o texto abaixo repete o motivo, porque
          // cor sozinha não comunica a quem não a distingue.
          borderColor:
            error !== undefined ? theme.colors.danger
            : aberto ? theme.colors.surfaceAccent
            : theme.colors.hairline,
          backgroundColor: pressed ? theme.colors.surface : theme.colors.surfaceInner,
          opacity: bloqueado ? 0.6 : 1,
        })}
      >
        {escolhida?.icon}

        <Text
          variant="body"
          tone={escolhida === null ? 'muted' : 'ink'}
          numberOfLines={1}
          style={{ flex: 1, minWidth: 0 }}
        >
          {escolhida?.label ?? placeholder}
        </Text>

        <Text variant="captionBody" tone="muted">
          ⌄
        </Text>
      </Pressable>

      {vazio && emptyMessage !== undefined && (
        <Text variant="captionBody" tone="muted">
          {emptyMessage}
        </Text>
      )}

      {error !== undefined && (
        <Text variant="captionBody" style={{ color: theme.colors.danger }}>
          {error}
        </Text>
      )}

      <Modal
        visible={aberto}
        transparent
        animationType="fade"
        onRequestClose={() => setAberto(false)}
      >
        {/* Toque fora fecha. É o gesto esperado, e sem ele o Android depende só
            do botão voltar — que o `onRequestClose` cobre, mas o iOS não tem. */}
        <Pressable
          onPress={() => setAberto(false)}
          accessibilityLabel="Fechar lista"
          style={{
            flex: 1,
            justifyContent: 'center',
            alignItems: 'center',
            padding: theme.space[24],
            backgroundColor: 'rgba(20, 20, 15, 0.35)',
          }}
        >
          {/* Pressable interno que não faz nada: intercepta o toque para que
              escolher uma opção não feche pelo backdrop antes do `onChange`. */}
          <Pressable
            onPress={() => {}}
            style={{
              width: '100%',
              maxWidth: 420,
              maxHeight: '70%',
              borderRadius: theme.radius.cards,
              backgroundColor: theme.colors.surface,
              borderWidth: 1,
              borderColor: theme.colors.hairline,
              overflow: 'hidden',
              ...theme.elevation.popover,
            }}
          >
            <View
              style={{
                paddingHorizontal: theme.space[24],
                paddingTop: theme.space[20],
                paddingBottom: theme.space[12],
              }}
            >
              <Text variant="bodyStrong">{label}</Text>
            </View>

            <ScrollView accessibilityRole="radiogroup">
              {clearable && (
                <LinhaDeOpcao
                  rotulo={clearLabel}
                  selecionada={value === null}
                  onPress={() => {
                    onChange(null);
                    setAberto(false);
                  }}
                />
              )}

              {options.map((opcao) => (
                <LinhaDeOpcao
                  key={opcao.value}
                  rotulo={opcao.label}
                  icone={opcao.icon}
                  selecionada={opcao.value === value}
                  onPress={() => {
                    onChange(opcao.value);
                    setAberto(false);
                  }}
                />
              ))}
            </ScrollView>
          </Pressable>
        </Pressable>
      </Modal>
    </View>
  );
}

function LinhaDeOpcao({
  rotulo,
  icone,
  selecionada,
  onPress,
}: {
  readonly rotulo: string;
  readonly icone?: React.ReactNode;
  readonly selecionada: boolean;
  readonly onPress: () => void;
}) {
  const theme = useTheme();

  return (
    <Pressable
      onPress={onPress}
      accessibilityRole="radio"
      accessibilityState={{ checked: selecionada }}
      accessibilityLabel={rotulo}
      style={({ pressed }) => ({
        flexDirection: 'row',
        alignItems: 'center',
        gap: theme.space[12],
        minHeight: theme.touch.comfortable,
        paddingHorizontal: theme.space[24],
        paddingVertical: theme.space[12],
        backgroundColor:
          selecionada ? theme.colors.surfaceAccentSoft
          : pressed ? theme.colors.surfaceInner
          : 'transparent',
      })}
    >
      {icone}

      <Text variant="body" numberOfLines={1} style={{ flex: 1, minWidth: 0 }}>
        {rotulo}
      </Text>

      {/* Marca de seleção além do fundo: a lavagem sozinha é diferença de tom
          sutil demais para carregar o estado por si só (WCAG 1.4.1). */}
      {selecionada && (
        <Text variant="bodyStrong" style={{ color: theme.colors.surfaceAccent }}>
          ✓
        </Text>
      )}
    </Pressable>
  );
}
