import type { ConnectorKind } from '@congrega/api-client/connectors';

/**
 * Um campo do formulário de um conector.
 *
 * A tela é **gerada a partir desta tabela**, e não escrita três vezes. Sem isso,
 * acrescentar o quarto conector significaria copiar um formulário inteiro — e é
 * copiando formulário que se esquece de marcar um campo como segredo.
 */
export interface CampoDeConector {
  readonly chave: string;
  readonly rotulo: string;
  readonly placeholder?: string;
  readonly ajuda?: string;

  /** Vai no `secret` da requisição, nunca em `settings`, e nunca volta do servidor. */
  readonly segredo?: boolean;

  /** Campo de texto longo — JSON de conta de serviço não cabe numa linha. */
  readonly multilinha?: boolean;

  readonly teclado?: 'default' | 'number-pad' | 'email-address';

  /** Escolha fixa, em vez de digitação livre. */
  readonly opcoes?: readonly { readonly valor: string; readonly rotulo: string }[];
}

export interface DefinicaoDeConector {
  readonly kind: ConnectorKind;
  readonly nome: string;
  readonly resumo: string;
  readonly icone: 'mail' | 'send' | 'hard-drive';

  /**
   * O que este conector já faz no sistema, hoje.
   *
   * Existe porque **dois dos três ainda não têm consumidor**, e uma tela que
   * apresentasse os três como equivalentes prometeria um funcionamento que não
   * existe. Dizer "guardado, ainda sem uso" é menos vendável e é verdade.
   */
  readonly uso: string;

  readonly campos: readonly CampoDeConector[];

  /** O que aparece no card quando o conector está configurado. */
  readonly resumoDoCampo: string;
}

export const CONECTORES: readonly DefinicaoDeConector[] = [
  {
    kind: 'Smtp',
    nome: 'E-mail',
    resumo: 'Uma conta de e-mail com permissão de envio — Gmail, Outlook, o provedor do site.',
    icone: 'mail',
    uso: 'Em uso: avisos da igreja saem por esta conta assim que ela é ligada.',
    resumoDoCampo: 'fromAddress',
    campos: [
      {
        chave: 'host',
        rotulo: 'Servidor de saída',
        placeholder: 'smtp.gmail.com',
        ajuda: 'Gmail: smtp.gmail.com · Outlook: smtp-mail.outlook.com',
      },
      {
        chave: 'port',
        rotulo: 'Porta',
        placeholder: '587',
        teclado: 'number-pad',
        ajuda: '587 com StartTls é o mais comum. 465 usa SslOnConnect.',
      },
      {
        chave: 'security',
        rotulo: 'Segurança',
        // Não há opção sem criptografia: SMTP em claro entrega a senha da conta
        // de e-mail para quem estiver no caminho — e essa conta costuma ser a
        // que recupera as outras senhas da igreja.
        opcoes: [
          { valor: 'StartTls', rotulo: 'StartTls (porta 587)' },
          { valor: 'SslOnConnect', rotulo: 'SslOnConnect (porta 465)' },
        ],
      },
      {
        chave: 'username',
        rotulo: 'Usuário',
        placeholder: 'igreja@gmail.com',
        teclado: 'email-address',
        ajuda: 'Quase sempre o endereço de e-mail inteiro.',
      },
      {
        chave: 'password',
        rotulo: 'Senha',
        segredo: true,
        placeholder: '••••••••••••',
        ajuda:
          'Gmail e Outlook não aceitam a senha da conta: gere uma senha de aplicativo, com a verificação em duas etapas ligada.',
      },
      {
        chave: 'fromAddress',
        rotulo: 'Enviar como',
        placeholder: 'igreja@gmail.com',
        teclado: 'email-address',
        ajuda: 'Precisa ser um endereço que a conta tem permissão de usar.',
      },
      {
        chave: 'fromName',
        rotulo: 'Nome do remetente',
        placeholder: 'Igreja Betel',
        ajuda: 'É o nome que aparece na caixa de entrada de quem recebe.',
      },
    ],
  },
  {
    kind: 'Telegram',
    nome: 'Telegram',
    resumo: 'Um bot que publica avisos num grupo ou canal da igreja.',
    icone: 'send',
    uso: 'Guardado e testável. Ainda não há aviso automático saindo por ele.',
    resumoDoCampo: 'chatId',
    campos: [
      {
        chave: 'botToken',
        rotulo: 'Token do bot',
        segredo: true,
        placeholder: '123456789:AAE...',
        ajuda: 'Fale com o @BotFather no Telegram, crie o bot e copie o token inteiro.',
      },
      {
        chave: 'chatId',
        rotulo: 'Id do chat',
        placeholder: '-1001234567890',
        ajuda:
          'Em grupo começa com "-100". Adicione o bot ao grupo, mande uma mensagem e abra api.telegram.org/bot<token>/getUpdates.',
      },
    ],
  },
  {
    kind: 'GoogleDrive',
    nome: 'Google Drive',
    resumo: 'Uma conta de serviço do Google com acesso a uma pasta.',
    icone: 'hard-drive',
    uso: 'Guardado e testável. Ainda não há arquivo sendo gravado por ele.',
    resumoDoCampo: 'clientEmail',
    campos: [
      {
        chave: 'serviceAccountJson',
        rotulo: 'Chave da conta de serviço',
        segredo: true,
        multilinha: true,
        placeholder: '{ "type": "service_account", ... }',
        ajuda:
          'No Google Cloud: IAM e Admin › Contas de serviço › Chaves › Adicionar chave (JSON). Cole o arquivo inteiro.',
      },
      {
        chave: 'clientEmail',
        rotulo: 'E-mail da conta de serviço',
        placeholder: 'congrega@projeto.iam.gserviceaccount.com',
        ajuda:
          'Está dentro do JSON, em "client_email". Compartilhe a pasta do Drive com este endereço, como Editor — sem isso a conta não enxerga nada.',
      },
      {
        chave: 'folderId',
        rotulo: 'Id da pasta',
        placeholder: '1A2b3C4d5E6f7G8h9I',
        ajuda: 'Abra a pasta no Drive: é o trecho final da URL, depois de /folders/.',
      },
    ],
  },
];

export function definicaoDe(kind: ConnectorKind): DefinicaoDeConector {
  const definicao = CONECTORES.find((c) => c.kind === kind);

  if (definicao === undefined) {
    throw new Error(`Conector sem definição na tela: ${kind}`);
  }

  return definicao;
}
