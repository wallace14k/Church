import { Image, StyleSheet, View, type ImageSourcePropType } from 'react-native';
import { Text } from './Text';
import { useTheme } from './theme';

export interface EmptyStateProps {
  readonly title: string;
  /** Uma frase. Diz o que fazer, não pede desculpa por não haver nada. */
  readonly description: string;
  readonly image?: ImageSourcePropType;
  /** Ação que resolve o vazio. Sem ela, a tela vira beco sem saída. */
  readonly action?: React.ReactNode;
}

/**
 * Estado vazio.
 *
 * "Uma tela vazia é um convite para agir" — a redação do `DESIGN.md` sobre
 * vazio e falha. Por isso o componente exige `title` e `description`, e recebe a
 * ação como filho: um vazio sem saída é um beco, não um estado.
 *
 * A imagem é opcional e decorativa. Quando existe, é fotográfica e discreta —
 * nada de ilustração ou ícone gigante, que o `DESIGN.md` veda.
 */
export function EmptyState({ title, description, image, action }: EmptyStateProps) {
  const theme = useTheme();

  return (
    <View style={[styles.container, { gap: theme.space[16], paddingVertical: theme.space[32] }]}>
      {image !== undefined && (
        <Image
          source={image}
          style={[styles.image, { borderRadius: theme.radius.images, borderColor: theme.colors.hairline }]}
          resizeMode="cover"
          // Decorativa: a informação está no título e na descrição. Narrar a foto
          // só atrasaria quem usa leitor de tela para chegar à ação.
          accessibilityElementsHidden
          importantForAccessibility="no-hide-descendants"
        />
      )}

      <View style={{ gap: theme.space[4], alignItems: 'center' }}>
        <Text variant="subheading" style={styles.centered}>
          {title}
        </Text>
        <Text variant="body" tone="muted" style={styles.centered}>
          {description}
        </Text>
      </View>

      {action}
    </View>
  );
}

const styles = StyleSheet.create({
  container: { alignItems: 'center' },
  image: { width: '100%', aspectRatio: 4 / 3, borderWidth: 1 },
  centered: { textAlign: 'center' },
});
