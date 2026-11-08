using System.Globalization;
using BenchmarkDotNet.Attributes;
using Lucene.Net.Index;
using Lucene.Net.Search;
using LeanIndexSearcher = Rowles.LeanCorpus.Search.Searcher.IndexSearcher;

namespace Rowles.LeanCorpus.Benchmarks;

/// <summary>
/// Measures PhraseQuery performance: exact and slop phrase matching.
/// Compares LeanCorpus vs Lucene.NET (Lifti lacks first-class phrase support).
/// </summary>
[MemoryDiagnoser]
[HtmlExporter]
[JsonExporterAttribute.Full]
[MarkdownExporterAttribute.GitHub]
[RPlotExporter]
public class PhraseQueryBenchmarks
{
    private const int TopN = 25;

    public static IEnumerable<int> DocCounts => BenchmarkData.GetDocCounts(BenchmarkData.DefaultDocCount);

    [ParamsSource(nameof(DocCounts))]
    public int DocumentCount { get; set; }

    [Params("ExactTwoWord", "ExactThreeWord", "SlopTwoWord", "SlopThreeTerm")]
    public string PhraseType { get; set; } = "ExactTwoWord";

    private LeanIndexSearcher? _leanSearcher;
    private string?[] _luceneLogicalIdsByDocumentId = [];

    private static readonly Dictionary<string, PhraseDefinition> PhraseDefinitions = new(StringComparer.Ordinal)
    {
        ["ExactTwoWord"] = new(["new", "york"], [0, 1], 0),
        ["ExactThreeWord"] = new(["new", "york", "stock"], [0, 1, 2], 0),
        ["SlopTwoWord"] = new(["said", "government"], [0, 1], 2),
        ["SlopThreeTerm"] = new(["government", "people", "market"], [0, 1, 2], 4)
    };

    [GlobalSetup]
    public void Setup()
    {
        SharedStandardIndex.EnsureInitialised(DocumentCount);
        _leanSearcher = SharedStandardIndex.LeanSearcher;
        _luceneLogicalIdsByDocumentId = BuildLuceneLogicalIdMap();
        ValidatePhraseParity();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        // All resources are owned by SharedStandardIndex; do not dispose.
    }

    [Benchmark(Baseline = true)]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_PhraseQuery()
    {
        return _leanSearcher!.Search(BuildLeanPhraseQuery(), TopN).TotalHits;
    }

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_PhraseQueryConstruction()
    {
        return BuildLeanPhraseQuery().GetHashCode();
    }

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LuceneNet_PhraseQuery()
    {
        return SharedStandardIndex.LuceneSearcher.Search(BuildLucenePhraseQuery(), TopN).TotalHits;
    }

    private Rowles.LeanCorpus.Search.Queries.PhraseQuery BuildLeanPhraseQuery()
    {
        PhraseDefinition definition = GetPhraseDefinition();
        return new Rowles.LeanCorpus.Search.Queries.PhraseQuery(
            "body", definition.Terms, definition.Positions, definition.Slop);
    }

    private Lucene.Net.Search.PhraseQuery BuildLucenePhraseQuery()
    {
        PhraseDefinition definition = GetPhraseDefinition();
        var query = new Lucene.Net.Search.PhraseQuery { Slop = definition.Slop };
        for (int i = 0; i < definition.Terms.Length; i++)
            query.Add(new Term("body", definition.Terms[i]), definition.Positions[i]);
        return query;
    }

    private PhraseDefinition GetPhraseDefinition() =>
        PhraseDefinitions.TryGetValue(PhraseType, out var definition)
            ? definition
            : throw new InvalidOperationException($"Unknown phrase type '{PhraseType}'.");

    private void ValidatePhraseParity()
    {
        PhraseDefinition definition = GetPhraseDefinition();
        var leanResults = _leanSearcher!.Search(BuildLeanPhraseQuery(), DocumentCount);
        var luceneResults = SharedStandardIndex.LuceneSearcher.Search(BuildLucenePhraseQuery(), DocumentCount);
        string[] leanLogicalIds = leanResults.ScoreDocs
            .Select(hit => _leanSearcher.GetStoredFields(hit.DocId)["id"][0])
            .ToArray();
        string[] luceneLogicalIds = luceneResults.ScoreDocs
            .Select(hit => _luceneLogicalIdsByDocumentId[hit.Doc]
                ?? throw new InvalidOperationException($"Lucene.NET document {hit.Doc} has no logical id."))
            .ToArray();
        string[] orderedLeanLogicalIds = leanLogicalIds.OrderBy(static id => id, StringComparer.Ordinal).ToArray();
        string[] orderedLuceneLogicalIds = luceneLogicalIds.OrderBy(static id => id, StringComparer.Ordinal).ToArray();
        if (leanResults.TotalHits != luceneResults.TotalHits
            || leanLogicalIds.Length != leanResults.TotalHits
            || luceneLogicalIds.Length != luceneResults.TotalHits
            || !orderedLeanLogicalIds.SequenceEqual(orderedLuceneLogicalIds, StringComparer.Ordinal))
        {
            var luceneIdSet = luceneLogicalIds.ToHashSet(StringComparer.Ordinal);
            var leanIdSet = leanLogicalIds.ToHashSet(StringComparer.Ordinal);
            string[] leanOnlyIds = leanIdSet.Except(luceneIdSet, StringComparer.Ordinal)
                .OrderBy(static id => id, StringComparer.Ordinal).ToArray();
            string[] luceneOnlyIds = luceneIdSet.Except(leanIdSet, StringComparer.Ordinal)
                .OrderBy(static id => id, StringComparer.Ordinal).ToArray();

            throw new InvalidOperationException(
                $"Phrase parity failed for {PhraseType}: LeanCorpus returned {leanResults.TotalHits} hits and Lucene.NET returned {luceneResults.TotalHits} hits. "
                + DescribeDifferences("LeanCorpus-only", leanOnlyIds, definition.Terms) + Environment.NewLine
                + DescribeDifferences("Lucene.NET-only", luceneOnlyIds, definition.Terms));
        }

        Console.WriteLine($"Phrase parity {PhraseType}: {leanResults.TotalHits} hits");
    }

    private static string?[] BuildLuceneLogicalIdMap()
    {
        var reader = SharedStandardIndex.LuceneSearcher.IndexReader;
        Terms terms = MultiFields.GetTerms(reader, "id")
            ?? throw new InvalidOperationException("Lucene.NET index has no id terms.");
        var logicalIdsByDocumentId = new string?[reader.MaxDoc];
        TermsEnum termsEnum = terms.GetEnumerator();
        while (termsEnum.MoveNext())
        {
            string logicalId = termsEnum.Term.Utf8ToString();
            DocsEnum documents = termsEnum.Docs(null, null, DocsFlags.NONE);
            for (int documentId = documents.NextDoc();
                 documentId != DocIdSetIterator.NO_MORE_DOCS;
                 documentId = documents.NextDoc())
            {
                if (logicalIdsByDocumentId[documentId] is not null)
                    throw new InvalidOperationException($"Lucene.NET document {documentId} has more than one id term.");
                logicalIdsByDocumentId[documentId] = logicalId;
            }
        }

        if (logicalIdsByDocumentId.Any(static id => id is null))
            throw new InvalidOperationException("Lucene.NET id terms did not identify every indexed document.");

        return logicalIdsByDocumentId;
    }

    private static string DescribeDifferences(string engine, IEnumerable<string> logicalIds,
        IReadOnlyList<string> queryTerms)
    {
        string[] details = logicalIds.Take(5)
            .Select(id => $"{id}: {DescribeTermPositions(id, queryTerms)}")
            .ToArray();
        return $"{engine} logical IDs and query-term positions: {(details.Length == 0 ? "(none)" : string.Join("; ", details))}";
    }

    private static string DescribeTermPositions(string logicalId, IReadOnlyList<string> queryTerms)
    {
        if (!int.TryParse(logicalId, NumberStyles.None, CultureInfo.InvariantCulture, out int documentIndex))
            throw new InvalidOperationException($"Logical id '{logicalId}' is not a shared-index insertion id.");

        string[] documents = SharedStandardIndex.Documents;
        if ((uint)documentIndex >= (uint)documents.Length)
            throw new InvalidOperationException($"Logical id '{logicalId}' is outside the shared document body table.");

        var sink = new PhrasePositionTokenSink();
        new Rowles.LeanCorpus.Analysis.Analysers.StandardAnalyser()
            .Analyse(documents[documentIndex].AsSpan(), sink);
        return string.Join(", ", queryTerms.Select(term =>
            $"{term}=[{string.Join(",", sink.Tokens
                .Where(token => string.Equals(token.Term, term, StringComparison.Ordinal))
                .Select(static token => token.Position))}]"));
    }

    private sealed record PhraseDefinition(string[] Terms, int[] Positions, int Slop);

    private sealed class PhrasePositionTokenSink : Rowles.LeanCorpus.Analysis.ISpanTokenSink
    {
        private int _position = -1;

        internal List<(string Term, int Position)> Tokens { get; } = [];

        public void Add(ReadOnlySpan<char> text, int startOffset, int endOffset,
            string type = Rowles.LeanCorpus.Analysis.Token.DefaultType, int positionIncrement = 1,
            byte[]? payload = null)
        {
            _position = checked(_position + positionIncrement);
            Tokens.Add((text.ToString(), _position));
        }
    }
}
