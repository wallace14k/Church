import { createEvent } from '@congrega/api-client/events';
import { router } from 'expo-router';
import { apiClient } from '../../../src/api';
import { FormularioDeEvento } from '../../../src/FormularioDeEvento';

export default function NovoEvento() {
  return (
    <FormularioDeEvento
      eyebrow="AGENDA"
      titulo="Novo evento"
      onSalvar={async (entrada) => {
        await createEvent(apiClient, entrada);
        // `replace` e não `push`: voltar ao formulário depois de salvar
        // convidaria a agendar o mesmo culto duas vezes.
        router.replace('/agenda');
      }}
    />
  );
}
