using Congrega.Domain.Childcare;

namespace Congrega.Domain.UnitTests;

/// <summary>
/// Check-in infantil — a classe de dado de maior severidade do sistema.
/// </summary>
/// <remarks>
/// Cada teste aqui corresponde a um dos portões que o doc 05 declara
/// inegociáveis por prazo. Nenhum deles é exercitado pelo caminho feliz do
/// balcão, e é exatamente por isso que precisam de teste: o custo de descobrir
/// que o código de retirada não expirava é uma criança entregue a quem não
/// deveria.
/// </remarks>
public sealed class ChildcareTests
{
    private static readonly DateTimeOffset Agora = new(2026, 8, 16, 10, 0, 0, TimeSpan.Zero);
    private static readonly byte[] CodigoCerto = [1, 2, 3, 4];
    private static readonly byte[] CodigoErrado = [9, 9, 9, 9];

    private const long Tenant = 1;
    private const long Crianca = 10;
    private const long Evento = 100;
    private const long Voluntario = 50;
    private const long Responsavel = 70;

    /// <summary>Comparação byte a byte — o papel que o ISecretHasher cumpre em produção.</summary>
    private static bool Comparar(byte[] a, byte[] b) => a.SequenceEqual(b);

    private static ChildCheckIn Aberto(DateTimeOffset? expiraEm = null) => ChildCheckIn.Open(
        tenantId: Tenant,
        childId: Crianca,
        eventId: Evento,
        checkedInBy: Voluntario,
        pickupCodeHash: CodigoCerto,
        codeExpiresAt: expiraEm ?? Agora.AddHours(4),
        idempotencyKey: "tablet-1:abc",
        now: Agora);

    // -------------------------------------------------------------------------
    // Ficha da criança
    // -------------------------------------------------------------------------

    [Fact]
    public void Ficha_nasce_ativa_e_com_identificador_opaco()
    {
        // O public_id é o que vai IMPRESSO na etiqueta. Se fosse o id
        // sequencial, qualquer pessoa na fila do berçário inferiria quantas
        // crianças há e conseguiria endereçar as outras.
        var crianca = Child.Register(Tenant, "Ana Clara", new DateOnly(2020, 3, 15), Agora);

        Assert.True(crianca.IsActive);
        Assert.NotEqual(Guid.Empty, crianca.PublicId);
    }

    [Fact]
    public void Ficha_recusa_nascimento_no_futuro()
    {
        // Erro de digitação de ano. Sem a barreira a idade sai negativa e
        // ninguém percebe — o campo simplesmente fica estranho num relatório.
        Assert.Throws<ArgumentException>(() =>
            Child.Register(Tenant, "Ana", new DateOnly(2030, 1, 1), Agora));
    }

    [Fact]
    public void Idade_conta_aniversario_ja_ocorrido_no_ano()
    {
        var crianca = Child.Register(Tenant, "Ana", new DateOnly(2020, 3, 15), Agora);

        // Em 16/08/2026 já passou o aniversário de março: 6 anos.
        Assert.Equal(6, crianca.AgeOn(new DateOnly(2026, 8, 16)));
        // Em 01/02/2026 ainda não: 5.
        Assert.Equal(5, crianca.AgeOn(new DateOnly(2026, 2, 1)));
    }

    [Fact]
    public void Campos_sensiveis_so_entram_ja_cifrados()
    {
        // O tipo não aceita string nesses campos — o domínio nunca vê alergia em
        // texto claro, então não há como vazá-la num log ou serializador.
        var crianca = Child.Register(Tenant, "Ana", new DateOnly(2020, 3, 15), Agora);

        crianca.UpdateSensitiveData([0xAA, 0xBB], [0xCC], null, Agora);

        Assert.Equal<byte[]>([0xAA, 0xBB], crianca.AllergiesEncrypted!);
        Assert.Null(crianca.PhotoReferenceEncrypted);
    }

    // -------------------------------------------------------------------------
    // Responsáveis
    // -------------------------------------------------------------------------

    [Fact]
    public void Responsavel_pode_existir_sem_autorizacao_de_retirada()
    {
        // O caso que uma coluna `guardian_id` não representaria: acordo de
        // guarda que registra o pai como responsável e não o autoriza a buscar.
        var vinculo = ChildGuardian.Link(Tenant, Crianca, Responsavel, "Pai", canPickup: false, Agora);

        Assert.False(vinculo.CanPickup);
    }

    // -------------------------------------------------------------------------
    // Código de retirada — os portões
    // -------------------------------------------------------------------------

    [Fact]
    public void Codigo_nao_pode_nascer_vencido()
    {
        // Impediria a retirada da criança que acabou de entrar, e o balcão só
        // descobriria na saída, com o responsável esperando.
        Assert.Throws<ArgumentException>(() => Aberto(expiraEm: Agora.AddMinutes(-1)));
    }

    [Fact]
    public void Retirada_com_codigo_certo_por_autorizado_funciona()
    {
        var checkin = Aberto();

        var recusa = checkin.TryPickUp(
            CodigoCerto, Responsavel, isAuthorizedGuardian: true, Comparar, Agora.AddHours(1));

        Assert.Null(recusa);
        Assert.Equal(CheckInStatus.PickedUp, checkin.Status);
        Assert.Equal(Responsavel, checkin.PickedUpByMemberId);
        Assert.Contains(checkin.DomainEvents, e => e is ChildPickedUp);
    }

    [Fact]
    public void Codigo_errado_recusa_e_emite_alerta()
    {
        // "O evento que mais importa detectar em tempo real neste sistema
        // inteiro" (ADR-014). Recusar em silêncio deixaria uma tentativa de
        // levar criança errada indistinguível de um erro de digitação.
        var checkin = Aberto();

        var recusa = checkin.TryPickUp(
            CodigoErrado, Responsavel, isAuthorizedGuardian: true, Comparar, Agora);

        Assert.Equal(PickupRefusal.WrongCode, recusa);
        Assert.Equal(CheckInStatus.Present, checkin.Status);

        var alerta = Assert.Single(checkin.DomainEvents.OfType<ChildPickupRefused>());
        Assert.Equal(PickupRefusal.WrongCode, alerta.Reason);
    }

    [Fact]
    public void Codigo_vencido_recusa_mesmo_estando_correto()
    {
        var checkin = Aberto(expiraEm: Agora.AddHours(2));

        var recusa = checkin.TryPickUp(
            CodigoCerto, Responsavel, isAuthorizedGuardian: true, Comparar, Agora.AddHours(3));

        Assert.Equal(PickupRefusal.CodeExpired, recusa);
        Assert.Equal(CheckInStatus.Present, checkin.Status);
    }

    [Fact]
    public void Nao_autorizado_e_recusado_antes_de_o_codigo_ser_conferido()
    {
        // A ordem importa: se o código fosse conferido primeiro, a resposta
        // viraria oráculo — quem tem o código certo mas não a autorização
        // receberia um erro diferente de quem errou o código, e a diferença
        // ensina qual das duas coisas conseguiu.
        var checkin = Aberto();

        var recusa = checkin.TryPickUp(
            CodigoCerto, Responsavel, isAuthorizedGuardian: false, Comparar, Agora);

        Assert.Equal(PickupRefusal.NotAuthorized, recusa);
    }

    [Fact]
    public void Codigo_e_de_uso_unico()
    {
        // A segunda apresentação do mesmo código, mesmo por quem é autorizado,
        // encontra o check-in já encerrado.
        var checkin = Aberto();
        checkin.TryPickUp(CodigoCerto, Responsavel, isAuthorizedGuardian: true, Comparar, Agora);

        var segunda = checkin.TryPickUp(
            CodigoCerto, Responsavel, isAuthorizedGuardian: true, Comparar, Agora);

        Assert.Equal(PickupRefusal.AlreadyClosed, segunda);
        Assert.Contains(checkin.DomainEvents, e => e is ChildPickupRefused);
    }

    [Fact]
    public void Expirar_tira_da_lista_de_presentes_e_e_idempotente()
    {
        var checkin = Aberto();

        Assert.True(checkin.Expire(Agora.AddHours(6)));
        Assert.Equal(CheckInStatus.Expired, checkin.Status);
        Assert.False(checkin.Expire(Agora.AddHours(7)));
    }

    [Fact]
    public void Retirado_nao_pode_ser_expirado_depois()
    {
        // Um job de limpeza rodando sobre uma criança já retirada não pode
        // reescrever o desfecho — o histórico diria que ela nunca foi buscada.
        var checkin = Aberto();
        checkin.TryPickUp(CodigoCerto, Responsavel, isAuthorizedGuardian: true, Comparar, Agora);

        Assert.False(checkin.Expire(Agora.AddHours(6)));
        Assert.Equal(CheckInStatus.PickedUp, checkin.Status);
    }

    // -------------------------------------------------------------------------
    // Consentimento parental (Art. 14)
    // -------------------------------------------------------------------------

    [Fact]
    public void Consentimento_registra_a_versao_do_texto()
    {
        // Sem a versão é impossível demonstrar depois A QUE a pessoa consentiu,
        // e o registro perde o valor jurídico que é a razão de existir.
        var consentimento = ParentalConsent.Grant(
            Tenant, Crianca, Responsavel, "checkin-v1-2026-08", Agora, grantedIp: "203.0.113.7");

        Assert.Equal("checkin-v1-2026-08", consentimento.ConsentVersion);
        Assert.True(consentimento.IsActiveOn(Agora));
    }

    [Fact]
    public void Consentimento_exige_versao()
    {
        Assert.Throws<ArgumentException>(() =>
            ParentalConsent.Grant(Tenant, Crianca, Responsavel, "  ", Agora));
    }

    [Fact]
    public void Revogar_nao_apaga_e_e_idempotente()
    {
        // A prova de que houve consentimento no passado é o que protege o
        // tratamento já feito sob ele.
        var consentimento = ParentalConsent.Grant(Tenant, Crianca, Responsavel, "v1", Agora);

        Assert.True(consentimento.Revoke(Agora.AddDays(1)));
        Assert.False(consentimento.Revoke(Agora.AddDays(2)));
        Assert.Equal(Agora.AddDays(1), consentimento.RevokedAt);
        Assert.False(consentimento.IsActiveOn(Agora.AddDays(3)));

        // Continua válido para o passado — é isso que sustenta o já ocorrido.
        Assert.True(consentimento.IsActiveOn(Agora));
    }
}
