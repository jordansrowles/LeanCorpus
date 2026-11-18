using System.Globalization;
using System.Text.RegularExpressions;
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Search;
using Rowles.LeanCorpus.Search.Queries;
using Rowles.LeanCorpus.Search.Searcher;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Tests.Core.Search;

/// <summary>
/// Regular-expression options and constructor-time culture change which terms match, so they must be
/// part of query equality and result-cache identity.
/// </summary>
[Category(TestCategory.Integration)]
[Area(TestArea.Search)]
public sealed class RegexpQueryIdentityTests : IDisposable
{
    private readonly string _path = RepositoryTemporaryStorage.NewPath("tests", "regexp-identity-");
    public RegexpQueryIdentityTests() => Directory.CreateDirectory(_path);
    public void Dispose() => Directory.Delete(_path, true);

    [Theory]
    [InlineData("cat", RegexOptions.IgnoreCase)]
    [InlineData(".*", RegexOptions.Singleline)]
    [InlineData("c a t", RegexOptions.IgnorePatternWhitespace)]
    public void DifferentOptions_HaveDistinctIdentity(string pattern, RegexOptions options)
    {
        var plain = new RegexpQuery("word", pattern);
        var variant = new RegexpQuery("word", pattern, options);
        Assert.NotEqual(plain, variant);
        Assert.NotEqual(QueryCache.CreateQueryFingerprint(plain), QueryCache.CreateQueryFingerprint(variant));

        var cache = new QueryCache();
        cache.Put(plain, 10, new TopDocs(1, [new ScoreDoc(17, 1)]));
        Assert.Null(cache.TryGet(variant, 10));
    }

    [Fact]
    public void CompiledFlag_IsIgnoredByIdentity()
    {
        var plain = new RegexpQuery("word", "cat", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        var compiled = new RegexpQuery("word", "cat", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
        Assert.Equal(plain, compiled);
        Assert.Equal(plain.GetHashCode(), compiled.GetHashCode());
        Assert.Equal(QueryCache.CreateQueryFingerprint(plain), QueryCache.CreateQueryFingerprint(compiled));
    }

    [Fact]
    public void CultureSensitiveIgnoreCase_IsPartOfIdentity()
    {
        var original = CultureInfo.CurrentCulture;
        RegexpQuery english, turkish, englishInvariant, turkishInvariant;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            english = new RegexpQuery("word", "i", RegexOptions.IgnoreCase);
            englishInvariant = new RegexpQuery("word", "i", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
            turkish = new RegexpQuery("word", "i", RegexOptions.IgnoreCase);
            turkishInvariant = new RegexpQuery("word", "i", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }

        // The compiled expressions disagree on capital I, so the queries must not share results.
        Assert.NotEqual(english.CompiledRegex.IsMatch("I"), turkish.CompiledRegex.IsMatch("I"));
        Assert.NotEqual(english, turkish);
        var cache = new QueryCache();
        cache.Put(english, 10, TopDocs.Empty);
        Assert.Null(cache.TryGet(turkish, 10));

        // Culture-invariant matching does not depend on the constructing culture.
        Assert.Equal(englishInvariant, turkishInvariant);
        Assert.Equal(QueryCache.CreateQueryFingerprint(englishInvariant), QueryCache.CreateQueryFingerprint(turkishInvariant));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void CachedSearches_ReturnEachOptionSetsOwnResults(bool enableCache, bool ignoreCaseFirst)
    {
        using var directory = new MMapDirectory(_path);
        using (var writer = new IndexWriter(directory, new IndexWriterConfig { MergePolicy = NoMergePolicy.Instance, MaxBufferedDocs = 1 }))
        {
            foreach (string word in new[] { "cat", "CAT", "dog" })
            {
                var document = new LeanDocument();
                document.Add(new StringField("word", word));
                writer.AddDocument(document);
                writer.Commit();
            }
        }

        using var searcher = new IndexSearcher(directory, new IndexSearcherConfig { EnableQueryCache = enableCache, ParallelSearch = false });
        var ct = TestContext.Current.CancellationToken;
        Query plain = new RegexpQuery("word", "cat");
        Query ignoreCase = new RegexpQuery("word", "cat", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        Query nestedPlain = new BooleanQuery.Builder().Add(plain, Occur.Must).Build();
        Query nestedIgnoreCase = new BooleanQuery.Builder().Add(ignoreCase, Occur.Must).Build();

        var order = ignoreCaseFirst
            ? new[] { (ignoreCase, 2), (plain, 1), (nestedIgnoreCase, 2), (nestedPlain, 1) }
            : new[] { (plain, 1), (ignoreCase, 2), (nestedPlain, 1), (nestedIgnoreCase, 2) };
        foreach (var (query, expected) in order)
            Assert.Equal(expected, searcher.Search(query, 10, ct).TotalHits);

        // Repeating the searches exercises cache hits after both identities are stored.
        foreach (var (query, expected) in order)
            Assert.Equal(expected, searcher.Search(query, 10, ct).TotalHits);
    }
}
