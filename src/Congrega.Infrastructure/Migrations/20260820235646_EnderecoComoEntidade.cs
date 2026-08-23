using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Congrega.Infrastructure.Migrations;

/// <summary>
/// Endereço vira entidade; o CEP ganha cache global.
/// </summary>
/// <remarks>
/// <para>
/// Corpo trocado pelo DDL. O scaffold não exprime nenhuma das quatro coisas que
/// importam aqui: a <b>policy de RLS</b> de <c>addresses</c>, o <b>CHECK</b> que
/// proíbe andar em casa, o <b>CHECK</b> de formato do CEP, e sobretudo a
/// <b>migração dos dados</b> — os seis campos de endereço que já existiam em
/// <c>members</c> precisam virar linhas antes de as colunas caírem.
/// </para>
/// <para>
/// O aviso "may result in the loss of data" do scaffold é procedente, e é
/// exatamente o que o script embutido evita: ele copia para <c>addresses</c>,
/// liga por uma coluna temporária que carrega o <c>members.id</c>, e só então
/// derruba as colunas antigas.
/// </para>
/// </remarks>
public partial class EnderecoComoEntidade : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(SqlEmbutido.Ler("Congrega.Db.140_enderecos.sql"));
    }

    /// <summary>
    /// Volta aos campos inline, preservando o que cabe.
    /// </summary>
    /// <remarks>
    /// <b>Lossy por natureza</b>, e quem executar precisa saber: o endereço de
    /// <b>evento</b> não tem para onde voltar — <c>events</c> nunca teve colunas
    /// de endereço — e é descartado. O tipo de residência e o andar também somem,
    /// porque o modelo antigo não os tinha. Só o endereço de membro volta, e sem
    /// o andar.
    /// </remarks>
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            ALTER TABLE members ADD COLUMN address_street   VARCHAR(200);
            ALTER TABLE members ADD COLUMN address_number   VARCHAR(20);
            ALTER TABLE members ADD COLUMN address_district VARCHAR(100);
            ALTER TABLE members ADD COLUMN address_city     VARCHAR(100);
            ALTER TABLE members ADD COLUMN address_state    CHAR(2);
            ALTER TABLE members ADD COLUMN address_zip      VARCHAR(9);

            UPDATE members m
            SET address_street   = a.logradouro,
                address_number   = a.numero,
                address_district = a.bairro,
                address_city     = a.localidade,
                -- `estado` guarda o nome por extenso e a coluna antiga é CHAR(2).
                -- Truncar produziria "Sã"; nulo é mais honesto que errado.
                address_state    = NULL,
                address_zip      = a.cep
            FROM addresses a
            WHERE a.id = m.address_id;

            ALTER TABLE members DROP CONSTRAINT IF EXISTS fk_members_address;
            ALTER TABLE events  DROP CONSTRAINT IF EXISTS fk_events_address;
            ALTER TABLE members DROP COLUMN address_id;
            ALTER TABLE events  DROP COLUMN address_id;

            DROP TABLE addresses;
            DROP TABLE postal_codes;
            """);
    }
}
