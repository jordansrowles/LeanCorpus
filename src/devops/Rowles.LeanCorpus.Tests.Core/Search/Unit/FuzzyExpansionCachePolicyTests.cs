using System.Reflection;
using System.Collections.Concurrent;
using Rowles.LeanCorpus.Codecs.TermDictionary;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Tests.Shared.Fixtures;

namespace Rowles.LeanCorpus.Tests.Core.Search;

[Category(TestCategory.Unit)]
[Area(TestArea.Search)]
public sealed class FuzzyExpansionCachePolicyTests : IClassFixture<TestDirectoryFixture>
{
    private readonly TestDirectoryFixture _fixture;

    public FuzzyExpansionCachePolicyTests(TestDirectoryFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void FuzzyExpansionCachePolicyIsInternalAndEnabledByDefault()
    {
        var configType = typeof(IndexSearcherConfig);
        var property = configType.GetProperty("UseFuzzyExpansionCache", BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.Null(configType.GetProperty("UseFuzzyExpansionCache", BindingFlags.Instance | BindingFlags.Public));
        Assert.NotNull(property);
        Assert.True(property!.GetMethod!.IsAssembly);
        Assert.True((bool)property.GetValue(new IndexSearcherConfig())!);
    }

    [Fact]
    public void QueryResultAndFuzzyExpansionCachePoliciesAreIndependent()
    {
        var property = typeof(IndexSearcherConfig).GetProperty("UseFuzzyExpansionCache", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(property);

        var config = new IndexSearcherConfig { EnableQueryCache = true };
        property!.SetValue(config, false);

        Assert.True(config.EnableQueryCache);
        Assert.False((bool)property.GetValue(config)!);
    }

    [Fact]
    public void BypassedFuzzyExpansionMatchesCachedResultsWithoutReplacingCacheEntry()
    {
        using var reader = OpenDictionary("fuzzy-policy-populated");
        var lookupWithoutCache = BindCachePolicyLookup(reader);

        var cases = new (string Query, int MaxEdits)[]
        {
            ("hello", 0),
            ("hella", 1),
            ("café", 2),
            ("日本語", 1)
        };

        foreach (var (query, maxEdits) in cases)
        {
            var cached = reader.GetFuzzyMatches("body\0", query.AsSpan(), maxEdits, maxExpansions: 50);
            Assert.Same(cached, reader.GetFuzzyMatches("body\0", query.AsSpan(), maxEdits, maxExpansions: 50));

            var uncachedFirst = lookupWithoutCache("body\0", query.AsSpan(), maxEdits, 50, useCache: false);
            var uncachedSecond = lookupWithoutCache("body\0", query.AsSpan(), maxEdits, 50, useCache: false);

            Assert.Equal(cached, uncachedFirst);
            Assert.Equal(cached, uncachedSecond);
            Assert.NotSame(cached, uncachedFirst);
            Assert.NotSame(uncachedFirst, uncachedSecond);
            Assert.Same(cached, reader.GetFuzzyMatches("body\0", query.AsSpan(), maxEdits, maxExpansions: 50));
        }
    }

    [Fact]
    public void EmptyFuzzyExpansionIsCachedByDefaultAndRecomputedWhenBypassed()
    {
        using var reader = OpenDictionary("fuzzy-policy-empty");
        var lookupWithoutCache = BindCachePolicyLookup(reader);

        var cached = reader.GetFuzzyMatches("body\0", "zzzznomatch".AsSpan(), 2, maxExpansions: 50);
        Assert.Empty(cached);
        Assert.Same(cached, reader.GetFuzzyMatches("body\0", "zzzznomatch".AsSpan(), 2, maxExpansions: 50));

        var uncachedFirst = lookupWithoutCache("body\0", "zzzznomatch".AsSpan(), 2, 50, useCache: false);
        var uncachedSecond = lookupWithoutCache("body\0", "zzzznomatch".AsSpan(), 2, 50, useCache: false);

        Assert.Empty(uncachedFirst);
        Assert.Empty(uncachedSecond);
        Assert.NotSame(cached, uncachedFirst);
        Assert.NotSame(uncachedFirst, uncachedSecond);
        Assert.Same(cached, reader.GetFuzzyMatches("body\0", "zzzznomatch".AsSpan(), 2, maxExpansions: 50));
    }

    [Fact]
    public void EmptyFuzzyExpansionBypassExecutesFstIntersectionOnEveryCall()
    {
        using var reader = OpenDictionary("fuzzy-policy-empty-fst-probe");
        int intersections = 0;
        Action recordIntersection = () => Interlocked.Increment(ref intersections);

        var cached = reader.GetFuzzyMatches("body\0", "zzzznomatch".AsSpan(), 2, maxExpansions: 50);
        Assert.Empty(cached);
        Assert.Same(cached, reader.GetFuzzyMatches("body\0", "zzzznomatch".AsSpan(), 2, maxExpansions: 50));
        Assert.Equal(0, Volatile.Read(ref intersections));

        var uncachedFirst = reader.GetFuzzyMatches(
            "body\0", "zzzznomatch".AsSpan(), 2, 50, useCache: false, beforeFstIntersection: recordIntersection);
        Assert.Empty(uncachedFirst);
        Assert.Equal(1, Volatile.Read(ref intersections));

        var uncachedSecond = reader.GetFuzzyMatches(
            "body\0", "zzzznomatch".AsSpan(), 2, 50, useCache: false, beforeFstIntersection: recordIntersection);
        Assert.Empty(uncachedSecond);
        Assert.Equal(2, Volatile.Read(ref intersections));
        Assert.NotSame(uncachedFirst, uncachedSecond);
        Assert.Same(cached, reader.GetFuzzyMatches("body\0", "zzzznomatch".AsSpan(), 2, maxExpansions: 50));
        Assert.Equal(2, Volatile.Read(ref intersections));
    }

    [Fact]
    public void BypassDoesNotClearOrTaintConcurrentDefaultCacheUsers()
    {
        using var reader = OpenDictionary("fuzzy-policy-concurrent");
        var lookupWithoutCache = BindCachePolicyLookup(reader);
        const string query = "hello";
        var cached = reader.GetFuzzyMatches("body\0", query.AsSpan(), 1, maxExpansions: 50);
        var bypassedResults = new ConcurrentBag<List<(string Term, long Offset, int Distance)>>();

        Parallel.For(0, 64, iteration =>
        {
            if ((iteration & 1) == 0)
            {
                var bypassed = lookupWithoutCache("body\0", query.AsSpan(), 1, 50, useCache: false);
                Assert.NotSame(cached, bypassed);
                Assert.Equal(cached, bypassed);
                bypassedResults.Add(bypassed);
            }
            else
            {
                Assert.Same(cached, reader.GetFuzzyMatches("body\0", query.AsSpan(), 1, maxExpansions: 50));
            }
        });

        Assert.Equal(32, bypassedResults.Count);
        Assert.All(bypassedResults, result =>
        {
            Assert.NotSame(cached, result);
            Assert.Equal(cached, result);
        });
        Assert.Same(cached, reader.GetFuzzyMatches("body\0", query.AsSpan(), 1, maxExpansions: 50));
    }

    private TermDictionaryReader OpenDictionary(string name)
    {
        var path = Path.Combine(_fixture.Path, name + ".dic");
        var terms = new List<string>
        {
            "body\0cafe",
            "body\0café",
            "body\0caféx",
            "body\0hello",
            "body\0helo",
            "body\0hallo",
            "body\0日本語",
            "body\0日本国"
        };
        terms.Sort(StringComparer.Ordinal);
        var offsets = terms.Select((term, index) => (term, index))
            .ToDictionary(static pair => pair.term, static pair => (long)pair.index, StringComparer.Ordinal);

        TermDictionaryWriter.Write(path, terms, offsets);
        return TermDictionaryReader.Open(path);
    }

    private static FuzzyCachePolicyLookup BindCachePolicyLookup(TermDictionaryReader reader)
    {
        var method = typeof(TermDictionaryReader).GetMethod(
            nameof(TermDictionaryReader.GetFuzzyMatches),
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            types: [typeof(string), typeof(ReadOnlySpan<char>), typeof(int), typeof(int), typeof(bool)],
            modifiers: null);

        Assert.NotNull(method);
        return (FuzzyCachePolicyLookup)method!.CreateDelegate(typeof(FuzzyCachePolicyLookup), reader);
    }

    private delegate List<(string Term, long Offset, int Distance)> FuzzyCachePolicyLookup(
        string fieldPrefix, ReadOnlySpan<char> queryTerm, int maxEdits, int maxExpansions, bool useCache);
}
