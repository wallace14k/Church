import React, { useMemo, useState } from 'react';
import { Alert, Pressable, ScrollView, StyleSheet, Switch, Text, View } from 'react-native';
import { Ionicons } from '@expo/vector-icons';
import { useNavigation } from '@react-navigation/native';
import type { NativeStackNavigationProp } from '@react-navigation/native-stack';

import { Screen } from '../components/Screen';
import { audioDiskUsage, formatBytes, pruneOrphanAudio } from '../audio/storage';
import { useAuth } from '../auth/AuthContext';
import { cancelDailyReminder, scheduleDailyReminder } from '../notifications/reminders';
import { useApp } from '../store/AppContext';
import { colors, radius, spacing, typography } from '../theme';
import type { RootStackParamList } from '../navigation/types';

type Nav = NativeStackNavigationProp<RootStackParamList>;

const GOAL_OPTIONS = [10, 20, 30, 50];
const REMINDER_TIMES = ['08:00', '12:00', '19:00', '21:00'];

/** Conta, meta diária, preferências de estudo e gerenciamento dos dados locais. */
export function ProfileScreen() {
  const navigation = useNavigation<Nav>();
  const { user, signOut } = useAuth();
  const { streak, settings, premium, entitlements, cards, setDailyGoal, updateSettings, wipeAllData } =
    useApp();

  const [diskUsage, setDiskUsage] = useState(() => audioDiskUsage());
  const [reminderBusy, setReminderBusy] = useState(false);

  const audioCount = useMemo(
    () => cards.filter((card) => card.frontAudio || card.backAudio).length,
    [cards]
  );

  const providerLabel =
    user?.provider === 'google' ? 'Google' : user?.provider === 'apple' ? 'Apple' : 'Sem conta';

  const toggleReminder = async (enabled: boolean) => {
    setReminderBusy(true);
    try {
      if (!enabled) {
        await cancelDailyReminder();
        await updateSettings({ reminderEnabled: false });
        return;
      }
      const scheduled = await scheduleDailyReminder(settings.reminderTime, streak.current);
      await updateSettings({ reminderEnabled: scheduled });
      if (!scheduled) {
        Alert.alert(
          'Notificações bloqueadas',
          'Libere as notificações do LinguaCards nos ajustes do aparelho para receber o lembrete.'
        );
      }
    } finally {
      setReminderBusy(false);
    }
  };

  const changeReminderTime = async (time: string) => {
    await updateSettings({ reminderTime: time });
    if (settings.reminderEnabled) {
      await scheduleDailyReminder(time, streak.current);
    }
  };

  const cleanUp = () => {
    const referenced = new Set<string>();
    cards.forEach((card) => {
      if (card.frontAudio) referenced.add(card.frontAudio.uri);
      if (card.backAudio) referenced.add(card.backAudio.uri);
    });
    const removed = pruneOrphanAudio(referenced);
    setDiskUsage(audioDiskUsage());
    Alert.alert(
      'Limpeza concluída',
      removed > 0
        ? `${removed} ${removed === 1 ? 'arquivo solto foi removido' : 'arquivos soltos foram removidos'}.`
        : 'Nenhum arquivo solto encontrado.'
    );
  };

  const confirmWipe = () => {
    Alert.alert(
      'Apagar todos os dados',
      'Baralhos, cards, áudios, histórico e ofensiva serão apagados deste aparelho. Não dá para desfazer.',
      [
        { text: 'Cancelar', style: 'cancel' },
        {
          text: 'Apagar tudo',
          style: 'destructive',
          onPress: async () => {
            await wipeAllData();
            setDiskUsage(audioDiskUsage());
          },
        },
      ]
    );
  };

  const confirmSignOut = () => {
    Alert.alert(
      'Sair da conta',
      'Seus baralhos continuam salvos neste aparelho e voltam quando você entrar de novo.',
      [
        { text: 'Cancelar', style: 'cancel' },
        { text: 'Sair', style: 'destructive', onPress: () => void signOut() },
      ]
    );
  };

  return (
    <Screen>
      <ScrollView contentContainerStyle={styles.content}>
        <Text style={styles.title}>Perfil</Text>

        <View style={styles.accountCard}>
          <View style={styles.avatar}>
            <Ionicons
              name={user?.provider === 'guest' ? 'person-outline' : 'person'}
              size={22}
              color={colors.primary}
            />
          </View>
          <View style={styles.accountBody}>
            <Text style={styles.accountName}>{user?.name ?? 'Estudante'}</Text>
            <Text style={styles.accountMeta}>{user?.email ?? providerLabel}</Text>
          </View>
          {premium ? (
            <View style={styles.premiumTag}>
              <Ionicons name="sparkles" size={12} color={colors.premium} />
              <Text style={styles.premiumTagText}>PREMIUM</Text>
            </View>
          ) : null}
        </View>

        {!premium ? (
          <Pressable
            onPress={() => navigation.navigate('Paywall', { source: 'perfil' })}
            style={styles.upsell}
          >
            <Ionicons name="sparkles" size={20} color={colors.premium} />
            <View style={styles.upsellBody}>
              <Text style={styles.upsellTitle}>Liberar tudo com o Premium</Text>
              <Text style={styles.upsellText}>
                Sem anúncios, baralhos ilimitados e áudio nos dois lados.
              </Text>
            </View>
            <Ionicons name="chevron-forward" size={18} color={colors.premium} />
          </Pressable>
        ) : null}

        <Section title="Meta diária">
          <Text style={styles.sectionHint}>
            Bater a meta é o que mantém a ofensiva viva. Você tem {streak.freezes}{' '}
            {streak.freezes === 1 ? 'protetor' : 'protetores'} guardado
            {streak.freezes === 1 ? '' : 's'}.
          </Text>
          <View style={styles.goalRow}>
            {GOAL_OPTIONS.map((goal) => {
              const active = streak.dailyGoal === goal;
              return (
                <Pressable
                  key={goal}
                  onPress={() => void setDailyGoal(goal)}
                  style={[styles.goalOption, active && styles.goalOptionActive]}
                  accessibilityRole="radio"
                  accessibilityState={{ selected: active }}
                >
                  <Text style={[styles.goalValue, active && styles.goalValueActive]}>{goal}</Text>
                  <Text style={styles.goalUnit}>cards</Text>
                </Pressable>
              );
            })}
          </View>
        </Section>

        <Section title="Lembrete diário">
          <Toggle
            label="Avisar na hora de estudar"
            description="Uma notificação local por dia, para a ofensiva não escapar."
            value={settings.reminderEnabled}
            onChange={(value) => void toggleReminder(value)}
            disabled={reminderBusy}
          />
          {settings.reminderEnabled ? (
            <View style={styles.goalRow}>
              {REMINDER_TIMES.map((time) => {
                const active = settings.reminderTime === time;
                return (
                  <Pressable
                    key={time}
                    onPress={() => void changeReminderTime(time)}
                    style={[styles.goalOption, active && styles.goalOptionActive]}
                    accessibilityRole="radio"
                    accessibilityState={{ selected: active }}
                  >
                    <Text style={[styles.goalValue, active && styles.goalValueActive]}>{time}</Text>
                  </Pressable>
                );
              })}
            </View>
          ) : null}
        </Section>

        <Section title="Durante o treino">
          <Toggle
            label="Tocar o áudio da frente sozinho"
            description="Ouve a pronúncia assim que o card aparece."
            value={settings.autoPlayFrontAudio}
            onChange={(value) => void updateSettings({ autoPlayFrontAudio: value })}
          />
          <Toggle
            label="Tocar o diálogo ao revelar"
            description="Reproduz o áudio do verso junto com a resposta."
            value={settings.autoPlayBackAudio}
            onChange={(value) => void updateSettings({ autoPlayBackAudio: value })}
          />
          <Toggle
            label="Mostrar quando o card volta"
            description="Exibe o intervalo previsto em cada botão de resposta."
            value={settings.showNextInterval}
            onChange={(value) => void updateSettings({ showNextInterval: value })}
          />
          <Toggle
            label="Vibração"
            value={settings.hapticsEnabled}
            onChange={(value) => void updateSettings({ hapticsEnabled: value })}
          />
        </Section>

        <Section title="Armazenamento">
          <Text style={styles.sectionHint}>
            {audioCount} {audioCount === 1 ? 'card com áudio' : 'cards com áudio'} ·{' '}
            {formatBytes(diskUsage)} em disco.
          </Text>
          <Pressable onPress={cleanUp} style={styles.rowAction}>
            <Ionicons name="sparkles-outline" size={18} color={colors.text} />
            <Text style={styles.rowActionLabel}>Limpar áudios sem card</Text>
          </Pressable>
        </Section>

        <Section title="Conta">
          {entitlements.premium && entitlements.expiresAt ? (
            <Text style={styles.sectionHint}>
              Premium até {new Date(entitlements.expiresAt).toLocaleDateString('pt-BR')}.
            </Text>
          ) : null}
          <Pressable onPress={confirmSignOut} style={styles.rowAction}>
            <Ionicons name="log-out-outline" size={18} color={colors.text} />
            <Text style={styles.rowActionLabel}>Sair da conta</Text>
          </Pressable>
          <Pressable onPress={confirmWipe} style={styles.rowAction}>
            <Ionicons name="trash-outline" size={18} color={colors.forgot} />
            <Text style={[styles.rowActionLabel, { color: colors.forgot }]}>
              Apagar todos os dados
            </Text>
          </Pressable>
        </Section>

        <Text style={styles.footer}>
          Nesta versão tudo fica salvo apenas neste aparelho. A sincronização entre dispositivos vem
          numa próxima atualização.
        </Text>
      </ScrollView>
    </Screen>
  );
}

function Section({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <View style={styles.section}>
      <Text style={styles.sectionTitle}>{title}</Text>
      {children}
    </View>
  );
}

function Toggle({
  label,
  description,
  value,
  onChange,
  disabled,
}: {
  label: string;
  description?: string;
  value: boolean;
  onChange: (value: boolean) => void;
  disabled?: boolean;
}) {
  return (
    <View style={styles.toggle}>
      <View style={styles.toggleBody}>
        <Text style={styles.toggleLabel}>{label}</Text>
        {description ? <Text style={styles.toggleDescription}>{description}</Text> : null}
      </View>
      <Switch
        value={value}
        onValueChange={onChange}
        disabled={disabled}
        trackColor={{ false: colors.surfaceAlt, true: colors.primary }}
        thumbColor="#FFFFFF"
      />
    </View>
  );
}

const styles = StyleSheet.create({
  content: { padding: spacing.lg, gap: spacing.lg, paddingBottom: spacing.xxl },
  title: { ...typography.h1 },

  accountCard: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.md,
    backgroundColor: colors.bgElevated,
    borderRadius: radius.lg,
    borderWidth: 1,
    borderColor: colors.border,
    padding: spacing.lg,
  },
  avatar: {
    width: 46,
    height: 46,
    borderRadius: 23,
    backgroundColor: colors.primarySoft,
    alignItems: 'center',
    justifyContent: 'center',
  },
  accountBody: { flex: 1, gap: 2 },
  accountName: { ...typography.h3, fontSize: 16 },
  accountMeta: { ...typography.tiny, color: colors.textFaint },
  premiumTag: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 3,
    backgroundColor: colors.premiumSoft,
    borderRadius: radius.sm,
    paddingHorizontal: spacing.sm,
    paddingVertical: 3,
  },
  premiumTagText: { fontSize: 9, fontWeight: '800', color: colors.premium, letterSpacing: 0.4 },

  upsell: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.md,
    backgroundColor: colors.premiumSoft,
    borderRadius: radius.lg,
    padding: spacing.lg,
  },
  upsellBody: { flex: 1, gap: 2 },
  upsellTitle: { ...typography.h3, fontSize: 15, color: colors.premium },
  upsellText: { ...typography.tiny, color: colors.textMuted, lineHeight: 16 },

  section: {
    backgroundColor: colors.bgElevated,
    borderRadius: radius.lg,
    borderWidth: 1,
    borderColor: colors.border,
    padding: spacing.lg,
    gap: spacing.md,
  },
  sectionTitle: { ...typography.h3, fontSize: 16 },
  sectionHint: { ...typography.tiny, color: colors.textFaint, lineHeight: 17 },

  goalRow: { flexDirection: 'row', gap: spacing.sm },
  goalOption: {
    flex: 1,
    alignItems: 'center',
    paddingVertical: spacing.md,
    borderRadius: radius.md,
    backgroundColor: colors.surface,
    borderWidth: 2,
    borderColor: 'transparent',
  },
  goalOptionActive: { borderColor: colors.streak, backgroundColor: colors.streakSoft },
  goalValue: { fontSize: 18, fontWeight: '700', color: colors.textMuted },
  goalValueActive: { color: colors.streak },
  goalUnit: { ...typography.tiny, color: colors.textFaint },

  toggle: { flexDirection: 'row', alignItems: 'center', gap: spacing.md },
  toggleBody: { flex: 1, gap: 2 },
  toggleLabel: { ...typography.body },
  toggleDescription: { ...typography.tiny, color: colors.textFaint, lineHeight: 16 },

  rowAction: { flexDirection: 'row', alignItems: 'center', gap: spacing.md, paddingVertical: spacing.sm },
  rowActionLabel: { ...typography.body },

  footer: { ...typography.tiny, color: colors.textFaint, textAlign: 'center', lineHeight: 16 },
});
