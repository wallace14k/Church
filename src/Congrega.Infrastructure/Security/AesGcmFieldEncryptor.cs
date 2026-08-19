using System.Security.Cryptography;
using System.Text;
using Congrega.Application.Abstractions;
using Microsoft.Extensions.Options;

namespace Congrega.Infrastructure.Security;

/// <summary>
/// <see cref="IFieldEncryptor"/> em AES-256-GCM.
/// </summary>
/// <remarks>
/// <para>
/// <b>GCM e não CBC.</b> GCM é autenticado: a tag detecta qualquer alteração no
/// texto cifrado e a decifragem falha. CBC sem MAC decifraria bytes adulterados
/// em lixo silencioso — e numa ficha de alergia, lixo silencioso é uma criança
/// recebendo o que não pode.
/// </para>
/// <para>
/// <b>Nonce novo a cada operação, de CSPRNG.</b> Reusar nonce com a mesma chave
/// em GCM é a falha catastrófica do modo: dois textos cifrados sob o mesmo par
/// (chave, nonce) permitem recuperar o XOR dos textos claros e, pior, forjar a
/// autenticação. Por isso ele nunca é derivado do conteúdo nem de contador.
/// </para>
/// <para>
/// Formato persistido: <c>nonce(12) || tag(16) || ciphertext</c>. O nonce e a
/// tag não são segredos — só precisam viajar junto para a decifragem existir.
/// </para>
/// </remarks>
internal sealed class AesGcmFieldEncryptor : IFieldEncryptor, IDisposable
{
    private readonly AesGcm _aes;

    public AesGcmFieldEncryptor(IOptions<ChildSafetyOptions> options)
    {
        byte[] chave;

        try
        {
            chave = Convert.FromBase64String(options.Value.DataKey);
        }
        catch (FormatException ex)
        {
            throw new InvalidOperationException(
                $"{ChildSafetyOptions.SectionName}:DataKey não é Base64 válido.", ex);
        }

        if (chave.Length != ChildSafetyOptions.DataKeyBytes)
        {
            // Falhar aqui, na composição, e não no primeiro campo cifrado: uma
            // chave curta demais é erro de provisionamento, e descobri-lo no
            // meio de um check-in seria descobri-lo tarde.
            throw new InvalidOperationException(
                $"{ChildSafetyOptions.SectionName}:DataKey precisa ter exatamente "
                + $"{ChildSafetyOptions.DataKeyBytes} bytes (AES-256); tem {chave.Length}.");
        }

        _aes = new AesGcm(chave, AesGcm.TagByteSizes.MaxSize);

        // A cópia local da chave não é mais necessária — AesGcm já a absorveu.
        // Zerar reduz a janela em que ela aparece num dump de memória.
        CryptographicOperations.ZeroMemory(chave);
    }

    public byte[]? Encrypt(string? plaintext)
    {
        if (plaintext is null)
        {
            return null;
        }

        byte[] claro = Encoding.UTF8.GetBytes(plaintext);

        var saida = new byte[AesGcm.NonceByteSizes.MaxSize + AesGcm.TagByteSizes.MaxSize + claro.Length];
        var nonce = saida.AsSpan(0, AesGcm.NonceByteSizes.MaxSize);
        var tag = saida.AsSpan(AesGcm.NonceByteSizes.MaxSize, AesGcm.TagByteSizes.MaxSize);
        var cifrado = saida.AsSpan(AesGcm.NonceByteSizes.MaxSize + AesGcm.TagByteSizes.MaxSize);

        RandomNumberGenerator.Fill(nonce);
        _aes.Encrypt(nonce, claro, cifrado, tag);

        CryptographicOperations.ZeroMemory(claro);

        return saida;
    }

    public string? Decrypt(byte[]? ciphertext)
    {
        if (ciphertext is null)
        {
            return null;
        }

        const int cabecalho = 12 + 16; // nonce + tag

        if (ciphertext.Length < cabecalho)
        {
            throw new CryptographicException(
                "Texto cifrado curto demais para conter nonce e tag — dado corrompido ou não cifrado por este componente.");
        }

        var nonce = ciphertext.AsSpan(0, AesGcm.NonceByteSizes.MaxSize);
        var tag = ciphertext.AsSpan(AesGcm.NonceByteSizes.MaxSize, AesGcm.TagByteSizes.MaxSize);
        var cifrado = ciphertext.AsSpan(cabecalho);

        var claro = new byte[cifrado.Length];

        // Lança CryptographicException se a tag não conferir. Deixar subir é
        // deliberado: quem chama não tem decisão melhor a tomar do que abortar,
        // e engolir aqui devolveria string vazia como se o campo estivesse em
        // branco — indistinguível de "criança sem alergia".
        _aes.Decrypt(nonce, cifrado, tag, claro);

        string texto = Encoding.UTF8.GetString(claro);
        CryptographicOperations.ZeroMemory(claro);

        return texto;
    }

    public void Dispose() => _aes.Dispose();
}
