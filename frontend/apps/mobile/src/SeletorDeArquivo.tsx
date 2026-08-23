import { Text } from '@congrega/ui/Text';
import { useTheme } from '@congrega/ui/theme';
import { Feather } from '@expo/vector-icons';
import * as DocumentPicker from 'expo-document-picker';
import { useEffect, useRef, useState } from 'react';
import { Platform, Pressable, View } from 'react-native';
import {
  base64DaDataUri,
  EXTENSOES_ACEITAS,
  normalizarTipo,
  problemaComOArquivo,
  type ArquivoEscolhido,
} from './arquivoEmBase64';

/**
 * Id do `<input type="file">` escondido, no web.
 *
 * Estável de propósito: é o que permite o teste automatizado entregar um
 * arquivo ao campo sem abrir o seletor nativo do sistema operacional, que
 * nenhum teste consegue controlar.
 */
const ID_DO_INPUT = 'anexo-do-comprovante';

interface Props {
  readonly arquivo: ArquivoEscolhido | null;
  readonly onEscolher: (arquivo: ArquivoEscolhido) => void;
  readonly onRemover: () => void;
}

/**
 * Escolhe um arquivo e o entrega em Base64.
 *
 * <b>Dois caminhos, porque as plataformas não oferecem o mesmo.</b> No
 * navegador, um `<input type="file">` de verdade — é o que dá arrastar-e-soltar
 * de graça e o que um teste automatizado consegue alimentar. No celular,
 * `expo-document-picker`, que abre o seletor do sistema.
 *
 * Os dois terminam no mesmo lugar: `FileReader.readAsDataURL`, do qual o Base64
 * é extraído. Ler o arquivo inteiro em memória é aceitável porque o teto é
 * 10 MB — acima disso o arquivo é recusado antes de ser lido.
 */
export function SeletorDeArquivo({ arquivo, onEscolher, onRemover }: Props) {
  const theme = useTheme();
  const [erro, setErro] = useState<string | null>(null);
  const [lendo, setLendo] = useState(false);
  const inputRef = useRef<HTMLInputElement | null>(null);

  // O input do web é criado uma vez e vive escondido no documento. Criá-lo sob
  // demanda e clicá-lo desanexado funciona, mas deixa o campo invisível para
  // qualquer ferramenta que precise preenchê-lo — inclusive o teste.
  useEffect(() => {
    if (Platform.OS !== 'web') return undefined;

    const input = document.createElement('input');
    input.type = 'file';
    input.id = ID_DO_INPUT;
    input.accept = EXTENSOES_ACEITAS;
    input.style.position = 'absolute';
    input.style.width = '1px';
    input.style.height = '1px';
    input.style.opacity = '0';
    input.setAttribute('aria-label', 'Anexo do comprovante');

    input.addEventListener('change', () => {
      const escolhido = input.files?.[0];
      if (escolhido === undefined) return;

      void lerArquivoDoNavegador(escolhido);

      // Sem isto, escolher o MESMO arquivo duas vezes seguidas não dispara
      // `change` na segunda — o valor não mudou, e o evento não acontece.
      input.value = '';
    });

    document.body.appendChild(input);
    inputRef.current = input;

    return () => {
      input.remove();
      inputRef.current = null;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  async function lerArquivoDoNavegador(file: File) {
    const problema = problemaComOArquivo(file.name, file.type, file.size);
    if (problema !== null) {
      setErro(problema);
      return;
    }

    setErro(null);
    setLendo(true);

    try {
      const dataUri = await new Promise<string>((resolver, rejeitar) => {
        const leitor = new FileReader();
        leitor.onload = () => resolver(String(leitor.result));
        leitor.onerror = () => rejeitar(new Error('Não foi possível ler o arquivo.'));
        leitor.readAsDataURL(file);
      });

      onEscolher({
        fileName: file.name,
        contentType: normalizarTipo(file.type),
        contentBase64: base64DaDataUri(dataUri),
        sizeBytes: file.size,
      });
    } catch {
      setErro('Não foi possível ler o arquivo. Tente escolher de novo.');
    } finally {
      setLendo(false);
    }
  }

  /**
   * Caminho do celular.
   *
   * O picker devolve uma URI no cache do app; `fetch` sobre ela dá o blob, e o
   * `FileReader` termina do mesmo jeito que no navegador. `copyToCacheDirectory`
   * é o que garante que a URI seja legível — sem isso, no Android ela aponta
   * para um provedor de conteúdo que some assim que o seletor fecha.
   *
   * **Este caminho não foi executado nesta máquina.** Não há dispositivo aqui;
   * está registrado como pendente de verificação em TODO.md.
   */
  async function escolherNoCelular() {
    setErro(null);

    const resultado = await DocumentPicker.getDocumentAsync({
      type: ['application/pdf', 'image/png', 'image/jpeg'],
      copyToCacheDirectory: true,
      multiple: false,
    });

    if (resultado.canceled) return;

    const asset = resultado.assets[0];
    if (asset === undefined) return;

    const tamanho = asset.size ?? 0;
    const tipo = asset.mimeType ?? '';

    const problema = problemaComOArquivo(asset.name, tipo, tamanho);
    if (problema !== null) {
      setErro(problema);
      return;
    }

    setLendo(true);

    try {
      const resposta = await fetch(asset.uri);
      const blob = await resposta.blob();

      const dataUri = await new Promise<string>((resolver, rejeitar) => {
        const leitor = new FileReader();
        leitor.onload = () => resolver(String(leitor.result));
        leitor.onerror = () => rejeitar(new Error('Não foi possível ler o arquivo.'));
        leitor.readAsDataURL(blob);
      });

      onEscolher({
        fileName: asset.name,
        contentType: normalizarTipo(tipo),
        contentBase64: base64DaDataUri(dataUri),
        sizeBytes: tamanho,
      });
    } catch {
      setErro('Não foi possível ler o arquivo. Tente escolher de novo.');
    } finally {
      setLendo(false);
    }
  }

  function abrir() {
    if (Platform.OS === 'web') {
      inputRef.current?.click();
      return;
    }

    void escolherNoCelular();
  }

  if (arquivo !== null) {
    return (
      <View style={{ gap: theme.space[8] }}>
        <View
          style={{
            flexDirection: 'row',
            alignItems: 'center',
            gap: theme.space[12],
            paddingVertical: theme.space[12],
            paddingHorizontal: theme.space[16],
            borderRadius: theme.radius.smallCards,
            borderWidth: 1,
            borderColor: theme.colors.hairline,
            backgroundColor: theme.colors.surfaceAccentSoft,
          }}
        >
          <Feather
            name={arquivo.contentType === 'application/pdf' ? 'file-text' : 'image'}
            size={18}
            color={theme.colors.textOnAccentSoft}
          />
          <View style={{ flex: 1, minWidth: 0 }}>
            <Text variant="bodyStrong" numberOfLines={1}>
              {arquivo.fileName}
            </Text>
            <Text variant="captionBody" tone="muted">
              {formatarTamanho(arquivo.sizeBytes)}
            </Text>
          </View>
          <Pressable
            onPress={onRemover}
            accessibilityRole="button"
            accessibilityLabel={`Remover ${arquivo.fileName}`}
            hitSlop={8}
          >
            <Text variant="caption" style={{ color: theme.colors.danger }}>
              Remover
            </Text>
          </Pressable>
        </View>
      </View>
    );
  }

  return (
    <View style={{ gap: theme.space[8] }}>
      <Pressable
        onPress={abrir}
        disabled={lendo}
        accessibilityRole="button"
        accessibilityLabel="Anexar comprovante"
        style={({ pressed }) => ({
          alignItems: 'center',
          paddingVertical: theme.space[24],
          paddingHorizontal: theme.space[16],
          borderRadius: theme.radius.smallCards,
          borderWidth: 1,
          borderStyle: 'dashed',
          borderColor: pressed ? theme.colors.surfaceAccent : theme.colors.hairline,
          backgroundColor: theme.colors.surfaceInner,
          gap: theme.space[4],
        })}
      >
        <Feather name="upload" size={22} color={theme.colors.textMuted} />
        <Text variant="bodyStrong">
          {lendo ? 'Lendo o arquivo…' : 'Clique para anexar o comprovante'}
        </Text>
        <Text variant="captionBody" tone="muted">
          PDF, PNG ou JPG até 10 MB
        </Text>
      </Pressable>

      {erro !== null ? (
        <Text variant="captionBody" style={{ color: theme.colors.danger }}>
          {erro}
        </Text>
      ) : null}
    </View>
  );
}

/** "240 KB", "1,4 MB". Bytes crus não dizem nada a quem anexa uma nota. */
export function formatarTamanho(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${Math.round(bytes / 1024)} KB`;

  return `${(bytes / (1024 * 1024)).toFixed(1).replace('.', ',')} MB`;
}
