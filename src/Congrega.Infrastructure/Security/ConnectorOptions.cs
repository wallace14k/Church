using System.ComponentModel.DataAnnotations;

namespace Congrega.Infrastructure.Security;

/// <summary>
/// Segredo que protege as credenciais de integração de cada igreja.
/// </summary>
/// <remarks>
/// <para>
/// <b>Chave própria, e não a do check-in infantil.</b> As duas protegem dados
/// sem relação e têm ciclos de vida independentes: se um integrador vir esta
/// chave, ela precisa ser rotacionada — e essa rotação não pode tornar ilegível
/// a ficha de alergia de nenhuma criança. Compartilhar a chave amarraria as duas
/// rotações uma à outra, e a mais frágil ditaria o ritmo da mais sensível.
/// </para>
/// <para>
/// <c>[Required]</c> com <c>ValidateOnStart</c>: o processo não sobe sem ela.
/// Subir com a cifragem desligada gravaria a senha da conta de e-mail de cada
/// igreja em texto claro, sem que nada acusasse — não subir é a falha
/// preferível. É a mesma postura de <see cref="ChildSafetyOptions"/> e da
/// premissa P8.
/// </para>
/// </remarks>
public sealed class ConnectorOptions
{
    public const string SectionName = "Connectors";

    /// <summary>
    /// Chave AES-256 em Base64, do secret manager.
    /// </summary>
    /// <remarks>
    /// Gerar uma:
    /// <c>dotnet user-secrets set "Connectors:DataKey" "$(openssl rand -base64 32)"</c>
    /// </remarks>
    [Required]
    public required string DataKey { get; init; }
}
