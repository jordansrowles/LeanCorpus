using System.Collections.Generic;
using System.Net;
using System.Text.Json;
using Microsoft.Playwright;

namespace Rowles.LeanCorpus.Tests.Docs;

internal static class DocsSiteViewports
{
    public const int Phone = 390;
    public const int NarrowPhone = 360;
    public const int Tablet = 768;
    public const int SmallLaptop = 1024;
    public const int Laptop = 1280;
    public const int Desktop = 1440;
    public const int Height = 900;

    public static readonly int[] Widths = [NarrowPhone, Phone, Tablet, SmallLaptop, Laptop, Desktop, 1920];
}

public sealed class DocsSiteAcceptanceTests
{
    private static readonly (string SourcePath, string Route)[] ExpectedImports =
    [
        ("README.md", "about/repository-overview.html"),
        ("CONTRIBUTING.md", "contributors/contributing.html"),
        ("lexicons/README.md", "analysis/06-lexicons.html"),
        ("src/core/Rowles.Text/README.md", "analysis/08-rowles-text.html"),
        ("src/core/Rowles.Text/CONTRIBUTING.md", "contributors/rowles-text.html"),
        ("src/core/Rowles.LeanCorpus.SourceGen/README.md", "getting-started/04-source-generated-mapping.html"),
        ("src/core/Rowles.LeanCorpus.Compression.LZ4/README.md", "tips/compression/lz4.html"),
        ("src/core/Rowles.LeanCorpus.Compression.Snappy/README.md", "tips/compression/snappy.html"),
        ("src/core/Rowles.LeanCorpus.Compression.Zstandard/README.md", "tips/compression/zstandard.html"),
        ("src/examples/README.md", "examples/index.html"),
        ("src/examples/e2e/Rowles.LeanCorpus.Example.LinuxKernelCodeSearch/README.md", "examples/linux-kernel-code-search.html"),
        ("src/devops/README.md", "contributors/devops-projects.html"),
        ("src/devops/CONTRIBUTING.md", "contributors/devops-and-tests.html"),
        ("src/server/README.md", "server/community-server.html")
    ];

    private static readonly (string LegacyPath, string CanonicalPath)[] LegacyImportRoutes =
    [
        ("repository-overview", "about/repository-overview"),
        ("contributors/contributing", "contributors/contributing"),
        ("analysis/06-lexicons", "analysis/06-lexicons"),
        ("analysis/08-rowles-text", "analysis/08-rowles-text"),
        ("contributors/rowles-text", "contributors/rowles-text"),
        ("getting-started/04-source-generated-mapping", "getting-started/04-source-generated-mapping"),
        ("tips/compression/lz4", "tips/compression/lz4"),
        ("tips/compression/snappy", "tips/compression/snappy"),
        ("tips/compression/zstandard", "tips/compression/zstandard"),
        ("examples/index", "examples/index"),
        ("examples/linux-kernel-code-search", "examples/linux-kernel-code-search"),
        ("contributors/devops-projects", "contributors/devops-projects"),
        ("contributors/devops-and-tests", "contributors/devops-and-tests"),
        ("contributors/server-proof-of-concept", "server/community-server")
    ];

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

    private const string ContrastRatiosExpression = """
        () => {
            const channels = value => value.match(/[\d.]+/g).slice(0, 3).map(Number).map(channel => channel / 255);
            const linear = channel => channel <= 0.04045 ? channel / 12.92 : Math.pow((channel + 0.055) / 1.055, 2.4);
            const luminance = value => {
                const [red, green, blue] = channels(value).map(linear);
                return 0.2126 * red + 0.7152 * green + 0.0722 * blue;
            };
            const background = luminance(getComputedStyle(document.body).backgroundColor);
            const paragraph = document.querySelector('article p');
            const link = document.querySelector('article p a');
            if (!paragraph || !link) return [];
            return [paragraph, link].map(element => {
                const foreground = luminance(getComputedStyle(element).color);
                return (Math.max(foreground, background) + 0.05) / (Math.min(foreground, background) + 0.05);
            });
        }
        """;

    private const string FocusContrastExpression = """
        element => {
            const channels = value => value.match(/[\d.]+/g).slice(0, 3).map(Number).map(channel => channel / 255);
            const linear = channel => channel <= 0.04045 ? channel / 12.92 : Math.pow((channel + 0.055) / 1.055, 2.4);
            const luminance = value => {
                const [red, green, blue] = channels(value).map(linear);
                return 0.2126 * red + 0.7152 * green + 0.0722 * blue;
            };
            const outline = luminance(getComputedStyle(element).outlineColor);
            const background = luminance(getComputedStyle(document.body).backgroundColor);
            return (Math.max(outline, background) + 0.05) / (Math.min(outline, background) + 0.05);
        }
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
            var table = page.Locator("article table").First;
            Assert.True(await table.CountAsync() > 0);

            foreach (var width in DocsSiteViewports.Widths)
            {
                await page.SetViewportSizeAsync(width, DocsSiteViewports.Height);
                var documentWidth = await page.EvaluateAsync<int>("() => document.documentElement.scrollWidth");
                var overflowElements = await page.EvaluateAsync<string>("""() => Array.from(document.querySelectorAll('body *')).filter(element => element.getBoundingClientRect().right > window.innerWidth + 1).slice(0, 8).map(element => element.tagName.toLowerCase() + '#' + element.id + '.' + Array.from(element.classList).join('.') + ':' + Math.round(element.getBoundingClientRect().right)).join(', ')""");
                Assert.True(documentWidth <= width, $"The document overflows at {width}px: scroll width is {documentWidth}px. Elements: {overflowElements}");
                Assert.Equal("auto", await table.EvaluateAsync<string>("element => getComputedStyle(element).overflowX"));
            }

            await page.GotoAsync(site.Url("api/Rowles.LeanCorpus.Index.Indexer.IndexWriter.html"));
            foreach (var width in DocsSiteViewports.Widths)
            {
                await page.SetViewportSizeAsync(width, DocsSiteViewports.Height);
                var documentWidth = await page.EvaluateAsync<int>("() => document.documentElement.scrollWidth");
                Assert.True(documentWidth <= width, $"A long API identifier overflows at {width}px: scroll width is {documentWidth}px.");
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
            var preformattedBlock = page.Locator("article pre").First;

            foreach (var width in DocsSiteViewports.Widths)
            {
                await page.SetViewportSizeAsync(width, DocsSiteViewports.Height);
                var documentWidth = await page.EvaluateAsync<int>("() => document.documentElement.scrollWidth");
                Assert.True(documentWidth <= width, $"A code block causes document overflow at {width}px: scroll width is {documentWidth}px.");
                Assert.Equal("auto", await preformattedBlock.EvaluateAsync<string>("element => getComputedStyle(element).overflowX"));
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
        var page = await site.NewPageAsync(DocsSiteViewports.Phone, DocsSiteViewports.Height, hasTouch: true);

        try
        {
            await page.GotoAsync(site.Url("index.html"));
            var toggle = page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Toggle navigation" });
            Assert.True(await toggle.IsVisibleAsync());
            await toggle.TapAsync();
            var learn = page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Learn", Exact = true });
            await learn.TapAsync();
            var getStarted = page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Show pages in Get started", Exact = true });
            await getStarted.TapAsync();
            var hideGetStarted = page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Hide pages in Get started", Exact = true });
            Assert.Equal("true", await hideGetStarted.GetAttributeAsync("aria-expanded"));
            var firstLink = page.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = "Installation and first index", Exact = true });
            await firstLink.WaitForAsync();
            Assert.True(await firstLink.IsVisibleAsync());
            var documentWidth = await page.EvaluateAsync<int>("() => document.documentElement.scrollWidth");
            Assert.True(documentWidth <= DocsSiteViewports.Phone, $"Mobile navigation overflows to {documentWidth}px.");
            await firstLink.TapAsync();
            await page.WaitForFunctionAsync("() => location.pathname.endsWith('/getting-started/01-installation.html')");
            Assert.Contains("getting-started/01-installation.html", page.Url, StringComparison.Ordinal);
        }
        catch
        {
            await site.CaptureFailureAsync(page, nameof(MobileHeader_OpensNavigationMenu));
            throw;
        }
    }

    [Fact(DisplayName = "Docs Site: Header Categories Open, Navigate And Close Accessibly")]
    public async Task HeaderCategories_OpenNavigateAndCloseAccessibly()
    {
        await using var site = await BrowserSite.StartAsync();
        var page = await site.NewPageAsync(1440, DocsSiteViewports.Height);

        try
        {
            await page.GotoAsync(site.Url("index.html"));
            var navigationToggle = page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Toggle navigation" });
            if (await navigationToggle.IsVisibleAsync()) await navigationToggle.ClickAsync();
            var engine = page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Engine", Exact = true });
            await engine.WaitForAsync();
            await engine.PressAsync("Enter");
            Assert.Equal("true", await engine.GetAttributeAsync("aria-expanded"));
            var focusStyle = await engine.EvaluateAsync<string[]>("element => { const style = getComputedStyle(element); return [style.outlineStyle, style.outlineWidth]; }");
            Assert.NotEqual("none", focusStyle[0]);
            Assert.True(double.Parse(focusStyle[1].Replace("px", "", StringComparison.Ordinal), System.Globalization.CultureInfo.InvariantCulture) >= 2,
                $"The keyboard focus indicator is only {focusStyle[1]} wide.");
            var focusContrast = await engine.EvaluateAsync<double>(FocusContrastExpression);
            Assert.True(focusContrast >= 3, $"The keyboard focus indicator contrast is {focusContrast:F2}:1.");

            var queryParser = page.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = "The query parser", Exact = true });
            await queryParser.WaitForAsync();
            Assert.True(await queryParser.IsVisibleAsync());

            await page.Keyboard.PressAsync("Escape");
            Assert.Equal("false", await engine.GetAttributeAsync("aria-expanded"));
            Assert.Equal("Engine", await page.EvaluateAsync<string>("() => document.activeElement?.getAttribute('aria-label')"));

            await engine.ClickAsync();
            await page.Locator("#autocollapse .navbar-brand").ClickAsync();
            Assert.Equal("false", await engine.GetAttributeAsync("aria-expanded"));

            await engine.ClickAsync();
            await queryParser.ClickAsync();
            Assert.Contains("searching/04-query-parser.html", page.Url, StringComparison.Ordinal);
            await page.WaitForFunctionAsync("() => document.querySelector('.lc-menu-category-toggle[data-active=\"true\"]')?.textContent.includes('Engine') === true");
            Assert.Equal("page", await page.Locator(".lc-menu-page-link[aria-current='page']").GetAttributeAsync("aria-current"));
        }
        catch
        {
            await site.CaptureFailureAsync(page, nameof(HeaderCategories_OpenNavigateAndCloseAccessibly));
            throw;
        }
    }

    [Fact(DisplayName = "Docs Site: Desktop Header Shows Five Unwrapped Categories")]
    public async Task DesktopHeader_ShowsUnwrappedCategoriesAtWideViewports()
    {
        await using var site = await BrowserSite.StartAsync();
        var page = await site.NewPageAsync(DocsSiteViewports.Laptop, DocsSiteViewports.Height);

        try
        {
            await page.GotoAsync(site.Url("index.html"));
            await page.Locator(".lc-primary-nav").WaitForAsync();
            var navigationToggle = page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Toggle navigation" });
            var categoryButtons = page.Locator(".lc-menu-category-toggle");
            Assert.Equal(5, await categoryButtons.CountAsync());

            foreach (var width in new[] { DocsSiteViewports.Laptop, DocsSiteViewports.Desktop, 1920 })
            {
                await page.SetViewportSizeAsync(width, DocsSiteViewports.Height);
                Assert.False(await navigationToggle.IsVisibleAsync(), $"The navigation drawer toggle remains visible at {width}px.");
                Assert.True(await categoryButtons.First.IsVisibleAsync(), $"The category navigation is hidden at {width}px.");
                var documentWidth = await page.EvaluateAsync<int>("() => document.documentElement.scrollWidth");
                Assert.True(documentWidth <= width, $"The header overflows to {documentWidth}px at a {width}px viewport.");
                var categoryHeights = await categoryButtons.EvaluateAllAsync<double[]>("elements => elements.map(element => element.getBoundingClientRect().height)");
                Assert.All(categoryHeights, height => Assert.InRange(height, 0, 48));
            }
        }
        catch
        {
            await site.CaptureFailureAsync(page, nameof(DesktopHeader_ShowsUnwrappedCategoriesAtWideViewports));
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

    [Fact(DisplayName = "Docs Site: Header Navigation And Search Work")]
    public async Task NavigationAndSearch_AreAvailable()
    {
        await using var site = await BrowserSite.StartAsync();
        var page = await site.NewPageAsync();

        try
        {
            await page.GotoAsync(site.Url("getting-started/01-installation.html"));
            var navigationToggle = page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Toggle navigation" });
            if (await navigationToggle.IsVisibleAsync()) await navigationToggle.ClickAsync();
            await page.Locator("#navbar.lc-navigation-ready .lc-primary-nav").WaitForAsync();
            await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Learn", Exact = true }).ClickAsync();
            var installation = page.Locator(".lc-primary-nav").GetByRole(AriaRole.Link, new LocatorGetByRoleOptions { Name = "Installation and first index", Exact = true });
            await installation.WaitForAsync();
            Assert.True(await installation.IsVisibleAsync());
            Assert.DoesNotContain("Loading section navigation", await page.Locator("body").InnerTextAsync(), StringComparison.Ordinal);

            await page.WaitForFunctionAsync("() => { const input = document.querySelector('#search-query'); return input && !input.disabled; }");
            var search = page.Locator("#search-query");
            if (!await search.IsVisibleAsync())
            {
                await page.Locator("#search").ClickAsync();
            }

            (string Text, string? ExpectedRoute, bool ExpectResults)[] queries =
            [
                ("API", null, true),
                ("IndexWriter", "api/Rowles.LeanCorpus.Index.Indexer.IndexWriter.html", true),
                ("ADR041", "articles/ADRs/ADR041-physical-index-runtime-lifecycle.html", true),
                ("Rowles.Text", "analysis/08-rowles-text.html", true),
                ("IndexWriterr", null, false)
            ];

            foreach (var query in queries)
            {
                await page.GotoAsync(site.Url("index.html"));
                await page.WaitForFunctionAsync("() => { const input = document.querySelector('#search-query'); return input && !input.disabled; }");
                search = page.Locator("#search-query");
                if (!await search.IsVisibleAsync())
                {
                    await page.Locator("#search").ClickAsync();
                }

                await search.FillAsync(query.Text);
                if (query.Text == "Rowles.Text")
                {
                    Assert.Equal("Rowles Text", await search.InputValueAsync());
                }
                await page.WaitForFunctionAsync(
                    "() => { const results = document.querySelector('#search-results'); return results && (results.querySelector('.sr-item') || results.textContent.includes('No results for')); }",
                    null,
                    new PageWaitForFunctionOptions { Timeout = 15_000 });

                var resultItems = page.Locator("#search-results .sr-item");
                if (!query.ExpectResults)
                {
                    Assert.Equal(0, await resultItems.CountAsync());
                    Assert.Contains("No results for", await page.Locator("#search-results").InnerTextAsync(), StringComparison.Ordinal);
                    continue;
                }

                Assert.True(await resultItems.CountAsync() > 0, $"Search for '{query.Text}' returned no results.");
                if (query.ExpectedRoute is not null)
                {
                    var resultRoutes = await resultItems.EvaluateAllAsync<string[]>("items => items.map(item => { const link = item instanceof HTMLAnchorElement ? item : item.querySelector('a'); return link?.getAttribute('href') ?? ''; })");
                    Assert.Contains(resultRoutes, route => route.Contains(query.ExpectedRoute, StringComparison.OrdinalIgnoreCase));
                }
            }
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

    [Fact(DisplayName = "Docs Site: Article Text And Links Meet Contrast In Both Themes")]
    public async Task ArticleTextAndLinks_MeetContrastInBothThemes()
    {
        await using var site = await BrowserSite.StartAsync();
        var page = await site.NewPageAsync();

        try
        {
            await page.GotoAsync(site.Url("getting-started/01-installation.html"));
            var themeControl = page.GetByTitle("Change theme");
            var contrastRatios = await page.EvaluateAsync<double[]>(ContrastRatiosExpression);
            Assert.Equal(2, contrastRatios.Length);
            Assert.All(contrastRatios, ratio => Assert.True(ratio >= 4.5, $"Article text contrast is {ratio:F2}:1."));

            await themeControl.ClickAsync();
            await page.GetByText("Dark", new PageGetByTextOptions { Exact = true }).ClickAsync();
            await page.WaitForFunctionAsync("() => document.documentElement.dataset.bsTheme === 'dark'");
            contrastRatios = await page.EvaluateAsync<double[]>(ContrastRatiosExpression);
            Assert.Equal(2, contrastRatios.Length);
            Assert.All(contrastRatios, ratio => Assert.True(ratio >= 4.5, $"Dark theme article text contrast is {ratio:F2}:1."));
        }
        catch
        {
            await site.CaptureFailureAsync(page, nameof(ArticleTextAndLinks_MeetContrastInBothThemes));
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

            await page.GotoAsync(site.Url("benchmarks/overview.html"));
            var benchmarkOverview = await page.Locator("article").InnerTextAsync();
            Assert.Contains("No qualified benchmark results are published for this build", benchmarkOverview, StringComparison.Ordinal);
            Assert.True(await page.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = "Performance", Exact = true }).CountAsync() > 0);
            Assert.True(await page.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = "Benchmarking guide", Exact = true }).CountAsync() > 0);

            await page.GotoAsync(site.Url("analysis/08-rowles-text.html"));
            var sourceUrl = await page.Locator("meta[name='docfx:docurl']").GetAttributeAsync("content");
            var editUrl = await page.Locator("a.edit-link").GetAttributeAsync("href");
            Assert.NotNull(sourceUrl);
            Assert.Equal(sourceUrl, editUrl);
            Assert.EndsWith("/src/core/Rowles.Text/README.md", sourceUrl, StringComparison.Ordinal);
            var breadcrumb = await page.Locator("#breadcrumb").InnerTextAsync();
            Assert.Contains("Rowles.Text", breadcrumb, StringComparison.Ordinal);
            Assert.DoesNotContain("Examples", breadcrumb, StringComparison.OrdinalIgnoreCase);

            var searchIndexPath = Path.Combine(site.SiteDirectory, "index.json");
            using var searchIndex = JsonDocument.Parse(await File.ReadAllTextAsync(searchIndexPath, TestContext.Current.CancellationToken));
            var indexedRoutes = searchIndex.RootElement.EnumerateObject().Select(static entry => entry.Name).ToArray();
            Assert.Single(indexedRoutes, static route => route.Equals("analysis/08-rowles-text.html", StringComparison.Ordinal));
            Assert.DoesNotContain("generated/analysis/08-rowles-text.html", indexedRoutes);
            Assert.DoesNotContain(indexedRoutes, static route => route.EndsWith("toc.html", StringComparison.OrdinalIgnoreCase));
            var supportTocs = Directory.EnumerateFiles(site.SiteDirectory, "toc.html", SearchOption.AllDirectories).ToArray();
            Assert.NotEmpty(supportTocs);
            Assert.Equal(0, await page.Locator(".lc-primary-nav a[href*='toc.html']").CountAsync());
            Assert.False(File.Exists(Path.Combine(site.SiteDirectory, "generated", "analysis", "08-rowles-text.html")));

            foreach (var import in ExpectedImports)
            {
                var response = await page.GotoAsync(site.Url(import.Route), new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
                Assert.NotNull(response);
                Assert.Equal(200, response.Status);
                var importedSourceUrl = await page.Locator("meta[name='docfx:docurl']").GetAttributeAsync("content");
                var importedEditUrl = await page.Locator("a.edit-link").GetAttributeAsync("href");
                Assert.NotNull(importedSourceUrl);
                Assert.Equal(importedSourceUrl, importedEditUrl);
                Assert.Contains("/blob/", importedSourceUrl, StringComparison.Ordinal);
                Assert.EndsWith($"/{import.SourcePath}", importedSourceUrl, StringComparison.Ordinal);
            }

            var redirectPath = Path.Combine(site.SiteDirectory, "_redirects");
            Assert.True(File.Exists(redirectPath), "The Cloudflare Pages redirect manifest was not copied to the site output.");
            var redirectRules = File.ReadAllLines(redirectPath)
                .Where(static line => !string.IsNullOrWhiteSpace(line) && !line.TrimStart().StartsWith('#'))
                .Select(static line => line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
                .ToArray();
            Assert.All(redirectRules, static rule => Assert.Equal(3, rule.Length));
            var redirectBySource = redirectRules.ToDictionary(static rule => rule[0], static rule => (rule[1], rule[2]), StringComparer.Ordinal);
            Assert.Equal(LegacyImportRoutes.Length * 2, redirectBySource.Count);
            foreach (var route in LegacyImportRoutes)
            {
                foreach (var suffix in new[] { string.Empty, ".html" })
                {
                    var source = $"/generated/{route.LegacyPath}{suffix}";
                    Assert.True(redirectBySource.TryGetValue(source, out var redirect), $"Missing legacy route redirect: {source}");
                    Assert.Equal($"/{route.CanonicalPath}.html", redirect.Item1);
                    Assert.Equal("301", redirect.Item2);
                }
                Assert.Contains($"{route.CanonicalPath}.html", indexedRoutes);
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

            await page.GotoAsync(site.Url("getting-started/01-installation.html"));
            var articleTopBeforeNavigation = await page.Locator("article h1").EvaluateAsync<double>("element => element.getBoundingClientRect().top");
            await page.WaitForFunctionAsync("() => document.querySelector('#navbar.lc-navigation-ready .lc-primary-nav') !== null");
            await page.WaitForTimeoutAsync(100);
            var articleTopAfterNavigation = await page.Locator("article h1").EvaluateAsync<double>("element => element.getBoundingClientRect().top");
            Assert.InRange(Math.Abs(articleTopAfterNavigation - articleTopBeforeNavigation), 0, 1);
            var activeNavigation = page.Locator(".lc-menu-page-link[aria-current='page']");
            var activeCount = await activeNavigation.CountAsync();
            var navigationState = await page.Locator("#navbar").EvaluateAsync<string>("element => Array.from(element.querySelectorAll('.lc-menu-category-toggle, .lc-menu-section, .lc-menu-page-link')).map(item => `${item.textContent.trim()}|${item.className}|${item.getAttribute('aria-current') || item.dataset.active || ''}`).join('; ')");
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
                    if (await navigationToggle.IsVisibleAsync()) await navigationToggle.ClickAsync();
                    await page.GetByTitle("Change theme").ClickAsync();
                    await page.GetByText("Dark", new PageGetByTextOptions { Exact = true }).ClickAsync();
                    await page.WaitForFunctionAsync("() => document.documentElement.dataset.bsTheme === 'dark'");
                    if (await navigationToggle.IsVisibleAsync() && string.Equals(await navigationToggle.GetAttributeAsync("aria-expanded"), "true", StringComparison.Ordinal))
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
        var baselinePath = GetVisualBaselinePath(repositoryRoot, name);
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

        Assert.True(changedPixelRatio <= 0.005, $"Visual baseline '{name}' at '{baselinePath}' differs in {changedPixelRatio:P2} of pixels (limit 0.50%). Actual screenshot: '{actualPath}'.");
    }

    private static string GetVisualBaselinePath(string repositoryRoot, string name)
    {
        var baselineDirectory = Path.Combine(repositoryRoot, "src", "devops", "Rowles.LeanCorpus.Tests.Docs", "VisualBaselines");
        var defaultPath = Path.Combine(baselineDirectory, $"{name}.png");
        var updateBaselines = string.Equals(Environment.GetEnvironmentVariable("LEAN_CORPUS_UPDATE_DOCS_VISUAL_BASELINES"), "1", StringComparison.Ordinal);
        var variant = GetVisualBaselineVariant();
        if (variant is null)
        {
            return defaultPath;
        }

        var variantPath = Path.Combine(baselineDirectory, $"{name}.{variant}.png");
        return File.Exists(variantPath) || updateBaselines ? variantPath : defaultPath;
    }

    private static string? GetVisualBaselineVariant()
    {
        if (!OperatingSystem.IsLinux() || !File.Exists("/etc/os-release"))
        {
            return null;
        }

        string? id = null;
        string? versionId = null;
        foreach (var line in File.ReadLines("/etc/os-release"))
        {
            if (line.StartsWith("ID=", StringComparison.Ordinal))
            {
                id = line[3..].Trim().Trim('"');
            }
            else if (line.StartsWith("VERSION_ID=", StringComparison.Ordinal))
            {
                versionId = line["VERSION_ID=".Length..].Trim().Trim('"');
            }
        }

        return string.Equals(id, "ubuntu", StringComparison.OrdinalIgnoreCase)
            && string.Equals(versionId, "24.04", StringComparison.Ordinal)
                ? "ubuntu-24.04"
                : null;
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
            throw new DirectoryNotFoundException($"Generated docs site not found at '{siteDirectory}'. Run './devops docs build' first.");
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

    public async Task<IPage> NewPageAsync(int width = 1280, int height = 900, bool javaScriptEnabled = true, bool hasTouch = false)
    {
        var context = await _browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = width, Height = height },
            JavaScriptEnabled = javaScriptEnabled,
            HasTouch = hasTouch,
            IsMobile = hasTouch
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

        var diagnosticsPath = Path.Combine(outputDirectory, $"{testName}.browser-fonts.json");
        try
        {
            var session = await page.Context.NewCDPSessionAsync(page);
            try
            {
                await session.SendAsync("DOM.enable", new Dictionary<string, object>());
                await session.SendAsync("CSS.enable", new Dictionary<string, object>());

                var document = await session.SendAsync("DOM.getDocument", new Dictionary<string, object>())
                    ?? throw new InvalidOperationException("Chromium did not return the document node.");
                var rootNodeId = document.GetProperty("root").GetProperty("nodeId").GetInt32();
                var body = await session.SendAsync("DOM.querySelector", new Dictionary<string, object>
                {
                    ["nodeId"] = rootNodeId,
                    ["selector"] = "body"
                }) ?? throw new InvalidOperationException("Chromium did not return the body node.");
                var bodyNodeId = body.GetProperty("nodeId").GetInt32();
                var nav = await session.SendAsync("DOM.querySelector", new Dictionary<string, object>
                {
                    ["nodeId"] = rootNodeId,
                    ["selector"] = "#autocollapse"
                }) ?? throw new InvalidOperationException("Chromium did not return the navbar node.");
                var article = await session.SendAsync("DOM.querySelector", new Dictionary<string, object>
                {
                    ["nodeId"] = rootNodeId,
                    ["selector"] = "article"
                }) ?? throw new InvalidOperationException("Chromium did not return the article node.");
                var pageMetrics = await page.EvaluateAsync<string>(
                    "() => JSON.stringify({ userAgent: navigator.userAgent, viewportWidth: innerWidth, documentWidth: document.documentElement.scrollWidth, bodyFontFamily: getComputedStyle(document.body).fontFamily, bodyFontSize: getComputedStyle(document.body).fontSize })");
                using var pageDocument = JsonDocument.Parse(pageMetrics);
                var platformFonts = new
                {
                    body = await GetPlatformFontsAsync(session, bodyNodeId),
                    navbar = await GetPlatformFontsAsync(session, nav.GetProperty("nodeId").GetInt32()),
                    article = await GetPlatformFontsAsync(session, article.GetProperty("nodeId").GetInt32())
                };

                var diagnostics = JsonSerializer.Serialize(new
                {
                    page = pageDocument.RootElement,
                    platformFonts
                }, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(diagnosticsPath, diagnostics);
            }
            finally
            {
                await session.DetachAsync();
            }
        }
        catch (Exception exception)
        {
            await File.WriteAllTextAsync(diagnosticsPath, $"Browser font diagnostics failed: {exception}");
        }
    }

    private static async Task<JsonElement> GetPlatformFontsAsync(ICDPSession session, int nodeId)
    {
        var result = await session.SendAsync("CSS.getPlatformFontsForNode", new Dictionary<string, object>
        {
            ["nodeId"] = nodeId
        });
        return result ?? throw new InvalidOperationException("Chromium did not return platform-font details.");
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
            if (File.Exists(Path.Combine(directory.FullName, "global.json")) &&
                File.Exists(Path.Combine(directory.FullName, "Directory.Build.props")) &&
                File.Exists(Path.Combine(directory.FullName, "src", "Rowles.LeanCorpus.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("Could not locate the LeanCorpus repository markers from the test output directory.");
    }
}
