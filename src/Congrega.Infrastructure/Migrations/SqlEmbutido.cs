using System.Reflection;

namespace Congrega.Infrastructure.Migrations;

/// <summary>
/// Lê os scripts DDL embutidos no assembly.
/// </summary>
/// <remarks>
/// Os arquivos em <c>db/</c> continuam sendo a fonte legível e revisável do
/// schema; o assembly carrega uma cópia para que aplicar migrations não dependa
/// do sistema de arquivos do servidor.
/// </remarks>
internal static class SqlEmbutido
{
    public static string Ler(string nomeLogico)
    {
        var assembly = typeof(SqlEmbutido).Assembly;

        using var fluxo = assembly.GetManifestResourceStream(nomeLogico)
            ?? throw new InvalidOperationException(
                $"Script '{nomeLogico}' não está embutido em {assembly.GetName().Name}. "
                + $"Disponíveis: {string.Join(", ", assembly.GetManifestResourceNames())}");

        using var leitor = new StreamReader(fluxo);
        return leitor.ReadToEnd();
    }
}
