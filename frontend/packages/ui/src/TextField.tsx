import { forwardRef, useCallback, useRef, useState } from 'react';
import {
  Platform,
  StyleSheet,
  Text as RNText,
  TextInput,
  View,
  type TextInputProps,
  type TextStyle,
  type ViewStyle,
} from 'react-native';
import { useTheme } from './theme';

export interface TextFieldProps extends Omit<TextInputProps, 'style' | 'value' | 'onChangeText'> {
  readonly label: string;
  readonly hint?: string;
  readonly error?: string;
  /** Valor inicial. Ver a nota sobre componente não controlado. */
  readonly defaultValue?: string;
  readonly onValueChange?: (value: string) => void;
  /** Transforma o texto a cada tecla — máscara de CPF, filtro de dígitos do OTP. */
  readonly transform?: (raw: string) => string;
  readonly containerStyle?: ViewStyle;
  /**
   * Ajuste fino do texto digitado — tracking do código OTP, alinhamento à
   * direita em campo de valor.
   *
   * Deliberadamente estreito: aceita apenas as propriedades de texto que fazem
   * sentido ajustar por campo. Expor `style` inteiro deixaria qualquer tela
   * sobrescrever cor, borda e altura, e o componente deixaria de garantir alvo
   * de toque e contraste.
   */
  readonly inputStyle?: Pick<TextStyle, 'letterSpacing' | 'textAlign' | 'fontFamily'>;
}

/**
 * Campo de texto **não controlado**.
 *
 * A skill `react-native-best-practices` classifica o `TextInput` controlado como
 * problema de impacto ALTO, e o motivo é concreto: manter o valor em estado do
 * React faz cada tecla disparar render da tela inteira e devolver o valor ao
 * nativo. O efeito é o cursor "pulando" e letras somindo em aparelho modesto —
 * exatamente o aparelho da maioria dos membros.
 *
 * Aqui o `TextInput` guarda o próprio valor; o React só é notificado por
 * callback. O único estado local é o de foco, que muda uma vez por interação e
 * não por caractere.
 *
 * Quando o valor precisa ser transformado (máscara de CPF, filtro de dígitos do
 * OTP), `transform` roda e o texto é reescrito no nativo via `setNativeProps` —
 * sem passar por render.
 */
export const TextField = forwardRef<TextInput, TextFieldProps>(function TextField(
  { label, hint, error, defaultValue, onValueChange, transform, containerStyle, inputStyle, ...inputProps },
  forwardedRef,
) {
  const theme = useTheme();
  const [isFocused, setIsFocused] = useState(false);
  const innerRef = useRef<TextInput | null>(null);
  const lastValue = useRef(defaultValue ?? '');

  const handleChangeText = useCallback(
    (raw: string) => {
      const next = transform ? transform(raw) : raw;

      if (next !== raw) {
        // A transformação encurtou ou reescreveu o texto (ex.: usuário colou
        // "123 456" no campo de OTP, ou a máscara de data inseriu uma barra).
        // Reescreve no nativo sem re-render.
        //
        // react-native-web não implementa `setNativeProps` no ref do
        // TextInput — chamá-lo direto derruba a tela em qualquer campo com
        // `transform` (data, telefone, código OTP) a partir da primeira tecla
        // que a máscara reescreve. O próprio react-native-web usa
        // `node.value = ...` internamente para `TextInput.clear()`; é o
        // equivalente correto para escrever no input sem passar por estado.
        if (Platform.OS === 'web') {
          const node = innerRef.current as unknown as { value?: string } | null;
          if (node) node.value = next;
        } else {
          innerRef.current?.setNativeProps({ text: next });
        }
      }

      lastValue.current = next;
      onValueChange?.(next);
    },
    [onValueChange, transform],
  );

  const hasError = error !== undefined && error.length > 0;
  const borderColor = hasError
    ? theme.colors.danger
    : isFocused
      ? theme.colors.text
      : theme.colors.hairline;

  return (
    <View style={[styles.container, { gap: theme.space[8] }, containerStyle]}>
      {/* `caption` e não `eyebrow`: o eyebrow do sistema tem 10px, tamanho de
          etiqueta de categoria. Rótulo de campo de formulário a 10px vira
          decoração — e a §9 pede rótulo visível justamente para que ele seja
          lido, não adivinhado pelo placeholder. */}
      <RNText style={[theme.type.caption, { color: theme.colors.textMuted }]}>
        {label.toUpperCase()}
      </RNText>

      {/* Anel de foco. Fica num contentor externo com padding fixo de 2px, e
          não na espessura da borda do campo: engrossar a borda no foco
          deslocaria o texto 1px a cada clique, e o salto é visível numa
          coluna de campos.

          O anel é em TINTA, não no lima. Lima sobre canvas mede 1,19:1 — como
          indicador de foco reprovaria os 3:1 da WCAG para componente não
          textual, que é exatamente o caso de uso onde isso importa mais. */}
      <View
        style={{
          padding: 2,
          borderRadius: theme.radius.inputs + 2,
          backgroundColor: isFocused ? theme.colors.text : 'transparent',
        }}
      >
        <TextInput
          {...inputProps}
          ref={(node) => {
            innerRef.current = node;
            if (typeof forwardedRef === 'function') forwardedRef(node);
            else if (forwardedRef) forwardedRef.current = node;
          }}
          defaultValue={defaultValue}
          onChangeText={handleChangeText}
          onFocus={(event) => {
            setIsFocused(true);
            inputProps.onFocus?.(event);
          }}
          onBlur={(event) => {
            setIsFocused(false);
            inputProps.onBlur?.(event);
          }}
          placeholderTextColor={theme.colors.placeholder}
          style={[
            theme.type.body,
            styles.input,
            {
              minHeight: theme.touch.comfortable,
              paddingHorizontal: theme.space[16],
              borderRadius: theme.radius.inputs,
              borderColor,
              // Branco, mesmo quando o campo está sobre um cartão pergaminho:
              // a §9 fixa `#ffffff` no campo, e é o que separa "área onde se
              // escreve" de "superfície onde se lê".
              backgroundColor: theme.colors.surfaceInner,
              color: theme.colors.text,
            },
            inputStyle,
          ]}
          // Erro anunciado junto do campo: sem isso, o leitor de tela lê o campo
          // e o erro como elementos independentes, e o usuário não liga um ao
          // outro.
          accessibilityLabel={label}
          accessibilityHint={hasError ? error : hint}
        />
      </View>

      {(hasError || hint !== undefined) && (
        // A mensagem de erro vai em TINTA PRINCIPAL, não no vermelho.
        // `colors.danger` até passa em contraste, mas a §15 mantém a cor de
        // estado como linguagem secundária: o vermelho fica na borda do campo,
        // que é forma, e a leitura fica na tinta do resto da interface.
        //
        // Isso também atende quem não distingue vermelho: a informação não
        // depende da cor, está no texto e na borda.
        <RNText
          style={[
            theme.type.captionBody,
            { color: hasError ? theme.colors.text : theme.colors.textMuted },
          ]}
        >
          {hasError ? error : hint}
        </RNText>
      )}
    </View>
  );
});

const styles = StyleSheet.create({
  container: {
    width: '100%',
  },
  input: {
    borderWidth: 1,
  },
});
