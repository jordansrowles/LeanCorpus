using System.Globalization;
using System.Text.Json;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Util;
using Rowles.LeanCorpus.Analysis.Analysers;
using Rowles.LeanCorpus.Diagnostics;
using Rowles.LeanCorpus.Store;
using LeanDocument = Rowles.LeanCorpus.Document.LeanDocument;
using LeanIndexSearcher = Rowles.LeanCorpus.Search.Searcher.IndexSearcher;
using LeanIndexWriter = Rowles.LeanCorpus.Index.Indexer.IndexWriter;
using LeanIndexWriterConfig = Rowles.LeanCorpus.Index.Indexer.IndexWriterConfig;
using LeanMatchAllDocsQuery = Rowles.LeanCorpus.Search.Queries.MatchAllDocsQuery;
using LeanStringField = Rowles.LeanCorpus.Document.Fields.StringField;
using LeanTextField = Rowles.LeanCorpus.Document.Fields.TextField;
using LuceneDirectory = Lucene.Net.Store.Directory;
using LuceneDocument = Lucene.Net.Documents.Document;
using LuceneFSDirectory = Lucene.Net.Store.FSDirectory;
using LuceneIndexWriter = Lucene.Net.Index.IndexWriter;
using LuceneIndexWriterConfig = Lucene.Net.Index.IndexWriterConfig;
using LuceneMMapDirectory = Lucene.Net.Store.MMapDirectory;
using LuceneStringField = Lucene.Net.Documents.StringField;
using LuceneTextField = Lucene.Net.Documents.TextField;
using LuceneDirectoryReader = Lucene.Net.Index.DirectoryReader;

namespace Rowles.LeanCorpus.Benchmarks;

internal sealed record GutenbergPersistenceVerification(
    string ProfileId,
    string ClaimClass,
    string Engine,
    string DirectoryType,
    bool DurableCommits,
    double RamBufferSizeMB,
    double RamPerThreadHardLimitMB,
    int MaxBufferedDocs,
    bool UseCompoundFile,
    string CommitPolicy,
    string MetadataSyncContract,
    bool DirectoryMetadataSyncObservable,
    long? FileSyncOperationCount,
    long? FileSyncBytes,
    long CommitCount,
    int ExplicitCommitCount,
    int ReopenedDocumentCount,
    string[] ReopenedDocumentIds,
    long IndexByteCount,
    long IndexBytesPerCommit,
    int IndexFileCount,
    string WriterSettings);

internal static class GutenbergPersistenceFixtureVerifier
{
    internal const string FixturePath = ".config/devops/benchmark-correctness-fixtures/bval-13-gutenberg-persistence-restart.json";

    private static readonly IReadOnlyDictionary<string, PersistenceProfile> Profiles = LoadProfiles();
    private static readonly string[] ProfileIds = Profiles.Keys.OrderBy(static id => id, StringComparer.Ordinal).ToArray();

    internal static IEnumerable<string> GetProfileIds() => ProfileIds;

    internal static PersistenceProfile GetProfile(string profileId)
        => Profiles.TryGetValue(profileId, out var profile)
            ? profile
            : throw new ArgumentException($"Unknown BVAL-13 persistence profile '{profileId}'.", nameof(profileId));

    internal static IReadOnlyList<GutenbergPersistenceVerification> Verify(string repoRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repoRoot);
        string[] expectedIds;
        int expectedCount;
        int explicitCommitCount;
        using (JsonDocument fixture = ReadFixture(repoRoot))
        {
            JsonElement root = fixture.RootElement;
            if (!string.Equals(root.GetProperty("input").GetProperty("verification").GetString(),
                    "explicit-commit-close-writer-and-directory-reopen-reader", StringComparison.Ordinal))
                throw new InvalidDataException($"BVAL-13 fixture verification contract is invalid: {FixturePath}.");
            JsonElement expected = root.GetProperty("expected");
            expectedIds = expected.GetProperty("reopenedDocumentIds")
                .EnumerateArray()
                .Select(static value => value.GetString() ?? string.Empty)
                .ToArray();
            expectedCount = expected.GetProperty("exactDocumentCount").GetInt32();
            explicitCommitCount = expected.GetProperty("explicitCommitCount").GetInt32();
        }
        var documents = ReadDocuments(repoRoot);
        if (documents.Length != expectedCount
            || !documents.Select(static document => document.Id).SequenceEqual(expectedIds, StringComparer.Ordinal))
            throw new InvalidDataException($"BVAL-13 fixture documents do not match the pinned expected IDs/count in {FixturePath}.");
        if (explicitCommitCount <= 0)
            throw new InvalidDataException($"BVAL-13 requires at least one explicit commit in {FixturePath}.");

        var results = new List<GutenbergPersistenceVerification>(Profiles.Count * 2);
        foreach (PersistenceProfile profile in Profiles.Values.OrderBy(static profile => profile.Id, StringComparer.Ordinal))
        {
            results.Add(VerifyLeanCorpus(profile, profile.LeanCorpus, documents, expectedIds, explicitCommitCount));
            results.Add(VerifyLucene(profile, profile.Lucene, documents, expectedIds, explicitCommitCount));
        }

        return results;
    }

    private static GutenbergPersistenceVerification VerifyLeanCorpus(
        PersistenceProfile profile,
        EngineSettings settings,
        FixtureDocument[] documents,
        string[] expectedIds,
        int explicitCommitCount)
    {
        if (!string.Equals(settings.Directory, nameof(MMapDirectory), StringComparison.Ordinal))
            throw new InvalidDataException($"BVAL-13 LeanCorpus currently requires MMapDirectory; fixture requested '{settings.Directory}'.");

        string path = BenchmarkHelpers.CreateTempDirectory($"bval13-{profile.Id}-leancorpus");
        var metrics = new DefaultMetricsCollector();
        try
        {
            using (var directory = new MMapDirectory(path))
            {
                var config = new LeanIndexWriterConfig
                {
                    DefaultAnalyser = new StandardAnalyser(),
                    DurableCommits = settings.DurableCommits,
                    RamBufferSizeMB = settings.RamBufferSizeMB,
                    RamPerThreadHardLimitMB = settings.RamPerThreadHardLimitMB,
                    MaxBufferedDocs = settings.MaxBufferedDocs,
                    UseCompoundFile = settings.UseCompoundFile,
                    Metrics = metrics
                };
                using var writer = new LeanIndexWriter(directory, config);
                foreach (FixtureDocument source in documents)
                    writer.AddDocument(CreateLeanDocument(source));
                writer.Commit();
            }

            string[] actualIds;
            using (var reopenedDirectory = new MMapDirectory(path))
            using (var searcher = new LeanIndexSearcher(reopenedDirectory))
            {
                var result = searcher.Search(new LeanMatchAllDocsQuery(), expectedIds.Length);
                if (result.TotalHits != expectedIds.Length || result.ScoreDocs.Length != expectedIds.Length)
                    throw new InvalidDataException($"BVAL-13 LeanCorpus {profile.Id} reopened {result.TotalHits} hits; expected {expectedIds.Length}.");
                actualIds = result.ScoreDocs
                    .Select(hit => searcher.GetStoredFields(hit.DocId)["id"][0])
                    .OrderBy(static id => id, StringComparer.Ordinal)
                    .ToArray();
            }

            EnsureExpectedIds(profile.Id, "LeanCorpus", expectedIds, actualIds);
            MetricsSnapshot snapshot = metrics.GetSnapshot();
            var (byteCount, fileCount) = MeasureIndexFiles(path);
            return new GutenbergPersistenceVerification(
                profile.Id, profile.ClaimClass, "LeanCorpus", nameof(MMapDirectory),
                settings.DurableCommits, settings.RamBufferSizeMB, settings.RamPerThreadHardLimitMB,
                settings.MaxBufferedDocs, settings.UseCompoundFile, settings.CommitPolicy,
                settings.MetadataSyncContract, false, snapshot.FileSyncOperationCount,
                snapshot.FileSyncBytes, snapshot.CommitCount, explicitCommitCount, actualIds.Length,
                actualIds, byteCount, byteCount / explicitCommitCount, fileCount, FormatSettings(settings));
        }
        finally
        {
            BenchmarkHelpers.DeleteDirectory(path);
        }
    }

    private static GutenbergPersistenceVerification VerifyLucene(
        PersistenceProfile profile,
        EngineSettings settings,
        FixtureDocument[] documents,
        string[] expectedIds,
        int explicitCommitCount)
    {
        string path = BenchmarkHelpers.CreateTempDirectory($"bval13-{profile.Id}-lucene");
        try
        {
            string actualDirectoryType;
            using (LuceneDirectory directory = OpenLuceneDirectory(settings.Directory, path))
            {
                using var analyzer = new StandardAnalyzer(LuceneVersion.LUCENE_48);
                var config = new LuceneIndexWriterConfig(LuceneVersion.LUCENE_48,
                    analyzer)
                {
                    RAMBufferSizeMB = settings.RamBufferSizeMB,
                    RAMPerThreadHardLimitMB = checked((int)settings.RamPerThreadHardLimitMB),
                    MaxBufferedDocs = settings.MaxBufferedDocs,
                    UseCompoundFile = settings.UseCompoundFile
                };
                actualDirectoryType = directory.GetType().Name;
                using var writer = new LuceneIndexWriter(directory, config);
                foreach (FixtureDocument source in documents)
                    writer.AddDocument(CreateLuceneDocument(source));
                writer.Commit();
            }

            string[] actualIds;
            using (LuceneDirectory reopenedDirectory = OpenLuceneDirectory(settings.Directory, path))
            using (LuceneDirectoryReader reader = LuceneDirectoryReader.Open(reopenedDirectory))
            {
                if (reader.NumDocs != expectedIds.Length || reader.MaxDoc != expectedIds.Length)
                    throw new InvalidDataException($"BVAL-13 Lucene.NET {profile.Id} reopened {reader.NumDocs}/{reader.MaxDoc} docs; expected {expectedIds.Length}.");
                actualIds = Enumerable.Range(0, reader.MaxDoc)
                    .Select(documentId => reader.Document(documentId).Get("id")
                        ?? throw new InvalidDataException($"BVAL-13 Lucene.NET {profile.Id} reopened a document without an ID."))
                    .OrderBy(static id => id, StringComparer.Ordinal)
                    .ToArray();
            }

            EnsureExpectedIds(profile.Id, "Lucene.NET", expectedIds, actualIds);
            var (byteCount, fileCount) = MeasureIndexFiles(path);
            return new GutenbergPersistenceVerification(
                profile.Id, profile.ClaimClass, "Lucene.NET", actualDirectoryType,
                settings.DurableCommits, settings.RamBufferSizeMB, settings.RamPerThreadHardLimitMB,
                settings.MaxBufferedDocs, settings.UseCompoundFile, settings.CommitPolicy,
                settings.MetadataSyncContract, false, null, null, 1, explicitCommitCount,
                actualIds.Length, actualIds, byteCount, byteCount / explicitCommitCount, fileCount, FormatSettings(settings));
        }
        finally
        {
            BenchmarkHelpers.DeleteDirectory(path);
        }
    }

    internal static LuceneDirectory OpenLuceneDirectory(string directory, string path)
        => directory switch
        {
            "MMapDirectory" => new LuceneMMapDirectory(new DirectoryInfo(path)),
            "FSDirectory.Open" => LuceneFSDirectory.Open(new DirectoryInfo(path)),
            _ => throw new InvalidDataException($"BVAL-13 has unsupported Lucene.NET directory '{directory}'.")
        };

    private static FixtureDocument[] ReadDocuments(string repoRoot)
    {
        using JsonDocument fixture = ReadFixture(repoRoot);
        return fixture.RootElement.GetProperty("input").GetProperty("documents")
            .EnumerateArray()
            .Select(static item => new FixtureDocument(
                item.GetProperty("id").GetString() ?? string.Empty,
                item.GetProperty("title").GetString() ?? string.Empty,
                item.GetProperty("body").GetString() ?? string.Empty))
            .ToArray();
    }

    private static Dictionary<string, PersistenceProfile> LoadProfiles()
    {
        using JsonDocument fixture = ReadFixture(BenchmarkHelpers.RepositoryRoot);
        JsonElement root = fixture.RootElement;
        if (root.GetProperty("schemaVersion").GetInt32() != 1
            || !string.Equals(root.GetProperty("fixtureId").GetString(), "bval-13-gutenberg-persistence-restart", StringComparison.Ordinal)
            || !root.GetProperty("deterministic").GetBoolean())
            throw new InvalidDataException($"BVAL-13 fixture metadata is invalid: {FixturePath}.");

        var profiles = new Dictionary<string, PersistenceProfile>(StringComparer.Ordinal);
        foreach (JsonElement item in root.GetProperty("input").GetProperty("profiles").EnumerateArray())
        {
            string id = item.GetProperty("id").GetString() ?? string.Empty;
            string claimClass = item.GetProperty("claimClass").GetString() ?? string.Empty;
            profiles.Add(id, new PersistenceProfile(
                id,
                claimClass,
                ReadSettings(item.GetProperty("leancorpus")),
                ReadSettings(item.GetProperty("lucene"))));
        }

        if (profiles.Count != 2
            || !profiles.TryGetValue("as-shipped-persistence", out var asShipped)
            || !string.Equals(asShipped.ClaimClass, "Directional", StringComparison.Ordinal)
            || !profiles.TryGetValue("matched-persistence", out var matched)
            || !string.Equals(matched.ClaimClass, "Competitive", StringComparison.Ordinal))
            throw new InvalidDataException($"BVAL-13 requires directional as-shipped and competitive matched profiles in {FixturePath}.");

        return profiles;
    }

    private static EngineSettings ReadSettings(JsonElement element)
        => new(
            element.GetProperty("durableCommits").GetBoolean(),
            element.GetProperty("directory").GetString() ?? string.Empty,
            element.GetProperty("ramBufferSizeMB").GetDouble(),
            element.GetProperty("ramPerThreadHardLimitMB").GetDouble(),
            element.GetProperty("maxBufferedDocs").GetInt32(),
            element.GetProperty("useCompoundFile").GetBoolean(),
            element.GetProperty("commit").GetString() ?? string.Empty,
            element.GetProperty("metadataSync").GetString() ?? string.Empty);

    private static JsonDocument ReadFixture(string repoRoot)
        => JsonDocument.Parse(File.ReadAllBytes(Path.Combine(
            repoRoot,
            FixturePath.Replace('/', Path.DirectorySeparatorChar))));

    private static LeanDocument CreateLeanDocument(FixtureDocument source)
    {
        var document = new LeanDocument();
        document.Add(new LeanStringField("id", source.Id));
        document.Add(new LeanStringField("title", source.Title));
        document.Add(new LeanTextField("body", source.Body));
        return document;
    }

    private static LuceneDocument CreateLuceneDocument(FixtureDocument source)
        => new()
        {
            new LuceneStringField("id", source.Id, Lucene.Net.Documents.Field.Store.YES),
            new LuceneStringField("title", source.Title, Lucene.Net.Documents.Field.Store.YES),
            new LuceneTextField("body", source.Body, Lucene.Net.Documents.Field.Store.NO)
        };

    private static (long ByteCount, int FileCount) MeasureIndexFiles(string path)
    {
        FileInfo[] files = Directory.EnumerateFiles(path)
            .Where(static file => !Path.GetFileName(file).EndsWith(".lock", StringComparison.Ordinal))
            .Select(static file => new FileInfo(file))
            .ToArray();
        return (files.Sum(static file => file.Length), files.Length);
    }

    private static string FormatSettings(EngineSettings settings)
        => string.Join(";", new[]
        {
            $"durableCommits={settings.DurableCommits.ToString().ToLowerInvariant()}",
            $"directory={settings.Directory}",
            $"ramBufferSizeMB={settings.RamBufferSizeMB.ToString(CultureInfo.InvariantCulture)}",
            $"ramPerThreadHardLimitMB={settings.RamPerThreadHardLimitMB.ToString(CultureInfo.InvariantCulture)}",
            $"maxBufferedDocs={settings.MaxBufferedDocs.ToString(CultureInfo.InvariantCulture)}",
            $"useCompoundFile={settings.UseCompoundFile.ToString().ToLowerInvariant()}",
            $"commit={settings.CommitPolicy}",
            $"metadataSync={settings.MetadataSyncContract}"
        });

    private static void EnsureExpectedIds(string profileId, string engine, string[] expected, string[] actual)
    {
        if (!actual.SequenceEqual(expected, StringComparer.Ordinal))
            throw new InvalidDataException(
                $"BVAL-13 {engine} {profileId} close/reopen ID mismatch: expected [{string.Join(",", expected)}], actual [{string.Join(",", actual)}].");
    }

    internal sealed record PersistenceProfile(
        string Id,
        string ClaimClass,
        EngineSettings LeanCorpus,
        EngineSettings Lucene);

    internal sealed record EngineSettings(
        bool DurableCommits,
        string Directory,
        double RamBufferSizeMB,
        double RamPerThreadHardLimitMB,
        int MaxBufferedDocs,
        bool UseCompoundFile,
        string CommitPolicy,
        string MetadataSyncContract);

    private sealed record FixtureDocument(string Id, string Title, string Body);
}
