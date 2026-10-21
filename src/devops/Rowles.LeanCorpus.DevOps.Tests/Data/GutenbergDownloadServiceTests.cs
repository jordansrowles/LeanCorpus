using System.Net;
using System.Text;
using System.Text.Json;
using Rowles.LeanCorpus.DevOps.Data;
using Rowles.LeanCorpus.DevOps.Terminal;

namespace Rowles.LeanCorpus.DevOps.Tests.Data;

public sealed class GutenbergDownloadServiceTests
{
    [Fact]
    public async Task FallsBackAcrossTheCuratedUrlsAndPromotesOnlyCompleteContent()
    {
        using var directory = new TemporaryDirectory();
        var requestedUris = new List<string>();
        using var client = new HttpClient(new StubHttpMessageHandler(request =>
        {
            requestedUris.Add(request.RequestUri!.AbsoluteUri);
            return request.RequestUri.AbsolutePath.EndsWith("/9-0.txt", StringComparison.Ordinal)
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("hamlet fixture", Encoding.UTF8) };
        }));
        var service = new GutenbergDownloadService(client, delay: static _ => Task.CompletedTask);

        var result = await service.DownloadAsync(new GutenbergDownloadRequest
        {
            RepositoryRoot = directory.Path,
            BookCount = 1,
            OutputDirectory = "books"
        }, TestContext.Current.CancellationToken);

        Assert.Equal(OutcomeKind.Success, result.CommandResult.Outcome);
        Assert.Equal(2, requestedUris.Count);
        Assert.EndsWith("/files/9/9-0.txt", requestedUris[0], StringComparison.Ordinal);
        Assert.EndsWith("/files/9/9.txt", requestedUris[1], StringComparison.Ordinal);
        Assert.Equal("hamlet fixture", await File.ReadAllTextAsync(Path.Combine(directory.Path, "books", "9.txt"), TestContext.Current.CancellationToken));
        Assert.True(File.Exists(result.ManifestPath));
        Assert.False(File.Exists(Path.Combine(directory.Path, "books", "9.txt.partial")));
        using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(result.ManifestPath, TestContext.Current.CancellationToken));
        Assert.Equal(1, manifest.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(1, manifest.RootElement.GetProperty("downloadedCount").GetInt32());
        Assert.Equal("2e48ed411d861a76707dfa0bd0f83e71c47bbddceb4eccd8ee7f27fa22ac2cfe",
            manifest.RootElement.GetProperty("books")[0].GetProperty("sha256").GetString());
    }

    [Fact]
    public async Task ReportsAnUnavailableBookAndRetainsNoPromotedOrPartialFile()
    {
        using var directory = new TemporaryDirectory();
        using var client = new HttpClient(new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound)));
        var service = new GutenbergDownloadService(client, delay: static _ => Task.CompletedTask);

        var result = await service.DownloadAsync(new GutenbergDownloadRequest
        {
            RepositoryRoot = directory.Path,
            BookCount = 1,
            OutputDirectory = "books"
        }, TestContext.Current.CancellationToken);

        Assert.Equal(OutcomeKind.CommandFailure, result.CommandResult.Outcome);
        Assert.Equal(new[] { 9 }, result.FailedBookIds);
        Assert.False(File.Exists(Path.Combine(directory.Path, "books", "9.txt")));
        Assert.False(File.Exists(Path.Combine(directory.Path, "books", "9.txt.partial")));
        Assert.True(File.Exists(result.ManifestPath));
    }

    [Fact]
    public void ReadsQuotedCatalogueRowsAndReturnsTheLowestEligibleIds()
    {
        const string csv = """
            Text#,Language,Title
            5,en,"A ""quoted"" title, with comma"
            1,en,Excluded
            9,en,Nine
            3,en,Three
            2,fr,French
            """;
        using var reader = new StringReader(csv);

        var result = GutenbergDownloadService.ReadCatalogueIds(reader, new HashSet<int> { 1 }, 2);

        Assert.Equal(new[] { 3, 5 }, result);
    }

    [Fact]
    public async Task RejectsInvalidBookCountBeforeCreatingOutput()
    {
        using var directory = new TemporaryDirectory();
        using var client = new HttpClient(new StubHttpMessageHandler(_ => throw new InvalidOperationException("No request was expected.")));
        var service = new GutenbergDownloadService(client);

        var result = await service.DownloadAsync(new GutenbergDownloadRequest
        {
            RepositoryRoot = directory.Path,
            BookCount = 0,
            OutputDirectory = "books"
        }, TestContext.Current.CancellationToken);

        Assert.Equal(OutcomeKind.ValidationFailure, result.CommandResult.Outcome);
        Assert.False(Directory.Exists(Path.Combine(directory.Path, "books")));
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(responseFactory(request));
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "leancorpus-gutenberg-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
