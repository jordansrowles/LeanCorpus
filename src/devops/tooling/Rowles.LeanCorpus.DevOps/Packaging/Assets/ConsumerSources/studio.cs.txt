using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Rowles.LeanCorpus.Studio;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://127.0.0.1:0");
builder.Services.AddLeanCorpusStudio();
var app = builder.Build();
app.MapLeanCorpusStudio();
try
{
    await app.StartAsync();
    string address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()?.Addresses.Single()
        ?? throw new InvalidOperationException("Studio consumer did not bind an endpoint.");
    using var client = new HttpClient();
    string html = await client.GetStringAsync(new Uri(new Uri(address), "/studio"));
    if (!html.Contains("LeanCorpus Studio", StringComparison.Ordinal))
        throw new InvalidDataException("The packaged Studio endpoint did not return its page.");
    await app.StopAsync();
}
finally { await app.DisposeAsync(); }
Console.WriteLine("STUDIO package host passed.");