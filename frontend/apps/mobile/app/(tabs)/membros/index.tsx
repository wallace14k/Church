import type { Member } from '@congrega/api-client/members';
import { formatPhone } from '@congrega/core/validation';
import { Avatar } from '@congrega/ui/Avatar';
import { Button } from '@congrega/ui/Button';
import { Card } from '@congrega/ui/Card';
import { EmptyState } from '@congrega/ui/EmptyState';
import { EyebrowPill } from '@congrega/ui/EyebrowPill';
import { Screen } from '@congrega/ui/Screen';
import { SignatureButton } from '@congrega/ui/SignatureButton';
import { Text } from '@congrega/ui/Text';
import { TextField } from '@congrega/ui/TextField';
import { TextLink } from '@congrega/ui/TextLink';
import { useTheme } from '@congrega/ui/theme';
import { FlashList } from '@shopify/flash-list';
import { router } from 'expo-router';
import { useState } from 'react';
import { ActivityIndicator, Pressable, View } from 'react-native';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import { useMembers } from '../../../src/useMembers';

export default function ListaDeMembros() {
  const theme = useTheme();
  const insets = useSafeAreaInsets();
  const [busca, setBusca] = useState('');
  const { membros, total, carregando, carregandoMais, erro, temMais, carregarMais } = useMembers(busca);

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
        <View style={{ flexDirection: 'row', alignItems: 'flex-end', justifyContent: 'space-between' }}>
          <View style={{ gap: theme.space[4] }}>
            <Text variant="eyebrow" tone="muted">
              SUA IGREJA
            </Text>
            <Text variant="heading">Membros</Text>
          </View>

          {total > 0 && <EyebrowPill label={`${total} ${total === 1 ? 'pessoa' : 'pessoas'}`} />}
        </View>

        <TextLink label="Ver famílias" onPress={() => router.push('/membros/familias')} />

        <TextField
          label="Buscar"
          placeholder="Nome, e-mail ou telefone"
          defaultValue=""
          onValueChange={setBusca}
          autoCapitalize="none"
          autoCorrect={false}
          returnKeyType="search"
          // `clearButtonMode` só existe no iOS; no Android o usuário apaga com o
          // teclado. Não vale reimplementar um botão para igualar as plataformas
          // num campo que já é secundário na tela.
          clearButtonMode="while-editing"
        />

        {membros.length > 0 && (
          <View style={{ flexDirection: 'row', gap: theme.space[8] }}>
            <View style={{ flex: 1 }}>
              <SignatureButton label="Cadastrar membro" onPress={() => router.push('/membros/novo')} />
            </View>
            <Button label="Importar" onPress={() => router.push('/membros/importar')} />
          </View>
        )}
      </View>

      {carregando ? (
        <View style={{ flex: 1, alignItems: 'center', justifyContent: 'center' }}>
          <ActivityIndicator color={theme.colors.text} />
        </View>
      ) : erro !== null ? (
        <View style={{ paddingHorizontal: theme.space[24], paddingTop: theme.space[32] }}>
          <EmptyState
            title="Não deu para carregar a lista"
            description={erro}
            action={<SignatureButton label="Tentar de novo" onPress={() => setBusca((b) => b)} />}
          />
        </View>
      ) : membros.length === 0 ? (
        <View style={{ paddingHorizontal: theme.space[24], paddingTop: theme.space[16] }}>
          {busca.length > 0 ? (
            <EmptyState
              title="Ninguém com esse nome"
              description={`A busca por "${busca}" não encontrou membros. Confira a grafia ou cadastre a pessoa.`}
              action={
                <SignatureButton label="Cadastrar membro" onPress={() => router.push('/membros/novo')} />
              }
            />
          ) : (
            <EmptyState
              title="Sua igreja ainda não tem membros aqui"
              description="Cadastre a primeira pessoa, ou importe a lista que a igreja já tem numa planilha."
              action={
                <View style={{ gap: theme.space[8], width: '100%', maxWidth: 320 }}>
                  <SignatureButton label="Cadastrar membro" onPress={() => router.push('/membros/novo')} />
                  <Button label="Importar planilha" onPress={() => router.push('/membros/importar')} />
                </View>
              }
            />
          )}
        </View>
      ) : (
        // FlashList e não ScrollView: a skill de performance trata lista longa em
        // ScrollView como problema CRÍTICO, porque monta todos os itens de uma
        // vez. Uma igreja de porte médio tem centenas de membros.
        <FlashList
          data={membros}
          keyExtractor={(item) => item.id}
          renderItem={({ item }) => <LinhaDeMembro membro={item} />}
          contentContainerStyle={{
            paddingHorizontal: theme.space[24],
            paddingTop: theme.space[16],
            paddingBottom: insets.bottom + theme.space[24],
            maxWidth: theme.layout.pageMaxWidth,
            width: '100%',
            alignSelf: 'center',
          }}
          ItemSeparatorComponent={() => <View style={{ height: theme.space[8] }} />}
          onEndReached={carregarMais}
          onEndReachedThreshold={0.6}
          ListFooterComponent={
            carregandoMais ? (
              <View style={{ paddingVertical: theme.space[24] }}>
                <ActivityIndicator color={theme.colors.text} />
              </View>
            ) : temMais ? null : (
              <Text
                variant="captionBody"
                tone="muted"
                style={{ textAlign: 'center', paddingVertical: theme.space[24] }}
              >
                {total === 1 ? '1 membro' : `${total} membros`}
              </Text>
            )
          }
        />
      )}
    </Screen>
  );
}

function LinhaDeMembro({ membro }: { readonly membro: Member }) {
  const theme = useTheme();

  // Uma linha de apoio, montada com o que existir. Repetir rótulos ("E-mail:",
  // "Telefone:") gastaria a largura da tela para dizer o que o formato já diz.
  const detalhes = [
    membro.age !== null ? `${membro.age} anos` : null,
    formatPhone(membro.phone) || null,
    membro.email,
    membro.familyName,
  ].filter(Boolean);

  return (
    <Pressable
      onPress={() => router.push(`/membros/${membro.id}`)}
      accessibilityRole="button"
      accessibilityLabel={`Abrir ficha de ${membro.fullName}`}
      style={({ pressed }) => ({ opacity: pressed ? 0.75 : 1 })}
    >
      <Card>
        <View style={{ flexDirection: 'row', alignItems: 'center', gap: theme.space[12] }}>
          <Avatar name={membro.fullName} />

          <View style={{ flex: 1, gap: theme.space[4] }}>
            <View style={{ flexDirection: 'row', alignItems: 'center', gap: theme.space[8] }}>
              <Text variant="bodyStrong" style={{ flexShrink: 1 }} numberOfLines={1}>
                {membro.fullName}
              </Text>
              {membro.status !== 'Ativo' && <EyebrowPill label={membro.status} tone="badge" />}
            </View>

            {detalhes.length > 0 && (
              <Text variant="captionBody" tone="muted" numberOfLines={1}>
                {detalhes.join(' · ')}
              </Text>
            )}
          </View>
        </View>
      </Card>
    </Pressable>
  );
}
