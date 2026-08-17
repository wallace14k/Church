namespace Congrega.Domain.Common;

/// <summary>
/// Uma constraint de unicidade do banco recusou a gravação.
/// </summary>
/// <remarks>
/// <para>
/// Existe para que a borda possa transformar "esse nome já existe" numa resposta
/// HTTP útil <b>sem</b> conhecer EF Core ou Npgsql. A infraestrutura traduz a
/// exceção do provedor para esta; a API lê apenas <see cref="ConstraintName"/>.
/// </para>
/// <para>
/// O caminho é esse — e não um <c>if (!existe)</c> antes de inserir — porque a
/// verificação prévia é uma condição de corrida: entre consultar e gravar, outra
/// requisição insere o mesmo nome. A constraint é a única checagem que não tem
/// janela.
/// </para>
/// </remarks>
public sealed class UniqueConstraintViolationException(string constraintName, Exception innerException)
    : Exception($"Violação de unicidade em '{constraintName}'.", innerException)
{
    /// <summary>
    /// Nome da constraint no banco — ex.: <c>uq_giving_categories_tenant_nome</c>.
    /// É o que permite distinguir "categoria repetida" de "e-mail repetido" sem
    /// interpretar mensagem de erro, que muda com o idioma do servidor.
    /// </summary>
    public string ConstraintName { get; } = constraintName;
}
