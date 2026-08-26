using Grpc.Net.Client;
using System.Net;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Rowles.LeanCorpus.Server.AspNetCore;
using Rowles.LeanCorpus.Server.AspNetCore.DependencyInjection;
using Rowles.LeanCorpus.Server.Grpc;
using Rowles.LeanCorpus.Studio;

namespace Rowles.LeanCorpus.Server.Integration.Tests;

internal sealed class ServerHostScope : IAsyncDisposable
{
    private readonly WebApplication _application;
    private readonly bool _deleteRoot;

    private ServerHostScope(WebApplication application, string dataRoot, Uri address, bool deleteRoot)
    {
        _application = application;
        DataRoot = dataRoot;
        Address = address;
        _deleteRoot = deleteRoot;
    }

    internal string DataRoot { get; }

    internal Uri Address { get; }

    internal HttpClient CreateHttpClient() => new() { BaseAddress = Address };

    internal GrpcChannel CreateGrpcChannel() => GrpcChannel.ForAddress(Address);

    internal static async ValueTask<ServerHostScope> StartAsync(
        HttpProtocols protocols,
        string? dataRoot = null,
        bool deleteRoot = true)
    {
        string root = dataRoot ?? Path.Combine(Path.GetTempPath(), $"lean-corpus-server-integration-{Guid.NewGuid():N}");
        WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development,
            ContentRootPath = Directory.GetCurrentDirectory()
        });
        builder.WebHost.UseStaticWebAssets();
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0, listener => listener.Protocols = protocols));
        builder.Services
            .AddLeanCorpusServerCore(options =>
            {
                options.DataRoot = root;
                options.MaximumBulkOperations = 100;
                options.MaximumSearchResults = 100;
            })
            .AddLeanCorpusServerAspNetCore()
            .AddLeanCorpusStudio();
        builder.Services.AddGrpc();

        WebApplication application = builder.Build();
        application.UseStaticFiles();
        application.MapLeanCorpusServerEndpoints();
        application.MapLeanCorpusServerGrpc();
        application.MapLeanCorpusStudio();
        await application.StartAsync();

        IServer server = application.Services.GetRequiredService<IServer>();
        string address = server.Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        return new ServerHostScope(application, root, new Uri(address), deleteRoot);
    }

    public async ValueTask DisposeAsync()
    {
        await _application.StopAsync();
        await _application.DisposeAsync();
        if (_deleteRoot && Directory.Exists(DataRoot))
            Directory.Delete(DataRoot, recursive: true);
    }
}
