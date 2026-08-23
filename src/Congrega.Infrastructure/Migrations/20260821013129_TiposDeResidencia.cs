using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Congrega.Infrastructure.Migrations;

/// <summary>
/// Acrescenta Sítio/Chácara e Outro ao tipo de residência.
/// </summary>
/// <remarks>
/// <para>
/// Corpo trocado pelo DDL: o scaffold não vê constraint de <c>CHECK</c>, que é
/// exatamente o que muda aqui — o modelo do EF não sabe que <c>residence_type</c>
/// tinha um teto de 2.
/// </para>
/// <para>
/// <b>A regra do andar precisou ser reescrita junto</b>, e é o ponto sutil. Ela
/// era negativa — "casa não tem andar" — o que cobria tudo enquanto só existiam
/// dois valores. Com sítio e "outro" no conjunto, a mesma expressão passaria a
/// PERMITIR andar nos dois valores novos. Invertida para positiva ("só
/// apartamento tem andar"), ela vale para qualquer valor que venha depois.
/// </para>
/// </remarks>
public partial class TiposDeResidencia : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(SqlEmbutido.Ler("Congrega.Db.150_tipos_residencia.sql"));
    }

    /// <summary>
    /// Volta ao teto de dois valores.
    /// </summary>
    /// <remarks>
    /// <b>Lossy</b>, e quem executar precisa saber: endereços marcados como
    /// sítio ou "outro" viram <c>Casa</c>, porque o conjunto antigo não tem onde
    /// guardá-los. A conversão acontece ANTES do <c>CHECK</c> voltar — sem ela,
    /// a constraint recusaria as linhas existentes e a reversão falharia no meio.
    /// </remarks>
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            UPDATE addresses SET residence_type = 1 WHERE residence_type > 2;

            ALTER TABLE addresses DROP CONSTRAINT ck_addresses_residence_type;
            ALTER TABLE addresses
                ADD CONSTRAINT ck_addresses_residence_type CHECK (residence_type BETWEEN 1 AND 2);

            ALTER TABLE addresses DROP CONSTRAINT ck_addresses_andar;
            ALTER TABLE addresses
                ADD CONSTRAINT ck_addresses_andar CHECK (residence_type <> 1 OR andar IS NULL);
            """);
    }
}
