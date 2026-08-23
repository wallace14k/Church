using Congrega.Application.Abstractions;

namespace Congrega.Infrastructure.Security;

/// <summary>
/// Protege as credenciais dos conectores.
/// </summary>
/// <remarks>
/// <para>
/// Porta separada de <see cref="IFieldEncryptor"/> <b>de propósito</b>, e não por
/// simetria. Se as duas fossem a mesma interface, injetar a errada seria um erro
/// silencioso: o código compilaria, cifraria com a chave do check-in infantil, e
/// a falha só apareceria quando alguém rotacionasse uma das chaves e metade dos
/// dados parasse de decifrar. Tipos diferentes tornam a troca impossível.
/// </para>
/// <para>
/// O contrato também é mais estreito: <b>não aceita nulo</b>. Um campo de ficha
/// médica pode legitimamente estar vazio; um conector sem segredo é uma
/// configuração que nunca vai funcionar, e permitir <c>null</c> aqui só adiaria
/// a descoberta disso.
/// </para>
/// </remarks>
public interface IConnectorSecretProtector
{
    byte[] Protect(string secret);

    /// <summary>
    /// Revela o segredo, ou lança se os bytes foram adulterados.
    /// </summary>
    /// <remarks>
    /// Falhar é o comportamento certo: a tag do GCM detecta a alteração de um
    /// único bit, e devolver texto parcial faria a aplicação tentar autenticar
    /// no provedor com uma senha corrompida — gastando a tentativa e podendo
    /// bloquear a conta de e-mail da igreja.
    /// </remarks>
    string Reveal(byte[] ciphertext);
}

internal sealed class ConnectorSecretProtector(AesGcmFieldEncryptor cifrador)
    : IConnectorSecretProtector, IDisposable
{
    public byte[] Protect(string secret)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(secret);

        return cifrador.Encrypt(secret)
            ?? throw new InvalidOperationException("O cifrador devolveu nulo para um segredo presente.");
    }

    public string Reveal(byte[] ciphertext)
    {
        ArgumentNullException.ThrowIfNull(ciphertext);

        return cifrador.Decrypt(ciphertext)
            ?? throw new InvalidOperationException("O cifrador devolveu nulo para bytes presentes.");
    }

    public void Dispose() => cifrador.Dispose();
}
