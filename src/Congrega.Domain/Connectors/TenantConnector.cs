using Congrega.Domain.Common;

namespace Congrega.Domain.Connectors;

/// <summary>Com que serviço externo a igreja está falando.</summary>
public enum ConnectorKind : short
{
    /// <summary>Conta de e-mail com permissão de envio — Gmail, Outlook, o provedor da hospedagem.</summary>
    Smtp = 1,

    /// <summary>Bot do Telegram, para avisos num grupo ou canal.</summary>
    Telegram = 2,

    /// <summary>Conta de serviço do Google, para gravar arquivos numa pasta do Drive.</summary>
    GoogleDrive = 3,
}

/// <summary>Como a conexão SMTP é protegida.</summary>
/// <remarks>
/// <b>Não existe opção "sem criptografia".</b> SMTP em texto claro entrega a
/// senha da conta de e-mail para qualquer um no caminho, e a conta de e-mail de
/// uma igreja costuma ser a mesma que recupera as outras senhas dela. Provedor
/// que só aceita porta 25 sem TLS não é aceitável aqui.
/// </remarks>
public enum SmtpSecurity : short
{
    /// <summary>Conecta em claro e sobe para TLS com STARTTLS. É o caso da porta 587.</summary>
    StartTls = 1,

    /// <summary>TLS desde o primeiro byte. É o caso da porta 465.</summary>
    SslOnConnect = 2,
}

/// <summary>
/// Um serviço externo que a igreja configurou.
/// </summary>
/// <remarks>
/// <para>
/// <b>É por igreja, não da plataforma.</b> Cada tenant configura o próprio
/// e-mail, o próprio bot e a própria pasta do Drive — e o RLS garante que uma
/// igreja não enxergue a credencial da outra. Uma configuração global exigiria
/// que todas as igrejas mandassem e-mail pelo mesmo remetente, o que é errado
/// tanto de produto (a mensagem chega assinada por quem?) quanto de segurança.
/// </para>
/// <para>
/// <b>O segredo entra aqui já cifrado.</b> O domínio não conhece
/// <c>System.Security.Cryptography</c> — a cifragem acontece na borda, com a
/// chave do secret manager, e o que chega nesta entidade são bytes opacos. É a
/// mesma divisão de <c>ISecretHasher</c>: a regra de negócio sabe que existe um
/// segredo; não sabe como ele é protegido.
/// </para>
/// <para>
/// <b>A configuração não-secreta é um dicionário</b>, e não colunas.
/// Host e porta do SMTP, id do chat do Telegram e id da pasta do Drive não têm
/// nada em comum — dar coluna a cada um produziria uma tabela em que dois
/// terços das colunas são sempre nulas, e um quarto conector exigiria migration.
/// </para>
/// </remarks>
public sealed class TenantConnector : AggregateRoot
{
    public const int MaxSettingValueLength = 500;

    /// <summary>Teto da mensagem do último teste. Casa com o <c>VARCHAR</c> da coluna.</summary>
    public const int MaxTestMessageLength = 300;

    private Dictionary<string, string> _settings = new(StringComparer.Ordinal);

    private TenantConnector() { }

    public long Id { get; private set; }
    public Guid PublicId { get; private set; }
    public long TenantId { get; private set; }
    public ConnectorKind Kind { get; private set; }

    /// <summary>
    /// O conector está ligado.
    /// </summary>
    /// <remarks>
    /// Existe para desligar sem apagar: quando o provedor de e-mail bloqueia a
    /// conta, a igreja precisa parar os envios <b>sem</b> perder host, porta e
    /// remetente já digitados — e sem ter de redigitar a senha ao religar.
    /// </remarks>
    public bool IsEnabled { get; private set; }

    /// <summary>Configuração que <b>não</b> é segredo. Volta inteira para a tela.</summary>
    public IReadOnlyDictionary<string, string> Settings => _settings;

    /// <summary>
    /// O segredo, cifrado.
    /// </summary>
    /// <remarks>
    /// <b>Nunca sai da API.</b> Nem cifrado, nem mascarado, nem "só os últimos
    /// quatro caracteres": um token de bot tem entropia suficiente para que
    /// qualquer pedaço ajude quem o esteja adivinhando, e a tela não precisa
    /// dele para nada — precisa saber apenas <i>se existe</i>.
    /// </remarks>
    public byte[]? SecretCiphertext { get; private set; }

    public DateTimeOffset? LastTestedAt { get; private set; }

    /// <summary>
    /// Resultado do último teste, ou <c>null</c> se nunca foi testado.
    /// </summary>
    /// <remarks>
    /// Os três estados são diferentes e a tela precisa dos três: nunca testado
    /// é uma configuração em que ninguém confia ainda; testado e falho é um
    /// problema conhecido; testado e ok é a única que autoriza depender dele.
    /// </remarks>
    public bool? LastTestSucceeded { get; private set; }

    public string? LastTestMessage { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static TenantConnector Register(
        long tenantId,
        ConnectorKind kind,
        IReadOnlyDictionary<string, string> settings,
        byte[]? secretCiphertext,
        bool isEnabled,
        DateTimeOffset now)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tenantId);

        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentException("Tipo de conector inválido.", nameof(kind));
        }

        var limpas = Validar(kind, settings);

        // Um conector novo sem segredo é uma configuração que nunca vai
        // funcionar. Na EDIÇÃO o segredo pode faltar — ali significa "mantenha o
        // que já está lá" — mas aqui não há o que manter.
        if (secretCiphertext is null || secretCiphertext.Length == 0)
        {
            throw new ArgumentException(
                $"{DescreverSegredo(kind)} é obrigatório para criar o conector.",
                nameof(secretCiphertext));
        }

        return new TenantConnector
        {
            PublicId = Guid.NewGuid(),
            TenantId = tenantId,
            Kind = kind,
            _settings = limpas,
            SecretCiphertext = secretCiphertext,
            IsEnabled = isEnabled,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    /// <summary>
    /// Atualiza a configuração.
    /// </summary>
    /// <param name="secretCiphertext">
    /// <c>null</c> mantém o segredo atual.
    /// </param>
    /// <remarks>
    /// <b>Segredo ausente significa "não mexa", e isso não é conveniência.</b> A
    /// tela nunca recebe a senha de volta — logo ela não tem como reenviá-la. Se
    /// ausência apagasse, corrigir a porta de 465 para 587 apagaria a senha, o
    /// envio pararia, e a causa seria o campo que ninguém tocou.
    /// </remarks>
    public void Update(
        IReadOnlyDictionary<string, string> settings,
        byte[]? secretCiphertext,
        bool isEnabled,
        DateTimeOffset now)
    {
        _settings = Validar(Kind, settings);
        IsEnabled = isEnabled;

        if (secretCiphertext is { Length: > 0 })
        {
            SecretCiphertext = secretCiphertext;

            // O segredo mudou: o resultado do teste antigo passou a descrever
            // uma credencial que não é mais esta. Mantê-lo verde faria a tela
            // dizer "funcionando" sobre algo nunca experimentado.
            LastTestedAt = null;
            LastTestSucceeded = null;
            LastTestMessage = null;
        }

        UpdatedAt = now;
    }

    /// <summary>Registra o que aconteceu na última tentativa de conexão.</summary>
    public void RegistrarTeste(bool sucesso, string? mensagem, DateTimeOffset now)
    {
        LastTestedAt = now;
        LastTestSucceeded = sucesso;

        var limpa = mensagem?.Trim();
        LastTestMessage =
            string.IsNullOrEmpty(limpa) ? null
            : limpa.Length > MaxTestMessageLength ? limpa[..MaxTestMessageLength]
            : limpa;

        UpdatedAt = now;
    }

    // -------------------------------------------------------------------------
    // Validação por tipo
    // -------------------------------------------------------------------------

    /// <summary>Chaves obrigatórias de cada tipo.</summary>
    /// <remarks>
    /// Declarado como dado, e não como uma cascata de <c>if</c>: acrescentar um
    /// conector passa a ser acrescentar uma linha, e o erro devolvido nomeia o
    /// campo que faltou em vez de dizer "configuração inválida".
    /// </remarks>
    private static readonly Dictionary<ConnectorKind, string[]> Obrigatorias = new()
    {
        [ConnectorKind.Smtp] = ["host", "port", "security", "username", "fromAddress", "fromName"],
        [ConnectorKind.Telegram] = ["chatId"],
        [ConnectorKind.GoogleDrive] = ["folderId", "clientEmail"],
    };

    private static string DescreverSegredo(ConnectorKind kind) => kind switch
    {
        ConnectorKind.Smtp => "A senha da conta de e-mail",
        ConnectorKind.Telegram => "O token do bot",
        ConnectorKind.GoogleDrive => "A chave da conta de serviço",
        _ => "O segredo",
    };

    private static Dictionary<string, string> Validar(
        ConnectorKind kind,
        IReadOnlyDictionary<string, string> settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var limpas = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var (chave, valor) in settings)
        {
            var podado = valor?.Trim() ?? string.Empty;

            if (podado.Length > MaxSettingValueLength)
            {
                throw new ArgumentException(
                    $"O campo \"{chave}\" pode ter no máximo {MaxSettingValueLength} caracteres.",
                    nameof(settings));
            }

            // Vazio some em vez de virar string vazia: assim "obrigatório
            // ausente" e "obrigatório em branco" são o mesmo erro, e não dois.
            if (podado.Length > 0)
            {
                limpas[chave] = podado;
            }
        }

        foreach (var obrigatoria in Obrigatorias[kind])
        {
            if (!limpas.ContainsKey(obrigatoria))
            {
                throw new ArgumentException(
                    $"O campo \"{obrigatoria}\" é obrigatório para este conector.",
                    nameof(settings));
            }
        }

        if (kind == ConnectorKind.Smtp)
        {
            ValidarSmtp(limpas);
        }

        return limpas;
    }

    private static void ValidarSmtp(Dictionary<string, string> s)
    {
        if (!int.TryParse(s["port"], System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var porta)
            || porta is < 1 or > 65535)
        {
            throw new ArgumentException("A porta precisa ser um número entre 1 e 65535.", nameof(s));
        }

        if (!Enum.TryParse<SmtpSecurity>(s["security"], ignoreCase: true, out var seguranca)
            || !Enum.IsDefined(seguranca))
        {
            throw new ArgumentException(
                "A segurança precisa ser StartTls (porta 587) ou SslOnConnect (porta 465).",
                nameof(s));
        }

        // Um endereço de remetente errado não falha na configuração: falha no
        // envio, horas depois, num log que ninguém está olhando.
        var remetente = s["fromAddress"];
        var arroba = remetente.IndexOf('@', StringComparison.Ordinal);

        if (arroba <= 0 || arroba == remetente.Length - 1 || remetente.Contains(' ', StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"\"{remetente}\" não parece um endereço de e-mail.", nameof(s));
        }
    }
}
