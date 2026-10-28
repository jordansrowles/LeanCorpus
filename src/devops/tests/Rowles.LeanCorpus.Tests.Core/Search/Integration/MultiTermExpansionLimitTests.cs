using Rowles.LeanCorpus.Codecs.TermDictionary;
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Search;
using Rowles.LeanCorpus.Search.Queries;
using Rowles.LeanCorpus.Search.Searcher;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Tests.Core.Search;

[Category(TestCategory.Integration)]
[Area(TestArea.Search)]
public sealed class MultiTermExpansionLimitTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), "multi-term-limit-" + Guid.NewGuid().ToString("N"));
    public MultiTermExpansionLimitTests() => Directory.CreateDirectory(_path);
    public void Dispose() => Directory.Delete(_path, true);

    [Theory]
    [InlineData("wildcard")]
    [InlineData("prefix")]
    [InlineData("wildcard-prefix")]
    [InlineData("regexp")]
    [InlineData("regexp-prefix")]
    [InlineData("regexp-contains")]
    public void StopsAtNinthAdmissionAndRetainsLimitInNestedExecution(string kind)
    {
        using var directory = new MMapDirectory(_path);
        WriteSegment(directory, Enumerable.Range(0, 2048).Select(i => $"word{i:D4}"));
        Query bounded = Create(kind, 8);
        using (var dictionary = TermDictionaryReader.Open(Directory.GetFiles(_path, "*.dic").Single()))
        {
            int visited = 0;
            var budget = new MultiTermExpansionBudget(8);
            Assert.Throws<QueryExpansionLimitException>(() => dictionary.VisitMatchingTerms(bounded.Field,
                (bounded as PrefixQuery)?.Prefix, (bounded as WildcardQuery)?.Pattern,
                (bounded as RegexpQuery)?.CompiledRegex, _ =>
            {
                visited++;
                budget.Admit(_);
            }));
            Assert.Equal(9, visited);
            Assert.Equal(8, budget.AcceptedTerms);
        }
        using var searcher = new IndexSearcher(directory, new IndexSearcherConfig { ParallelSearch = true, EnableQueryCache = true });
        Query[] paths =
        [
            bounded,
            new BooleanQuery.Builder().Add(bounded, Occur.Must).Build(),
            new ConstantScoreQuery(bounded),
            new SpanMultiTermQueryWrapper(bounded)
        ];
        foreach (Query path in paths)
        {
            var failure = Assert.Throws<QueryExpansionLimitException>(() => searcher.Search(path, 10));
            Assert.Equal(9, failure.AttemptedExpansions);
            Assert.Throws<QueryExpansionLimitException>(() => searcher.Count(path));
            Assert.Throws<QueryExpansionLimitException>(() => searcher.SearchStreaming(path).ToArray());
            Assert.Throws<QueryExpansionLimitException>(() => searcher.Search(path, 10, SearchOptions.Default));
            Assert.Throws<QueryExpansionLimitException>(() => searcher.Search(path, 10, CancellationToken.None));
        }
        Assert.True(searcher.Search(Create(kind, null), 10).TotalHits > 0);
        Assert.True(searcher.Search(Create(kind, 2048), 10).TotalHits > 0);
        Assert.Throws<QueryExpansionLimitException>(() => searcher.Search(bounded, 10));
        Assert.Throws<OperationCanceledException>(() => searcher.Search(bounded, 10, new CancellationToken(true)));
        Assert.True(searcher.Search(bounded, 10, SearchOptions.WithTimeout(TimeSpan.Zero)).IsPartial);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CountsDistinctTermsAcrossSegmentsWithoutLeakingBetweenSearches(bool repeatTerms)
    {
        using var directory = new MMapDirectory(_path);
        WriteSegment(directory, Enumerable.Range(0, 6).Select(i => $"word{i:D4}"));
        WriteSegment(directory, Enumerable.Range(repeatTerms ? 0 : 6, 6).Select(i => $"word{i:D4}"));
        using var searcher = new IndexSearcher(directory);
        var query = new WildcardQuery("body", "*", 8);
        if (repeatTerms)
        {
            Assert.Equal(2, searcher.Search(query, 10).TotalHits);
            Assert.Equal(2, searcher.Search(query, 10).TotalHits);
        }
        else
        {
            Assert.Throws<QueryExpansionLimitException>(() => searcher.Search(query, 10));
            Assert.Throws<QueryExpansionLimitException>(() => searcher.Search(query, 10));
        }
    }

    [Fact]
    public void ParserCanBeReusedAfterExecutionAdmissionRejectsItsQuery()
    {
        using var directory = new MMapDirectory(_path);
        WriteSegment(directory, Enumerable.Range(0, 2048).Select(i => $"word{i:D4}"));
        using var searcher = new IndexSearcher(directory);
        var analyser = new Rowles.LeanCorpus.Analysis.Analysers.StandardAnalyser();
        var parser = new Rowles.LeanCorpus.Search.Parsing.QueryParser("body", analyser,
            new Rowles.LeanCorpus.Search.Parsing.QueryParserOptions { MaxWildcardExpansions = 8, MaxRegexpExpansions = 8 });
        Assert.Throws<QueryExpansionLimitException>(() => searcher.Search(parser.Parse("word*"), 10));
        Assert.Equal(1, searcher.Search(parser.Parse("word0000"), 10).TotalHits);
        Assert.Throws<QueryExpansionLimitException>(() => searcher.Search(parser.Parse("/.*/"), 10));
        Assert.Equal(1, searcher.Search(parser.Parse("word0000"), 10).TotalHits);
    }

    private static Query Create(string kind, int? limit) => kind switch
    {
        "wildcard-prefix" => new WildcardQuery("body", "word*", limit),
        "prefix" => new PrefixQuery("body", "word", limit),
        "regexp" => new RegexpQuery("body", ".*", System.Text.RegularExpressions.RegexOptions.None, limit),
        "regexp-prefix" => new RegexpQuery("body", "word.*", System.Text.RegularExpressions.RegexOptions.None, limit),
        "regexp-contains" => new RegexpQuery("body", ".*word.*", System.Text.RegularExpressions.RegexOptions.None, limit),
        _ => new WildcardQuery("body", "*", limit)
    };

    private static void WriteSegment(MMapDirectory directory, IEnumerable<string> terms)
    {
        using var writer = new IndexWriter(directory, new IndexWriterConfig { MergePolicy = NoMergePolicy.Instance, UseCompoundFile = false });
        var doc = new LeanDocument();
        doc.Add(new TextField("body", string.Join(' ', terms)));
        writer.AddDocument(doc);
        writer.Commit();
    }
}
