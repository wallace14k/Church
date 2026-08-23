namespace Congrega.Domain.Common;

/// <summary>
/// Uma chave estrangeira do banco recusou a gravação.
/// </summary>
/// <remarks>
/// <para>
/// Par da <see cref="UniqueConstraintViolationException"/>, e pelo mesmo motivo:
/// a borda precisa transformar "esse tipo ainda está em uso" numa resposta HTTP
/// útil <b>sem</b> conhecer EF Core ou Npgsql.
/// </para>
/// <para>
/// O caminho é este — e não um <c>if (contagem == 0)</c> antes de excluir —
/// porque a verificação prévia é uma condição de corrida: entre contar os
/// eventos e apagar o tipo, outra requisição agenda um evento naquele tipo. A
/// contagem ainda existe, mas só para <b>informar</b> a interface antes do
/// clique; quem recusa é a constraint, que não tem janela.
/// </para>
/// <para>
/// O <c>RESTRICT</c> que dispara isto é escolha deliberada sobre
/// <c>CASCADE</c> e <c>SET NULL</c>: o primeiro apagaria os eventos junto com o
/// tipo, e o segundo desclassificaria a agenda inteira em silêncio.
/// </para>
/// </remarks>
public sealed class ForeignKeyViolationException(string constraintName, Exception innerException)
    : Exception($"Violação de chave estrangeira em '{constraintName}'.", innerException)
{
    /// <summary>
    /// Nome da constraint no banco — ex.: <c>fk_events_event_type</c>. É o que
    /// permite distinguir qual referência bloqueou sem interpretar mensagem de
    /// erro, que muda com o idioma do servidor.
    /// </summary>
    public string ConstraintName { get; } = constraintName;
}
