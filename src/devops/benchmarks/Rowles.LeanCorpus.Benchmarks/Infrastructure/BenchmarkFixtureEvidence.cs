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

        string? artifactRoot = Environment.GetEnvironmentVariable(ArtifactDirectoryVariable);
        if (string.IsNullOrWhiteSpace(artifactRoot))
            return record;

        string directory = Path.Combine(Path.GetFullPath(artifactRoot), "topology");
        System.IO.Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, string.Concat(SafeFileName(record.Fixture), ".", SafeFileName(record.Engine), ".json"));
        byte[] content = JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["fixture"] = record.Fixture,
            ["engine"] = record.Engine,
            ["directoryType"] = record.DirectoryType,
            ["segmentCount"] = record.SegmentCount,
            ["documentCount"] = record.DocumentCount
        });
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

    private static string SafeFileName(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (char character in value)
            builder.Append(char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.' ? character : '-');
        return builder.ToString();
    }
}
