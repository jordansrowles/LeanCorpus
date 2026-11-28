using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using Rowles.LeanCorpus.Tests.Shared.Fixtures;

namespace Rowles.LeanCorpus.Tests.Browser;

[CollectionDefinition("Browser WASM", DisableParallelization = true)]
public sealed class BrowserCollection;

[Collection("Browser WASM")]
[Trait("Area", "Index")]
[Trait("Category", "Integration")]
public sealed class BrowserContractTests
{
    [Theory]
    [InlineData("defaults")]
    [InlineData("durability")]
    [InlineData("async")]
    [InlineData("cancellation")]
    [InlineData("options")]
    [InlineData("pending-close")]
    [InlineData("merge")]
    [InlineData("mutation")]
    [InlineData("leases")]
    [InlineData("interchange")]
    [InlineData("runtime-close")]
    [InlineData("byte-pressure")]
    [InlineData("rejection")]
    [InlineData("stream-cancellation")]
    public async Task BrowserContract(string testCase)
        => await RunCaseAsync(testCase, TimeSpan.FromSeconds(15));

    /// <summary>
    /// Runtime-assumption probes: prebuilt payloads, memory amplification, blocking, writer exclusion and
    /// unsupported subsystems. Measurements are written to the test output as run evidence.
    /// </summary>
    [Theory]
    [Trait("Category", "Probe")]
    [InlineData("payload-small-cfs")]
    [InlineData("payload-large-cfs")]
    [InlineData("payload-large-loose")]
    [InlineData("payload-large-cfs-streamed")]
    [InlineData("payload-large-cfs-buffered")]
    [InlineData("writer-exclusion")]
    [InlineData("backup")]
    [InlineData("analysis")]
    [InlineData("brotli-read")]
    [InlineData("blocking")]
    [InlineData("memory-limit")]
    public async Task BrowserProbe(string testCase)
    {
        BrowserApp.BrowserFixture.DesktopTemporaryRoot = RepositoryTemporaryStorage.GetAreaRoot("tests");
        // Build payloads before the page starts so browser load timings exclude desktop indexing.
        string? variant = testCase switch
        {
            "payload-small-cfs" => "small-cfs",
            "payload-large-loose" => "large-loose",
            "brotli-read" => "brotli",
            _ when testCase.StartsWith("payload-large-cfs", StringComparison.Ordinal) || testCase == "memory-limit" => "large-cfs",
            _ => null
        };
        if (variant is not null) BrowserPayloadHost.Get(variant);
        string text = await RunCaseAsync(testCase, TimeSpan.FromMinutes(8));
        TestContext.Current.TestOutputHelper?.WriteLine($"probe {testCase}: {text}");
    }

    private static async Task<string> RunCaseAsync(string testCase, TimeSpan deadline)
    {
        string root = FindRepository();
        BrowserApp.BrowserFixture.DesktopTemporaryRoot = RepositoryTemporaryStorage.GetAreaRoot("tests");
        string configuration = typeof(BrowserContractTests).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()!.Configuration;
        string framework = $"net{Environment.Version.Major}.0";
        string site = Environment.GetEnvironmentVariable("LEANCORPUS_BROWSER_SITE")
            ?? (Environment.GetEnvironmentVariable("LEANCORPUS_BROWSER_AOT") == "true"
                ? Path.Combine(root, "artifacts", "browser", "site", framework, configuration, "aot", "wwwroot")
                : Path.Combine(root, "artifacts", "browser", "site", framework, configuration, "wwwroot"));
        Assert.True(File.Exists(Path.Combine(site, "index.html")), $"Published browser client is missing: {site}");

        var builder = WebApplication.CreateEmptyBuilder(new WebApplicationOptions { ContentRootPath = site });
        builder.Logging.ClearProviders();
        builder.Services.AddRouting();
        builder.WebHost.UseKestrel().UseUrls("http://127.0.0.1:0");
        await using var server = builder.Build();
        using var files = new PhysicalFileProvider(site);
        server.UseDefaultFiles(new DefaultFilesOptions { FileProvider = files });
        server.UseStaticFiles(new StaticFileOptions { FileProvider = files, ServeUnknownFileTypes = true, DefaultContentType = "application/octet-stream" });
        server.MapGet("/fixture.zip", () => Results.Bytes(BrowserApp.BrowserFixture.Create(), "application/zip"));
        server.MapGet("/payload/{variant}/manifest.json", (string variant) => Results.Bytes(BrowserPayloadHost.Get(variant).Manifest, "application/json"));
        server.MapGet("/payload/{variant}/files/{name}", (string variant, string name) => BrowserPayloadHost.File(variant, name));
        server.MapGet("/expect/analysis", () => Results.Bytes(BrowserApp.BrowserPayload.AnalysisReport(), "application/json"));
        await server.StartAsync(TestContext.Current.CancellationToken);
        string address = server.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();

        using var playwright = await Playwright.CreateAsync();
        string engine = Environment.GetEnvironmentVariable("LEANCORPUS_BROWSER_ENGINE") ?? "chromium";
        IBrowserType browserType = engine switch
        {
            "chromium" => playwright.Chromium,
            "firefox" => playwright.Firefox,
            "webkit" => playwright.Webkit,
            _ => throw new InvalidOperationException("Unknown LEANCORPUS_BROWSER_ENGINE: " + engine)
        };
        var browser = await browserType.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        try
        {
            var page = await browser.NewPageAsync();
            var errors = new System.Collections.Concurrent.ConcurrentQueue<string>();
            var messages = new System.Collections.Concurrent.ConcurrentQueue<string>();
            // An uncaught page error or a runtime start failure can never produce a result: fail at once
            // instead of waiting for the case deadline.
            var crashed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            page.PageError += (_, error) => { errors.Enqueue(error); crashed.TrySetResult(); };
            page.Crash += (_, _) => { errors.Enqueue("renderer crashed"); crashed.TrySetResult(); };
            page.Console += (_, message) =>
            {
                messages.Enqueue(message.Text);
                if (message.Type == "error" && message.Text.Contains("mono_wasm_load_runtime", StringComparison.Ordinal))
                    crashed.TrySetResult();
            };
            page.Response += (_, response) => { if (response.Status >= 400) messages.Enqueue($"HTTP {response.Status}: {response.Url}"); };
            await page.GotoAsync(address + "/?" + testCase, new PageGotoOptions { Timeout = 15000 });
            var completion = page.WaitForFunctionAsync("() => ['passed','failed'].includes(document.querySelector('#result')?.dataset.state)",
                options: new PageWaitForFunctionOptions { Timeout = (float)deadline.TotalMilliseconds, PollingInterval = 250 });
            try
            {
                var first = await Task.WhenAny(completion, crashed.Task)
                    .WaitAsync(deadline + TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
                if (first == crashed.Task)
                {
                    _ = completion.ContinueWith(static task => _ = task.Exception, TaskContinuationOptions.OnlyOnFaulted);
                    throw new InvalidOperationException($"{testCase}: browser failed before completing. {string.Join("; ", errors.Concat(messages))}");
                }
                await completion;
            }
            catch (TimeoutException error)
            {
                throw new InvalidOperationException($"{testCase}: browser did not complete. {string.Join("; ", errors.Concat(messages))}", error);
            }
            var result = page.Locator("#result");
            string? state = await result.GetAttributeAsync("data-state").WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            string text = await result.InnerTextAsync().WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.True(state == "passed", $"{engine}/{framework}/{testCase}: {text}");
            if (testCase == "interchange") BrowserApp.BrowserFixture.Verify(Convert.FromBase64String(text));
            Assert.Equal(testCase, await result.GetAttributeAsync("data-case"));
            Assert.Equal(Environment.Version.Major.ToString(), await result.GetAttributeAsync("data-runtime"));
            Assert.Empty(errors);
            return text;
        }
        finally
        {
            // Dispose the driver even if an unresponsive renderer cannot honour graceful close.
            try { await browser.CloseAsync().WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken); }
            finally { await server.StopAsync(TestContext.Current.CancellationToken); }
        }
    }

    /// <summary>Builds each payload variant once per test process from the desktop engine.</summary>
    private static class BrowserPayloadHost
    {
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Lazy<(string Path, byte[] Manifest, HashSet<string> Files)>> s_payloads = new();

        public static (string Path, byte[] Manifest, HashSet<string> Files) Get(string variant)
            => s_payloads.GetOrAdd(variant, static key => new Lazy<(string, byte[], HashSet<string>)>(() =>
            {
                string root = BrowserApp.BrowserFixture.DesktopTemporaryRoot
                    ?? throw new InvalidOperationException("The fixture host must configure repository temporary storage.");
                string path = Path.Combine(root, "browser-payload-" + key + "-" + Guid.NewGuid().ToString("N"));
                AppDomain.CurrentDomain.ProcessExit += (_, _) => { try { Directory.Delete(path, recursive: true); } catch (IOException) { } };
                byte[] manifest = BrowserApp.BrowserPayload.Create(path, key);
                var files = BrowserApp.BrowserPayload.ReadManifest(manifest).Files.Select(static file => file.Name).ToHashSet(StringComparer.Ordinal);
                return (path, manifest, files);
            })).Value;

        public static IResult File(string variant, string name)
        {
            var payload = Get(variant);
            return payload.Files.Contains(name)
                ? Results.File(Path.Combine(payload.Path, name), "application/octet-stream")
                : Results.NotFound();
        }
    }

    private static string FindRepository()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "src", "Rowles.LeanCorpus.slnx"))) return directory.FullName;
        throw new DirectoryNotFoundException("Could not locate the LeanCorpus checkout from the browser test output.");
    }
}
