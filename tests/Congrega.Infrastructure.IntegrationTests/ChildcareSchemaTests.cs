using System.Security.Cryptography;
using System.Text;
using Congrega.Application.Abstractions;
using Congrega.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Congrega.Infrastructure.IntegrationTests;

/// <summary>
/// Os portões do check-in infantil, contra Postgres real.
/// </summary>
/// <remarks>
/// <para>
/// O ADR-014 escreve o critério de aceitação em uma frase: <b>"o DBA não deve
/// conseguir ler esses campos com um <c>SELECT</c>"</b>. Isso é verificável, e
/// só é verificável contra um banco de verdade — um teste de unidade sobre o
/// encryptor provaria que a função cifra, não que a coluna guardada está
/// cifrada.
/// </para>
/// <para>
/// O isolamento por RLS entra pelo mesmo motivo do teste cross-tenant da Onda
/// 1: uma tabela criada sem <c>ENABLE ROW LEVEL SECURITY</c> tem exatamente a
/// mesma aparência de uma protegida, e a diferença é a ficha de alergia de uma
/// criança visível para outra igreja.
/// </para>
/// </remarks>
public sealed class ChildcareSchemaTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("congrega")
        .WithUsername("congrega")
        .WithPassword("owner-" + Guid.NewGuid().ToString("N"))
        .Build();

    private readonly string _appPassword = "app-" + Guid.NewGuid().ToString("N");

    private NpgsqlDataSource _ownerDataSource = null!;
    private NpgsqlDataSource _appDataSource = null!;

    private long _tenantA;
    private long _tenantB;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        string conexaoDona = _container.GetConnectionString();

        await using (var bootstrap = new NpgsqlConnection(conexaoDona))
        {
            await bootstrap.OpenAsync();
            await using var cmd = new NpgsqlCommand("CREATE EXTENSION IF NOT EXISTS citext;", bootstrap);
            await cmd.ExecuteNonQueryAsync();
        }

        _ownerDataSource = NpgsqlDataSource.Create(conexaoDona);

        await using (var migracao = new Congrega.Infrastructure.Persistence.CongregaDbContext(
            new DbContextOptionsBuilder<Congrega.Infrastructure.Persistence.CongregaDbContext>()
                .UseNpgsql(_ownerDataSource).Options,
            ContextoCrossTenant.Instance,
            TimeProvider.System))
        {
            await migracao.Database.MigrateAsync();
        }

        await using var conn = await _ownerDataSource.OpenConnectionAsync();

        await using (var senha = new NpgsqlCommand(
            $"ALTER ROLE congrega_app WITH PASSWORD '{_appPassword}'", conn))
        {
            await senha.ExecuteNonQueryAsync();
        }

        _tenantA = await CriarTenantAsync(conn, "Igreja A", "igreja-a");
        _tenantB = await CriarTenantAsync(conn, "Igreja B", "igreja-b");

        var construtor = new NpgsqlConnectionStringBuilder(conexaoDona)
        {
            Username = "congrega_app",
            Password = _appPassword,
        };
        _appDataSource = NpgsqlDataSource.Create(construtor.ConnectionString);
    }

    public async Task DisposeAsync()
    {
        await _appDataSource.DisposeAsync();
        await _ownerDataSource.DisposeAsync();
        await _container.DisposeAsync();
    }

    private static async Task<long> CriarTenantAsync(NpgsqlConnection conn, string nome, string slug)
    {
        await using var cmd = new NpgsqlCommand(
            "INSERT INTO tenants (name, slug, status) VALUES (@nome, @slug, 1) RETURNING id;", conn);
        cmd.Parameters.AddWithValue("nome", nome);
        cmd.Parameters.AddWithValue("slug", slug);
        return (long)(await cmd.ExecuteScalarAsync())!;
    }

    private async Task<long> InserirCriancaAsync(long tenantId, string nome, byte[]? alergiaCifrada)
    {
        await using var conn = await _ownerDataSource.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO children (tenant_id, full_name, birth_date, allergies_enc)
            VALUES (@tenant, @nome, DATE '2020-03-15', @alergia)
            RETURNING id;
            """, conn);

        cmd.Parameters.AddWithValue("tenant", tenantId);
        cmd.Parameters.AddWithValue("nome", nome);
        cmd.Parameters.AddWithValue("alergia", (object?)alergiaCifrada ?? DBNull.Value);

        return (long)(await cmd.ExecuteScalarAsync())!;
    }

    private static AesGcmFieldEncryptor CriarEncryptor()
    {
        var chave = new byte[ChildSafetyOptions.DataKeyBytes];
        RandomNumberGenerator.Fill(chave);

        return new AesGcmFieldEncryptor(Options.Create(new ChildSafetyOptions
        {
            DataKey = Convert.ToBase64String(chave),
            PickupCodePepper = new string('p', 32),
        }));
    }

    // -------------------------------------------------------------------------
    // O critério de aceitação escrito no ADR-014
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Alergia_gravada_e_ilegivel_num_SELECT_cru()
    {
        // Este é o teste que o ADR pede textualmente. Se alguém trocar a coluna
        // para TEXT ou tirar a cifragem do caminho de escrita, ele falha.
        const string alergia = "Alergia grave a amendoim";
        using var encryptor = CriarEncryptor();

        long id = await InserirCriancaAsync(_tenantA, "Ana", encryptor.Encrypt(alergia));

        await using var conn = await _ownerDataSource.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT allergies_enc FROM children WHERE id = @id;", conn);
        cmd.Parameters.AddWithValue("id", id);

        var bruto = (byte[])(await cmd.ExecuteScalarAsync())!;

        // O que o DBA vê. Nem o texto, nem qualquer pedaço reconhecível dele.
        string comoTexto = Encoding.UTF8.GetString(bruto);
        Assert.DoesNotContain("amendoim", comoTexto, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Alergia", comoTexto, StringComparison.OrdinalIgnoreCase);

        // E quem tem a chave lê de volta.
        Assert.Equal(alergia, encryptor.Decrypt(bruto));
    }

    [Fact]
    public void Mesmo_texto_cifrado_duas_vezes_produz_bytes_diferentes()
    {
        // Prova o nonce aleatório. Sem ele, textos cifrados iguais revelariam
        // quais crianças têm a mesma alergia — e reusar nonce em GCM permite
        // recuperar o XOR dos textos claros e forjar autenticação.
        using var encryptor = CriarEncryptor();

        var primeira = encryptor.Encrypt("amendoim")!;
        var segunda = encryptor.Encrypt("amendoim")!;

        Assert.NotEqual(primeira, segunda);
        Assert.Equal("amendoim", encryptor.Decrypt(primeira));
        Assert.Equal("amendoim", encryptor.Decrypt(segunda));
    }

    [Fact]
    public void Texto_cifrado_adulterado_falha_em_vez_de_devolver_lixo()
    {
        // É o que a tag do GCM garante, e a razão de não usar CBC: decifrar
        // bytes adulterados em lixo silencioso, numa ficha de alergia, tem
        // consequência física.
        using var encryptor = CriarEncryptor();
        var cifrado = encryptor.Encrypt("Alergia a amendoim")!;

        cifrado[^1] ^= 0xFF;

        // AuthenticationTagMismatchException, e não CryptographicException genérica:
        // asseverar o tipo exato documenta QUEM pegou a adulteração — a tag do
        // GCM. Se alguém trocar o modo por um não autenticado, este teste falha
        // em vez de continuar passando por acidente.
        Assert.Throws<AuthenticationTagMismatchException>(() => encryptor.Decrypt(cifrado));
    }

    [Fact]
    public void Chave_de_tamanho_errado_falha_na_composicao()
    {
        // Erro de provisionamento. Descobri-lo no primeiro check-in seria
        // descobri-lo tarde, com a fila do berçário formada.
        var curta = Convert.ToBase64String(new byte[16]);

        var erro = Assert.Throws<InvalidOperationException>(() =>
            new AesGcmFieldEncryptor(Options.Create(new ChildSafetyOptions
            {
                DataKey = curta,
                PickupCodePepper = new string('p', 32),
            })));

        Assert.Contains("32 bytes", erro.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Nulo_continua_nulo_nos_dois_sentidos()
    {
        // Ausência de alergia não é segredo — cifrar NULL geraria bytes que
        // sugeririam a existência de um dado onde não há nenhum.
        using var encryptor = CriarEncryptor();

        Assert.Null(encryptor.Encrypt(null));
        Assert.Null(encryptor.Decrypt(null));
    }

    // -------------------------------------------------------------------------
    // Isolamento
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Rls_impede_que_uma_igreja_veja_crianca_de_outra()
    {
        await InserirCriancaAsync(_tenantA, "Ana da Igreja A", null);
        await InserirCriancaAsync(_tenantB, "Bruno da Igreja B", null);

        // Conexão com congrega_app (RLS aplicado), contexto no tenant A —
        // exatamente o que a API tem depois de resolver a membership.
        await using var conn = await _appDataSource.OpenConnectionAsync();

        await using (var contexto = new NpgsqlCommand(
            "SELECT set_config('app.tenant_id', @tenant, false);", conn))
        {
            contexto.Parameters.AddWithValue(
                "tenant", _tenantA.ToString(System.Globalization.CultureInfo.InvariantCulture));
            await contexto.ExecuteNonQueryAsync();
        }

        // Pede TODAS as crianças, sem filtro nenhum na consulta.
        await using var cmd = new NpgsqlCommand("SELECT full_name FROM children;", conn);
        await using var leitor = await cmd.ExecuteReaderAsync();

        var nomes = new List<string>();
        while (await leitor.ReadAsync())
        {
            nomes.Add(leitor.GetString(0));
        }

        Assert.Contains("Ana da Igreja A", nomes);
        Assert.DoesNotContain("Bruno da Igreja B", nomes);
    }

    [Fact]
    public async Task Uma_crianca_nao_entra_duas_vezes_no_mesmo_evento()
    {
        // O índice parcial `uq_checkins_presente`. É a defesa contra a fila
        // offline reapresentando com chave nova — a idempotency key cobre a
        // reapresentação idêntica, este índice cobre a duplicata de verdade.
        await using var conn = await _ownerDataSource.OpenConnectionAsync();

        long criancaId = await InserirCriancaAsync(_tenantA, "Ana", null);
        long eventoId = await CriarEventoAsync(conn, _tenantA);
        long usuarioId = await CriarUsuarioAsync(conn, "voluntario@teste.congrega");

        await InserirCheckinAsync(conn, _tenantA, criancaId, eventoId, usuarioId, "chave-1");

        var erro = await Assert.ThrowsAsync<PostgresException>(() =>
            InserirCheckinAsync(conn, _tenantA, criancaId, eventoId, usuarioId, "chave-2"));

        Assert.Equal("23505", erro.SqlState);
        Assert.Equal("uq_checkins_presente", erro.ConstraintName);
    }

    [Fact]
    public async Task Mesma_chave_de_idempotencia_nao_grava_duas_vezes()
    {
        // A fila offline reapresenta a MESMA operação quando o Wi-Fi volta.
        await using var conn = await _ownerDataSource.OpenConnectionAsync();

        long criancaId = await InserirCriancaAsync(_tenantA, "Ana", null);
        long eventoId = await CriarEventoAsync(conn, _tenantA);
        long usuarioId = await CriarUsuarioAsync(conn, "voluntario2@teste.congrega");

        await InserirCheckinAsync(conn, _tenantA, criancaId, eventoId, usuarioId, "tablet-1:xyz");

        // Outra criança, mesma chave: quem barra é a chave, não o índice de
        // presença — é isso que este caso distingue do teste anterior.
        long outra = await InserirCriancaAsync(_tenantA, "Bruno", null);

        var erro = await Assert.ThrowsAsync<PostgresException>(() =>
            InserirCheckinAsync(conn, _tenantA, outra, eventoId, usuarioId, "tablet-1:xyz"));

        Assert.Equal("uq_checkins_idempotency", erro.ConstraintName);
    }

    [Fact]
    public async Task Retirada_sem_registrar_quem_retirou_e_recusada_pelo_banco()
    {
        // `ck_checkins_retirada`. A pergunta que se faz quando algo dá errado é
        // "quem levou a criança" — o estado que não responde isso não pode existir.
        await using var conn = await _ownerDataSource.OpenConnectionAsync();

        long criancaId = await InserirCriancaAsync(_tenantA, "Ana", null);
        long eventoId = await CriarEventoAsync(conn, _tenantA);
        long usuarioId = await CriarUsuarioAsync(conn, "voluntario3@teste.congrega");

        await InserirCheckinAsync(conn, _tenantA, criancaId, eventoId, usuarioId, "chave-retirada");

        await using var cmd = new NpgsqlCommand(
            "UPDATE child_checkins SET picked_up_at = now() WHERE idempotency_key = 'chave-retirada';",
            conn);

        var erro = await Assert.ThrowsAsync<PostgresException>(() => cmd.ExecuteNonQueryAsync());
        Assert.Equal("ck_checkins_retirada", erro.ConstraintName);
    }

    // -------------------------------------------------------------------------
    // Apoio
    // -------------------------------------------------------------------------

    private static async Task<long> CriarEventoAsync(NpgsqlConnection conn, long tenantId)
    {
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO events (tenant_id, title, starts_at, ends_at)
            VALUES (@tenant, 'Culto', now(), now() + INTERVAL '2 hours')
            RETURNING id;
            """, conn);
        cmd.Parameters.AddWithValue("tenant", tenantId);
        return (long)(await cmd.ExecuteScalarAsync())!;
    }

    private static async Task<long> CriarUsuarioAsync(NpgsqlConnection conn, string email)
    {
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO users (email, full_name, status, email_verified)
            VALUES (@email, 'Voluntario', 1, TRUE)
            RETURNING id;
            """, conn);
        cmd.Parameters.AddWithValue("email", email);
        return (long)(await cmd.ExecuteScalarAsync())!;
    }

    private static async Task InserirCheckinAsync(
        NpgsqlConnection conn,
        long tenantId,
        long childId,
        long eventId,
        long userId,
        string idempotencyKey)
    {
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO child_checkins
                (tenant_id, child_id, event_id, checked_in_by,
                 pickup_code_hash, pickup_code_expires_at, idempotency_key)
            VALUES
                (@tenant, @child, @evento, @user,
                 '\x0102030405'::bytea, now() + INTERVAL '4 hours', @chave);
            """, conn);

        cmd.Parameters.AddWithValue("tenant", tenantId);
        cmd.Parameters.AddWithValue("child", childId);
        cmd.Parameters.AddWithValue("evento", eventId);
        cmd.Parameters.AddWithValue("user", userId);
        cmd.Parameters.AddWithValue("chave", idempotencyKey);

        await cmd.ExecuteNonQueryAsync();
    }

    private sealed class ContextoCrossTenant : ITenantContext
    {
        public static readonly ContextoCrossTenant Instance = new();
        public long? TenantId => null;
        public long? UserId => null;
        public bool IsCrossTenantOperation => true;
    }
}
