import { describeError } from '@congrega/api-client/errors';
import type { SaveEventInput } from '@congrega/api-client/events';
import { Button } from '@congrega/ui/Button';
import { Screen } from '@congrega/ui/Screen';
import { SignatureButton } from '@congrega/ui/SignatureButton';
import { Text } from '@congrega/ui/Text';
import { TextField } from '@congrega/ui/TextField';
import { useTheme } from '@congrega/ui/theme';
import { router } from 'expo-router';
import { useRef, useState } from 'react';
import { KeyboardAvoidingView, Platform, ScrollView, View } from 'react-native';
import { useSafeAreaInsets } from 'react-native-safe-area-context';

/** `dd/mm/aaaa` + `hh:mm` → ISO com o offset do aparelho. */
function paraIso(dataBr: string, hora: string): string | undefined {
  const partesData = /^(\d{2})\/(\d{2})\/(\d{4})$/u.exec(dataBr.trim());
  const partesHora = /^(\d{2}):(\d{2})$/u.exec(hora.trim());
  if (partesData === null || partesHora === null) return undefined;

  const [, dia, mes, ano] = partesData;
  const [, hh, mm] = partesHora;

  const data = new Date(
    Number(ano),
    Number(mes) - 1,
    Number(dia),
    Number(hh),
    Number(mm),
  );

  // `Number.isNaN` pega 31/02 e 25:00: o construtor de Date normaliza em
  // silêncio, então checar o resultado é a única forma de recusar a entrada.
  if (Number.isNaN(data.getTime())) return undefined;
  if (data.getDate() !== Number(dia) || data.getMonth() !== Number(mes) - 1) return undefined;
  if (Number(hh) > 23 || Number(mm) > 59) return undefined;

  return data.toISOString();
}

export function mascaraData(valor: string): string {
  const d = valor.replace(/\D/gu, '').slice(0, 8);
  if (d.length <= 2) return d;
  if (d.length <= 4) return `${d.slice(0, 2)}/${d.slice(2)}`;
  return `${d.slice(0, 2)}/${d.slice(2, 4)}/${d.slice(4)}`;
}

export function mascaraHora(valor: string): string {
  const d = valor.replace(/\D/gu, '').slice(0, 4);
  return d.length <= 2 ? d : `${d.slice(0, 2)}:${d.slice(2)}`;
}

/** Quebra um ISO em `dd/mm/aaaa` e `hh:mm` locais, para preencher o formulário. */
export function partesLocais(iso: string): { readonly data: string; readonly hora: string } {
  const d = new Date(iso);
  const p = (n: number): string => String(n).padStart(2, '0');
  return {
    data: `${p(d.getDate())}/${p(d.getMonth() + 1)}/${d.getFullYear()}`,
    hora: `${p(d.getHours())}:${p(d.getMinutes())}`,
  };
}

export interface FormularioDeEventoProps {
  readonly titulo: string;
  readonly eyebrow: string;
  readonly inicial?: {
    readonly title: string;
    readonly description: string | null;
    readonly location: string | null;
    readonly startsAt: string;
    readonly endsAt: string;
  };
  readonly onSalvar: (entrada: SaveEventInput) => Promise<void>;
}

/**
 * Formulário compartilhado entre agendar e editar.
 *
 * Um só componente porque os dois têm exatamente os mesmos campos e as mesmas
 * regras — duplicá-los faria a validação de data divergir entre as telas na
 * primeira mudança.
 */
export function FormularioDeEvento({ titulo, eyebrow, inicial, onSalvar }: FormularioDeEventoProps) {
  const theme = useTheme();
  const insets = useSafeAreaInsets();

  const inicioPartes = inicial ? partesLocais(inicial.startsAt) : null;
  const fimPartes = inicial ? partesLocais(inicial.endsAt) : null;

  const nome = useRef(inicial?.title ?? '');
  const local = useRef(inicial?.location ?? '');
  const descricao = useRef(inicial?.description ?? '');
  const dataInicio = useRef(inicioPartes?.data ?? '');
  const horaInicio = useRef(inicioPartes?.hora ?? '');
  const dataFim = useRef(fimPartes?.data ?? '');
  const horaFim = useRef(fimPartes?.hora ?? '');

  const [erros, setErros] = useState<Record<string, string>>({});
  const [erroGeral, setErroGeral] = useState<string | null>(null);
  const [salvando, setSalvando] = useState(false);

  async function salvar() {
    const problemas: Record<string, string> = {};

    if (nome.current.trim().length < 2) {
      problemas['titulo'] = 'Informe o nome do evento.';
    }

    const inicioIso = paraIso(dataInicio.current, horaInicio.current);
    if (inicioIso === undefined) {
      problemas['inicio'] = 'Informe data e hora de início.';
    }

    // Fim em branco herda a data do início: o caso comum é um evento que
    // começa e termina no mesmo dia, e repetir a data é digitação à toa.
    const dataFimEfetiva = dataFim.current.trim() || dataInicio.current;
    const fimIso = paraIso(dataFimEfetiva, horaFim.current);
    if (fimIso === undefined) {
      problemas['fim'] = 'Informe a hora de término.';
    }

    if (inicioIso !== undefined && fimIso !== undefined && fimIso <= inicioIso) {
      problemas['fim'] = 'O término precisa ser depois do início.';
    }

    setErros(problemas);
    if (Object.keys(problemas).length > 0) return;

    setErroGeral(null);
    setSalvando(true);

    try {
      await onSalvar({
        title: nome.current.trim(),
        startsAt: inicioIso!,
        endsAt: fimIso!,
        ...(local.current.trim() ? { location: local.current.trim() } : {}),
        ...(descricao.current.trim() ? { description: descricao.current.trim() } : {}),
      });
    } catch (causa) {
      setErroGeral(describeError(causa));
    } finally {
      setSalvando(false);
    }
  }

  return (
    <Screen padded={false} wide>
      <KeyboardAvoidingView behavior={Platform.OS === 'ios' ? 'padding' : undefined} style={{ flex: 1 }}>
        <ScrollView
          contentContainerStyle={{
            paddingTop: insets.top + theme.space[16],
            paddingHorizontal: theme.space[24],
            paddingBottom: insets.bottom + theme.space[48],
            gap: theme.space[16],
            maxWidth: 480,
            width: '100%',
            alignSelf: 'center',
          }}
          keyboardShouldPersistTaps="handled"
        >
          <View style={{ gap: theme.space[4], marginBottom: theme.space[8] }}>
            <Text variant="eyebrow" tone="muted">
              {eyebrow}
            </Text>
            <Text variant="heading">{titulo}</Text>
          </View>

          <TextField
            label="Nome do evento"
            placeholder="Culto de domingo"
            defaultValue={inicial?.title ?? ''}
            onValueChange={(v) => {
              nome.current = v;
              if (erros['titulo']) setErros((e) => ({ ...e, titulo: '' }));
            }}
            {...(erros['titulo'] ? { error: erros['titulo'] } : {})}
            autoCapitalize="sentences"
            autoFocus={inicial === undefined}
          />

          <View style={{ flexDirection: 'row', gap: theme.space[8] }}>
            <View style={{ flex: 3 }}>
              <TextField
                label="Data"
                placeholder="dia/mês/ano"
                defaultValue={inicioPartes?.data ?? ''}
                transform={mascaraData}
                onValueChange={(v) => {
                  dataInicio.current = v;
                  if (erros['inicio']) setErros((e) => ({ ...e, inicio: '' }));
                }}
                keyboardType="number-pad"
              />
            </View>
            <View style={{ flex: 2 }}>
              <TextField
                label="Início"
                placeholder="19:00"
                defaultValue={inicioPartes?.hora ?? ''}
                transform={mascaraHora}
                onValueChange={(v) => {
                  horaInicio.current = v;
                  if (erros['inicio']) setErros((e) => ({ ...e, inicio: '' }));
                }}
                keyboardType="number-pad"
              />
            </View>
          </View>

          {erros['inicio'] ? (
            <Text variant="captionBody" style={{ color: theme.colors.danger }}>
              {erros['inicio']}
            </Text>
          ) : null}

          <View style={{ flexDirection: 'row', gap: theme.space[8] }}>
            <View style={{ flex: 3 }}>
              <TextField
                label="Data de término"
                placeholder="mesmo dia"
                defaultValue={fimPartes?.data ?? ''}
                transform={mascaraData}
                onValueChange={(v) => {
                  dataFim.current = v;
                  if (erros['fim']) setErros((e) => ({ ...e, fim: '' }));
                }}
                keyboardType="number-pad"
                hint="Em branco = mesmo dia"
              />
            </View>
            <View style={{ flex: 2 }}>
              <TextField
                label="Término"
                placeholder="21:00"
                defaultValue={fimPartes?.hora ?? ''}
                transform={mascaraHora}
                onValueChange={(v) => {
                  horaFim.current = v;
                  if (erros['fim']) setErros((e) => ({ ...e, fim: '' }));
                }}
                keyboardType="number-pad"
              />
            </View>
          </View>

          {erros['fim'] ? (
            <Text variant="captionBody" style={{ color: theme.colors.danger }}>
              {erros['fim']}
            </Text>
          ) : null}

          <TextField
            label="Local"
            placeholder="opcional"
            defaultValue={inicial?.location ?? ''}
            onValueChange={(v) => {
              local.current = v;
            }}
          />

          <TextField
            label="Descrição"
            placeholder="opcional"
            defaultValue={inicial?.description ?? ''}
            onValueChange={(v) => {
              descricao.current = v;
            }}
          />

          {erroGeral !== null && (
            <View
              style={{
                padding: theme.space[12],
                borderRadius: theme.radius.inputs,
                borderWidth: 1,
                borderColor: theme.colors.danger,
              }}
            >
              <Text variant="captionBody">{erroGeral}</Text>
            </View>
          )}

          <SignatureButton
            label={salvando ? 'Salvando' : 'Salvar evento'}
            onPress={() => void salvar()}
            loading={salvando}
            style={{ marginTop: theme.space[8] }}
          />

          <Button label="Cancelar" variant="ghost" onPress={() => router.back()} />
        </ScrollView>
      </KeyboardAvoidingView>
    </Screen>
  );
}
