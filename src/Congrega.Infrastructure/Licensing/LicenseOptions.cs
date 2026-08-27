using System.ComponentModel.DataAnnotations;

namespace Congrega.Infrastructure.Licensing;

/// <summary>
/// Licença desta instalação, no modelo self-hosted.
/// </summary>
/// <remarks>
/// <para>
/// Preenchido pelo <b>cliente</b>, no <c>.env</c> que acompanha o
/// <c>docker compose</c>. Ver <c>.env.example</c> na raiz e
/// <c>docs/08-self-hosted-e-licenciamento.md</c>.
/// </para>
/// <para>
/// <b>Sem <c>[Required]</c> na chave, e a ausência é decisão.</b> Uma igreja
/// pode rodar o ChMS inteiro — membros, agenda, financeiro — sem nunca assinar o
/// Congrega+. Exigir a licença no startup transformaria o produto pago em
/// pré-requisito do produto que a igreja já hospeda no servidor dela.
/// </para>
/// </remarks>
public sealed class LicenseOptions
{
    public const string SectionName = "Licensing";

    /// <summary>
    /// Chave desta instalação. Vazio = instalação sem Congrega+.
    /// </summary>
    /// <remarks>
    /// Vai no cabeçalho das chamadas ao servidor central, e <b>nunca</b> aparece
    /// em log — ver <see cref="LicenseKeyHandler"/>.
    /// </remarks>
    public string Key { get; init; } = string.Empty;

    /// <summary>Base do servidor central de licenças.</summary>
    [Required]
    public string CentralUrl { get; init; } = "https://licenca.congrega.app";

    /// <summary>
    /// Chave PÚBLICA que verifica o veredito assinado do servidor central.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Pública, e não compartilhada: o cliente consegue <b>verificar</b> um
    /// veredito e não consegue <b>forjar</b> um. Se fosse um segredo simétrico,
    /// qualquer cliente com acesso ao próprio <c>.env</c> assinaria uma licença
    /// vitalícia para si mesmo em cinco minutos.
    /// </para>
    /// <para>
    /// Vai embutida na imagem com um padrão; o campo existe para rotação sem
    /// republicar imagem.
    /// </para>
    /// </remarks>
    public string PublicKeyPem { get; init; } = string.Empty;

    /// <summary>
    /// De quanto em quanto tempo perguntar de novo ao servidor central.
    /// </summary>
    /// <remarks>
    /// Seis horas: quatro perguntas por dia por instalação. Com mil igrejas são
    /// quatro mil requisições diárias — nada. Um minuto daria 1,4 milhão, e é
    /// exatamente o que derruba o servidor central de graça.
    /// </remarks>
    public TimeSpan RefreshInterval { get; init; } = TimeSpan.FromHours(6);

    /// <summary>
    /// Por quanto tempo a última resposta boa continua valendo se o servidor
    /// central estiver inacessível.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>É a diferença entre uma indisponibilidade nossa e mil igrejas sem
    /// culto de domingo.</b> Sem janela de tolerância, o nosso servidor vira
    /// ponto único de falha de todas as instalações do mundo — e uma queda de
    /// duas horas num domingo de manhã seria um incidente de produto, não de
    /// infraestrutura.
    /// </para>
    /// <para>
    /// Sete dias é o outro lado da moeda, e precisa ser dito: <b>revogar uma
    /// licença leva até sete dias para surtir efeito</b> numa instalação que
    /// perdeu contato. É o preço aceito para não ser SPOF.
    /// </para>
    /// </remarks>
    public TimeSpan GraceWindow { get; init; } = TimeSpan.FromDays(7);

    /// <summary>Quanto esperar pelo servidor central antes de desistir da tentativa.</summary>
    /// <remarks>
    /// Curto de propósito: a validação acontece no caminho de uma requisição do
    /// membro, e ninguém deve esperar meio minuto para descobrir que o servidor
    /// de licenças está lento. Expirar cai na tolerância, que é o caminho certo.
    /// </remarks>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(8);
}
