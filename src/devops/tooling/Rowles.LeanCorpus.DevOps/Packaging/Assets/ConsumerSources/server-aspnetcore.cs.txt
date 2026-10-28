using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Rowles.LeanCorpus.Server.AspNetCore.DependencyInjection;
using Rowles.LeanCorpus.Server.Core.Services;

string dataRoot = Path.Combine(Environment.CurrentDirectory, "server-aspnetcore-data");
var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://127.0.0.1:0");
builder.Services.AddLeanCorpusServerCore(options => options.DataRoot = dataRoot)
    .AddLeanCorpusServerAspNetCore();
var app = builder.Build();
_ = app.Services.GetRequiredService<LocalServerCore>();
app.MapGet("/consumer-smoke", () => Results.Ok("ready"));
try
{
    await app.StartAsync();
    if (!app.Urls.Any(url => url.StartsWith("http://127.0.0.1:", StringComparison.Ordinal)))
        throw new InvalidOperationException("ASP.NET Core host did not bind its ephemeral endpoint.");
    await app.StopAsync();
}
finally { await app.DisposeAsync(); }
if (Directory.Exists(dataRoot)) Directory.Delete(dataRoot, recursive: true);
if (Directory.Exists(dataRoot)) throw new IOException("ASP.NET Core consumer data cleanup failed.");
Console.WriteLine("SERVER ASPNETCORE start/stop passed.");