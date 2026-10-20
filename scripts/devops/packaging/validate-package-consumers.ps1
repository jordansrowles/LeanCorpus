$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

. (Join-Path $PSScriptRoot 'validate-package-archive.ps1')
. (Join-Path $PSScriptRoot 'validate-compression-package-archive.ps1')

function Get-ConsumerPackageManifest {
    param([string]$PackageDirectory)

    $manifestPath = Join-Path $PackageDirectory 'manifest.json'
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
        throw "Package manifest is missing: $manifestPath"
    }
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json -AsHashtable
    if ($manifest.schemaVersion -ne 1 -or [string]::IsNullOrWhiteSpace([string]$manifest.sourceCommit)) {
        throw "Package manifest has an unsupported schema or missing sourceCommit: $manifestPath"
    }

    $entries = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($package in @($manifest.packages | Where-Object { [string]$_.path -match '\.nupkg$' })) {
        $id = [string]$package.packageId
        if ([string]::IsNullOrWhiteSpace($id) -or $entries.ContainsKey($id)) {
            throw "Package manifest has an empty or duplicate package ID '$id'."
        }
        $path = Join-Path $PackageDirectory ([string]$package.path)
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Manifest package archive is missing: $path" }
        $actualHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $path).Hash.ToLowerInvariant()
        if ($actualHash -cne ([string]$package.sha256).ToLowerInvariant()) {
            throw "Package manifest SHA-256 does not match $id $($package.version)."
        }
        $entries.Add($id, [pscustomobject]@{
            packageId = $id
            version = [string]$package.version
            path = $path
            relativePath = [string]$package.path
            sha256 = ([string]$package.sha256).ToLowerInvariant()
        })
    }
    return [pscustomobject]@{ Manifest = $manifest; Entries = $entries }
}

function Get-ConsumerPackageEntry {
    param(
        [Parameter(Mandatory = $true)][object]$PackageIndex,
        [Parameter(Mandatory = $true)][string]$PackageId
    )
    if (-not $PackageIndex.Entries.ContainsKey($PackageId)) {
        throw "Package '$PackageId' is absent from the generated package manifest."
    }
    return $PackageIndex.Entries[$PackageId]
}

function Get-ConsumerPackageSources {
    $sources = @(& dotnet nuget list source --format short 2>&1 | ForEach-Object {
        if ([string]$_ -match '^E\s+(.+)$') { $Matches[1].Trim() }
    })
    if ($LASTEXITCODE -ne 0 -or $sources.Count -eq 0) {
        throw 'Cannot discover enabled NuGet feeds for isolated consumers.'
    }
    return $sources
}

function Write-ConsumerNuGetConfig {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$PackageDirectory,
        [Parameter(Mandatory = $true)][string[]]$ExternalSources,
        [switch]$AllowExternalLeanCorpus
    )

    $local = [Security.SecurityElement]::Escape([IO.Path]::GetFullPath($PackageDirectory))
    $feedNodes = [Collections.Generic.List[string]]::new()
    $mappingNodes = [Collections.Generic.List[string]]::new()
    $feedNodes.Add("<add key=`"local`" value=`"$local`" />")
    $mappingNodes.Add('<packageSource key="local"><package pattern="LeanCorpus*"/><package pattern="Rowles.Text"/><package pattern="Rowles.LeanCorpus.*"/></packageSource>')
    for ($i = 0; $i -lt $ExternalSources.Count; $i++) {
        $key = "feed$i"
        $url = [Security.SecurityElement]::Escape([string]$ExternalSources[$i])
        $feedNodes.Add("<add key=`"$key`" value=`"$url`" />")
        $mappingNodes.Add("<packageSource key=`"$key`"><package pattern=`"*`"/></packageSource>")
        if ($AllowExternalLeanCorpus) {
            $mappingNodes.Add("<packageSource key=`"$key`"><package pattern=`"LeanCorpus`"/></packageSource>")
        }
    }
    $mapping = if ($AllowExternalLeanCorpus) {
        # The negative compression probe must be able to obtain the older Core
        # baseline from a legitimate remote source while all normal restores
        # remain pinned to the local generated feed.
        $localMapping = '<packageSource key="local"><package pattern="LeanCorpus.Compression.*"/><package pattern="LeanCorpus*"/><package pattern="Rowles.Text"/><package pattern="Rowles.LeanCorpus.*"/></packageSource>'
        $externalMappings = for ($i = 0; $i -lt $ExternalSources.Count; $i++) {
            "<packageSource key=`"feed$i`"><package pattern=`"*`"/><package pattern=`"LeanCorpus`"/></packageSource>"
        }
        $localMapping + ($externalMappings -join '')
    } else { $mappingNodes -join '' }
    $xml = "<configuration><packageSources><clear/>$($feedNodes -join '')</packageSources><packageSourceMapping>$mapping</packageSourceMapping></configuration>"
    [IO.File]::WriteAllText($Path, $xml, [Text.UTF8Encoding]::new($false))
}

function Get-ConsumerProjectTemplate {
    return [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'Consumer.csproj.template'))
}

function Get-ConsumerScenarioSource {
    param([Parameter(Mandatory = $true)][string]$Kind)

    switch ($Kind) {
        'core' {
            return @'
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Search.Queries;
using Rowles.LeanCorpus.Search.Searcher;
using Rowles.LeanCorpus.Store;

if (args.Length != 2) throw new ArgumentException("Expected write/read mode and index path.");
string mode = args[0];
string indexPath = args[1];
if (mode == "write")
{
    using var directory = new MMapDirectory(indexPath);
    using var writer = new IndexWriter(directory, new IndexWriterConfig { MergePolicy = NoMergePolicy.Instance });
    for (int i = 1; i <= 3; i++)
    {
        var document = new LeanDocument();
        document.Add(new StringField("id", $"doc-{i}", stored: true));
        document.Add(new StringField("kind", i == 2 ? "selected" : "other", stored: false));
        document.Add(new StoredField("title", $"deterministic title {i}"));
        document.Add(new StoredField("source", "package-consumer"));
        writer.AddDocument(document);
    }
    writer.Commit();
    Console.WriteLine("CORE write passed with three documents.");
}
else if (mode == "read")
{
    using var directory = new MMapDirectory(indexPath);
    using var searcher = new IndexSearcher(directory);
    var hits = searcher.Search(new TermQuery("kind", "selected"), 10);
    if (hits.ScoreDocs.Length != 1) throw new InvalidDataException($"Expected one hit, found {hits.ScoreDocs.Length}.");
    var stored = searcher.GetStoredFields(hits.ScoreDocs[0].DocId);
    if (stored["id"].Single() != "doc-2" ||
        stored["title"].Single() != "deterministic title 2" ||
        stored["source"].Single() != "package-consumer")
        throw new InvalidDataException("Stored values did not match the deterministic document.");
    Console.WriteLine("CORE read passed with one exact hit and expected stored values.");
}
else throw new ArgumentException("Expected write or read mode.");
'@
        }
        'text' {
            return @'
using Rowles.LeanCorpus.Analysis;
using Rowles.LeanCorpus.Analysis.Analysers;

var sink = new MaterialisingSink();
new StandardAnalyser().Analyse("The QUICK fox", sink);
if (!sink.Tokens.SequenceEqual(["quick", "fox"]))
    throw new InvalidDataException($"Unexpected token sequence: {string.Join(",", sink.Tokens)}");
Console.WriteLine("TEXT " + string.Join("|", sink.Tokens));

sealed class MaterialisingSink : ISpanTokenSink
{
    public List<string> Tokens { get; } = [];
    public void Add(ReadOnlySpan<char> text, int startOffset, int endOffset, string type = Token.DefaultType,
        int positionIncrement = 1, byte[]? payload = null) => Tokens.Add(text.ToString());
}
'@
        }
        'sourcegen' {
            return @'
using Rowles.LeanCorpus.Mapping;
using Rowles.LeanCorpus.Mapping.Attributes;

var product = new Product { Id = "package-42", Title = "Generated package map" };
var document = ProductIndex.ToDocument(product);
if (document.Fields.Count < 2 || !ProductIndex.CreateSchema().Mappings.ContainsKey("id"))
    throw new InvalidDataException("The generated document map or schema was not usable.");
if (ProductIndex.Map.DocumentName != "Product")
    throw new InvalidDataException("The generated mapper has the wrong document name.");
Console.WriteLine($"SOURCEGEN {ProductIndex.Map.DocumentName} {ProductIndex.CreateSchema().Mappings.Count}");

[LeanDocument]
public partial class Product
{
    [LeanString("id", Required = true)]
    public required string Id { get; init; }

    [LeanText("title")]
    public string? Title { get; init; }
}
'@
        }
        'abstractions' {
            return @'
using Rowles.LeanCorpus.Server.Abstractions.Services;

if (!typeof(IHealthService).IsInterface || typeof(IHealthService).GetMethods().Length == 0)
    throw new InvalidDataException("The public health service contract is unavailable.");
Console.WriteLine("SERVER ABSTRACTIONS IHealthService");
'@
        }
        'server-core' {
            return @'
using Rowles.LeanCorpus.Server.Abstractions.Services;
using Rowles.LeanCorpus.Server.Core.Configuration;
using Rowles.LeanCorpus.Server.Core.Services;

string dataRoot = Path.Combine(Environment.CurrentDirectory, "server-core-data");
using (LocalServerCore server = await LocalServerCore.OpenAsync(new ServerCoreOptions { DataRoot = dataRoot }))
{
    if (server is not IHealthService) throw new InvalidDataException("Server Core did not expose the health contract.");
}
Directory.Delete(dataRoot, recursive: true);
if (Directory.Exists(dataRoot)) throw new IOException("Server Core consumer data cleanup failed.");
Console.WriteLine("SERVER CORE local service graph passed.");
'@
        }
        'server-aspnetcore' {
            return @'
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
'@
        }
        'grpc' {
            return @'
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Rowles.LeanCorpus.Server.Grpc;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://127.0.0.1:0");
builder.Services.AddGrpc();
var app = builder.Build();
app.MapLeanCorpusServerGrpc();
await app.StartAsync();
await app.StopAsync();
await app.DisposeAsync();
Console.WriteLine("SERVER GRPC integration mapped and started successfully.");
'@
        }
        'studio' {
            return @'
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
'@
        }
        'orchard' {
            return @'
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
'@
        }
        'server-local-launcher' {
            return @'
Console.WriteLine("SERVER LOCAL package dependency launcher built.");
'@
        }
        default { throw "Unknown package consumer source kind '$Kind'." }
    }
}

function Get-ConsumerScenarioDefinitions {
    param([ValidateSet('all', 'compression')][string]$Scope)

    $scenarios = [Collections.Generic.List[object]]::new()
    $frameworks = if ($Scope -eq 'compression') { @('net10.0', 'net11.0') } else { @('net10.0', 'net11.0') }
    if ($Scope -eq 'all') {
        foreach ($framework in $frameworks) {
            $scenarios.Add([pscustomobject]@{ Name = 'core'; PackageId = 'LeanCorpus'; PackageIds = @('LeanCorpus'); ExactInternalIds = @('LeanCorpus'); Kind = 'core'; Framework = $framework; Sdk = 'Microsoft.NET.Sdk'; ExtraProperties = ''; ExternalReferences = @(); RuntimeKind = 'core' })
        }
        foreach ($codec in @('LZ4', 'Snappy', 'Zstandard')) {
            $id = "LeanCorpus.Compression.$codec"
            foreach ($framework in $frameworks) {
                $scenarios.Add([pscustomobject]@{ Name = "compression-$codec"; PackageId = $id; PackageIds = @('LeanCorpus', $id); ExactInternalIds = @('LeanCorpus', $id); Kind = "compression-$codec"; Framework = $framework; Sdk = 'Microsoft.NET.Sdk'; ExtraProperties = ''; ExternalReferences = @(); RuntimeKind = 'compression' })
            }
        }
        foreach ($framework in $frameworks) {
            $scenarios.Add([pscustomobject]@{ Name = 'text'; PackageId = 'Rowles.Text'; PackageIds = @('Rowles.Text'); ExactInternalIds = @('Rowles.Text'); Kind = 'text'; Framework = $framework; Sdk = 'Microsoft.NET.Sdk'; ExtraProperties = ''; ExternalReferences = @(); RuntimeKind = 'single' })
            $scenarios.Add([pscustomobject]@{ Name = 'sourcegen'; PackageId = 'LeanCorpus.SourceGen'; PackageIds = @('LeanCorpus', 'LeanCorpus.SourceGen'); ExactInternalIds = @('LeanCorpus', 'LeanCorpus.SourceGen'); Kind = 'sourcegen'; Framework = $framework; Sdk = 'Microsoft.NET.Sdk'; ExtraProperties = ''; ExternalReferences = @(); RuntimeKind = 'single' })
        }

        $server = @(
            @{ Name = 'server-abstractions'; PackageId = 'Rowles.LeanCorpus.Server.Abstractions'; PackageIds = @('Rowles.LeanCorpus.Server.Abstractions'); ExactInternalIds = @('Rowles.LeanCorpus.Server.Abstractions'); Kind = 'abstractions'; Sdk = 'Microsoft.NET.Sdk'; RuntimeKind = 'single'; ExternalReferences = @(); ExtraProperties = '' },
            @{ Name = 'server-core'; PackageId = 'Rowles.LeanCorpus.Server.Core'; PackageIds = @('Rowles.LeanCorpus.Server.Core'); ExactInternalIds = @('Rowles.LeanCorpus.Server.Core', 'Rowles.LeanCorpus.Server.Abstractions', 'LeanCorpus'); Kind = 'server-core'; Sdk = 'Microsoft.NET.Sdk'; RuntimeKind = 'single'; ExternalReferences = @(); ExtraProperties = '' },
            @{ Name = 'server-aspnetcore'; PackageId = 'Rowles.LeanCorpus.Server.AspNetCore'; PackageIds = @('Rowles.LeanCorpus.Server.AspNetCore'); ExactInternalIds = @('Rowles.LeanCorpus.Server.AspNetCore', 'Rowles.LeanCorpus.Server.Core', 'Rowles.LeanCorpus.Server.Abstractions', 'LeanCorpus'); Kind = 'server-aspnetcore'; Sdk = 'Microsoft.NET.Sdk.Web'; RuntimeKind = 'single'; ExternalReferences = @(); ExtraProperties = '' },
            @{ Name = 'server-grpc'; PackageId = 'Rowles.LeanCorpus.Server.Grpc'; PackageIds = @('Rowles.LeanCorpus.Server.Grpc'); ExactInternalIds = @('Rowles.LeanCorpus.Server.Grpc', 'Rowles.LeanCorpus.Server.Abstractions'); Kind = 'grpc'; Sdk = 'Microsoft.NET.Sdk.Web'; RuntimeKind = 'single'; ExternalReferences = @(); ExtraProperties = '' },
            @{ Name = 'studio'; PackageId = 'Rowles.LeanCorpus.Studio'; PackageIds = @('Rowles.LeanCorpus.Studio'); ExactInternalIds = @('Rowles.LeanCorpus.Studio', 'Rowles.LeanCorpus.Server.Abstractions'); Kind = 'studio'; Sdk = 'Microsoft.NET.Sdk.Web'; RuntimeKind = 'single'; ExternalReferences = @(); ExtraProperties = '<AddRazorSupportForMvc>true</AddRazorSupportForMvc>' },
            @{ Name = 'orchard-search'; PackageId = 'Rowles.LeanCorpus.OrchardCore.Search'; PackageIds = @('Rowles.LeanCorpus.OrchardCore.Search'); ExactInternalIds = @('Rowles.LeanCorpus.OrchardCore.Search', 'LeanCorpus'); Kind = 'orchard'; Sdk = 'Microsoft.NET.Sdk.Web'; RuntimeKind = 'single'; ExternalReferences = @('OrchardCore.Application.Cms.Targets'); ExtraProperties = '' },
            @{ Name = 'server-local'; PackageId = 'Rowles.LeanCorpus.Server.Local'; PackageIds = @('Rowles.LeanCorpus.Server.Local'); ExactInternalIds = @(); Kind = 'server-local-launcher'; Sdk = 'Microsoft.NET.Sdk'; RuntimeKind = 'server-local'; ExternalReferences = @(); ExtraProperties = '' }
        )
        foreach ($scenario in $server) {
            foreach ($framework in $frameworks) {
                $scenarios.Add([pscustomobject]@{
                    Name = $scenario.Name; PackageId = $scenario.PackageId; PackageIds = $scenario.PackageIds
                    ExactInternalIds = $scenario.ExactInternalIds; Kind = $scenario.Kind; Framework = $framework
                    Sdk = $scenario.Sdk; RuntimeKind = $scenario.RuntimeKind; ExternalReferences = $scenario.ExternalReferences
                    ExtraProperties = $scenario.ExtraProperties
                })
            }
        }
    } else {
        foreach ($codec in @('LZ4', 'Snappy', 'Zstandard')) {
            $id = "LeanCorpus.Compression.$codec"
            foreach ($framework in $frameworks) {
                $scenarios.Add([pscustomobject]@{ Name = "compression-$codec"; PackageId = $id; PackageIds = @('LeanCorpus', $id); ExactInternalIds = @('LeanCorpus', $id); Kind = "compression-$codec"; Framework = $framework; Sdk = 'Microsoft.NET.Sdk'; ExtraProperties = ''; ExternalReferences = @(); RuntimeKind = 'compression' })
            }
        }
    }
    return $scenarios
}

function Get-CentralConsumerPackageVersion {
    param([string]$RepoRoot, [string]$PackageId)
    $path = Join-Path $RepoRoot 'Directory.Packages.props'
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Central package versions are missing: $path" }
    [xml]$document = Get-Content -LiteralPath $path -Raw
    $node = $document.Project.ItemGroup.PackageVersion | Where-Object { [string]$_.Include -ceq $PackageId } | Select-Object -First 1
    if ($null -eq $node -or [string]::IsNullOrWhiteSpace([string]$node.Version)) {
        throw "No centrally managed external package version is declared for $PackageId."
    }
    return [string]$node.Version
}

function Get-ConsumerPackageReferenceXml {
    param([object]$Scenario, [object]$PackageIndex, [string]$RepoRoot)

    $nodes = [Collections.Generic.List[string]]::new()
    foreach ($id in @($Scenario.PackageIds)) {
        $entry = Get-ConsumerPackageEntry -PackageIndex $PackageIndex -PackageId $id
        if ($id -eq 'LeanCorpus.SourceGen') {
            $nodes.Add("    <PackageReference Include=`"$id`" Version=`"[$($entry.version)]`" OutputItemType=`"Analyzer`" ReferenceOutputAssembly=`"false`" PrivateAssets=`"all`" />")
        } else {
            $nodes.Add("    <PackageReference Include=`"$id`" Version=`"[$($entry.version)]`" />")
        }
    }
    foreach ($id in @($Scenario.ExternalReferences)) {
        $version = Get-CentralConsumerPackageVersion -RepoRoot $RepoRoot -PackageId $id
        $nodes.Add("    <PackageReference Include=`"$id`" Version=`"[$version]`" />")
    }
    return $nodes -join "`n"
}

function Get-ConsumerInternalPackageId {
    param([string]$LibraryKey)
    $id = $LibraryKey.Split('/')[0]
    if ($id -ceq 'LeanCorpus' -or $id -like 'LeanCorpus.*' -or $id -ceq 'Rowles.Text' -or $id -like 'Rowles.LeanCorpus.*') {
        return $id
    }
    return $null
}

function Get-ConsumerCachedArchivePath {
    param([string]$CacheDirectory, [string]$PackageId, [string]$Version)
    $packageRoot = Join-Path $CacheDirectory (Join-Path $PackageId.ToLowerInvariant() $Version.ToLowerInvariant())
    if (-not (Test-Path -LiteralPath $packageRoot -PathType Container)) { throw "Isolated cache does not contain $PackageId ${Version}: $packageRoot" }
    $archive = @(Get-ChildItem -LiteralPath $packageRoot -File -Filter '*.nupkg' | Where-Object {
        $_.Name -ceq "$($PackageId.ToLowerInvariant()).$($Version.ToLowerInvariant()).nupkg"
    } | Select-Object -First 1)
    if ($archive.Count -ne 1) { throw "Isolated cache archive is missing for $PackageId ${Version}: $packageRoot" }
    return $archive[0].FullName
}

function Invoke-ConsumerCommand {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string[]]$Arguments,
        [Parameter(Mandatory = $true)][string]$WorkingDirectory,
        [Parameter(Mandatory = $true)][string]$ScenarioDirectory,
        [TimeSpan]$Timeout = ([TimeSpan]::FromMinutes(5)),
        [scriptblock]$OnProgress
    )
    $stdout = Join-Path $ScenarioDirectory "$Name.stdout.log"
    $stderr = Join-Path $ScenarioDirectory "$Name.stderr.log"
    $parameters = @{
        FileName = 'dotnet'
        Arguments = $Arguments
        WorkingDirectory = $WorkingDirectory
        StdOutPath = $stdout
        StdErrPath = $stderr
        CaptureOutput = $true
        MirrorOutput = $false
        Timeout = $Timeout
        ProgressIntervalSeconds = 1
    }
    if ($null -ne $OnProgress) { $parameters.OnProgress = $OnProgress }
    $result = Invoke-ProcessWithLifecycle @parameters
    if ($result.TimedOut) { throw "$Name timed out; see $stdout and $stderr." }
    return $result
}

function Invoke-PackageConsumerScenario {
    param(
        [Parameter(Mandatory = $true)][object]$Scenario,
        [Parameter(Mandatory = $true)][object]$PackageIndex,
        [Parameter(Mandatory = $true)][string]$PackageDirectory,
        [Parameter(Mandatory = $true)][string]$RepoRoot,
        [Parameter(Mandatory = $true)][string[]]$ExternalSources,
        [Parameter(Mandatory = $true)][string]$ConsumerRoot,
        [Parameter(Mandatory = $true)][string]$CacheRoot,
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][Collections.Generic.List[object]]$Results
    )

    $primary = Get-ConsumerPackageEntry -PackageIndex $PackageIndex -PackageId $Scenario.PackageId
    $directoryName = "$($Scenario.Name)-$($Scenario.Framework)"
    $scenarioDirectory = Join-Path $ConsumerRoot $directoryName
    [void][IO.Directory]::CreateDirectory($scenarioDirectory)
    $cache = Join-Path $CacheRoot $directoryName
    [void][IO.Directory]::CreateDirectory($cache)
    $row = [ordered]@{
        scenario = [string]$Scenario.Name
        packageId = [string]$primary.packageId
        packageVersion = [string]$primary.version
        packageSha256 = [string]$primary.sha256
        targetFramework = [string]$Scenario.Framework
        restore = 'not-run'
        build = 'not-run'
        runtime = 'not-run'
        verifiedPackages = @()
        workingDirectory = $scenarioDirectory
        cacheDirectory = $cache
        error = $null
    }
    try {
        if ($Scenario.Name -like 'compression-*') {
            $expectedCompressionFrameworks = @(Get-ConsumerScenarioDefinitions -Scope compression |
                ForEach-Object { [string]$_.Framework } | Sort-Object -Unique)
            Assert-CompressionPackageDependencyRange -PackageId $Scenario.PackageId `
                -PackagePath $primary.path -ExpectedFrameworks $expectedCompressionFrameworks
        }
        $project = Join-Path $scenarioDirectory 'Consumer.csproj'
        $template = Get-ConsumerProjectTemplate
        $references = Get-ConsumerPackageReferenceXml -Scenario $Scenario -PackageIndex $PackageIndex -RepoRoot $RepoRoot
        $projectXml = $template.Replace('SDK_NAME', [string]$Scenario.Sdk)
        $projectXml = $projectXml.Replace('FRAMEWORK', [string]$Scenario.Framework)
        $projectXml = $projectXml.Replace('EXTRA_PROPERTIES', [string]$Scenario.ExtraProperties)
        $projectXml = $projectXml.Replace('PACKAGE_REFERENCES', $references)
        [IO.File]::WriteAllText($project, $projectXml, [Text.UTF8Encoding]::new($false))
        if ($projectXml -match '<ProjectReference\b') { throw 'Generated consumer must not use ProjectReference.' }
        $source = if ($Scenario.RuntimeKind -eq 'compression') {
            $codec = [string]$Scenario.PackageId.Substring('LeanCorpus.Compression.'.Length)
            $registration = if ($codec -eq 'LZ4') { 'Lz4Compression' } else { "$codec`Compression" }
            $policy = if ($codec -eq 'LZ4') { 'Lz4' } else { $codec }
            $core = Get-ConsumerPackageEntry -PackageIndex $PackageIndex -PackageId 'LeanCorpus'
            $programTemplate = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'Program.cs.template'))
            $coreAssembly = ([version]$core.version).ToString() + '.0'
            $codecAssembly = ([version]$primary.version).ToString() + '.0'
            $program = $programTemplate.Replace('CORE_ASSEMBLY_VERSION', $coreAssembly)
            $program = $program.Replace('OPTIONAL_ASSEMBLY_VERSION', $codecAssembly)
            $program = $program.Replace('CODEC', $codec).Replace('REGISTRATION', $registration).Replace('POLICY', $policy)
            $program
        } else { Get-ConsumerScenarioSource -Kind ([string]$Scenario.Kind) }
        [IO.File]::WriteAllText((Join-Path $scenarioDirectory 'Program.cs'), $source, [Text.UTF8Encoding]::new($false))
        if ($Scenario.Kind -eq 'sourcegen') {
            if ($projectXml -notmatch 'OutputItemType="Analyzer"' -or $projectXml -notmatch 'ReferenceOutputAssembly="false"') {
                throw 'SourceGen consumer must reference the generator as a private analyser dependency.'
            }
        }
        if ($Scenario.Kind -eq 'studio') {
            $pages = Join-Path $scenarioDirectory 'Pages'
            [void][IO.Directory]::CreateDirectory($pages)
            [IO.File]::WriteAllText((Join-Path $pages 'Consumer.cshtml'), "@page `"/razor-smoke`"`n<h1>Razor package consumer</h1>`n", [Text.UTF8Encoding]::new($false))
        }

        $config = Join-Path $scenarioDirectory 'NuGet.Config'
        Write-ConsumerNuGetConfig -Path $config -PackageDirectory $PackageDirectory -ExternalSources $ExternalSources
        $restore = Invoke-ConsumerCommand -Name 'restore' -Arguments @(
            'restore', $project, '--packages', $cache, '--configfile', $config,
            '--disable-build-servers', '-p:NuGetAudit=false'
        ) -WorkingDirectory $scenarioDirectory -ScenarioDirectory $scenarioDirectory
        if ($restore.ExitCode -ne 0) { throw "Consumer restore failed with exit code $($restore.ExitCode); see $($restore.StdOutPath) and $($restore.StdErrPath)." }
        $row.restore = 'passed'

        $assetsPath = Join-Path $scenarioDirectory 'obj/project.assets.json'
        if (-not (Test-Path -LiteralPath $assetsPath -PathType Leaf)) { throw "Restore did not create $assetsPath" }
        $assets = Get-Content -LiteralPath $assetsPath -Raw | ConvertFrom-Json -AsHashtable
        if ([string]$assets.project.restore.projectStyle -cne 'PackageReference') {
            throw "Consumer restore style was not PackageReference in $assetsPath."
        }
        $libraryKeys = @($assets.libraries.Keys)
        $verified = [Collections.Generic.List[object]]::new()
        $internalResolved = [Collections.Generic.Dictionary[string, string]]::new([StringComparer]::OrdinalIgnoreCase)
        foreach ($key in $libraryKeys) {
            $id = Get-ConsumerInternalPackageId -LibraryKey ([string]$key)
            if ($null -eq $id) { continue }
            $version = [string]$key.Split('/')[1]
            if ($internalResolved.ContainsKey($id) -and $internalResolved[$id] -cne $version) {
                throw "Consumer resolved multiple versions for internal package $id."
            }
            $internalResolved[$id] = $version
            $manifestEntry = Get-ConsumerPackageEntry -PackageIndex $PackageIndex -PackageId $id
            if ($manifestEntry.version -cne $version) {
                throw "Consumer resolved $id $version, but the package manifest records $($manifestEntry.version)."
            }
            $cachedArchive = Get-ConsumerCachedArchivePath -CacheDirectory $cache -PackageId $id -Version $version
            $cachedHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $cachedArchive).Hash.ToLowerInvariant()
            if ($cachedHash -cne $manifestEntry.sha256) {
                throw "Consumer cache archive hash does not match the manifest for $id $version."
            }
            $verified.Add([ordered]@{ packageId = $id; version = $version; sha256 = $cachedHash })
        }
        foreach ($id in @($Scenario.PackageIds)) {
            if (-not $internalResolved.ContainsKey([string]$id)) { throw "Consumer assets do not contain required package $id." }
        }
        if (@($Scenario.ExactInternalIds).Count -gt 0) {
            $actualIds = @($internalResolved.Keys | Sort-Object -Culture invariant)
            $expectedIds = @($Scenario.ExactInternalIds | Sort-Object -Culture invariant)
            if (Compare-Object -ReferenceObject $expectedIds -DifferenceObject $actualIds -CaseSensitive) {
                throw "Unexpected internal package graph for $($Scenario.Name): expected [$($expectedIds -join ', ')], found [$($actualIds -join ', ')]."
            }
        }
        if ($Scenario.Name -like 'compression-*' -and ($internalResolved.ContainsKey('Rowles.Text') -or
            @($internalResolved.Keys | Where-Object { $_ -like 'Rowles.LeanCorpus.*' }).Count -gt 0)) {
            throw 'Compression consumer resolved an unexpected Rowles.Text or renamed Core dependency.'
        }
        $row.verifiedPackages = @($verified)

        $build = Invoke-ConsumerCommand -Name 'build' -Arguments @(
            'build', $project, '-c', 'Release', '--no-restore', '--disable-build-servers', '-m:1', '-p:UseSharedCompilation=false'
        ) -WorkingDirectory $scenarioDirectory -ScenarioDirectory $scenarioDirectory
        if ($build.ExitCode -ne 0) { throw "Consumer build failed with exit code $($build.ExitCode); see $($build.StdOutPath) and $($build.StdErrPath)." }
        $row.build = 'passed'
        $consumerDll = Join-Path $scenarioDirectory "bin/Release/$($Scenario.Framework)/Consumer.dll"

        switch ($Scenario.RuntimeKind) {
            'core' {
                $index = Join-Path $scenarioDirectory 'index'
                $write = Invoke-ConsumerCommand -Name 'runtime-write' -Arguments @($consumerDll, 'write', $index) -WorkingDirectory $scenarioDirectory -ScenarioDirectory $scenarioDirectory
                if ($write.ExitCode -ne 0) { throw "Core writer process failed with exit code $($write.ExitCode); see $($write.StdOutPath) and $($write.StdErrPath)." }
                $read = Invoke-ConsumerCommand -Name 'runtime-read' -Arguments @($consumerDll, 'read', $index) -WorkingDirectory $scenarioDirectory -ScenarioDirectory $scenarioDirectory
                if ($read.ExitCode -ne 0) { throw "Core reader process failed with exit code $($read.ExitCode); see $($read.StdOutPath) and $($read.StdErrPath)." }
                if (Test-Path -LiteralPath $index) {
                    [IO.Directory]::Delete($index, $true)
                    if (Test-Path -LiteralPath $index) { throw "Core index cleanup failed for $index." }
                }
                $row.runtime = 'passed-write-read-cleanup'
            }
            'compression' {
                $index = Join-Path $scenarioDirectory 'index'
                foreach ($mode in @('write', 'read')) {
                    $process = Invoke-ConsumerCommand -Name "runtime-$mode" -Arguments @($consumerDll, $mode, $index) -WorkingDirectory $scenarioDirectory -ScenarioDirectory $scenarioDirectory
                    if ($process.ExitCode -ne 0) { throw "Compression $mode process failed with exit code $($process.ExitCode); see $($process.StdOutPath) and $($process.StdErrPath)." }
                }
                [IO.Directory]::Delete($index, $true)
                if (Test-Path -LiteralPath $index) { throw "Compression index cleanup failed for $index." }

                $coreEntry = Get-ConsumerPackageEntry -PackageIndex $PackageIndex -PackageId 'LeanCorpus'
                $negativeConfig = Join-Path $scenarioDirectory 'Negative.NuGet.Config'
                Write-ConsumerNuGetConfig -Path $negativeConfig -PackageDirectory $PackageDirectory -ExternalSources $ExternalSources -AllowExternalLeanCorpus
                $projectXml = Get-Content -LiteralPath $project -Raw
                $negativeXml = $projectXml.Replace("Include=`"LeanCorpus`" Version=`"[$($coreEntry.version)]`"", 'Include="LeanCorpus" Version="[3.1.1]"')
                [IO.File]::WriteAllText($project, $negativeXml, [Text.UTF8Encoding]::new($false))
                $negative = Invoke-ConsumerCommand -Name 'negative-restore' -Arguments @(
                    'restore', $project, '--packages', $cache, '--configfile', $negativeConfig,
                    '--disable-build-servers', '-p:NuGetAudit=false'
                ) -WorkingDirectory $scenarioDirectory -ScenarioDirectory $scenarioDirectory
                $negativeText = ''
                foreach ($log in @($negative.StdOutPath, $negative.StdErrPath)) {
                    if (Test-Path -LiteralPath $log) { $negativeText += [IO.File]::ReadAllText($log) + "`n" }
                }
                if ($negative.ExitCode -eq 0 -or $negativeText -notmatch 'NU1605|NU1107') {
                    throw "Expected the incompatible Core 3.1.1 restore to fail with NU1605/NU1107; see $($negative.StdOutPath) and $($negative.StdErrPath)."
                }
                $row.runtime = 'passed-round-trip-marker-and-rejects-core-3.1.1'
            }
            'server-local' {
                $local = Get-ConsumerPackageEntry -PackageIndex $PackageIndex -PackageId 'Rowles.LeanCorpus.Server.Local'
                $localDirectory = Join-Path $cache (Join-Path $local.packageId.ToLowerInvariant() $local.version.ToLowerInvariant())
                $applicationAssets = Join-Path $localDirectory "lib/$($Scenario.Framework)"
                $localDll = Join-Path $applicationAssets 'Rowles.LeanCorpus.Server.Local.dll'
                if (-not (Test-Path -LiteralPath $localDll -PathType Leaf)) { throw "Server.Local package assembly is missing from the isolated cache: $localDll" }
                $deps = Join-Path $scenarioDirectory "bin/Release/$($Scenario.Framework)/Consumer.deps.json"
                $runtimeConfig = Join-Path $scenarioDirectory "bin/Release/$($Scenario.Framework)/Consumer.runtimeconfig.json"
                $dataRoot = Join-Path $scenarioDirectory 'server-local-data'
                $arguments = @('exec', '--depsfile', $deps, '--runtimeconfig', $runtimeConfig, '--additionalprobingpath', $cache, $localDll,
                    '--urls', 'http://127.0.0.1:0', '--LeanCorpus:DataRoot', $dataRoot)
                $ready = [pscustomobject]@{ value = $false }
                $stdoutPath = Join-Path $scenarioDirectory 'runtime.stdout.log'
                $stderrPath = Join-Path $scenarioDirectory 'runtime.stderr.log'
                $onProgress = {
                    param($process, $elapsed)
                    $output = ''
                    foreach ($path in @($stdoutPath, $stderrPath)) {
                        if (Test-Path -LiteralPath $path) { $output += [IO.File]::ReadAllText($path) }
                    }
                    if ($output -match 'Now listening on: http://127\.0\.0\.1:\d+') {
                        $ready.value = $true
                        try {
                            $process.Kill($true)
                        } catch [System.Management.Automation.MethodException] {
                            $process.Kill()
                        } catch [System.NotSupportedException] {
                            $process.Kill()
                        }
                        [void]$process.WaitForExit(5000)
                    }
                }.GetNewClosure()
                $launch = Invoke-ConsumerCommand -Name 'runtime' -Arguments $arguments -WorkingDirectory $scenarioDirectory -ScenarioDirectory $scenarioDirectory -Timeout ([TimeSpan]::FromSeconds(45)) -OnProgress $onProgress
                $output = ''
                foreach ($path in @($launch.StdOutPath, $launch.StdErrPath)) {
                    if (Test-Path -LiteralPath $path) { $output += [IO.File]::ReadAllText($path) }
                }
                if (-not $ready.value -and $output -notmatch 'Now listening on: http://127\.0\.0\.1:\d+') {
                    throw "Server.Local did not start and bind its ephemeral endpoint; see $($launch.StdOutPath) and $($launch.StdErrPath)."
                }
                if ($launch.TimedOut) { throw "Server.Local startup exceeded its short smoke bound; see $($launch.StdOutPath) and $($launch.StdErrPath)." }
                $row.runtime = 'passed-started-and-bound'
            }
            default {
                $runtime = Invoke-ConsumerCommand -Name 'runtime' -Arguments @($consumerDll) -WorkingDirectory $scenarioDirectory -ScenarioDirectory $scenarioDirectory
                if ($runtime.ExitCode -ne 0) { throw "Consumer runtime failed with exit code $($runtime.ExitCode); see $($runtime.StdOutPath) and $($runtime.StdErrPath)." }
                $runtimeOutput = if (Test-Path -LiteralPath $runtime.StdOutPath) { [IO.File]::ReadAllText($runtime.StdOutPath) } else { '' }
                if ($Scenario.Kind -eq 'text' -and $runtimeOutput -notmatch 'TEXT quick\|fox') { throw 'Text consumer output was not deterministic.' }
                if ($Scenario.Kind -eq 'sourcegen' -and $runtimeOutput -notmatch 'SOURCEGEN Product') { throw 'SourceGen output did not prove generated mapper use.' }
                if ($Scenario.Kind -eq 'orchard' -and $runtimeOutput -notmatch 'ORCHARD module bootstrap passed') { throw 'Orchard consumer did not finish its module bootstrap.' }
                $row.runtime = 'passed'
            }
        }
        $row.status = 'passed'
        $Results.Add($row)
        return $scenarioDirectory
    } catch {
        $row.status = 'failed'
        $row.error = $_.Exception.Message
        [IO.File]::WriteAllText(
            (Join-Path $scenarioDirectory 'exception.txt'),
            ($_ | Out-String) + "`n" + [string]$_.ScriptStackTrace,
            [Text.UTF8Encoding]::new($false))
        $Results.Add($row)
        throw
    }
}

function Test-PackageConsumerSet {
    param(
        [Parameter(Mandatory = $true)][string]$PackageDirectory,
        [Parameter(Mandatory = $true)][string]$RepoRoot,
        [ValidateSet('all', 'compression')][string]$Scope = 'all'
    )

    $target = if ($Scope -eq 'compression') { 'compression-package-consumer' } else { 'package-consumer' }
    $commandLine = if ($Scope -eq 'compression') { './devops pack -CompressionOnly -ValidateCompression' } else { './devops pack -Clean -ValidatePackages' }
    $run = New-ArtifactRun -Kind test -Target $target -CommandLine $commandLine -RepoRoot $RepoRoot
    $status = 'Failed'
    $consumerRoot = Join-Path $run.RunDirectory 'consumer-projects'
    $cacheRoot = Join-Path $run.RunDirectory 'isolated-caches'
    $acceptancePath = Join-Path $run.RunDirectory 'acceptance.json'
    $results = [Collections.Generic.List[object]]::new()
    $successDirectories = [Collections.Generic.List[string]]::new()
    $successCaches = [Collections.Generic.List[string]]::new()
    $manifestInfo = $null
    $failure = $null
    try {
        [void][IO.Directory]::CreateDirectory($consumerRoot)
        [void][IO.Directory]::CreateDirectory($cacheRoot)
        $manifestInfo = Get-ConsumerPackageManifest -PackageDirectory $PackageDirectory
        $externalSources = Get-ConsumerPackageSources
        foreach ($scenario in @(Get-ConsumerScenarioDefinitions -Scope $Scope)) {
            $directory = Invoke-PackageConsumerScenario -Scenario $scenario -PackageIndex $manifestInfo -PackageDirectory $PackageDirectory -RepoRoot $RepoRoot -ExternalSources $externalSources -ConsumerRoot $consumerRoot -CacheRoot $cacheRoot -Results $results
            $successDirectories.Add($directory)
            $successCaches.Add((Join-Path $cacheRoot "$($scenario.Name)-$($scenario.Framework)"))
        }
        $status = 'Passed'
    } catch {
        $failure = $_.Exception.Message
        throw
    } finally {
        $git = Get-ArtifactGitContext -RepoRoot $RepoRoot
        $sourceCommit = if ($null -ne $manifestInfo) { [string]$manifestInfo.Manifest.sourceCommit } else { [string]$git.commit }
        $acceptance = [ordered]@{
            schemaVersion = 1
            sourceCommit = $sourceCommit
            packageManifest = Join-Path $PackageDirectory 'manifest.json'
            generatedAtUtc = [DateTime]::UtcNow.ToString('O')
            status = $status.ToLowerInvariant()
            failure = $failure
            results = @($results)
        }
        Write-AtomicJsonFile -Path $acceptancePath -Value $acceptance
        if ($status -eq 'Passed') {
            try {
                foreach ($directory in $successDirectories) {
                    if (Test-Path -LiteralPath $directory) { Remove-Item -LiteralPath $directory -Recurse -Force }
                    if (Test-Path -LiteralPath $directory) { throw "Successful consumer working directory cleanup failed: $directory" }
                }
                foreach ($cache in $successCaches) {
                    if (Test-Path -LiteralPath $cache) { Remove-Item -LiteralPath $cache -Recurse -Force }
                    if (Test-Path -LiteralPath $cache) { throw "Successful consumer cache cleanup failed: $cache" }
                }
                if (Test-Path -LiteralPath $consumerRoot) { Remove-Item -LiteralPath $consumerRoot -Recurse -Force }
                if (Test-Path -LiteralPath $cacheRoot) { Remove-Item -LiteralPath $cacheRoot -Recurse -Force }
            } catch {
                $status = 'Failed'
                $failure = $_.Exception.Message
                $acceptance.status = 'failed'
                $acceptance.failure = $failure
                Write-AtomicJsonFile -Path $acceptancePath -Value $acceptance
            }
        }
        Complete-ArtifactRun -RunDirectory $run.RunDirectory -Status $status -AdditionalValues @{
            consumerDirectory = $consumerRoot
            cacheDirectory = $cacheRoot
            acceptance = $acceptancePath
            scenarioCount = $results.Count
        }
    }
    Write-Success "Package consumer acceptance evidence: $acceptancePath"
}
