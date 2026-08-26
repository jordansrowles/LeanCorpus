using Rowles.LeanCorpus.Server.Core.Configuration;
using Rowles.LeanCorpus.Server.Core.Services;

namespace Rowles.LeanCorpus.Server.Core.Tests;

internal sealed class LocalServerCoreScope(LocalServerCore server) : IAsyncDisposable
{
    internal LocalServerCore Server { get; } = server;

    internal static async ValueTask<LocalServerCoreScope> OpenAsync(ServerCoreOptions options) => new(await LocalServerCore.OpenAsync(options));

    public ValueTask DisposeAsync()
    {
        Server.Dispose();
        return ValueTask.CompletedTask;
    }
}
