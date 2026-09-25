using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using Rowles.DataForge;
using Rowles.DataForge.Workloads;

namespace Rowles.LeanCorpus.Benchmarks;

internal static class BenchmarkData
{
    /// <summary>Default document count used when <c>BENCH_DOC_COUNT</c> is not set.</summary>
    public const int DefaultDocCount = 20_000;

    private const ulong SearchSeed = 42;
    private static readonly LeanCorpusSearchProfile Profile = new();
    private static readonly ConcurrentDictionary<DatasetCacheKey, Lazy<SearchDataset>> Datasets = new();

    /// <summary>Returns the configured document count or the suite's default count matrix.</summary>
    public static IEnumerable<int> GetDocCounts(params int[] defaultCounts)
    {
        var env = Environment.GetEnvironmentVariable("BENCH_DOC_COUNT");
        if (int.TryParse(env, NumberStyles.Integer, CultureInfo.InvariantCulture, out var count) && count > 0)
            return [count];
        return defaultCounts;
    }

    /// <summary>Returns DataForge search record bodies, wrapping no external corpus.</summary>
    public static string[] BuildDocuments(int count)
        => GetRecords(count).Select(static record => record.Body).ToArray();

    /// <summary>Projects the profile's deterministic minor-unit prices to benchmark doubles.</summary>
    public static (string Body, double Price)[] BuildDocumentsWithPrices(int count)
    {
        var records = GetRecords(count);
        var documents = new (string Body, double Price)[records.Length];
        for (var index = 0; index < records.Length; index++)
            documents[index] = (records[index].Body, records[index].PriceMinor / 100d);
        return documents;
    }

    /// <summary>Builds parent-child blocks from profile titles and bodies.</summary>
    public static (string ParentTitle, string[] ChildBodies)[] BuildParentChildBlocks(
        int blockCount,
        int childrenPerBlock = 3)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(blockCount);
        ArgumentOutOfRangeException.ThrowIfNegative(childrenPerBlock);
        if (blockCount == 0)
            return [];

        var records = GetRecords(checked(blockCount * (childrenPerBlock + 1)));
        var blocks = new (string ParentTitle, string[] ChildBodies)[blockCount];
        var recordIndex = 0;
        for (var blockIndex = 0; blockIndex < blockCount; blockIndex++)
        {
            var parent = records[recordIndex++];
            var children = new string[childrenPerBlock];
            for (var childIndex = 0; childIndex < children.Length; childIndex++)
                children[childIndex] = records[recordIndex++].Body;
            blocks[blockIndex] = (parent.Title, children);
        }
        return blocks;
    }

    /// <summary>Returns the existing JSON benchmark shape using the typed search records.</summary>
    public static string[] BuildJsonDocuments(int count)
    {
        var records = GetRecords(count);
        var documents = new string[records.Length];
        for (var index = 0; index < records.Length; index++)
        {
            var record = records[index];
            var id = record.Ordinal.ToString(CultureInfo.InvariantCulture);
            var price = string.Concat(
                (record.PriceMinor / 100).ToString(CultureInfo.InvariantCulture),
                ".",
                (record.PriceMinor % 100).ToString("D2", CultureInfo.InvariantCulture));
            var active = record.Active ? "true" : "false";
            documents[index] = $"{{\"id\":{id},\"body\":{JsonSerializer.Serialize(record.Body)},\"price\":{price},\"active\":{active}}}";
        }
        return documents;
    }

    /// <summary>Returns the identity for the same cached records used by benchmark projections.</summary>
    public static DataForgeDatasetIdentity GetDatasetIdentity(int count)
        => GetDataset(count).Identity;

    /// <summary>Returns typed profile records for benchmark setup and diagnostic reporting.</summary>
    public static SearchRecord[] GetRecords(int count)
        => GetDataset(count).Records;

    /// <summary>Builds the established misspelling cases against the generated profile vocabulary.</summary>
    public static (string Original, string Misspelled)[] BuildMisspelledTerms()
    {
        return
        [
            ("government", "goverment"),
            ("president", "presiden"),
            ("market", "markts"),
            ("company", "compny"),
            ("million", "milion"),
            ("financial", "finanical"),
            ("reported", "reportd"),
            ("political", "politcal"),
            ("economic", "econmic"),
            ("hospital", "hosptal"),
            ("computer", "computr"),
            ("network", "netork"),
            ("message", "mesage"),
            ("article", "artcle"),
            ("because", "becuase"),
            ("people", "pepole"),
            ("national", "nationl"),
            ("through", "throgh"),
            ("without", "withut"),
            ("believe", "beleive"),
        ];
    }

    private static SearchDataset GetDataset(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
        var key = new DatasetCacheKey(Profile.Descriptor.ProfileVersion, SearchSeed, count);
        return Datasets.GetOrAdd(
            key,
            static datasetKey => new Lazy<SearchDataset>(
                () => GenerateDataset(datasetKey),
                LazyThreadSafetyMode.ExecutionAndPublication)).Value;
    }

    private static SearchDataset GenerateDataset(DatasetCacheKey key)
    {
        var profile = new LeanCorpusSearchProfile();
        if (profile.Descriptor.ProfileVersion != key.ProfileVersion)
            throw new InvalidOperationException("The DataForge search profile version changed while resolving a cached dataset.");

        var options = new DataForgeGenerationOptions(key.Seed, key.RecordCount);
        var records = profile.Generate(options).ToArray();

        using var writer = new CanonicalJsonWriter(Stream.Null);
        foreach (var record in records)
        {
            profile.CanonicalRecordWriter.Write(writer, record);
            writer.WriteLine();
        }

        var identity = new DataForgeDatasetIdentity(
            DataForgeSourceKind.Generated,
            DataForgeVersions.DataForgeVersion,
            profile.Descriptor.ProfileId,
            profile.Descriptor.ProfileVersion,
            DatasetId: null,
            DatasetVersion: null,
            Seed: key.Seed,
            RecordCount: records.Length,
            Parameters: options.Parameters,
            ContentSha256: Convert.ToHexString(writer.GetSha256()).ToLowerInvariant());
        BenchmarkDatasetSidecars.Write(identity);
        return new SearchDataset(records, identity);
    }

    private readonly record struct DatasetCacheKey(int ProfileVersion, ulong Seed, int RecordCount);

    private sealed record SearchDataset(SearchRecord[] Records, DataForgeDatasetIdentity Identity);
}
