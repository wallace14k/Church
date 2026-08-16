using Congrega.Domain.Congregation;

namespace Congrega.Domain.UnitTests;

public sealed class MemberTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 15, 12, 0, 0, TimeSpan.Zero);

    private static Member Register() =>
        Member.Register(tenantId: 1, fullName: "  Maria   Aparecida  ", now: Now, email: "MARIA@Igreja.com");

    [Fact]
    public void Register_normaliza_espacos_do_nome_sem_mudar_a_grafia()
    {
        var membro = Register();

        // Colapsa espaços duplicados, mas não faz Title Case: "d'Ávila" e
        // "MEIRELLES" são grafias legítimas que uma "correção" desrespeitaria.
        Assert.Equal("Maria Aparecida", membro.FullName);
    }

    [Fact]
    public void Register_normaliza_email_para_minusculo()
    {
        var membro = Register();
        Assert.Equal("maria@igreja.com", membro.Email);
    }

    [Fact]
    public void Register_recusa_data_de_nascimento_futura()
    {
        var futura = DateOnly.FromDateTime(Now.UtcDateTime).AddDays(1);

        // Data futura é erro de digitação, quase sempre ano trocado. Barrar aqui
        // evita relatório de aniversariantes com gente que nasceu no futuro.
        Assert.Throws<ArgumentException>(() =>
            Member.Register(tenantId: 1, fullName: "Alguém", now: Now, birthDate: futura));
    }

    [Fact]
    public void UpdateProfile_atualiza_nome_contato_e_nascimento_juntos()
    {
        var membro = Register();
        var nascimento = new DateOnly(1990, 5, 20);
        var endereco = new Address { City = "Recife", State = "PE" };

        membro.UpdateProfile("João Silva", "joao@igreja.com", "81999998888", nascimento, endereco, Now);

        Assert.Equal("João Silva", membro.FullName);
        Assert.Equal("joao@igreja.com", membro.Email);
        Assert.Equal("81999998888", membro.Phone);
        Assert.Equal(nascimento, membro.BirthDate);
        Assert.Equal("Recife", membro.Address.City);
    }

    [Fact]
    public void UpdateProfile_recusa_nome_vazio()
    {
        var membro = Register();

        Assert.Throws<ArgumentException>(() =>
            membro.UpdateProfile("   ", null, null, null, Address.Empty, Now));
    }

    [Fact]
    public void UpdateProfile_recusa_data_de_nascimento_futura()
    {
        var membro = Register();
        var futura = DateOnly.FromDateTime(Now.UtcDateTime).AddDays(1);

        // A mesma checagem de Register: editar não pode abrir uma porta que
        // cadastrar fecha.
        Assert.Throws<ArgumentException>(() =>
            membro.UpdateProfile("Maria Aparecida", null, null, futura, Address.Empty, Now));
    }

    [Fact]
    public void UpdateProfile_aceita_email_e_telefone_nulos_para_limpar_o_campo()
    {
        var membro = Register();

        membro.UpdateProfile("Maria Aparecida", null, null, null, Address.Empty, Now);

        Assert.Null(membro.Email);
        Assert.Null(membro.Phone);
    }

    [Fact]
    public void ChangeStatus_move_o_membro_para_inativo()
    {
        var membro = Register();
        Assert.Equal(MemberStatus.Ativo, membro.Status);

        membro.ChangeStatus(MemberStatus.Inativo, Now);

        Assert.Equal(MemberStatus.Inativo, membro.Status);
    }
}
