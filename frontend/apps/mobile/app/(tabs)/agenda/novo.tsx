import { createEvent } from '@congrega/api-client/events';
import { useCarregamentoGlobal } from '@congrega/ui/GlobalLoading';
import { router } from 'expo-router';
import { apiClient } from '../../../src/api';
import { FormularioDeEvento } from '../../../src/FormularioDeEvento';

export default function NovoEvento() {
  const { executar } = useCarregamentoGlobal();

  return (
    <FormularioDeEvento
      eyebrow="AGENDA"
      titulo="Novo evento"
      onSalvar={async (entrada) => {
        // A mensagem muda com a recorrência: criar uma série grava 53 linhas e
        // demora visivelmente mais que um evento só. Dizer "salvando o evento"
        // enquanto o servidor escreve o ano inteiro faria a espera parecer
        // travamento.
        await executar(
          entrada.recurrence === 'Semanal' ? 'Criando a série…' : 'Salvando o evento…',
          entrada.recurrence === 'Semanal'
            ? 'Agendando cerca de 52 encontros semanais.'
            : 'Guardando na agenda da igreja.',
          () => createEvent(apiClient, entrada),
        );
        // `replace` e não `push`: voltar ao formulário depois de salvar
        // convidaria a agendar o mesmo culto duas vezes.
        router.replace('/agenda');
      }}
    />
  );
}
