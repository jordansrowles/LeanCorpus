using System.Text.RegularExpressions;
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Search;
using Rowles.LeanCorpus.Search.Queries;
using Rowles.LeanCorpus.Search.Searcher;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Tests.Shared.Fixtures;

namespace Rowles.LeanCorpus.Tests.Core.Search;

/// <summary>
/// Verifies that <see cref="RegexpQuery"/> matches whole terms on every route (ADR048): the
/// literal-prefix, contains and fallback routes return the terms a whole-term reference returns.
/// </summary>
[Category(TestCategory.Integration)]
[Area(TestArea.Search)]
public sealed class RegexpWholeTermMatchingTests : IClassFixture<TestDirectoryFixture>
{
    private static readonly string[] GovernmentTerms = ["government", "governments", "misgovernment", "mismanagement", "movement"];
    private static readonly string[] CarTerms = ["cat", "car", "card", "dog", "cargo"];

    private readonly TestDirectoryFixture _fixture;

    public RegexpWholeTermMatchingTests(TestDirectoryFixture fixture) => _fixture = fixture;

    /// <summary>
    /// Expected values are Lucene.NET's whole-term results for the same terms (Sprint 6 background).
    /// </summary>
    [Theory(DisplayName = "Regexp: Whole-Term Matching Agrees With Lucene.NET")]
    [InlineData("gov.*ment", "government")]
    [InlineData("(gov).*ment", "government")]
    [InlineData("^gov.*ment$", "government")]
    [InlineData("ment", "")]
    [InlineData("[gm].*ment", "government,misgovernment,mismanagement,movement")]
    public void Regexp_WholeTermMatching_AgreesWithLuceneNet(string pattern, string expected)
    {
        using var searcher = BuildIndex("regexp_lucene_" + Guid.NewGuid().ToString("N"), GovernmentTerms, segments: 1);

        Assert.Equal(Split(expected), MatchedTerms(searcher, pattern));
    }

    [Theory(DisplayName = "Regexp: Anchors Match Whole Terms")]
    [InlineData("^car", "car")]
    [InlineData("car", "car")]
    [InlineData("car.*", "car,card,cargo")]
    [InlineData("^car.*$", "car,card,cargo")]
    public void Regexp_Anchors_MatchWholeTerms(string pattern, string expected)
    {
        using var searcher = BuildIndex("regexp_car_" + Guid.NewGuid().ToString("N"), CarTerms, segments: 1);

        Assert.Equal(Split(expected), MatchedTerms(searcher, pattern));
    }

    /// <summary>
    /// Property: every route returns exactly the terms a whole-term reference matches, on one segment
    /// and on many. The patterns cover the literal-prefix, contains and fallback routes, plus the
    /// quantifier, escape and alternation cases that would make a literal prefix wrong.
    /// </summary>
    [Fact(DisplayName = "Regexp: All Routes Agree With A Whole-Term Reference")]
    public void Regexp_AllRoutes_AgreeWithWholeTermReference()
    {
        string[] terms =
        [
            "government", "governments", "misgovernment", "mismanagement", "movement", "governor", "gov",
            "go", "gold", "car", "card", "cargo", "cat", "nation", "national", "international", "nations",
            "a1", "ab", "abb", "abc", "x9ment", "ment", "moment"
        ];
        string[] patterns =
        [
            "gov.*", "gov.*ment", "(gov).*ment", "governments?", "govern(ment)?s?", "go.*", "gov", "gov+",
            ".*ment", ".*nation.*", ".*ation.*", "[gm].*ment", "(car|cat)", "car|cat", "ca[rt]d?", "ab*",
            "ab*c", "a\\d", "\\w+ment", "mo?ment", "nation(al)?", "inter.*", ".*", "x", "^gov.*$", "g.ld"
        ];

        using var single = BuildIndex("regexp_property_one_" + Guid.NewGuid().ToString("N"), terms, segments: 1);
        using var many = BuildIndex("regexp_property_many_" + Guid.NewGuid().ToString("N"), terms, segments: terms.Length);
        foreach (string pattern in patterns)
        {
            var reference = new Regex(string.Concat(@"\A(?:", pattern, @")\z"), RegexOptions.None, TimeSpan.FromSeconds(1));
            string[] expected = terms.Where(term => reference.IsMatch(term)).Order(StringComparer.Ordinal).ToArray();

            Assert.True(expected.SequenceEqual(MatchedTerms(single, pattern)), $"single segment, pattern '{pattern}'");
            Assert.True(expected.SequenceEqual(MatchedTerms(many, pattern)), $"many segments, pattern '{pattern}'");
        }
    }

    /// <summary>
    /// Property: the contains shortcut and the verified routes agree on terms with line terminators.
    /// With default options '.' does not match '\n', so '.*nation.*' must reject 'x\nnation' as the
    /// fallback '.*(nation).*' does, on one segment (unverified shortcut) and on many (verified).
    /// </summary>
    [Fact(DisplayName = "Regexp: Routes Agree On Terms With Line Terminators")]
    public void Regexp_Routes_AgreeOnLineTerminators()
    {
        string[] terms =
        [
            "nation", "x\nnation", "nation\n", "\nnation", "nat\nion", "na\ntion", "a\r\nnation", "nation\rx",
            "international", "inter\nnational", "\n", "plain", "gov\nment", "government"
        ];
        string[] patterns =
        [
            ".*nation.*", ".*(nation).*", ".*nat\nion.*", ".*\n.*", ".*", "nation.*", ".*nation", "gov.*ment",
            ".*ion.*", ".*\rx.*", ".*ation.*"
        ];

        using var single = BuildIndex("regexp_newline_one_" + Guid.NewGuid().ToString("N"), terms, segments: 1);
        using var many = BuildIndex("regexp_newline_many_" + Guid.NewGuid().ToString("N"), terms, segments: terms.Length);
        foreach (string pattern in patterns)
        {
            var reference = new Regex(string.Concat(@"\A(?:", pattern, @")\z"), RegexOptions.None, TimeSpan.FromSeconds(1));
            string[] expected = terms.Where(term => reference.IsMatch(term)).Order(StringComparer.Ordinal).ToArray();

            Assert.True(expected.SequenceEqual(MatchedTerms(single, pattern)), $"single segment, pattern '{Regex.Escape(pattern)}'");
            Assert.True(expected.SequenceEqual(MatchedTerms(many, pattern)), $"many segments, pattern '{Regex.Escape(pattern)}'");
        }
    }

    [Fact(DisplayName = "Regexp: Unbalanced Pattern Cannot Escape The Whole-Term Anchors")]
    public void Regexp_UnbalancedPattern_IsRejected()
        => Assert.ThrowsAny<ArgumentException>(() => new RegexpQuery("word", "a)|(b"));

    private IndexSearcher BuildIndex(string name, IReadOnlyList<string> terms, int segments)
    {
        string path = Path.Combine(_fixture.Path, name);
        Directory.CreateDirectory(path);
        var directory = new MMapDirectory(path);
        using (var writer = new IndexWriter(directory, new IndexWriterConfig
        {
            MaxBufferedDocs = Math.Max(1, (int)Math.Ceiling(terms.Count / (double)segments)),
            MergePolicy = NoMergePolicy.Instance
        }))
        {
            foreach (string term in terms)
            {
                var document = new LeanDocument();
                document.Add(new StringField("word", term, stored: true));
                writer.AddDocument(document);
            }
            writer.Commit();
        }
        return new IndexSearcher(directory);
    }

    private static string[] MatchedTerms(IndexSearcher searcher, string pattern)
    {
        var results = searcher.Search(new RegexpQuery("word", pattern), 1_000, TestContext.Current.CancellationToken);
        string[] terms = results.ScoreDocs
            .Select(hit => searcher.GetStoredFields(hit.DocId)["word"][0])
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(terms.Length, terms.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(terms.Length, results.TotalHits);
        return terms;
    }

    private static string[] Split(string value)
        => value.Length == 0 ? [] : value.Split(',').Order(StringComparer.Ordinal).ToArray();
}
