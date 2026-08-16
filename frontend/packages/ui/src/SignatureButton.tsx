import { LinearGradient } from 'expo-linear-gradient';
import { ActivityIndicator, Pressable, StyleSheet, View, type ViewStyle } from 'react-native';
import { Text } from './Text';
import { useTheme } from './theme';

export interface SignatureButtonProps {
  readonly label: string;
  readonly onPress: () => void;
  readonly loading?: boolean;
  readonly disabled?: boolean;
  readonly style?: ViewStyle;
}

/** Espessura da borda de gradiente, conforme o `DESIGN.md`. */
const BORDER_WIDTH = 1.5;

/**
 * Botão de ação primária — o elemento de assinatura do sistema.
 *
 * Pílula transparente com borda de 1,5px em gradiente de latão, texto em tinta
 * principal. É **o único elemento com cor saturada da interface**.
 *
 * <b>Use no máximo um por tela.</b> O `DESIGN.md` é explícito, e a razão é
 * direta: o arco-íris só funciona como assinatura enquanto for raro. Dois deles
 * na mesma tela e nenhum é especial.
 *
 * <b>Nunca preencha com o gradiente.</b> Botão colorido preenchido está na lista
 * de proibições — a ação primária é sempre contorno, e a secundária é texto puro
 * sem fundo nem borda.
 *
 * <b>Implementação:</b> o React Native não tem borda com gradiente. A técnica é
 * um gradiente ocupando todo o botão com um retângulo branco por dentro,
 * recuado pela espessura da borda. O raio interno é o externo menos a espessura,
 * senão os cantos ficam com filete grosso e o resto fino.
 */
export function SignatureButton({
  label,
  onPress,
  loading = false,
  disabled = false,
  style,
}: SignatureButtonProps) {
  const theme = useTheme();
  const inativo = disabled || loading;

  return (
    <Pressable
      onPress={onPress}
      disabled={inativo}
      accessibilityRole="button"
      accessibilityLabel={label}
      accessibilityState={{ disabled: inativo, busy: loading }}
      style={({ pressed }) => [
        {
          borderRadius: theme.radius.buttons,
          minHeight: theme.touch.comfortable,
          // Opacidade é o único recurso de estado aqui: mudar a cor do gradiente
          // no toque quebraria a assinatura, e escurecer a borda a apagaria.
          opacity: inativo ? 0.45 : pressed ? 0.75 : 1,
        },
        style,
      ]}
    >
      <LinearGradient
        colors={[...theme.brass]}
        start={{ x: 0, y: 0.5 }}
        end={{ x: 1, y: 0.5 }}
        style={[styles.gradient, { borderRadius: theme.radius.buttons }]}
      >
        <View
          style={[
            styles.inner,
            {
              backgroundColor: theme.colors.background,
              borderRadius: theme.radius.buttons - BORDER_WIDTH,
              paddingHorizontal: theme.space[24],
            },
          ]}
        >
          {loading ? (
            <ActivityIndicator color={theme.colors.text} />
          ) : (
            <Text variant="bodyStrong">{label}</Text>
          )}
        </View>
      </LinearGradient>
    </Pressable>
  );
}

const styles = StyleSheet.create({
  gradient: {
    padding: BORDER_WIDTH,
  },
  inner: {
    flex: 1,
    alignItems: 'center',
    justifyContent: 'center',
    minHeight: 0,
    paddingVertical: 12,
  },
});
