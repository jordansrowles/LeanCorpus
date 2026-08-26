using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.RequestDecompression;
using Microsoft.AspNetCore.ResponseCompression;
using Rowles.LeanCorpus.Server.AspNetCore;
using Rowles.LeanCorpus.Server.AspNetCore.DependencyInjection;
using Rowles.LeanCorpus.Server.Grpc;
using Rowles.LeanCorpus.Studio;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
string dataRoot = builder.Configuration["LeanCorpus:DataRoot"]
    ?? Path.Combine(builder.Environment.ContentRootPath, "data");

builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = builder.Configuration.GetValue<long?>("LeanCorpus:MaximumRequestBodyBytes") ?? 104_857_600;
});
string[] configuredListeners = builder.Configuration.GetSection("LeanCorpus:Listeners").Get<string[]>()
    ?? ["http://127.0.0.1:5080", "http://[::1]:5080"];
if (string.IsNullOrWhiteSpace(builder.Configuration["urls"]))
{
    builder.WebHost.UseUrls(configuredListeners);
}

builder.Services
    .AddLeanCorpusServerCore(options =>
    {
        options.DataRoot = dataRoot;
        options.MaximumBulkOperations = builder.Configuration.GetValue("LeanCorpus:MaximumBulkOperations", options.MaximumBulkOperations);
        options.MaximumSearchResults = builder.Configuration.GetValue("LeanCorpus:MaximumSearchResults", options.MaximumSearchResults);
        options.MaximumDocumentBytes = builder.Configuration.GetValue("LeanCorpus:MaximumDocumentBytes", options.MaximumDocumentBytes);
        options.MaximumQueryDepth = builder.Configuration.GetValue("LeanCorpus:MaximumQueryDepth", options.MaximumQueryDepth);
        options.MaximumBooleanClauses = builder.Configuration.GetValue("LeanCorpus:MaximumBooleanClauses", options.MaximumBooleanClauses);
        options.MaximumWildcardExpansions = builder.Configuration.GetValue("LeanCorpus:MaximumWildcardExpansions", options.MaximumWildcardExpansions);
        options.MaximumRegexpComplexity = builder.Configuration.GetValue("LeanCorpus:MaximumRegexpComplexity", options.MaximumRegexpComplexity);
        options.MaximumInspectionItems = builder.Configuration.GetValue("LeanCorpus:MaximumInspectionItems", options.MaximumInspectionItems);
        options.MaximumInspectionValueLength = builder.Configuration.GetValue("LeanCorpus:MaximumInspectionValueLength", options.MaximumInspectionValueLength);
        options.MaximumIdempotencyEntries = builder.Configuration.GetValue("LeanCorpus:MaximumIdempotencyEntries", options.MaximumIdempotencyEntries);
        options.MaximumUncommittedOperations = builder.Configuration.GetValue("LeanCorpus:MaximumUncommittedOperations", options.MaximumUncommittedOperations);
        options.MaximumConsistencyWait = builder.Configuration.GetValue("LeanCorpus:MaximumConsistencyWait", options.MaximumConsistencyWait);
        options.CommitInterval = builder.Configuration.GetValue("LeanCorpus:CommitInterval", options.CommitInterval);
        options.RefreshInterval = builder.Configuration.GetValue("LeanCorpus:RefreshInterval", options.RefreshInterval);
        options.ShutdownTimeout = builder.Configuration.GetValue("LeanCorpus:ShutdownTimeout", options.ShutdownTimeout);
    })
    .AddLeanCorpusServerAspNetCore()
    .AddLeanCorpusStudio();
builder.Services.AddGrpc();
builder.Services.AddRequestDecompression();
builder.Services.AddResponseCompression(options =>
    options.Providers.Add<ZstandardCompressionProvider>());

WebApplication application = builder.Build();
string[] effectiveListeners = builder.Configuration["urls"]?
    .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
    ?? configuredListeners;
foreach (string listener in effectiveListeners)
{
    string host = Uri.TryCreate(listener, UriKind.Absolute, out Uri? uri)
        ? uri.Host.Trim('[', ']')
        : string.Empty;
    if (uri is not null
        && host is not ("localhost" or "127.0.0.1" or "::1")
        && !(IPAddress.TryParse(host, out IPAddress? address) && IPAddress.IsLoopback(address)))
    {
        application.Logger.LogWarning("LeanCorpus Server is listening on {Listener}; bind only to trusted networks and configure authentication before exposing it.", listener);
    }
}
application.UseExceptionHandler();
application.UseRequestDecompression();
application.UseResponseCompression();
application.UseStaticFiles();
application.Use(async (context, next) =>
{
    context.Response.Headers["X-Api-Version"] = "1";
    await next(context);
});
application.MapLeanCorpusServerEndpoints();
application.MapLeanCorpusServerGrpc();
application.MapLeanCorpusStudio();
application.Run();

/// <summary>Exposes the reference host entry point to integration tests.</summary>
public partial class Program;
