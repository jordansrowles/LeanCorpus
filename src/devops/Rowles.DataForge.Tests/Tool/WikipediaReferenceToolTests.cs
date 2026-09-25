using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using Rowles.DataForge.Tool;
using Rowles.DataForge.Workloads;
using SharpCompress.Compressors;
using SharpCompress.Compressors.BZip2;

namespace Rowles.DataForge.Tests.Tool;

public sealed class WikipediaReferenceToolTests
{
    [Fact]
    public void Resumes_a_partial_file_at_the_exact_byte_boundary()
    {
        WithTempDirectory(root =>
        {
            var path = Path.Combine(root, WikipediaReferenceContract.PrimaryFilename);
            File.WriteAllText(path + ".partial", "abc", Encoding.ASCII);
            var handler = new StubHandler((request, _) =>
            {
                Assert.Equal(3L, request.Headers.Range?.Ranges.Single().From);
                Assert.Contains(request.Headers.UserAgent, static value => value.Product?.Name == "Rowles.DataForge.WikipediaReference");
                var response = Response(HttpStatusCode.PartialContent, "def");
                response.Content.Headers.ContentRange = new ContentRangeHeaderValue(3, 5, 6);
                return Task.FromResult(response);
            });
            using var client = new HttpClient(handler);
            var expectedHash = Sha1("abcdef");

            new WikipediaReferenceDownloader(client).DownloadVerifiedFileAsync(
                WikipediaReferenceContract.PrimaryFilename, path, 6, expectedHash, false, CancellationToken.None).GetAwaiter().GetResult();

            Assert.Equal("abcdef", File.ReadAllText(path, Encoding.ASCII));
            Assert.False(File.Exists(path + ".partial"));
        });
    }

    [Fact]
    public void Restarts_when_the_server_ignores_a_range_request()
    {
        WithTempDirectory(root =>
        {
            var path = Path.Combine(root, WikipediaReferenceContract.IndexFilename);
            File.WriteAllText(path + ".partial", "stale", Encoding.ASCII);
            var handler = new StubHandler((request, _) =>
            {
                Assert.NotNull(request.Headers.Range);
                return Task.FromResult(Response(HttpStatusCode.OK, "fresh!"));
            });
            using var client = new HttpClient(handler);

            new WikipediaReferenceDownloader(client).DownloadVerifiedFileAsync(
                WikipediaReferenceContract.IndexFilename, path, 6, Sha1("fresh!"), false, CancellationToken.None).GetAwaiter().GetResult();

            Assert.Equal("fresh!", File.ReadAllText(path, Encoding.ASCII));
        });
    }

    [Fact]
    public void Retains_truncated_responses_and_quarantines_checksum_mismatches()
    {
        WithTempDirectory(root =>
        {
            var truncatedPath = Path.Combine(root, WikipediaReferenceContract.IndexFilename);
            var truncatedHandler = new StubHandler((_, _) => Task.FromResult(Response(HttpStatusCode.OK, "abc")));
            using (var client = new HttpClient(truncatedHandler))
            {
                Assert.Throws<InvalidDataException>(() => new WikipediaReferenceDownloader(client).DownloadVerifiedFileAsync(
                    WikipediaReferenceContract.IndexFilename, truncatedPath, 6, Sha1("abcdef"), false, CancellationToken.None).GetAwaiter().GetResult());
            }
            Assert.Equal("abc", File.ReadAllText(truncatedPath + ".partial", Encoding.ASCII));

            var badPath = Path.Combine(root, WikipediaReferenceContract.PrimaryFilename);
            var badHandler = new StubHandler((_, _) => Task.FromResult(Response(HttpStatusCode.OK, "badbad")));
            using (var client = new HttpClient(badHandler))
            {
                Assert.Throws<InvalidDataException>(() => new WikipediaReferenceDownloader(client).DownloadVerifiedFileAsync(
                    WikipediaReferenceContract.PrimaryFilename, badPath, 6, Sha1("good!!"), false, CancellationToken.None).GetAwaiter().GetResult());
            }
            Assert.False(File.Exists(badPath));
            Assert.Single(Directory.EnumerateFiles(root, Path.GetFileName(badPath) + ".partial.invalid-*"));
        });
    }

    [Fact]
    public void Preserves_a_partial_file_on_cancellation_and_rejects_external_redirects()
    {
        WithTempDirectory(root =>
        {
            var path = Path.Combine(root, WikipediaReferenceContract.IndexFilename);
            File.WriteAllText(path + ".partial", "resume", Encoding.ASCII);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            using (var client = new HttpClient(new StubHandler((_, _) => Task.FromResult(Response(HttpStatusCode.OK, "ignored")))))
                Assert.ThrowsAny<OperationCanceledException>(() => new WikipediaReferenceDownloader(client).DownloadVerifiedFileAsync(
                    WikipediaReferenceContract.IndexFilename, path, 10, Sha1("resume---"), false, cancellation.Token).GetAwaiter().GetResult());
            Assert.Equal("resume", File.ReadAllText(path + ".partial", Encoding.ASCII));

            var redirectHandler = new StubHandler((_, _) =>
            {
                var response = new HttpResponseMessage(HttpStatusCode.Redirect);
                response.Headers.Location = new Uri("https://example.test/file");
                return Task.FromResult(response);
            });
            using var redirectClient = new HttpClient(redirectHandler);
            Assert.Throws<InvalidDataException>(() => new WikipediaReferenceDownloader(redirectClient).DownloadVerifiedFileAsync(
                WikipediaReferenceContract.IndexFilename, Path.Combine(root, "redirected"), 1, Sha1("x"), false, CancellationToken.None).GetAwaiter().GetResult());
        });
    }

    [Fact]
    public void Extracts_requested_pages_from_three_bounded_concatenated_members()
    {
        WithTempDirectory(root =>
        {
            var pages = new[] { PageXml(11), PageXml(22), PageXml(33) };
            var (dump, offsets) = CompressMembers(pages);
            var path = Path.Combine(root, WikipediaReferenceContract.PrimaryFilename);
            File.WriteAllBytes(path, dump);
            var candidates = new[]
            {
                Candidate(offsets[0], 11), Candidate(offsets[1], 22), Candidate(offsets[2], 33)
            };
            var found = new Dictionary<ulong, WikipediaPageRevision>();

            var readOffsets = new WikipediaPageExtractor().ExtractEach(path, offsets, candidates, page => found.Add(page.PageId, page));

            Assert.Equal(offsets, readOffsets);
            Assert.Equal(new ulong[] { 11, 22, 33 }, found.Keys.Order());
            Assert.Equal("Title 22", found[22].Title);
            Assert.Equal(111UL, found[11].RevisionId);
        });
    }

    [Fact]
    public void Rejects_candidates_missing_from_their_stream_and_members_over_the_test_bound()
    {
        WithTempDirectory(root =>
        {
            var (dump, offsets) = CompressMembers([PageXml(11)]);
            var path = Path.Combine(root, WikipediaReferenceContract.PrimaryFilename);
            File.WriteAllBytes(path, dump);
            Assert.Throws<InvalidDataException>(() => new WikipediaPageExtractor().ExtractEach(path, offsets,
                [Candidate(0, 99)], _ => { }));
            Assert.Throws<InvalidDataException>(() => new WikipediaPageExtractor(10).ExtractEach(path, offsets,
                [Candidate(0, 11)], _ => { }));
        });
    }

    [Fact]
    public void Prohibits_dtd_and_entity_expansion_in_page_fragments()
    {
        const string xml = "<!DOCTYPE page [<!ENTITY value 'expanded'>]><page><title>&value;</title><ns>0</ns><id>1</id></page>";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));

        Assert.ThrowsAny<System.Xml.XmlException>(() => WikipediaPageXmlParser.Parse(stream, new HashSet<ulong> { 1 }).ToArray());
    }

    [Fact]
    public void Miniature_reference_build_is_byte_reproducible_and_verifies_without_source_files()
    {
        WithTempDirectory(root =>
        {
            var cache = Path.Combine(root, "cache");
            Directory.CreateDirectory(cache);
            var pages = new[] { PageXml(11), PageXml(22), PageXml(33), PageXml(44) };
            var (dump, offsets) = CompressMembers(pages);
            File.WriteAllBytes(Path.Combine(cache, WikipediaReferenceContract.PrimaryFilename), dump);
            var indexText = string.Join('\n', Enumerable.Range(0, pages.Length).Select(index => $"{offsets[index]}:{new[] { 11, 22, 33, 44 }[index]}:Fixture title {index}"));
            var indexBytes = CompressMembers([indexText]).Bytes;
            File.WriteAllBytes(Path.Combine(cache, WikipediaReferenceContract.IndexFilename), indexBytes);
            var first = Path.Combine(root, "reference-a");
            var second = Path.Combine(root, "reference-b");

            var firstResult = WikipediaReferenceBuilder.BuildFixture(cache, first, 2);
            var secondResult = WikipediaReferenceBuilder.BuildFixture(cache, second, 2);

            Assert.Equal(firstResult.Manifest.ContentSha256, secondResult.Manifest.ContentSha256);
            Assert.Equal(File.ReadAllBytes(Path.Combine(first, "records.ndjson")), File.ReadAllBytes(Path.Combine(second, "records.ndjson")));
            Assert.Equal(File.ReadAllBytes(Path.Combine(first, "manifest.json")), File.ReadAllBytes(Path.Combine(second, "manifest.json")));
            Assert.Equal(File.ReadAllBytes(Path.Combine(first, "LICENSES", "attribution.txt")), File.ReadAllBytes(Path.Combine(second, "LICENSES", "attribution.txt")));
            Assert.Equal(2, firstResult.SelectedCount);
            Assert.Equal(2, WikipediaReferenceVerifier.Verify(first, 2).RecordCount);

            var copied = Path.Combine(root, "copied-reference");
            CopyDirectory(first, copied);
            Directory.Delete(cache, recursive: true);
            Assert.Equal(firstResult.Manifest.ContentSha256, WikipediaReferenceVerifier.Verify(copied, 2).ContentSha256);
        });
    }

    private static WikipediaCandidate Candidate(long offset, ulong pageId) => new(new WikipediaIndexEntry(offset, pageId, $"Title {pageId}"));

    private static string PageXml(ulong pageId)
    {
        var text = string.Concat(Enumerable.Repeat("wordtoken ", 50));
        return $"<page><title>Title {pageId}</title><ns>0</ns><id>{pageId}</id><revision><id>{pageId + 100}</id><timestamp>2026-09-01T00:00:00Z</timestamp><text>{text}</text></revision></page>";
    }

    private static (byte[] Bytes, long[] Offsets) CompressMembers(IReadOnlyList<string> members)
    {
        using var output = new MemoryStream();
        var offsets = new long[members.Count];
        for (var index = 0; index < members.Count; index++)
        {
            offsets[index] = output.Position;
            using (var compressor = BZip2Stream.Create(output, CompressionMode.Compress, decompressConcatenated: false, leaveOpen: true))
            {
                var bytes = Encoding.UTF8.GetBytes(members[index]);
                compressor.Write(bytes);
            }
        }
        return (output.ToArray(), offsets);
    }

    private static HttpResponseMessage Response(HttpStatusCode status, string content)
    {
        var response = new HttpResponseMessage(status) { Content = new ByteArrayContent(Encoding.ASCII.GetBytes(content)) };
        return response;
    }

    private static string Sha1(string value) => Convert.ToHexString(SHA1.HashData(Encoding.ASCII.GetBytes(value))).ToLowerInvariant();

    private static void WithTempDirectory(Action<string> action)
    {
        var root = Path.Combine(Path.GetTempPath(), "dataforge-wikipedia-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try { action(root); }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.GetFiles(source))
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
        foreach (var directory in Directory.GetDirectories(source))
            CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
    }

    private sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken);
    }
}
