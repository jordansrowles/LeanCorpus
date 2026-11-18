using Rowles.LeanCorpus.Codecs.Fst;
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Search;
using Rowles.LeanCorpus.Search.Queries;
using Rowles.LeanCorpus.Search.Searcher;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Tests.Core.Search;

/// <summary>
/// Every wildcard route must select the same terms: the outputs-only FST traversal, the term-returning
/// traversal, literal-prefix narrowing, the trailing-star prefix route, bounded admission and the public
/// <see cref="WildcardQuery.Matches"/> helper. <c>?</c> is one Unicode scalar value and a backslash escapes.
/// </summary>
[Category(TestCategory.Integration)]
[Area(TestArea.Search)]
public sealed class WildcardRouteAgreementTests : IDisposable
{
    private static readonly string[] Words =
    [
        "cat", "cats", "catapult", "cot", "dog", "café", "cafe", "a*b", "a?b", "axb", "🦊", "🦊cat", @"x\", "xy", "é",
    ];

    private readonly string _path = RepositoryTemporaryStorage.NewPath("tests", "wildcard-routes-");
    public WildcardRouteAgreementTests() => Directory.CreateDirectory(_path);
    public void Dispose() => Directory.Delete(_path, true);

    public static TheoryData<string> Patterns => new()
    {
        "?", "??", "c?t", "c?ts", "caf?", @"a\*b", @"a\?b", "a?b", "*🦊*", "🦊*", "?cat", "c*", "*t", "ca*t",
        @"x\", @"x\\", "*", "cat*", "ca?*", "*a*", "dog", "zzz*", "?*?",
    };

    [Theory]
    [InlineData("cat", "c?t", true)]
    [InlineData("cats", "c?t", false)]
    [InlineData("café", "caf?", true)]
    [InlineData("🦊", "?", true)]
    [InlineData("🦊cat", "?cat", true)]
    [InlineData("🦊cat", "??cat", false)]
    [InlineData("a*b", @"a\*b", true)]
    [InlineData("axb", @"a\*b", false)]
    [InlineData("a?b", @"a\?b", true)]
    [InlineData("axb", @"a\?b", false)]
    [InlineData(@"x\", @"x\", true)]
    [InlineData(@"x\", @"x\\", true)]
    [InlineData("xy", @"x\", false)]
    [InlineData("🦊cat", "*🦊*", true)]
    public void Helper_UsesScalarAndEscapeSemantics(string term, string pattern, bool expected)
        => Assert.Equal(expected, WildcardQuery.Matches(term, pattern));

    [Fact]
    public void TrailingAcceptingState_WithRejectedBytes_IsNotASink()
    {
        var automaton = new WildcardAutomaton("c?t");
        int state = automaton.Start;
        foreach (byte value in "cat"u8)
            state = automaton.Step(state, value);
        Assert.True(automaton.IsAccept(state));
        Assert.False(automaton.IsSink(state));

        var trailingStar = new WildcardAutomaton("ca*");
        state = trailingStar.Start;
        foreach (byte value in "ca"u8)
            state = trailingStar.Step(state, value);
        Assert.True(trailingStar.IsSink(state));
    }

    [Theory]
    [MemberData(nameof(Patterns))]
    public void SegmentTraversals_AgreeWithHelper(string pattern)
    {
        using var directory = Build(manySegments: false);
        var expected = Expected(pattern);
        using var searcher = new IndexSearcher(directory, new IndexSearcherConfig { EnableQueryCache = false, ParallelSearch = false });
        var reader = Assert.Single(searcher.GetSegmentReaders());
        var terms = reader.GetTermsMatching("word\0", pattern).Select(static match => match.Term[5..]).Order(StringComparer.Ordinal).ToArray();
        var offsets = reader.GetTermOffsetsMatching("word\0", pattern);
        Assert.Equal(expected, terms);
        Assert.Equal(expected.Length, offsets.Count);

        var (prefix, suffix) = WildcardPattern.SplitLeadingLiteral(pattern);
        if (prefix.Length > 0)
        {
            var narrowed = reader.GetTermsMatchingWithPrefix("word", prefix, suffix).Select(static match => match.Term[5..]).Order(StringComparer.Ordinal).ToArray();
            Assert.Equal(expected, narrowed);
            Assert.Equal(expected.Length, reader.GetTermOffsetsMatchingWithPrefix("word", prefix, suffix).Count);
        }
    }

    [Theory]
    [MemberData(nameof(Patterns))]
    public void Search_MatchesHelperOnOneAndManySegments(string pattern)
    {
        var expected = Expected(pattern);
        foreach (bool many in new[] { false, true })
        {
            using var directory = Build(many);
            using var searcher = new IndexSearcher(directory, new IndexSearcherConfig { EnableQueryCache = false, ParallelSearch = false });
            var ct = TestContext.Current.CancellationToken;
            Assert.Equal(expected, HitWords(searcher, searcher.Search(new WildcardQuery("word", pattern), 100, ct)));
            Assert.Equal(expected, HitWords(searcher, searcher.Search(new WildcardQuery("word", pattern), 100, SearchOptions.Default)));
            Assert.Equal(expected.Length, searcher.Count(new WildcardQuery("word", pattern)));
            Assert.Equal(expected.Length, searcher.SearchStreaming(new WildcardQuery("word", pattern), 100).Count());
        }
    }

    [Theory]
    [MemberData(nameof(Patterns))]
    public void ExpansionLimit_CountsTheSameTermsExecutionReturns(string pattern)
    {
        var expected = Expected(pattern);
        if (expected.Length == 0)
            return;

        foreach (bool many in new[] { false, true })
        {
            using var directory = Build(many);
            using var searcher = new IndexSearcher(directory, new IndexSearcherConfig { EnableQueryCache = false, ParallelSearch = false });
            var ct = TestContext.Current.CancellationToken;
            Assert.Equal(expected, HitWords(searcher, searcher.Search(new WildcardQuery("word", pattern, expected.Length), 100, ct)));
            if (expected.Length > 1)
            {
                Assert.Throws<QueryExpansionLimitException>(() =>
                    searcher.Search(new WildcardQuery("word", pattern, expected.Length - 1), 100, ct));
            }

        }
    }

    private static string[] Expected(string pattern)
        => Words.Where(word => WildcardQuery.Matches(word, pattern)).Order(StringComparer.Ordinal).ToArray();

    private static string[] HitWords(IndexSearcher searcher, TopDocs result)
        => result.ScoreDocs.Select(hit => searcher.GetStoredFields(hit.DocId)["word"][0]).Order(StringComparer.Ordinal).ToArray();

    private MMapDirectory Build(bool manySegments)
    {
        string path = Path.Combine(_path, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        var directory = new MMapDirectory(path);
        using var writer = new IndexWriter(directory, new IndexWriterConfig
        {
            MergePolicy = NoMergePolicy.Instance,
            MaxBufferedDocs = manySegments ? 1 : 1000,
        });
        foreach (string word in Words)
        {
            var document = new LeanDocument();
            document.Add(new StringField("word", word, stored: true));
            writer.AddDocument(document);
            if (manySegments)
                writer.Commit();
        }

        writer.Commit();
        return directory;
    }
}

/// <summary>Coordinator queries must apply the same distinct-term admission limits as their bounded children.</summary>
[Category(TestCategory.Integration)]
[Area(TestArea.Search)]
public sealed class CompositeExpansionAdmissionTests : IDisposable
{
    private readonly string _path = RepositoryTemporaryStorage.NewPath("tests", "composite-admission-");
    public CompositeExpansionAdmissionTests() => Directory.CreateDirectory(_path);
    public void Dispose() => Directory.Delete(_path, true);

    public static TheoryData<string> Children => new() { "prefix", "wildcard", "regexp" };

    [Theory]
    [MemberData(nameof(Children))]
    public void BlockJoinAndRrf_RejectOverLimitChildrenOnEveryEntryPoint(string kind)
    {
        using var directory = new MMapDirectory(_path);
        using (var writer = new IndexWriter(directory, new IndexWriterConfig { MergePolicy = NoMergePolicy.Instance }))
        {
            writer.AddDocumentBlock([Document("word", "word0"), Document("word", "word1"), Document("kind", "parent")]);
            writer.Commit();
        }

        using var searcher = new IndexSearcher(directory, new IndexSearcherConfig { EnableQueryCache = false, ParallelSearch = false });
        var ct = TestContext.Current.CancellationToken;
        Query[] shapes =
        [
            Child(kind, 1),
            new BlockJoinQuery(Child(kind, 1)),
            new RrfQuery().Add(new TermQuery("kind", "parent")).Add(new BlockJoinQuery(Child(kind, 1))),
        ];

        // The limit counts the two distinct child terms, not the single returned parent.
        foreach (var query in shapes)
        {
            Assert.Throws<QueryExpansionLimitException>(() => searcher.Search(query, 10));
            Assert.Throws<QueryExpansionLimitException>(() => searcher.Search(query, 10, ct));
            Assert.Throws<QueryExpansionLimitException>(() => searcher.Search(query, 10, SearchOptions.Default));
            Assert.Throws<QueryExpansionLimitException>(() => searcher.Count(query));
            Assert.Throws<QueryExpansionLimitException>(() => searcher.SearchStreaming(query, 10).ToArray());
            Assert.Throws<QueryExpansionLimitException>(() => searcher.Search(query, 10, SortField.Score));
        }

        // At the limit the same shapes execute normally.
        Assert.Equal(1, searcher.Search(new BlockJoinQuery(Child(kind, 2)), 10, ct).TotalHits);
        Assert.Equal(1, searcher.Search(new BlockJoinQuery(Child(kind, 2)), 10, SearchOptions.Default).TotalHits);
        Assert.Equal(1, searcher.Count(new BlockJoinQuery(Child(kind, 2))));
    }

    private static Query Child(string kind, int limit) => kind switch
    {
        "prefix" => new PrefixQuery("word", "word", limit),
        "wildcard" => new WildcardQuery("word", "word*", limit),
        _ => new RegexpQuery("word", "word.*", System.Text.RegularExpressions.RegexOptions.None, limit),
    };

    private static LeanDocument Document(string field, string value)
    {
        var document = new LeanDocument();
        document.Add(new StringField(field, value));
        return document;
    }
}
