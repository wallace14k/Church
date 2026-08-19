using System.ComponentModel.DataAnnotations;

namespace Congrega.Infrastructure.Security;

/// <summary>
/// Segredos do módulo de check-in infantil.
/// </summary>
/// <remarks>
/// <para>
/// Seção própria, e não dentro de <c>Authentication</c>, porque são segredos de
/// ciclo de vida independente: rotacionar o pepper do OTP não pode invalidar os
/// códigos de retirada em circulação, e vice-versa.
/// </para>
/// <para>
/// <b>Os dois são <c>[Required]</c> com <c>ValidateOnStart</c>.</b> O processo
/// não sobe sem eles — mesma postura de <c>IEmailSender</c> e
/// <c>AddCongregaPayments</c> (premissa P8). Subir o check-in infantil com a
/// criptografia desligada seria gravar alergia de criança em texto claro sem
/// que nada acusasse; não subir é a falha preferível.
/// </para>
/// </remarks>
public sealed class ChildSafetyOptions
{
    public const string SectionName = "ChildSafety";

    /// <summary>Tamanho exato da chave AES-256, em bytes.</summary>
    public const int DataKeyBytes = 32;

    /// <summary>
    /// Chave AES-256 em Base64, do secret manager. <b>Nunca</b> versionada,
    /// nunca no banco — é o que separa "cifrado" de "cifrado com a chave ao
    /// lado".
    /// </summary>
    [Required]
    public required string DataKey { get; init; }

    /// <summary>
    /// Pepper do HMAC do código de retirada. Ver <c>ISecretHasher.HashPickupCode</c>.
    /// </summary>
    [Required, MinLength(32)]
    public required string PickupCodePepper { get; init; }
}
