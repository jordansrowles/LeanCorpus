using System.Security.Cryptography;
using System.Text;
using BenchmarkDotNet.Reports;
using Rowles.DataForge;

namespace Rowles.LeanCorpus.Benchmarks;

// Core-only parts of the canonical benchmark report. The shared report in BenchmarkRunReport.cs is
// also linked into Rowles.Text.Benchmarks, which leaves these partial members unimplemented.

internal sealed partial class BenchmarkRunReport
{
    public List<GutenbergPersistenceVerification> PersistenceVerifications { get; set; } = [];
    public List<SuggesterQualityEvidence> SuggesterQuality { get; set; } = [];
}

internal static partial class BenchmarkRunReportBuilder
{
    static partial void AddProjectEvidence(BenchmarkRunReport report, IReadOnlyList<(string Suite, Summary Summary)> suiteSummaries)
    {
        bool includesGutenbergPersistence = suiteSummaries
            .SelectMany(static source => source.Summary.BenchmarksCases)
            .Any(static benchmarkCase => benchmarkCase.Descriptor.Type == typeof(GutenbergIndexingBenchmarks));
        if (includesGutenbergPersistence)
            report.PersistenceVerifications = GutenbergPersistenceFixtureVerifier.Verify(BenchmarkHelpers.RepositoryRoot).ToList();
    }

    static partial void AddProjectQualityFlags(string typeName, string methodName, IReadOnlyDictionary<string, string> parameters, List<string> flags)
    {
        if (methodName.Contains("ColdPerWordAdapter", StringComparison.Ordinal))
            flags.Add("AdapterOverhead");

        if (typeName == nameof(CombinedFieldsQueryBenchmarks)
            && methodName.Contains("CombinedFieldsQuery", StringComparison.Ordinal))
            flags.Add("DifferentSemantics");

        if (typeName == nameof(GeoQueryBenchmarks))
            flags.Add("DifferentIndexAlgorithm");

        if (typeName == nameof(HnswSearchBenchmarks)
            && methodName.Contains("Reference_ScalarFlatScan", StringComparison.Ordinal))
            flags.Add("ExactReference");

        if (typeName == nameof(IndexSortSearchBenchmarks)
            && methodName.Contains("LuceneNet", StringComparison.Ordinal))
            flags.Add("DifferentLifecycle");

        if (typeName == nameof(GutenbergIndexingBenchmarks)
            && parameters.TryGetValue("PersistenceProfile", out string? persistenceProfile)
            && string.Equals(persistenceProfile, "as-shipped-persistence", StringComparison.Ordinal))
            flags.Add("DirectionalPersistenceMismatch");
    }
}

internal static partial class BenchmarkProvenanceBuilder
{
    static partial void BuildCorpus(int? effectiveDocCount, IReadOnlyList<BenchmarkDatasetEvidence> datasets, ref BenchmarkCorpusReport? corpus)
    {
        var hasSearchDataset = effectiveDocCount is > 0 && datasets.Any(dataset =>
            dataset.Identity.SourceKind == DataForgeSourceKind.Generated &&
            string.Equals(dataset.Identity.ProfileId, "leancorpus-search", StringComparison.Ordinal) &&
            dataset.Identity.RecordCount == effectiveDocCount.Value ||
            dataset.Identity.SourceKind == DataForgeSourceKind.Imported &&
            string.Equals(dataset.Identity.DatasetId, "leancorpus-wikipedia-en", StringComparison.Ordinal) &&
            dataset.Identity.DatasetVersion == 1 && dataset.Identity.RecordCount == 20_000);
        corpus = hasSearchDataset ? BenchmarkCorpusReportBuilder.Build(effectiveDocCount) : null;
    }
}

internal static class BenchmarkCorpusReportBuilder
{
    public static BenchmarkCorpusReport? Build(int? documentCount)
    {
        if (documentCount is not > 0)
            return null;

        var documents = BenchmarkData.BuildDocuments(documentCount.Value);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var sink = new CorpusTokenSink();
        var analyser = new Rowles.LeanCorpus.Analysis.Analysers.StandardAnalyser();
        long byteCount = 0;

        foreach (var body in documents)
        {
            var bytes = Encoding.UTF8.GetBytes(body);
            byteCount += bytes.Length;
            hash.AppendData(bytes);
            hash.AppendData([ (byte)'\n' ]);
            analyser.Analyse(body.AsSpan(), sink);
        }

        return new BenchmarkCorpusReport
        {
            DocumentCount = documents.Length,
            Utf8ByteCount = byteCount,
            TokenCount = sink.TokenCount,
            UniqueTermCount = sink.UniqueTerms.Count,
            PayloadSha256 = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant()
        };
    }

    private sealed class CorpusTokenSink : Rowles.LeanCorpus.Analysis.ISpanTokenSink
    {
        public long TokenCount { get; private set; }
        public HashSet<string> UniqueTerms { get; } = new(StringComparer.Ordinal);

        public void Add(ReadOnlySpan<char> text, int startOffset, int endOffset,
            string type = Rowles.LeanCorpus.Analysis.Token.DefaultType,
            int positionIncrement = 1, byte[]? payload = null)
        {
            TokenCount++;
            UniqueTerms.Add(text.ToString());
        }
    }
}

