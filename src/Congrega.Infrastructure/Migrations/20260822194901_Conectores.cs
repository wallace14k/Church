using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Congrega.Infrastructure.Migrations;

/// <summary>
/// Conectores: os serviços externos que cada igreja configura.
/// </summary>
/// <remarks>
/// <para>
/// Corpo trocado pelo DDL. O scaffold não exprime o RLS, nem os três
/// <c>CHECK</c> que sustentam as invariantes — em especial
/// <c>octet_length(secret_enc) &gt;= 29</c>, que é o alarme contra alguém gravar
/// uma senha em texto claro contornando o cifrador da aplicação: um segredo
/// realmente cifrado carrega nonce (12) e tag (16) antes do conteúdo.
/// </para>
/// <para>
/// Também não exprime a concessão de <c>connectors.manage</c> ao
/// <c>ChurchAdmin</c>, sem a qual a tela existe e nenhuma policy a aprova.
/// </para>
/// </remarks>
public partial class Conectores : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(SqlEmbutido.Ler("Congrega.Db.200_conectores.sql"));
    }

    /// <summary>
    /// Remove os conectores.
    /// </summary>
    /// <remarks>
    /// <b>Leva junto as credenciais, e não há como não levar:</b> elas só existem
    /// aqui, cifradas. Quem descer esta migration precisa reconfigurar cada
    /// igreja à mão — o que exige ter as senhas de aplicativo em outro lugar.
    /// </remarks>
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DROP TABLE IF EXISTS tenant_connectors;

            DELETE FROM role_permissions
            WHERE permission_id IN (SELECT id FROM permissions WHERE code = 'connectors.manage');

            DELETE FROM permissions WHERE code = 'connectors.manage';
            """);
    }
}
