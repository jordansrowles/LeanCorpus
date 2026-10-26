using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OrchardCore.Modules;
using Rowles.LeanCorpus.OrchardCore.Search;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ApplicationName = typeof(Startup).Assembly.GetName().Name,
    ContentRootPath = Environment.CurrentDirectory,
    EnvironmentName = "Development"
});
builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
{
    ["OrchardCore_AutoSetup:AutoSetupPath"] = string.Empty,
    ["OrchardCore_AutoSetup:Tenants:0:ShellName"] = "Default",
    ["OrchardCore_AutoSetup:Tenants:0:SiteName"] = "LeanCorpus package consumer",
    ["OrchardCore_AutoSetup:Tenants:0:SiteTimeZone"] = "UTC",
    ["OrchardCore_AutoSetup:Tenants:0:AdminUsername"] = "admin",
    ["OrchardCore_AutoSetup:Tenants:0:AdminEmail"] = "admin@example.test",
    ["OrchardCore_AutoSetup:Tenants:0:AdminPassword"] = "ConsumerSmokePassword!39",
    ["OrchardCore_AutoSetup:Tenants:0:DatabaseProvider"] = "Sqlite",
    ["OrchardCore_AutoSetup:Tenants:0:DatabaseConnectionString"] = string.Empty,
    ["OrchardCore_AutoSetup:Tenants:0:DatabaseTablePrefix"] = string.Empty,
    ["OrchardCore_AutoSetup:Tenants:0:RecipeName"] = "LeanCorpusPackageConsumer"
});
builder.WebHost.UseUrls("http://127.0.0.1:0");
builder.Logging.SetMinimumLevel(LogLevel.Warning);
builder.Services.AddOrchardCms(orchard => orchard.AddSetupFeatures(
    "OrchardCore.AutoSetup", "Rowles.LeanCorpus.OrchardCore.Search"));
var app = builder.Build();
app.UseOrchardCore();
await app.StartAsync();
await app.StopAsync();
await app.DisposeAsync();
Console.WriteLine("ORCHARD module bootstrap passed.");