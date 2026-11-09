using BenchmarkDotNet.Attributes;
using Lucene.Net.Index;
using Lucene.Net.Search;
using LeanIndexSearcher = Rowles.LeanCorpus.Search.Searcher.IndexSearcher;

namespace Rowles.LeanCorpus.Benchmarks;

/// <summary>
/// Compares <see cref="MultiPhraseQuery"/> throughput against Lucene.NET <c>MultiPhraseQuery</c>.
/// The slots use phrase anchors that the DataForge search profile emits, and setup checks both
/// engines against a hit count derived from the generated bodies (ADR045).
/// </summary>
[MemoryDiagnoser]
[HtmlExporter]
[JsonExporterAttribute.Full]
[MarkdownExporterAttribute.GitHub]
[RPlotExporter]
public class MultiPhraseQueryBenchmarks
{
    private const int TopN = 25;

    /// <summary>Slot 0: "new" or "said"; slot 1: "york" or "public".</summary>
    internal static readonly string[][] Slots = [["new", "said"], ["york", "public"]];

    public static IEnumerable<int> DocCounts => BenchmarkData.GetDocCounts(BenchmarkData.DefaultDocCount);

    [ParamsSource(nameof(DocCounts))]
    public int DocumentCount { get; set; }

    private LeanIndexSearcher? _leanSearcher;

    [GlobalSetup]
    public void Setup()
    {
        SharedStandardIndex.EnsureInitialised(DocumentCount);
        _leanSearcher = SharedStandardIndex.LeanSearcher;
        int expected = CountExpectedHits(BenchmarkData.BuildDocuments(DocumentCount), Slots);
        ValidateHits(expected, LeanCorpus_MultiPhraseQuery(), LuceneNet_MultiPhraseQuery());
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        // All resources are owned by SharedStandardIndex; do not dispose.
    }

    [Benchmark(Baseline = true)]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_MultiPhraseQuery()
    {
        var q = new Rowles.LeanCorpus.Search.Queries.MultiPhraseQuery("body", Slots);
        return _leanSearcher!.Search(q, TopN).TotalHits;
    }

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LuceneNet_MultiPhraseQuery()
    {
        var q = new Lucene.Net.Search.MultiPhraseQuery();
        foreach (var slot in Slots)
            q.Add(slot.Select(static term => new Term("body", term)).ToArray());
        return SharedStandardIndex.LuceneSearcher.Search(q, TopN).TotalHits;
    }

    /// <summary>
    /// Counts documents in which a term from each slot occurs at consecutive positions, using
    /// whitespace tokens of the generated bodies. This is independent of both engines' analysers;
    /// the slot terms are ASCII and are not stop words, so both analysers keep them in place.
    /// </summary>
    internal static int CountExpectedHits(IReadOnlyList<string> documents, IReadOnlyList<string[]> slots)
    {
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(slots);
        var slotSets = slots.Select(static slot => new HashSet<string>(slot, StringComparer.Ordinal)).ToArray();
        int hits = 0;
        foreach (var document in documents)
        {
            var tokens = document.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(static token => token.ToLowerInvariant())
                .ToArray();
            for (int start = 0; start + slotSets.Length <= tokens.Length; start++)
            {
                bool matches = true;
                for (int slot = 0; slot < slotSets.Length && matches; slot++)
                    matches = slotSets[slot].Contains(tokens[start + slot]);
                if (matches)
                {
                    hits++;
                    break;
                }
            }
        }

        return hits;
    }

    /// <summary>Fails setup unless both engines match the independently derived, non-zero count.</summary>
    internal static void ValidateHits(int expected, int leanHits, int luceneHits)
    {
        BenchmarkFixtureEvidence.RequireWork("multiphrase", "derived", "generated bodies", expected);
        BenchmarkFixtureEvidence.RequireWork("multiphrase", "new|said york|public", BenchmarkFixtureEvidence.LeanCorpusEngine, leanHits);
        BenchmarkFixtureEvidence.RequireWork("multiphrase", "new|said york|public", BenchmarkFixtureEvidence.LuceneNetEngine, luceneHits);
        if (leanHits != expected || luceneHits != expected)
            throw new InvalidOperationException(string.Create(System.Globalization.CultureInfo.InvariantCulture,
                $"Multi-phrase hits differ from the {expected} derived from the generated bodies: LeanCorpus {leanHits}, Lucene.NET {luceneHits}."));
    }
}
