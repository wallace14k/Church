import React from 'react';
import { StyleSheet, View, type ViewStyle } from 'react-native';
import { SafeAreaView, type Edge } from 'react-native-safe-area-context';

import { colors } from '../theme';

interface Props {
  children: React.ReactNode;
  /** Bordas em que o recorte seguro é aplicado. */
  edges?: Edge[];
  style?: ViewStyle;
}

/** Fundo e área segura padrão de todas as telas. */
export function Screen({ children, edges = ['top'], style }: Props) {
  return (
    <View style={styles.root}>
      <SafeAreaView style={[styles.safe, style]} edges={edges}>
        {children}
      </SafeAreaView>
    </View>
  );
}

const styles = StyleSheet.create({
  root: { flex: 1, backgroundColor: colors.bg },
  safe: { flex: 1 },
});
