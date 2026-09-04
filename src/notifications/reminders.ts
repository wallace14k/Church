import { Platform } from 'react-native';
import * as Notifications from 'expo-notifications';

/**
 * Lembrete diário de estudo.
 *
 * É a peça da mecânica de ofensiva que age fora do app: um aviso local, no
 * horário escolhido, para o usuário não perder o dia. Tudo é agendado no
 * aparelho — não há servidor de push envolvido.
 */

const REMINDER_IDENTIFIER = 'linguacards-daily-reminder';
const ANDROID_CHANNEL = 'estudo';

Notifications.setNotificationHandler({
  handleNotification: async () => ({
    shouldShowBanner: true,
    shouldShowList: true,
    shouldPlaySound: false,
    shouldSetBadge: false,
  }),
});

/** Converte 'HH:mm' em horas e minutos, com 20:00 como reserva. */
export function parseTime(time: string): { hour: number; minute: number } {
  const [rawHour, rawMinute] = time.split(':');
  const hour = Number.parseInt(rawHour, 10);
  const minute = Number.parseInt(rawMinute, 10);
  if (!Number.isFinite(hour) || !Number.isFinite(minute)) return { hour: 20, minute: 0 };
  return { hour: Math.min(23, Math.max(0, hour)), minute: Math.min(59, Math.max(0, minute)) };
}

async function ensureAndroidChannel(): Promise<void> {
  if (Platform.OS !== 'android') return;
  await Notifications.setNotificationChannelAsync(ANDROID_CHANNEL, {
    name: 'Lembretes de estudo',
    importance: Notifications.AndroidImportance.DEFAULT,
    vibrationPattern: [0, 200],
  });
}

/**
 * Agenda (ou reagenda) o lembrete diário. Devolve `false` se o usuário não
 * autorizou notificações, para a tela poder desligar o botão de volta.
 */
export async function scheduleDailyReminder(time: string, streakDays: number): Promise<boolean> {
  try {
    const permission = await Notifications.getPermissionsAsync();
    const granted = permission.granted || (await Notifications.requestPermissionsAsync()).granted;
    if (!granted) return false;

    await ensureAndroidChannel();
    await cancelDailyReminder();

    const { hour, minute } = parseTime(time);
    await Notifications.scheduleNotificationAsync({
      identifier: REMINDER_IDENTIFIER,
      content: {
        title: streakDays > 0 ? `Ofensiva de ${streakDays} dias` : 'Hora de treinar',
        body:
          streakDays > 0
            ? 'Faça o treino de hoje para não perder a sequência.'
            : 'Alguns cards estão esperando por você.',
      },
      trigger: {
        type: Notifications.SchedulableTriggerInputTypes.DAILY,
        hour,
        minute,
        channelId: ANDROID_CHANNEL,
      },
    });
    return true;
  } catch {
    // Notificações têm suporte limitado no Expo Go; o app segue funcionando
    // normalmente sem o lembrete.
    return false;
  }
}

export async function cancelDailyReminder(): Promise<void> {
  try {
    await Notifications.cancelScheduledNotificationAsync(REMINDER_IDENTIFIER);
  } catch {
    // Nada agendado: não há o que cancelar.
  }
}
