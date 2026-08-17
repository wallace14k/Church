using Congrega.Domain.Congregation;

namespace Congrega.Domain.UnitTests;

public sealed class FamilyTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 15, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Register_normaliza_espacos_do_nome()
    {
        var familia = Family.Register(tenantId: 1, name: "  Família   Silva  ", now: Now);
        Assert.Equal("Família Silva", familia.Name);
    }

    [Fact]
    public void Register_recusa_nome_vazio()
    {
        Assert.Throws<ArgumentException>(() => Family.Register(tenantId: 1, name: "   ", now: Now));
    }

    [Fact]
    public void Register_recusa_tenant_invalido()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Family.Register(tenantId: 0, name: "Silva", now: Now));
    }

    [Fact]
    public void Rename_troca_o_nome()
    {
        var familia = Family.Register(tenantId: 1, name: "Silva", now: Now);
        familia.Rename("Silva Oliveira", Now);
        Assert.Equal("Silva Oliveira", familia.Name);
    }

    [Fact]
    public void Rename_recusa_nome_vazio()
    {
        var familia = Family.Register(tenantId: 1, name: "Silva", now: Now);
        Assert.Throws<ArgumentException>(() => familia.Rename(" ", Now));
    }
}
