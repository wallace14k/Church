using Congrega.Infrastructure;
using Microsoft.Extensions.Hosting;

namespace Congrega.Workers;

/// <summary>
/// <see cref="IHostEnvironmentAccessor"/> sobre o <see cref="IHostEnvironment"/> do
/// generic host.
/// </summary>
/// <remarks>
/// A implementação da API embrulha <c>IWebHostEnvironment</c>, que só existe em
/// host web. O Workers é um <c>Host.CreateApplicationBuilder</c> comum — esta
/// classe existe só por causa dessa diferença de tipo entre os dois hosts.
/// </remarks>
internal sealed class WorkersHostEnvironmentAccessor(IHostEnvironment environment) : IHostEnvironmentAccessor
{
    public bool IsDevelopment => environment.IsDevelopment();
}
