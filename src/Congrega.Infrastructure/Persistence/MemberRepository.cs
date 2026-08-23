using System.Globalization;
using System.Text;
using Congrega.Domain.Congregation;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Congrega.Infrastructure.Persistence;

internal sealed class MemberConfiguration : IEntityTypeConfiguration<Member>
{
    public void Configure(EntityTypeBuilder<Member> builder)
    {
        builder.ToTable("members");
        builder.HasKey(m => m.Id);

        builder.Property(m => m.Id).HasColumnName("id").UseIdentityAlwaysColumn();
        builder.Property(m => m.PublicId).HasColumnName("public_id");
        builder.Property(m => m.TenantId).HasColumnName("tenant_id");
        builder.Property(m => m.UserId).HasColumnName("user_id");
        builder.Property(m => m.FamilyId).HasColumnName("family_id");
        builder.Property(m => m.FullName).HasColumnName("full_name").HasMaxLength(200).IsRequired();
        builder.Property(m => m.Email).HasColumnName("email").HasColumnType("citext");
        builder.Property(m => m.Phone).HasColumnName("phone").HasMaxLength(20);
        builder.Property(m => m.BirthDate).HasColumnName("birth_date");
        builder.Property(m => m.Gender).HasColumnName("gender").HasConversion<short?>();
        builder.Property(m => m.MaritalStatus).HasColumnName("marital_status").HasConversion<short?>();
        builder.Property(m => m.Status).HasColumnName("status").HasConversion<short>();
        builder.Property(m => m.MembershipDate).HasColumnName("membership_date");
        builder.Property(m => m.BaptismDate).HasColumnName("baptism_date");
        builder.Property(m => m.Notes).HasColumnName("notes");
        builder.Property(m => m.PhotoKey).HasColumnName("photo_key").HasMaxLength(500);
        builder.Property(m => m.CreatedAt).HasColumnName("created_at");
        builder.Property(m => m.UpdatedAt).HasColumnName("updated_at");
        builder.Property(m => m.AnonymizedAt).HasColumnName("anonymized_at");

        // Endereço mapeado como tipo complexo nas colunas da própria tabela.
        // Uma pessoa tem um endereço no ChMS; normalizar em tabela separada
        // acrescentaria um JOIN a toda listagem para resolver um problema que a
        // igreja não tem.
        builder.Property(m => m.AddressId).HasColumnName("address_id");

        builder.HasIndex(m => m.PublicId).IsUnique();

        builder.Ignore(m => m.DomainEvents);
    }
}

internal sealed class MemberRepository(CongregaDbContext db, TimeProvider timeProvider) : IMemberRepository
{
    public async Task<PagedResult<MemberListItem>> ListAsync(
        MemberQuery query,
        CancellationToken cancellationToken)
    {
        // Teto rígido, aplicado aqui e não só na borda: qualquer chamador que
        // esqueça de validar continua limitado.
        int pageSize = Math.Clamp(query.PageSize, 1, 100);
        int page = Math.Max(query.Page, 1);

        // O filtro por tenant vem do Global Query Filter — não aparece aqui de
        // propósito. Ver a nota em IMemberRepository.
        IQueryable<Member> source = db.Members.AsNoTracking();

        if (query.Status is { } status)
        {
            source = source.Where(m => m.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            string termo = query.Search.Trim();

            // Normaliza os DOIS lados: o termo digitado e o nome guardado passam
            // pela mesma função. Sem isso, "JOAO" não encontra "João" — e é assim
            // que a secretária digita: sem acento e com pressa.
            //
            // A expressão é idêntica à do índice ix_members_busca. Divergir aqui
            // faria o índice existir sem nunca ser usado: custo de escrita em toda
            // inserção, zero benefício na leitura.
            string alvo = NormalizacaoDeBusca.RemoverAcentos(termo).ToLowerInvariant();

            // ILike já é insensível a caixa no PostgreSQL, então o lower() do lado
            // do banco é redundante — e removê-lo elimina o alerta de cultura, que
            // aqui seria falso: a chamada nunca executa em .NET, é traduzida.
            source = source.Where(m =>
                EF.Functions.ILike(CongregaDbContext.Unaccent(m.FullName), $"%{alvo}%") ||
                (m.Email != null && EF.Functions.ILike(m.Email, $"%{termo}%")) ||
                (m.Phone != null && m.Phone.Contains(termo)));
        }

        if (query.BirthdayMonth is { } mes)
        {
            source = source.Where(m => m.BirthDate != null && m.BirthDate.Value.Month == mes);
        }

        source = AplicarLacuna(source, query.Gap);

        // Uma contagem e uma página. Duas idas ao banco, não N+1: a alternativa
        // (contar em memória) traria a tabela inteira para o processo.
        int total = await source.CountAsync(cancellationToken);

        var hoje = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);

        // Aniversariante ordena por dia do mês, não por nome: quem pergunta "quem
        // faz aniversário este mês" quer saber quem vem primeiro, não uma lista
        // alfabética que mistura dia 3 com dia 28.
        IOrderedQueryable<Member> ordenado = query.BirthdayMonth is not null
            ? source.OrderBy(m => m.BirthDate!.Value.Day).ThenBy(m => m.FullName)
            : source.OrderBy(m => m.FullName);

        var itens = await ordenado
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(m => new
            {
                m.PublicId,
                m.FullName,
                m.Email,
                m.Phone,
                m.BirthDate,
                m.Status,
                FamilyName = db.Families
                    .Where(f => f.Id == m.FamilyId)
                    .Select(f => f.Name)
                    .FirstOrDefault(),
            })
            .ToListAsync(cancellationToken);

        return new PagedResult<MemberListItem>
        {
            Items = itens.ConvertAll(m => new MemberListItem
            {
                PublicId = m.PublicId,
                FullName = m.FullName,
                Email = m.Email,
                Phone = m.Phone,
                BirthDate = m.BirthDate,
                // Idade calculada em memória: a regra vive no domínio, e traduzi-la
                // para SQL duplicaria a lógica em dois lugares que divergiriam.
                Age = m.BirthDate is null ? null : CalcularIdade(m.BirthDate.Value, hoje),
                Status = m.Status,
                FamilyName = m.FamilyName,
            }),
            Page = page,
            PageSize = pageSize,
            TotalCount = total,
        };
    }

    public Task<Member?> FindByPublicIdAsync(Guid publicId, CancellationToken cancellationToken) =>
        db.Members.FirstOrDefaultAsync(m => m.PublicId == publicId, cancellationToken);

    public Task<int> CountActiveAsync(CancellationToken cancellationToken) =>
        db.Members.CountAsync(m => m.Status == MemberStatus.Ativo, cancellationToken);

    public async Task<IReadOnlySet<string>> ListEmailsAsync(CancellationToken cancellationToken)
    {
        var emails = await db.Members
            .AsNoTracking()
            .Where(m => m.Email != null)
            .Select(m => m.Email!)
            .ToListAsync(cancellationToken);

        return emails.Select(e => e.ToLowerInvariant()).ToHashSet();
    }

    public void Add(Member member) => db.Members.Add(member);

    private static int CalcularIdade(DateOnly nascimento, DateOnly referencia)
    {
        int idade = referencia.Year - nascimento.Year;
        return referencia < nascimento.AddYears(idade) ? idade - 1 : idade;
    }

    /// <summary>
    /// Remove acentos do termo de busca, no lado do cliente.
    /// </summary>
    /// <remarks>
    /// O nome guardado é normalizado pelo PostgreSQL; o termo digitado precisa
    /// receber o mesmo tratamento antes de virar parâmetro. Fazer isto em .NET
    /// evita uma chamada de função por linha do lado do banco.
    /// <para>
    /// A decomposição em <c>FormD</c> separa a letra do sinal diacrítico, que então
    /// é descartado por categoria Unicode. Funciona para todo o alfabeto latino, e
    /// não apenas para a lista de acentos do português — que é o que uma tabela de
    /// substituição escrita à mão costuma cobrir.
    /// </para>
    /// </remarks>
    /// <summary>
    /// Filtra por lacuna do cadastro.
    /// </summary>
    /// <remarks>
    /// <b>Vazio conta como ausente, não só nulo.</b> Uma importação de planilha
    /// que traga a célula em branco grava string vazia, e um filtro que olhasse
    /// só `IS NULL` diria que a pessoa tem telefone — deixando de fora
    /// exatamente quem a secretaria precisa cobrar.
    /// </remarks>
    private static IQueryable<Member> AplicarLacuna(IQueryable<Member> source, MemberGap? gap) =>
        gap switch
        {
            MemberGap.SemTelefone => source.Where(m => m.Phone == null || m.Phone == ""),
            MemberGap.SemEmail => source.Where(m => m.Email == null || m.Email == ""),
            MemberGap.Any => source.Where(m =>
                m.Phone == null || m.Phone == "" || m.Email == null || m.Email == ""),
            _ => source,
        };

    public async Task<MemberSummary> GetSummaryAsync(
        MemberStatus? status,
        int birthdayMonth,
        CancellationToken cancellationToken)
    {
        IQueryable<Member> source = db.Members.AsNoTracking();

        if (status is { } filtro)
        {
            source = source.Where(m => m.Status == filtro);
        }

        // Uma passada só. Cinco `CountAsync` separados seriam cinco varreduras
        // da mesma tabela para desenhar uma linha de chips.
        //
        // O EF traduz este `GroupBy` constante para um SELECT de agregados sem
        // GROUP BY — é o idioma que ele reconhece para "agregue a tabela
        // inteira". Escrever cinco `Count(x => ...)` soltos não teria o mesmo
        // efeito: cada um vira sua própria consulta.
        var agregado = await source
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Total = g.Count(),
                Aniversariantes = g.Count(m => m.BirthDate != null && m.BirthDate.Value.Month == birthdayMonth),
                SemTelefone = g.Count(m => m.Phone == null || m.Phone == ""),
                SemEmail = g.Count(m => m.Email == null || m.Email == ""),
                Incompletos = g.Count(m =>
                    m.Phone == null || m.Phone == "" || m.Email == null || m.Email == ""),
            })
            .FirstOrDefaultAsync(cancellationToken);

        // Igreja sem membro nenhum: o `GroupBy` não produz linha, e sem este
        // caminho o resumo viria nulo em vez de cinco zeros.
        return agregado is null
            ? new MemberSummary
            {
                Total = 0,
                BirthdayThisMonth = 0,
                Incomplete = 0,
                WithoutPhone = 0,
                WithoutEmail = 0,
            }
            : new MemberSummary
            {
                Total = agregado.Total,
                BirthdayThisMonth = agregado.Aniversariantes,
                Incomplete = agregado.Incompletos,
                WithoutPhone = agregado.SemTelefone,
                WithoutEmail = agregado.SemEmail,
            };
    }
}
