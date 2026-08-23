import {
  TAMANHO_MAXIMO_DO_ANEXO,
  TIPOS_DE_ANEXO_ACEITOS,
} from '@congrega/api-client/giving';

/**
 * Um arquivo escolhido pelo usuário, pronto para subir.
 *
 * `contentBase64` é **Base64 puro**, sem o prefixo `data:`. O prefixo é uma
 * convenção de URI do navegador, não parte do conteúdo — mandá-lo junto faria o
 * servidor gravar 22 bytes de lixo no começo de todo comprovante, e o PDF não
 * abriria em nenhum leitor.
 */
export interface ArquivoEscolhido {
  readonly fileName: string;
  readonly contentType: string;
  readonly contentBase64: string;
  readonly sizeBytes: number;
}

/** O que o `accept` do seletor oferece. Espelha o CHECK da coluna. */
export const EXTENSOES_ACEITAS = '.pdf,.png,.jpg,.jpeg';

/**
 * Recusa o arquivo antes de gastar rede com ele.
 *
 * O servidor recusa de qualquer jeito — e é ele quem manda. Verificar aqui
 * evita subir 40 MB para receber um 400 no fim, o que numa conexão de igreja
 * pode levar minutos.
 *
 * Devolve `null` quando está tudo certo.
 */
export function problemaComOArquivo(nome: string, tipo: string, tamanho: number): string | null {
  if (tamanho === 0) {
    return 'O arquivo está vazio.';
  }

  if (tamanho > TAMANHO_MAXIMO_DO_ANEXO) {
    const mb = (tamanho / (1024 * 1024)).toFixed(1);
    return `O arquivo tem ${mb} MB e o limite é 10 MB.`;
  }

  // `image/jpg` não é um tipo válido, mas alguns sistemas o emitem — e é
  // exatamente o mesmo conteúdo de `image/jpeg`. Recusá-lo faria o usuário
  // olhar para um JPG legítimo sendo rejeitado sem entender por quê.
  const normalizado = tipo === 'image/jpg' ? 'image/jpeg' : tipo;

  if (!TIPOS_DE_ANEXO_ACEITOS.includes(normalizado)) {
    return `"${nome}" não é PDF, PNG nem JPG.`;
  }

  return null;
}

/** Corrige o `image/jpg` que alguns sistemas emitem. */
export function normalizarTipo(tipo: string): string {
  return tipo === 'image/jpg' ? 'image/jpeg' : tipo;
}

/**
 * Separa o Base64 de uma URI `data:`.
 *
 * `FileReader.readAsDataURL` devolve `data:application/pdf;base64,JVBERi0...`.
 * O que o servidor quer é só o que vem depois da vírgula.
 */
export function base64DaDataUri(dataUri: string): string {
  const virgula = dataUri.indexOf(',');
  return virgula < 0 ? dataUri : dataUri.slice(virgula + 1);
}
