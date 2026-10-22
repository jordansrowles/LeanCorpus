using System.Net;
using Microsoft.Playwright;

namespace Rowles.LeanCorpus.Tests.Docs;

internal static class DocsSiteViewports
{
    public const int Phone = 390;
    public const int Tablet = 768;
    public const int SmallLaptop = 1024;
    public const int Laptop = 1280;
    public const int Desktop = 1440;
    public const int Height = 900;

    public static readonly int[] Widths = [Phone, Tablet, SmallLaptop, Laptop, Desktop];
}

public sealed class DocsSiteAcceptanceTests
{
    private const string MermaidPaletteExpression = """
        (() => {
            const svg = document.querySelector('article .mermaid svg');
            const shape = svg?.querySelector('.node rect, .node polygon, .node circle, .node path');
            const label = svg?.querySelector('.nodeLabel');
            if (!shape || !label) return null;

            const palette = [
                getComputedStyle(shape).fill,
                getComputedStyle(shape).stroke,
                getComputedStyle(label).color
            ];
            return palette.every(Boolean) ? palette.join('|') : null;
        })()
        """;

    [Fact(DisplayName = "Docs Site: Landing Page Uses LeanCorpus Components")]
    public async Task LandingPage_UsesSemanticLeanCorpusComponents()
    {
        await using var site = await BrowserSite.StartAsync();
        var page = await site.NewPageAsync();

        try
        {
            await page.GotoAsync(site.Url("index.html"));

            Assert.Equal(1, await page.Locator("article .lc-hero").CountAsync());
            Assert.True(await page.Locator("article .lc-feature-grid").CountAsync() > 0);
            Assert.Equal(0, await page.Locator("article .row, article [class*='col-md-']").CountAsync());
        }
        catch
        {
            await site.CaptureFailureAsync(page, nameof(LandingPage_UsesSemanticLeanCorpusComponents));
            throw;
        }
    }

    [Fact(DisplayName = "Docs Site: Article Contains Wide Content At Every Viewport")]
    public async Task Article_ContainsWideContentAtSupportedViewports()
    {
        await using var site = await BrowserSite.StartAsync();
        var page = await site.NewPageAsync(DocsSiteViewports.Phone, DocsSiteViewports.Height);

        try
        {
            await page.GotoAsync(site.Url("server/08-operational-limits.html"));
            Assert.True(await page.Locator("article table").CountAsync() > 0);

            foreach (var width in DocsSiteViewports.Widths)
            {
                await page.SetViewportSizeAsync(width, DocsSiteViewports.Height);
                var documentWidth = await page.EvaluateAsync<int>("() => document.documentElement.scrollWidth");
                var overflowElements = await page.EvaluateAsync<string>("""() => Array.from(document.querySelectorAll('body *')).filter(element => element.getBoundingClientRect().right > window.innerWidth + 1).slice(0, 8).map(element => element.tagName.toLowerCase() + '#' + element.id + '.' + Array.from(element.classList).join('.') + ':' + Math.round(element.getBoundingClientRect().right)).join(', ')""");
                Assert.True(documentWidth <= width, $"The document overflows at {width}px: scroll width is {documentWidth}px. Elements: {overflowElements}");
            }
        }
        catch
        {
            await site.CaptureFailureAsync(page, nameof(Article_ContainsWideContentAtSupportedViewports));
            throw;
        }
    }

    [Fact(DisplayName = "Docs Site: Large Code Blocks Stay Inside The Article At Every Viewport")]
    public async Task LargeCodeBlock_DoesNotCauseDocumentOverflow()
    {
        await using var site = await BrowserSite.StartAsync();
        var page = await site.NewPageAsync(DocsSiteViewports.Phone, DocsSiteViewports.Height);

        try
        {
            await page.GotoAsync(site.Url("index-management/04-cli-checker.html"));
            var code = page.Locator("article pre code");
            Assert.True(await code.CountAsync() > 0);
            var largestCodeBlock = await code.EvaluateAllAsync<int>("elements => Math.max(...elements.map(element => element.innerText.length))");
            Assert.True(largestCodeBlock >= 500, $"The representative large code block contains only {largestCodeBlock} characters.");

            foreach (var width in DocsSiteViewports.Widths)
            {
                await page.SetViewportSizeAsync(width, DocsSiteViewports.Height);
                var documentWidth = await page.EvaluateAsync<int>("() => document.documentElement.scrollWidth");
                Assert.True(documentWidth <= width, $"A code block causes document overflow at {width}px: scroll width is {documentWidth}px.");
            }
        }
        catch
        {
            await site.CaptureFailureAsync(page, nameof(LargeCodeBlock_DoesNotCauseDocumentOverflow));
            throw;
        }
    }

    [Fact(DisplayName = "Docs Site: Mobile Header Opens Accessible Navigation")]
    public async Task MobileHeader_OpensNavigationMenu()
    {
        await using var site = await BrowserSite.StartAsync();
        var page = await site.NewPageAsync(DocsSiteViewports.Phone, DocsSiteViewports.Height);

        try
        {
            await page.GotoAsync(site.Url("index.html"));
            var toggle = page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Toggle navigation" });
            Assert.True(await toggle.IsVisibleAsync());
            await toggle.ClickAsync();
            var firstLink = page.Locator("#navbar .navbar-nav a[href]").First;
            await firstLink.WaitForAsync();
            Assert.True(await firstLink.IsVisibleAsync());
            Assert.Contains("Get started", await page.Locator("#navbar").InnerTextAsync(), StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            await site.CaptureFailureAsync(page, nameof(MobileHeader_OpensNavigationMenu));
            throw;
        }
    }

    [Fact(DisplayName = "Docs Site: Right Rail Is Hidden Before Article Becomes Narrow")]
    public async Task RightRail_IsHiddenAtTabletLandscapeWidth()
    {
        await using var site = await BrowserSite.StartAsync();
        var page = await site.NewPageAsync(1024);

        try
        {
            await page.GotoAsync(site.Url("getting-started/01-installation.html"));
            var railDisplay = await page.Locator("main .affix").EvaluateAsync<string>("element => getComputedStyle(element).display");
            Assert.Equal("none", railDisplay);

            var articleWidth = await page.Locator("main article").EvaluateAsync<double>("element => element.getBoundingClientRect().width");
            Assert.True(articleWidth >= 540, $"Article width is {articleWidth}px at 1024px viewport width.");
        }
        catch
        {
            await site.CaptureFailureAsync(page, nameof(RightRail_IsHiddenAtTabletLandscapeWidth));
            throw;
        }
    }

    [Fact(DisplayName = "Docs Site: Mermaid Diagram Updates Across Theme Changes")]
    public async Task MermaidDiagram_UpdatesAcrossThemeChanges()
    {
        await using var site = await BrowserSite.StartAsync();
        var page = await site.NewPageAsync();

        try
        {
            await page.GotoAsync(site.Url("tips/02-query-cache.html"));
            var diagram = page.Locator("article .mermaid svg").First;
            await diagram.WaitForAsync(new LocatorWaitForOptions { Timeout = 10_000 });
            await page.WaitForFunctionAsync(
                $"() => document.documentElement.dataset.bsTheme === 'light' && {MermaidPaletteExpression} !== null",
                null,
                new PageWaitForFunctionOptions { Timeout = 10_000 });
            var lightPalette = await page.EvaluateAsync<string>(MermaidPaletteExpression);

            await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Toggle navigation" }).ClickAsync();
            var themeControl = page.GetByTitle("Change theme");
            await themeControl.ClickAsync();
            await page.GetByText("Dark", new PageGetByTextOptions { Exact = true }).ClickAsync();
            await page.WaitForFunctionAsync(
                $"() => document.documentElement.dataset.bsTheme === 'dark' && {MermaidPaletteExpression} !== {System.Text.Json.JsonSerializer.Serialize(lightPalette)}",
                null,
                new PageWaitForFunctionOptions { Timeout = 10_000 });
            var darkPalette = await page.EvaluateAsync<string>(MermaidPaletteExpression);
            Assert.NotEqual(lightPalette, darkPalette);

            await themeControl.ClickAsync();
            await page.GetByText("Light", new PageGetByTextOptions { Exact = true }).ClickAsync();
            await page.WaitForFunctionAsync(
                $"() => document.documentElement.dataset.bsTheme === 'light' && {MermaidPaletteExpression} === {System.Text.Json.JsonSerializer.Serialize(lightPalette)}",
                null,
                new PageWaitForFunctionOptions { Timeout = 10_000 });
            var restoredPalette = await page.EvaluateAsync<string>(MermaidPaletteExpression);
            Assert.Equal(lightPalette, restoredPalette);
        }
        catch
        {
            await site.CaptureFailureAsync(page, nameof(MermaidDiagram_UpdatesAcrossThemeChanges));
            throw;
        }
    }

    [Fact(DisplayName = "Docs Site: Landing Page Keeps Its Main Content Without JavaScript")]
    public async Task LandingPage_RemainsUsefulWithoutJavaScript()
    {
        await using var site = await BrowserSite.StartAsync();
        var page = await site.NewPageAsync(javaScriptEnabled: false);

        try
        {
            await page.GotoAsync(site.Url("index.html"));

            Assert.Equal(1, await page.Locator("article .lc-hero").CountAsync());
            Assert.Contains("Build your first search", await page.Locator("article").InnerTextAsync(), StringComparison.Ordinal);
            Assert.True(await page.Locator("article a[href='getting-started/01-installation.html']").CountAsync() > 0);
        }
        catch
        {
            await site.CaptureFailureAsync(page, nameof(LandingPage_RemainsUsefulWithoutJavaScript));
            throw;
        }
    }

    [Fact(DisplayName = "Docs Site: Native Navigation And Search Work")]
    public async Task NavigationAndSearch_AreAvailable()
    {
        await using var site = await BrowserSite.StartAsync();
        var page = await site.NewPageAsync();

        try
        {
            await page.GotoAsync(site.Url("guides/index.html"));
            await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Toggle navigation" }).ClickAsync();
            await page.Locator("#navbar .navbar-nav a[href]").First.WaitForAsync();
            Assert.DoesNotContain("Loading section navigation", await page.Locator("body").InnerTextAsync(), StringComparison.Ordinal);

            await page.WaitForFunctionAsync("() => { const input = document.querySelector('#search-query'); return input && !input.disabled; }");
            var search = page.Locator("#search-query");
            if (!await search.IsVisibleAsync())
            {
                await page.Locator("#search").ClickAsync();
            }

            await search.FillAsync("IndexWriter");
            await page.Locator("#search-results .sr-item").First.WaitForAsync(new LocatorWaitForOptions { Timeout = 15_000 });
            Assert.True(await page.Locator("#search-results .sr-item").CountAsync() > 0);
        }
        catch
        {
            await site.CaptureFailureAsync(page, nameof(NavigationAndSearch_AreAvailable));
            throw;
        }
    }

    [Fact(DisplayName = "Docs Site: Native Theme Control Switches Light And Dark")]
    public async Task NativeThemeControl_SwitchesBothThemes()
    {
        await using var site = await BrowserSite.StartAsync();
        var page = await site.NewPageAsync();

        try
        {
            await page.GotoAsync(site.Url("index.html"));
            await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Toggle navigation" }).ClickAsync();
            var themeControl = page.GetByTitle("Change theme");
            await themeControl.ClickAsync();
            await page.GetByText("Dark", new PageGetByTextOptions { Exact = true }).ClickAsync();
            await page.WaitForFunctionAsync("() => document.documentElement.dataset.bsTheme === 'dark'");
            var darkText = await page.Locator("article").EvaluateAsync<string>("element => getComputedStyle(element).color");

            await themeControl.ClickAsync();
            await page.GetByText("Light", new PageGetByTextOptions { Exact = true }).ClickAsync();
            await page.WaitForFunctionAsync("() => document.documentElement.dataset.bsTheme === 'light'");
            var lightText = await page.Locator("article").EvaluateAsync<string>("element => getComputedStyle(element).color");

            Assert.NotEqual(lightText, darkText);
        }
        catch
        {
            await site.CaptureFailureAsync(page, nameof(NativeThemeControl_SwitchesBothThemes));
            throw;
        }
    }

    [Fact(DisplayName = "Docs Site: Representative Generated Pages Load Without Local Resource Errors")]
    public async Task RepresentativePages_LoadWithoutBrowserOrLocalResourceErrors()
    {
        await using var site = await BrowserSite.StartAsync();
        var page = await site.NewPageAsync();
        var browserErrors = new List<string>();
        page.PageError += (_, message) => browserErrors.Add(message);
        page.Response += (_, response) =>
        {
            if (site.IsSameOrigin(response.Url) && response.Status >= 400)
            {
                browserErrors.Add($"HTTP {response.Status}: {response.Url}");
            }
        };
        page.RequestFailed += (_, request) =>
        {
            if (site.IsSameOrigin(request.Url))
            {
                browserErrors.Add($"Failed request: {request.Url}");
            }
        };

        try
        {
            var routes = new List<string>
            {
            "index.html",
            "getting-started/01-installation.html",
            "guides/index.html",
            "advanced/08-filtered-vector-search.html",
            "server/08-operational-limits.html",
            "tips/02-query-cache.html",
            "index-management/04-cli-checker.html",
            "api/Rowles.LeanCorpus.html",
            "api/Rowles.LeanCorpus.Index.Indexer.IndexWriter.html",
            "articles/ADRs/index.html",
            "articles/ADRs/ADR041-physical-index-runtime-lifecycle.html",
            "changelog/index.html",
            "changelog/devops/unreleased.html",
            "changelog/3.1.0 - 2026-09-03.html",
            "benchmarks/overview.html"
            };

            var benchmarkDirectory = Path.Combine(site.SiteDirectory, "benchmarks");
            var generatedBenchmarkPage = Directory.EnumerateFiles(benchmarkDirectory, "*.html", SearchOption.TopDirectoryOnly)
                .Select(path => Path.GetFileName(path)!)
                .Where(fileName => fileName is not ("overview.html" or "toc.html"))
                .OrderBy(fileName => fileName, StringComparer.Ordinal)
                .FirstOrDefault();
            if (generatedBenchmarkPage is not null)
            {
                routes.Add($"benchmarks/{Uri.EscapeDataString(generatedBenchmarkPage)}");
            }

            foreach (var route in routes)
            {
                var response = await page.GotoAsync(site.Url(route), new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
                Assert.NotNull(response);
                Assert.Equal(200, response.Status);
                Assert.True(await page.Locator("article :is(h1, h2, h3)").CountAsync() > 0, $"Route '{route}' has no article heading.");
            }

            await page.WaitForTimeoutAsync(100);
            Assert.Empty(browserErrors);

            var apiType = await page.GotoAsync(site.Url("api/Rowles.LeanCorpus.Index.Indexer.IndexWriter.html"));
            Assert.NotNull(apiType);
            var apiIcons = page.Locator("article img.api-icon");
            await apiIcons.First.WaitForAsync();
            Assert.True(await apiIcons.CountAsync() > 0, "The API type page has no semantic icons.");
            var iconLoaded = await apiIcons.First.EvaluateAsync<bool>("image => image.complete && image.naturalWidth > 0");
            Assert.True(iconLoaded, "The API icon did not load as an image.");
            var commitMember = page.Locator("article a.xref[href*='#Rowles_LeanCorpus_Index_Indexer_IndexWriter_Commit']").First;
            await commitMember.WaitForAsync();
            var memberHref = await commitMember.GetAttributeAsync("href");
            Assert.NotNull(memberHref);
            Assert.Contains("#", memberHref, StringComparison.Ordinal);
            await commitMember.ClickAsync();
            Assert.True(await page.Locator("article [data-uid*='IndexWriter.Commit']").CountAsync() > 0,
                "The API member anchor does not resolve on its containing type page.");

            await page.GotoAsync(site.Url("guides/index.html"));
            var articleTopBeforeNavigation = await page.Locator("article h1").EvaluateAsync<double>("element => element.getBoundingClientRect().top");
            await page.WaitForFunctionAsync("() => document.querySelector('#navbar .navbar-nav a[href]') !== null");
            await page.WaitForTimeoutAsync(100);
            var articleTopAfterNavigation = await page.Locator("article h1").EvaluateAsync<double>("element => element.getBoundingClientRect().top");
            Assert.InRange(Math.Abs(articleTopAfterNavigation - articleTopBeforeNavigation), 0, 1);
            var activeNavigation = page.Locator("#navbar .navbar-nav a.active, #navbar .navbar-nav a[aria-current='page']");
            var activeCount = await activeNavigation.CountAsync();
            var navigationState = await page.Locator("#navbar").EvaluateAsync<string>("element => Array.from(element.querySelectorAll('a[href]')).map(link => `${link.textContent.trim()}|${link.getAttribute('href')}|${link.className}|${link.getAttribute('aria-current')}`).join('; ')");
            Assert.True(activeCount > 0, $"The current navigation item is not marked active. Navigation: {navigationState}");
        }
        catch
        {
            await site.CaptureFailureAsync(page, nameof(RepresentativePages_LoadWithoutBrowserOrLocalResourceErrors));
            throw;
        }
    }

    [Fact(DisplayName = "Docs Site: High Value Layouts Match Visual Baselines")]
    public async Task HighValueLayouts_MatchVisualBaselines()
    {
        await using var site = await BrowserSite.StartAsync();

        var layouts = new[]
        {
            (Name: "homepage-desktop", Route: "index.html", Width: DocsSiteViewports.Desktop, Height: DocsSiteViewports.Height, Dark: false),
            (Name: "homepage-phone", Route: "index.html", Width: DocsSiteViewports.Phone, Height: 844, Dark: false),
            (Name: "article-desktop", Route: "getting-started/01-installation.html", Width: DocsSiteViewports.Laptop, Height: DocsSiteViewports.Height, Dark: false),
            (Name: "api-desktop", Route: "api/Rowles.LeanCorpus.Index.Indexer.IndexWriter.html", Width: DocsSiteViewports.Laptop, Height: DocsSiteViewports.Height, Dark: false),
            (Name: "article-dark", Route: "getting-started/01-installation.html", Width: DocsSiteViewports.Laptop, Height: DocsSiteViewports.Height, Dark: true),
            (Name: "api-dark", Route: "api/Rowles.LeanCorpus.Index.Indexer.IndexWriter.html", Width: DocsSiteViewports.Laptop, Height: DocsSiteViewports.Height, Dark: true)
        };

        foreach (var layout in layouts)
        {
            var page = await site.NewPageAsync(layout.Width, layout.Height);
            try
            {
                await page.GotoAsync(site.Url(layout.Route), new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
                await page.WaitForFunctionAsync("() => document.fonts.status === 'loaded'");

                if (layout.Dark)
                {
                    var navigationToggle = page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Toggle navigation" });
                    await navigationToggle.ClickAsync();
                    await page.GetByTitle("Change theme").ClickAsync();
                    await page.GetByText("Dark", new PageGetByTextOptions { Exact = true }).ClickAsync();
                    await page.WaitForFunctionAsync("() => document.documentElement.dataset.bsTheme === 'dark'");
                    if (string.Equals(await navigationToggle.GetAttributeAsync("aria-expanded"), "true", StringComparison.Ordinal))
                    {
                        await navigationToggle.ClickAsync();
                    }

                    await page.EvaluateAsync("window.scrollTo(0, 0)");
                }

                await page.WaitForTimeoutAsync(100);
                await AssertVisualBaselineAsync(site, page, layout.Name);
            }
            catch
            {
                await site.CaptureFailureAsync(page, $"VisualBaseline_{layout.Name}");
                throw;
            }
        }
    }

    private static async Task AssertVisualBaselineAsync(BrowserSite site, IPage page, string name)
    {
        var repositoryRoot = site.RepositoryRoot;
        var baselinePath = Path.Combine(repositoryRoot, "src", "devops", "Rowles.LeanCorpus.Tests.Docs", "VisualBaselines", $"{name}.png");
        var diagnosticsDirectory = Path.Combine(repositoryRoot, "artifacts", "docs", "diagnostics", "docs-site-visuals");
        Directory.CreateDirectory(diagnosticsDirectory);
        var actualPath = Path.Combine(diagnosticsDirectory, $"{name}.actual.png");
        var actual = await page.ScreenshotAsync(new PageScreenshotOptions { FullPage = false });
        await File.WriteAllBytesAsync(actualPath, actual);

        if (string.Equals(Environment.GetEnvironmentVariable("LEAN_CORPUS_UPDATE_DOCS_VISUAL_BASELINES"), "1", StringComparison.Ordinal))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(baselinePath)!);
            await File.WriteAllBytesAsync(baselinePath, actual);
            return;
        }

        Assert.True(File.Exists(baselinePath), $"Visual baseline '{baselinePath}' is missing. Set LEAN_CORPUS_UPDATE_DOCS_VISUAL_BASELINES=1 to capture the reviewed baseline.");
        var expected = Convert.ToBase64String(await File.ReadAllBytesAsync(baselinePath));
        var observed = Convert.ToBase64String(actual);
        var changedPixelRatio = await page.EvaluateAsync<double>(
            """
            async ([expectedBase64, observedBase64]) => {
            const decode = source => new Promise((resolve, reject) => {
                const image = new Image();
                image.onload = () => resolve(image);
                image.onerror = reject;
                image.src = `data:image/png;base64,${source}`;
            });
            const [expected, observed] = await Promise.all([decode(expectedBase64), decode(observedBase64)]);
            if (expected.width !== observed.width || expected.height !== observed.height) return 1;
            const canvas = document.createElement('canvas');
            canvas.width = expected.width;
            canvas.height = expected.height;
            const context = canvas.getContext('2d', { willReadFrequently: true });
            context.drawImage(expected, 0, 0);
            const expectedPixels = context.getImageData(0, 0, canvas.width, canvas.height).data;
            context.clearRect(0, 0, canvas.width, canvas.height);
            context.drawImage(observed, 0, 0);
            const observedPixels = context.getImageData(0, 0, canvas.width, canvas.height).data;
            let changed = 0;
            const pixelCount = expectedPixels.length / 4;
            for (let index = 0; index < expectedPixels.length; index += 4) {
                const delta = Math.max(
                    Math.abs(expectedPixels[index] - observedPixels[index]),
                    Math.abs(expectedPixels[index + 1] - observedPixels[index + 1]),
                    Math.abs(expectedPixels[index + 2] - observedPixels[index + 2]),
                    Math.abs(expectedPixels[index + 3] - observedPixels[index + 3]));
                if (delta > 12) changed++;
            }
                return changed / pixelCount;
            }
            """,
            new[] { expected, observed });

        Assert.True(changedPixelRatio <= 0.005, $"Visual baseline '{name}' differs in {changedPixelRatio:P2} of pixels (limit 0.50%). Actual screenshot: '{actualPath}'.");
    }
}

internal sealed class BrowserSite : IAsyncDisposable
{
    private readonly HttpListener _listener;
    private readonly Task _serverTask;
    private readonly string _siteDirectory;
    private readonly string _repositoryRoot;
    private readonly IPlaywright _playwright;
    private readonly IBrowser _browser;
    private readonly List<IBrowserContext> _contexts = new();

    private BrowserSite(
        HttpListener listener,
        Task serverTask,
        string siteDirectory,
        string repositoryRoot,
        string baseAddress,
        IPlaywright playwright,
        IBrowser browser)
    {
        _listener = listener;
        _serverTask = serverTask;
        _siteDirectory = siteDirectory;
        _repositoryRoot = repositoryRoot;
        BaseAddress = baseAddress;
        _playwright = playwright;
        _browser = browser;
    }

    private string BaseAddress { get; }
    public string RepositoryRoot => _repositoryRoot;
    public string SiteDirectory => _siteDirectory;

    public static async Task<BrowserSite> StartAsync()
    {
        var repositoryRoot = FindRepositoryRoot();
        var configuredSite = Environment.GetEnvironmentVariable("LEAN_CORPUS_DOCS_SITE_DIR");
        var siteDirectory = Path.GetFullPath(configuredSite ?? Path.Combine(repositoryRoot, "artifacts", "docs", "site"));
        if (!File.Exists(Path.Combine(siteDirectory, "index.html")))
        {
            throw new DirectoryNotFoundException($"Generated docs site not found at '{siteDirectory}'. Run 'pwsh ./devops.ps1 docs build' first.");
        }

        using var portProbe = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        portProbe.Start();
        var port = ((System.Net.IPEndPoint)portProbe.LocalEndpoint).Port;
        portProbe.Stop();

        var baseAddress = $"http://127.0.0.1:{port}/";
        var listener = new HttpListener();
        listener.Prefixes.Add(baseAddress);
        listener.Start();
        var serverTask = ServeAsync(listener, siteDirectory);

        IPlaywright? playwright = null;
        IBrowser? browser = null;
        try
        {
            playwright = await Playwright.CreateAsync();
            browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
            return new BrowserSite(listener, serverTask, siteDirectory, repositoryRoot, baseAddress, playwright, browser);
        }
        catch
        {
            browser?.DisposeAsync().AsTask().GetAwaiter().GetResult();
            playwright?.Dispose();
            listener.Stop();
            listener.Close();
            await serverTask;
            throw;
        }
    }

    public string Url(string relativePath) => new Uri(new Uri(BaseAddress), relativePath).ToString();

    public bool IsSameOrigin(string address) => new Uri(address).Authority == new Uri(BaseAddress).Authority;

    public async Task<IPage> NewPageAsync(int width = 1280, int height = 900, bool javaScriptEnabled = true)
    {
        var context = await _browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = width, Height = height },
            JavaScriptEnabled = javaScriptEnabled
        });
        _contexts.Add(context);
        return await context.NewPageAsync();
    }

    public async Task CaptureFailureAsync(IPage page, string testName)
    {
        var outputDirectory = Path.Combine(_repositoryRoot, "artifacts", "docs", "diagnostics", "docs-site-failures");
        Directory.CreateDirectory(outputDirectory);
        var path = Path.Combine(outputDirectory, $"{testName}.png");
        await page.ScreenshotAsync(new PageScreenshotOptions { Path = path, FullPage = true });
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var context in _contexts)
        {
            await context.CloseAsync();
        }

        await _browser.CloseAsync();
        _playwright.Dispose();
        _listener.Stop();
        _listener.Close();
        await _serverTask;
    }

    private static async Task ServeAsync(HttpListener listener, string siteDirectory)
    {
        var root = Path.GetFullPath(siteDirectory) + Path.DirectorySeparatorChar;
        while (listener.IsListening)
        {
            HttpListenerContext context;
            try
            {
                context = await listener.GetContextAsync();
            }
            catch (HttpListenerException)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }

            var requestedPath = Uri.UnescapeDataString(context.Request.Url?.AbsolutePath.TrimStart('/') ?? string.Empty);
            if (string.IsNullOrEmpty(requestedPath))
            {
                requestedPath = "index.html";
            }

            var filePath = Path.GetFullPath(Path.Combine(siteDirectory, requestedPath.Replace('/', Path.DirectorySeparatorChar)));
            if (!filePath.StartsWith(root, StringComparison.Ordinal) || !File.Exists(filePath))
            {
                context.Response.StatusCode = (int)HttpStatusCode.NotFound;
                context.Response.Close();
                continue;
            }

            var bytes = await File.ReadAllBytesAsync(filePath);
            context.Response.ContentType = ContentType(filePath);
            context.Response.ContentLength64 = bytes.Length;
            context.Response.Headers["Cache-Control"] = "no-store";
            try
            {
                await context.Response.OutputStream.WriteAsync(bytes);
                context.Response.Close();
            }
            catch (IOException)
            {
                context.Response.Abort();
            }
            catch (HttpListenerException)
            {
                context.Response.Abort();
            }
        }
    }

    private static string ContentType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".css" => "text/css; charset=utf-8",
        ".html" => "text/html; charset=utf-8",
        ".js" or ".mjs" => "text/javascript; charset=utf-8",
        ".json" => "application/json; charset=utf-8",
        ".svg" => "image/svg+xml",
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".woff" => "font/woff",
        ".woff2" => "font/woff2",
        ".xml" => "application/xml; charset=utf-8",
        _ => "application/octet-stream"
    };

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Rowles.LeanCorpus.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("Could not locate Rowles.LeanCorpus.slnx from the test output directory.");
    }
}
