import { forwardRef, useCallback, useRef, useState } from 'react';
import {
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
        // "123 456" no campo de OTP). Reescreve no nativo sem re-render.
        innerRef.current?.setNativeProps({ text: next });
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
      : theme.colors.divider;

  return (
    <View style={[styles.container, { gap: theme.space[4] }, containerStyle]}>
      <RNText style={[theme.type.eyebrow, { color: theme.colors.textMuted }]}>
        {label.toUpperCase()}
      </RNText>

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
        placeholderTextColor={theme.colors.textMuted}
        style={[
          theme.type.body,
          styles.input,
          {
            minHeight: theme.touch.comfortable,
            paddingHorizontal: theme.space[12],
            borderRadius: theme.radius.inputs,
            borderColor,
            backgroundColor: theme.colors.surface,
            color: theme.colors.text,
          },
          inputStyle,
        ]}
        // Erro anunciado junto do campo: sem isso, o leitor de tela lê o campo e
        // o erro como elementos independentes, e o usuário não liga um ao outro.
        accessibilityLabel={label}
        accessibilityHint={hasError ? error : hint}
      />

      {(hasError || hint !== undefined) && (
        // A mensagem de erro vai em TINTA PRINCIPAL, não no vermelho do espectro.
        // `colors.danger` mede 3.3:1 sobre o canvas — insuficiente para texto, e
        // mensagem de erro ilegível é pior que não ter mensagem. O vermelho fica
        // onde funciona: na borda do campo, que é forma e não leitura.
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
