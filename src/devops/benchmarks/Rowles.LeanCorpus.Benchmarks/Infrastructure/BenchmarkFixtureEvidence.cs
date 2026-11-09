using System.Globalization;
using System.Text;
using System.Text.Json;
using LeanDirectory = Rowles.LeanCorpus.Store.MMapDirectory;
using LeanIndexSearcher = Rowles.LeanCorpus.Search.Searcher.IndexSearcher;

namespace Rowles.LeanCorpus.Benchmarks;

/// <summary>Storage and segment topology of one engine's index in a benchmark fixture.</summary>
internal sealed record BenchmarkTopologyRecord(
    string Fixture,
    string Engine,
    string DirectoryType,
    int SegmentCount,
    int DocumentCount);

/// <summary>
/// Records head-to-head fixture evidence (ADR045): per-engine storage and topology, and the
/// output preflight that fails setup when a case does no work.
/// </summary>
internal static class BenchmarkFixtureEvidence
{
    private const string ArtifactDirectoryVariable = "LEANCORPUS_ARTIFACT_DIR";
    internal const string LeanCorpusEngine = "LeanCorpus";
    internal const string LuceneNetEngine = "Lucene.NET";

    /// <summary>Records a LeanCorpus searcher's directory type, segment count and document count.</summary>
    public static BenchmarkTopologyRecord RecordLeanCorpus(string fixture, LeanDirectory directory, LeanIndexSearcher searcher)
    {
        ArgumentNullException.ThrowIfNull(directory);
        ArgumentNullException.ThrowIfNull(searcher);
        var readers = searcher.GetSegmentReaders();
        int documentCount = 0;
        foreach (var reader in readers)
            documentCount += reader.MaxDoc;
        return Record(new BenchmarkTopologyRecord(fixture, LeanCorpusEngine, directory.GetType().Name, readers.Count, documentCount));
    }

    /// <summary>Records a Lucene.NET reader's directory type, segment count and document count.</summary>
    public static BenchmarkTopologyRecord RecordLucene(
        string fixture,
        Lucene.Net.Store.Directory directory,
        Lucene.Net.Index.IndexReader reader,
        string engine = LuceneNetEngine)
    {
        ArgumentNullException.ThrowIfNull(directory);
        ArgumentNullException.ThrowIfNull(reader);
        return Record(new BenchmarkTopologyRecord(fixture, engine, directory.GetType().Name, reader.Leaves.Count, reader.MaxDoc));
    }

    /// <summary>Writes a topology record to the benchmark log and, when a run artefact directory is set, as a sidecar.</summary>
    public static BenchmarkTopologyRecord Record(BenchmarkTopologyRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentException.ThrowIfNullOrWhiteSpace(record.Fixture);
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"Benchmark topology {record.Fixture} {record.Engine}: directory={record.DirectoryType}, segments={record.SegmentCount}, documents={record.DocumentCount}"));

        WriteSidecar("topology", record.Fixture, record.Engine, JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["fixture"] = record.Fixture,
            ["engine"] = record.Engine,
            ["directoryType"] = record.DirectoryType,
            ["segmentCount"] = record.SegmentCount,
            ["documentCount"] = record.DocumentCount
        }));
        return record;
    }

    /// <summary>
    /// Fails setup when a case's untimed preflight observed no work, unless the case declares
    /// that it must match nothing. Records the observed count in the benchmark log.
    /// </summary>
    public static long RequireWork(string fixture, string caseName, string engine, long observed, bool requiresNoMatch = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fixture);
        ArgumentException.ThrowIfNullOrWhiteSpace(caseName);
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"Benchmark preflight {fixture} {caseName} {engine}: observed={observed}, requiresNoMatch={requiresNoMatch}"));
        if (requiresNoMatch)
        {
            if (observed != 0)
                throw new InvalidOperationException(string.Create(CultureInfo.InvariantCulture,
                    $"{fixture} case '{caseName}' declares no match, but {engine} observed {observed}."));
            return observed;
        }

        if (observed <= 0)
            throw new InvalidOperationException(string.Create(CultureInfo.InvariantCulture,
                $"{fixture} case '{caseName}' did no work on {engine}: observed {observed}. Declare requiresNoMatch if that is intended."));
        return observed;
    }

    /// <summary>Writes a fixture profile (similarity, storage, DocValues) to the log and as a sidecar.</summary>
    public static void RecordProfile(string fixture, string engine, IReadOnlyDictionary<string, string> profile)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fixture);
        ArgumentNullException.ThrowIfNull(profile);
        Console.WriteLine(string.Concat("Benchmark profile ", fixture, " ", engine, ": ",
            string.Join(", ", profile.OrderBy(static pair => pair.Key, StringComparer.Ordinal).Select(static pair => string.Concat(pair.Key, "=", pair.Value)))));
        WriteSidecar("profile", fixture, engine, JsonSerializer.SerializeToUtf8Bytes(
            new SortedDictionary<string, string>(profile.ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal), StringComparer.Ordinal)));
    }

    /// <summary>Maps each Lucene.NET document number to its indexed <c>id</c> term.</summary>
    public static string?[] BuildLuceneLogicalIds(Lucene.Net.Index.IndexReader reader, string field = "id")
    {
        ArgumentNullException.ThrowIfNull(reader);
        var terms = Lucene.Net.Index.MultiFields.GetTerms(reader, field)
            ?? throw new InvalidOperationException($"Lucene.NET index has no '{field}' terms.");
        var ids = new string?[reader.MaxDoc];
        var termsEnum = terms.GetEnumerator();
        while (termsEnum.MoveNext())
        {
            string id = termsEnum.Term.Utf8ToString();
            var documents = termsEnum.Docs(null, null, Lucene.Net.Index.DocsFlags.NONE);
            for (int doc = documents.NextDoc(); doc != Lucene.Net.Search.DocIdSetIterator.NO_MORE_DOCS; doc = documents.NextDoc())
            {
                if (ids[doc] is not null)
                    throw new InvalidOperationException($"Lucene.NET document {doc} has more than one '{field}' term.");
                ids[doc] = id;
            }
        }

        return ids;
    }

    private static void WriteSidecar(string kind, string fixture, string engine, byte[] content)
    {
        string? artifactRoot = Environment.GetEnvironmentVariable(ArtifactDirectoryVariable);
        if (string.IsNullOrWhiteSpace(artifactRoot))
            return;

        string directory = Path.Combine(Path.GetFullPath(artifactRoot), kind);
        System.IO.Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, string.Concat(SafeFileName(fixture), ".", SafeFileName(engine), ".json"));
        string temporaryPath = Path.Combine(directory, string.Concat(".", Guid.NewGuid().ToString("N"), ".tmp"));
        try
        {
            File.WriteAllBytes(temporaryPath, content);
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    private static string SafeFileName(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (char character in value)
            builder.Append(char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.' ? character : '-');
        return builder.ToString();
    }
}
