using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Rowles.DataForge;
using Rowles.DataForge.Workloads;

namespace Rowles.LeanCorpus.Benchmarks;

internal static class BenchmarkData
{
    /// <summary>Default document count used when <c>BENCH_DOC_COUNT</c> is not set.</summary>
    public const int DefaultDocCount = 20_000;
    private const string DatasetModeVariable = "BENCH_DATASET_MODE";
    private const string ReferencePathVariable = "BENCH_REFERENCE_PATH";
    private const string WikipediaDatasetId = "leancorpus-wikipedia-en";
    private const int WikipediaDatasetVersion = 1;
    private const int WikipediaRecordCount = 20_000;

    private const ulong SearchSeed = 42;
    private static readonly LeanCorpusSearchProfile Profile = new();
    private static readonly ConcurrentDictionary<DatasetCacheKey, Lazy<BenchmarkDataset>> Datasets = new();
    private static readonly ConcurrentDictionary<ReferenceDatasetCacheKey, Lazy<BenchmarkDataset>> ReferenceDatasets = new();

    /// <summary>Returns the configured document count or the suite's default count matrix.</summary>
    public static IEnumerable<int> GetDocCounts(params int[] defaultCounts)
    {
        var env = Environment.GetEnvironmentVariable("BENCH_DOC_COUNT");
        if (int.TryParse(env, NumberStyles.Integer, CultureInfo.InvariantCulture, out var count) && count > 0)
            return [count];
        return defaultCounts;
    }

    /// <summary>Returns bodies from the selected benchmark dataset, synthetic by default.</summary>
    public static string[] BuildDocuments(int count)
        => GetDataset(count).Documents;

    /// <summary>True when benchmark bodies come from the frozen Wikipedia reference.</summary>
    public static bool IsWikipediaReferenceMode => ResolveDatasetMode() == DatasetMode.Wikipedia;

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

    /// <summary>Returns typed generated-profile records for structured benchmark fixtures.</summary>
    public static SearchRecord[] GetRecords(int count)
        => RequireRecords(GetDataset(count));

    /// <summary>Builds the established misspelling cases against the generated profile vocabulary.</summary>
    public static (string Original, string Misspelled)[] BuildMisspelledTerms()
    {
        if (IsWikipediaReferenceMode)
            throw new InvalidOperationException("Misspelling anchors are generated-profile data and are unavailable in Wikipedia reference mode.");

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

    private static BenchmarkDataset GetDataset(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
        if (ResolveDatasetMode() == DatasetMode.Wikipedia)
        {
            var referencePath = ResolveReferencePath();
            var referenceKey = new ReferenceDatasetCacheKey(referencePath, count);
            return ReferenceDatasets.GetOrAdd(
                referenceKey,
                static datasetKey => new Lazy<BenchmarkDataset>(
                    () => LoadWikipediaDataset(datasetKey.ReferencePath, datasetKey.RecordCount),
                    LazyThreadSafetyMode.ExecutionAndPublication)).Value;
        }

        var key = new DatasetCacheKey(Profile.Descriptor.ProfileVersion, SearchSeed, count);
        return Datasets.GetOrAdd(
            key,
            static datasetKey => new Lazy<BenchmarkDataset>(
                () => GenerateDataset(datasetKey),
                LazyThreadSafetyMode.ExecutionAndPublication)).Value;
    }

    private static BenchmarkDataset GenerateDataset(DatasetCacheKey key)
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
        return new BenchmarkDataset(records, records.Select(static record => record.Body).ToArray(), identity);
    }

    private static BenchmarkDataset LoadWikipediaDataset(string referencePath, int requestedCount)
    {
        var verification = DataForgeVerifier.VerifyMaterialised(referencePath);
        var manifest = verification.Manifest;
        if (manifest.SourceKind != DataForgeSourceKind.Imported || manifest.ProfileId is not null ||
            manifest.ProfileVersion is not null || manifest.Seed is not null || manifest.Parameters.Count != 0)
            throw new InvalidDataException("Wikipedia benchmark reference must use an imported manifest without generated profile, seed or parameter fields.");
        if (manifest.Source is null ||
            !manifest.Source.TryGetValue("datasetId", out var datasetId) || datasetId != WikipediaDatasetId ||
            !manifest.Source.TryGetValue("datasetVersion", out var datasetVersion) ||
            datasetVersion != WikipediaDatasetVersion.ToString(CultureInfo.InvariantCulture))
            throw new InvalidDataException("Wikipedia benchmark reference does not identify leancorpus-wikipedia-en v1.");
        if (manifest.RecordCount != WikipediaRecordCount)
            throw new InvalidDataException($"Wikipedia benchmark reference declares {manifest.RecordCount} records; expected {WikipediaRecordCount}.");
        if (requestedCount > manifest.RecordCount)
            throw new InvalidOperationException($"Wikipedia reference mode contains {manifest.RecordCount} records; requested document count {requestedCount} exceeds that limit.");

        var documents = new string[requestedCount];
        using (var stream = new FileStream(verification.RecordsPath, FileMode.Open, FileAccess.Read, FileShare.Read,
                   64 * 1024, FileOptions.SequentialScan))
        using (var reader = new StreamReader(stream, new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: false))
        {
            for (var index = 0; index < documents.Length; index++)
            {
                var line = reader.ReadLine() ?? throw new InvalidDataException($"Wikipedia reference ended after {index} records.");
                using var record = JsonDocument.Parse(line, new JsonDocumentOptions { MaxDepth = 64 });
                if (!record.RootElement.TryGetProperty("Text", out var textElement) || textElement.ValueKind != JsonValueKind.String)
                    throw new InvalidDataException($"Wikipedia reference record {index + 1} has no text body.");
                documents[index] = textElement.GetString() ?? throw new InvalidDataException($"Wikipedia reference record {index + 1} has a null text body.");
            }
        }

        BenchmarkDatasetSidecars.Write(verification.Identity);
        return new BenchmarkDataset(null, documents, verification.Identity);
    }

    private static DatasetMode ResolveDatasetMode()
    {
        var configured = Environment.GetEnvironmentVariable(DatasetModeVariable);
        if (string.IsNullOrWhiteSpace(configured) || configured.Equals("synthetic", StringComparison.OrdinalIgnoreCase))
            return DatasetMode.Synthetic;
        if (configured.Equals("wikipedia", StringComparison.OrdinalIgnoreCase))
            return DatasetMode.Wikipedia;
        throw new InvalidOperationException($"Unknown {DatasetModeVariable} value '{configured}'. Expected synthetic or wikipedia.");
    }

    private static string ResolveReferencePath()
    {
        var configured = Environment.GetEnvironmentVariable(ReferencePathVariable);
        if (string.IsNullOrWhiteSpace(configured))
            throw new InvalidOperationException($"{ReferencePathVariable} is required when {DatasetModeVariable}=wikipedia.");
        return Path.GetFullPath(configured);
    }

    private readonly record struct DatasetCacheKey(int ProfileVersion, ulong Seed, int RecordCount);
    private readonly record struct ReferenceDatasetCacheKey(string ReferencePath, int RecordCount);
    private sealed record BenchmarkDataset(SearchRecord[]? Records, string[] Documents, DataForgeDatasetIdentity Identity);
    private enum DatasetMode { Synthetic, Wikipedia }

    private static SearchRecord[] RequireRecords(BenchmarkDataset dataset)
    {
        return dataset.Records ?? throw new InvalidOperationException("This benchmark requires synthetic structured fields and cannot use Wikipedia reference mode.");
    }
}
