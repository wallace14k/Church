import { Screen } from '@congrega/ui/Screen';
import { Redirect } from 'expo-router';
import { ActivityIndicator } from 'react-native';
import { useSession } from '../src/session';

/**
 * Porta de entrada.
 *
 * Enquanto a sessão hidrata, não decide nada. Redirecionar antes de saber se há
 * sessão faria o usuário logado ver a tela de login por um instante a cada
 * abertura — o "flash de login" que denuncia app mal montado.
 */
export default function Index() {
  const { status } = useSession();

  if (status === 'carregando') {
    return (
      <Screen style={{ alignItems: 'center', justifyContent: 'center' }}>
        <ActivityIndicator />
      </Screen>
    );
  }

  return <Redirect href={status === 'autenticado' ? '/inicio' : '/entrar'} />;
}
