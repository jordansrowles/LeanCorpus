using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Rowles.DataForge;
using Rowles.DataForge.Tool;
using Rowles.DataForge.Workloads;
using SharpCompress.Compressors;
using SharpCompress.Compressors.BZip2;

namespace Rowles.DataForge.Tests.Tool;

public sealed class WikipediaReferenceToolTests
{
    [Fact]
    public void Accepts_completed_pinned_articles_multistream_job_and_files()
    {
        WithTempDirectory(root =>
        {
            var path = Path.Combine(root, "dumpstatus.json");
            File.WriteAllText(path, CreateDumpStatus(jobStatus: "done", includePrimary: true, includeIndex: true));

            WikipediaReferenceDownloader.ValidateDumpStatus(path);
        });
    }

    [Theory]
    [InlineData("running")]
    [InlineData("failed")]
    [InlineData("unknown")]
    public void Rejects_required_filenames_in_an_incomplete_or_unknown_job(string status)
    {
        WithTempDirectory(root =>
        {
            var path = Path.Combine(root, "dumpstatus.json");
            File.WriteAllText(path, CreateDumpStatus(status, includePrimary: true, includeIndex: true));

            Assert.Throws<InvalidDataException>(() => WikipediaReferenceDownloader.ValidateDumpStatus(path));
        });
    }

    [Fact]
    public void Rejects_a_completed_job_with_a_missing_index_file()
    {
        WithTempDirectory(root =>
        {
            var path = Path.Combine(root, "dumpstatus.json");
            File.WriteAllText(path, CreateDumpStatus("done", includePrimary: true, includeIndex: false));

            Assert.Throws<InvalidDataException>(() => WikipediaReferenceDownloader.ValidateDumpStatus(path));
        });
    }

    [Fact]
    public void Rejects_similar_files_in_an_unrelated_job()
    {
        WithTempDirectory(root =>
        {
            var path = Path.Combine(root, "dumpstatus.json");
            File.WriteAllText(path, CreateDumpStatus("done", includePrimary: true, includeIndex: true, jobName: "unrelatedjob"));

            Assert.Throws<InvalidDataException>(() => WikipediaReferenceDownloader.ValidateDumpStatus(path));
        });
    }

    [Fact]
    public void Rejects_malformed_dumpstatus_json()
    {
        WithTempDirectory(root =>
        {
            var path = Path.Combine(root, "dumpstatus.json");
            File.WriteAllText(path, "{\"jobs\":");

            Assert.Throws<InvalidDataException>(() => WikipediaReferenceDownloader.ValidateDumpStatus(path));
        });
    }

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
    public void Quarantines_an_index_file_with_the_wrong_published_sha1()
    {
        WithTempDirectory(root =>
        {
            var path = Path.Combine(root, WikipediaReferenceContract.IndexFilename);
            using var client = new HttpClient(new StubHandler((_, _) => Task.FromResult(Response(HttpStatusCode.OK, "wrong"))));

            Assert.Throws<InvalidDataException>(() => new WikipediaReferenceDownloader(client).DownloadVerifiedFileAsync(
                WikipediaReferenceContract.IndexFilename, path, 5, Sha1("right"), false, CancellationToken.None).GetAwaiter().GetResult());

            Assert.False(File.Exists(path));
            Assert.Single(Directory.EnumerateFiles(root, Path.GetFileName(path) + ".partial.invalid-*"));
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
    public void Extracts_multiple_candidates_sharing_one_multistream_offset()
    {
        WithTempDirectory(root =>
        {
            var xml = PageXml(11) + PageXml(22);
            var (dump, offsets) = CompressMembers([xml]);
            var path = Path.Combine(root, WikipediaReferenceContract.PrimaryFilename);
            File.WriteAllBytes(path, dump);
            var found = new Dictionary<ulong, WikipediaPageRevision>();

            var readOffsets = new WikipediaPageExtractor().ExtractEach(path, offsets,
                [Candidate(offsets[0], 11), Candidate(offsets[0], 22)], page => found.Add(page.PageId, page));

            Assert.Equal(new long[] { 0 }, readOffsets);
            Assert.Equal(new ulong[] { 11, 22 }, found.Keys.Order());
        });
    }

    [Fact]
    public void Fails_on_corrupt_bzip2_before_accepting_a_page()
    {
        WithTempDirectory(root =>
        {
            var path = Path.Combine(root, WikipediaReferenceContract.PrimaryFilename);
            File.WriteAllBytes(path, [0x42, 0x5A, 0x68, 0x39, 0x00, 0x01, 0x02, 0x03]);
            var accepted = 0;

            var exception = Record.Exception(() => new WikipediaPageExtractor().ExtractEach(path, [0], [Candidate(0, 11)], _ => accepted++));

            Assert.NotNull(exception);
            Assert.IsNotType<OutOfMemoryException>(exception);
            Assert.Equal(0, accepted);
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
    public void Rejects_malformed_page_xml()
    {
        const string xml = "<page><title>Title 11</title><ns>0</ns><id>11</id><revision><id>111</id><text>unfinished";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));

        Assert.ThrowsAny<System.Xml.XmlException>(() => WikipediaPageXmlParser.Parse(stream, new HashSet<ulong> { 11 }).ToArray());
    }

    [Fact]
    public void Retains_pages_with_missing_revision_or_text_as_ineligible_shapes()
    {
        const string missingRevision = "<page><title>Title 11</title><ns>0</ns><id>11</id></page>";
        const string missingText = "<page><title>Title 22</title><ns>0</ns><id>22</id><revision><id>122</id><timestamp>2026-09-01T00:00:00Z</timestamp></revision></page>";

        var noRevision = ParsePage(missingRevision, 11);
        var noText = ParsePage(missingText, 22);

        Assert.Equal(0UL, noRevision.RevisionId);
        Assert.Null(noRevision.RawText);
        Assert.Equal(122UL, noText.RevisionId);
        Assert.Null(noText.RawText);
    }

    [Fact]
    public void Caps_page_text_while_reading_xml()
    {
        var xml = $"<page><title>Title 11</title><ns>0</ns><id>11</id><revision><id>111</id><timestamp>2026-09-01T00:00:00Z</timestamp><text>{new string('x', 4 * 1024 * 1024 + 1)}</text></revision></page>";
        var page = ParsePage(xml, 11);

        Assert.True(page.RawTextTooLarge);
        Assert.Null(page.RawText);
    }

    [Fact]
    public void Extracts_namespace_and_redirect_fields_from_a_page()
    {
        var page = ParsePage(PageXml(11, namespaceId: 1, hasRedirect: true), 11);

        Assert.Equal(1, page.NamespaceId);
        Assert.True(page.HasRedirect);
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

    [Fact]
    public void Reference_verifier_rejects_record_manifest_and_licence_mutations()
    {
        WithTempDirectory(root =>
        {
            var reference = BuildMiniatureReference(root);

            Assert.Throws<InvalidDataException>(() => WikipediaReferenceVerifier.Verify(reference, 3));

            var wrongTextHash = CopyForMutation(reference, root, "wrong-text-hash");
            var wrongHashLines = ReadRecordLines(wrongTextHash);
            var wrongHashRecord = JsonSerializer.Deserialize<WikipediaReferenceRecord>(wrongHashLines[0])!;
            wrongHashLines[0] = SerialiseRecord(wrongHashRecord with { TextSha256 = new string('0', 64) });
            WriteRecordLines(wrongTextHash, wrongHashLines);
            RepairManifestRecordIntegrity(wrongTextHash);
            var textHashException = Assert.Throws<InvalidDataException>(() => WikipediaReferenceVerifier.Verify(wrongTextHash, 2));
            Assert.Contains("TextSha256", textHashException.Message, StringComparison.Ordinal);

            var duplicatePageId = CopyForMutation(reference, root, "duplicate-page-id");
            var duplicateLines = ReadRecordLines(duplicatePageId);
            var firstRecord = JsonSerializer.Deserialize<WikipediaReferenceRecord>(duplicateLines[0])!;
            var secondRecord = JsonSerializer.Deserialize<WikipediaReferenceRecord>(duplicateLines[1])!;
            duplicateLines[1] = SerialiseRecord(secondRecord with { PageId = firstRecord.PageId });
            WriteRecordLines(duplicatePageId, duplicateLines);
            RepairManifestRecordIntegrity(duplicatePageId);
            Assert.Contains("duplicate", Assert.Throws<InvalidDataException>(() => WikipediaReferenceVerifier.Verify(duplicatePageId, 2)).Message, StringComparison.OrdinalIgnoreCase);

            var wrongContentHash = CopyForMutation(reference, root, "wrong-content-hash");
            RewriteManifest(wrongContentHash, manifest => manifest with { ContentSha256 = new string('0', 64) });
            Assert.Throws<InvalidDataException>(() => WikipediaReferenceVerifier.Verify(wrongContentHash, 2));

            var wrongArtefactHash = CopyForMutation(reference, root, "wrong-artefact-hash");
            RewriteManifest(wrongArtefactHash, manifest => manifest with { ArtefactSha256 = new string('0', 64) });
            Assert.Throws<InvalidDataException>(() => WikipediaReferenceVerifier.Verify(wrongArtefactHash, 2));

            var wrongDatasetVersion = CopyForMutation(reference, root, "wrong-dataset-version");
            RewriteSourceValue(wrongDatasetVersion, "datasetVersion", "2");
            Assert.Throws<InvalidDataException>(() => WikipediaReferenceVerifier.Verify(wrongDatasetVersion, 2));

            var wrongDumpDate = CopyForMutation(reference, root, "wrong-dump-date");
            RewriteSourceValue(wrongDumpDate, "dumpDate", "20260902");
            Assert.Throws<InvalidDataException>(() => WikipediaReferenceVerifier.Verify(wrongDumpDate, 2));

            var malformedRecords = CopyForMutation(reference, root, "malformed-record-json");
            var malformedLines = ReadRecordLines(malformedRecords);
            malformedLines[0] = "{\"Id\":}";
            WriteRecordLines(malformedRecords, malformedLines);
            RepairManifestRecordIntegrity(malformedRecords);
            Assert.Throws<InvalidDataException>(() => WikipediaReferenceVerifier.Verify(malformedRecords, 2));

            var reorderedRecords = CopyForMutation(reference, root, "reordered-records");
            var reorderedLines = ReadRecordLines(reorderedRecords);
            (reorderedLines[0], reorderedLines[1]) = (reorderedLines[1], reorderedLines[0]);
            WriteRecordLines(reorderedRecords, reorderedLines);
            Assert.Throws<InvalidDataException>(() => WikipediaReferenceVerifier.Verify(reorderedRecords, 2));

            var missingLicence = CopyForMutation(reference, root, "missing-licence");
            File.Delete(Path.Combine(missingLicence, "LICENSES", "CC-BY-SA-4.0.txt"));
            Assert.Throws<FileNotFoundException>(() => WikipediaReferenceVerifier.Verify(missingLicence, 2));

            var missingAttribution = CopyForMutation(reference, root, "missing-attribution");
            File.Delete(Path.Combine(missingAttribution, "LICENSES", "attribution.txt"));
            Assert.Throws<FileNotFoundException>(() => WikipediaReferenceVerifier.Verify(missingAttribution, 2));
        });
    }

    [Fact]
    public void Candidate_limit_expansion_matches_brute_force_and_output_ignores_stream_order()
    {
        WithTempDirectory(root =>
        {
            var cache = Path.Combine(root, "selection-cache");
            Directory.CreateDirectory(cache);
            var pageIds = new ulong[] { 1, 2, 3, 4 };
            var ranked = pageIds.Select(pageId => new WikipediaCandidate(new WikipediaIndexEntry(0, pageId, $"Title {pageId}")))
                .OrderBy(static candidate => candidate)
                .ToArray();
            var rejectedIds = ranked.Take(2).Select(static candidate => candidate.Entry.PageId).ToHashSet();
            var streamOrder = ranked.Reverse().Select(static candidate => candidate.Entry.PageId).ToArray();
            var pages = streamOrder.Select(pageId => PageXml(pageId, rejectedIds.Contains(pageId) ? 1 : 0)).ToArray();
            var (dump, offsets) = CompressMembers(pages);
            File.WriteAllBytes(Path.Combine(cache, WikipediaReferenceContract.PrimaryFilename), dump);
            var indexText = string.Join('\n', streamOrder.Select((pageId, index) => $"{offsets[index]}:{pageId}:Title {pageId}"));
            File.WriteAllBytes(Path.Combine(cache, WikipediaReferenceContract.IndexFilename), CompressMembers([indexText]).Bytes);
            var output = Path.Combine(root, "selection-reference");

            var result = WikipediaReferenceBuilder.BuildFixture(cache, output, targetCount: 2, initialCandidateLimit: 2, maximumCandidateLimit: 4);

            var expected = ranked.Where(candidate => !rejectedIds.Contains(candidate.Entry.PageId)).Take(2)
                .Select(static candidate => candidate.Entry.PageId).ToArray();
            var actual = ReadRecordLines(output).Select(static line =>
            {
                using var document = JsonDocument.Parse(line);
                return document.RootElement.GetProperty("PageId").GetUInt64();
            }).ToArray();
            Assert.Equal(4, result.CandidateLimit);
            Assert.Equal(4, result.CandidateIdsInspected);
            Assert.Equal(4, result.UniqueOffsetsRead);
            Assert.Equal(expected, actual);
        });
    }

    private static WikipediaPageRevision ParsePage(string xml, ulong pageId)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));
        return Assert.Single(WikipediaPageXmlParser.Parse(stream, new HashSet<ulong> { pageId }));
    }

    private static string BuildMiniatureReference(string root)
    {
        var cache = Path.Combine(root, "mutation-cache");
        Directory.CreateDirectory(cache);
        var pages = new[] { PageXml(11), PageXml(22), PageXml(33), PageXml(44) };
        var (dump, offsets) = CompressMembers(pages);
        File.WriteAllBytes(Path.Combine(cache, WikipediaReferenceContract.PrimaryFilename), dump);
        var pageIds = new ulong[] { 11, 22, 33, 44 };
        var indexText = string.Join('\n', Enumerable.Range(0, pages.Length).Select(index => $"{offsets[index]}:{pageIds[index]}:Fixture title {index}"));
        File.WriteAllBytes(Path.Combine(cache, WikipediaReferenceContract.IndexFilename), CompressMembers([indexText]).Bytes);
        var reference = Path.Combine(root, "mutation-reference");
        _ = WikipediaReferenceBuilder.BuildFixture(cache, reference, 2);
        return reference;
    }

    private static string CopyForMutation(string source, string root, string name)
    {
        var copy = Path.Combine(root, name);
        CopyDirectory(source, copy);
        return copy;
    }

    private static string[] ReadRecordLines(string directory) =>
        File.ReadAllText(Path.Combine(directory, "records.ndjson"), new UTF8Encoding(false, true))
            .Split('\n', StringSplitOptions.RemoveEmptyEntries);

    private static void WriteRecordLines(string directory, IReadOnlyList<string> lines) =>
        File.WriteAllText(Path.Combine(directory, "records.ndjson"), string.Join('\n', lines) + "\n", new UTF8Encoding(false));

    private static string SerialiseRecord(WikipediaReferenceRecord record)
    {
        using var stream = new MemoryStream();
        using (var writer = new CanonicalJsonWriter(stream))
            WikipediaReferenceBuilder.WriteRecord(writer, record);
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void RepairManifestRecordIntegrity(string directory)
    {
        var records = File.ReadAllBytes(Path.Combine(directory, "records.ndjson"));
        var hash = Convert.ToHexString(SHA256.HashData(records)).ToLowerInvariant();
        RewriteManifest(directory, manifest => manifest with
        {
            LogicalByteCount = records.LongLength,
            ContentSha256 = hash,
            ArtefactSha256 = hash
        });
    }

    private static void RewriteSourceValue(string directory, string key, string value) =>
        RewriteManifest(directory, manifest =>
        {
            var source = manifest.Source!.ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal);
            source[key] = value;
            return manifest with { Source = source };
        });

    private static void RewriteManifest(string directory, Func<DataForgeManifest, DataForgeManifest> mutate)
    {
        var path = Path.Combine(directory, "manifest.json");
        var manifest = mutate(DataForgeManifestCodec.Read(path));
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        DataForgeManifestCodec.Write(stream, manifest);
    }

    private static WikipediaCandidate Candidate(long offset, ulong pageId) => new(new WikipediaIndexEntry(offset, pageId, $"Title {pageId}"));

    private static string CreateDumpStatus(string jobStatus, bool includePrimary, bool includeIndex,
        string jobName = "articlesmultistreamdumprecombine") =>
        JsonSerializer.Serialize(new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["jobs"] = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                [jobName] = new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["status"] = jobStatus,
                    ["updated"] = "2026-09-02 00:00:00",
                    ["files"] = CreateFileEntries(includePrimary, includeIndex)
                }
            },
            ["version"] = "1.0"
        });

    private static Dictionary<string, object> CreateFileEntries(bool includePrimary, bool includeIndex)
    {
        var entries = new Dictionary<string, object>(StringComparer.Ordinal);
        if (includePrimary)
            entries.Add(WikipediaReferenceContract.PrimaryFilename, new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["size"] = 26797495184L,
                ["url"] = "/enwiki/20260901/" + WikipediaReferenceContract.PrimaryFilename,
                ["md5"] = "f251f445259e452fafcb67d9f2c26513",
                ["sha1"] = WikipediaReferenceContract.PrimarySha1
            });
        if (includeIndex)
            entries.Add(WikipediaReferenceContract.IndexFilename, new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["size"] = 284375992L,
                ["url"] = "/enwiki/20260901/" + WikipediaReferenceContract.IndexFilename,
                ["md5"] = "1d730f63aae2805882f11cbaf639ca53",
                ["sha1"] = "ce0cbff600b0be2eed98da5ce189f847232d2282"
            });
        return entries;
    }

    private static string PageXml(ulong pageId, int namespaceId = 0, bool hasRedirect = false)
    {
        var text = string.Concat(Enumerable.Repeat("wordtoken ", 50));
        var redirect = hasRedirect ? "<redirect title=\"Target\" />" : string.Empty;
        return $"<page><title>Title {pageId}</title><ns>{namespaceId}</ns><id>{pageId}</id>{redirect}<revision><id>{pageId + 100}</id><timestamp>2026-09-01T00:00:00Z</timestamp><text>{text}</text></revision></page>";
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
