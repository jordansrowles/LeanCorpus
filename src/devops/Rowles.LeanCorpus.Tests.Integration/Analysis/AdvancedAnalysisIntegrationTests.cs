using Rowles.LeanCorpus.Analysis;
using Rowles.LeanCorpus.Analysis.Analysers;
using Rowles.LeanCorpus.Analysis.Filters;
using Rowles.LeanCorpus.Analysis.Tokenisers;
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index;
using Rowles.LeanCorpus.Search;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Tests.Shared.Fixtures;

namespace Rowles.LeanCorpus.Tests.Integration.Analysis;

[Trait("Category", "Analysis")]
[Trait("Category", "Integration")]
public sealed class AdvancedAnalysisIntegrationTests : IClassFixture<TestDirectoryFixture>
{
    private readonly TestDirectoryFixture _fixture;

    public AdvancedAnalysisIntegrationTests(TestDirectoryFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact(DisplayName = "UAX29 URL Email Tokeniser: Indexes URLs And Email Addresses")]
    public void Uax29UrlEmailTokeniser_IndexesUrlsAndEmailAddresses()
    {
        using var directory = new MMapDirectory(SubDir("uax29_special_terms"));
        var analyser = new Analyser(new Uax29UrlEmailTokeniser(), new LowercaseFilter());

        using (var writer = new IndexWriter(directory, new IndexWriterConfig { DefaultAnalyser = analyser }))
        {
            var document = new LeanDocument();
            document.Add(new TextField("body", "Reach dev@example.com or visit https://example.com/docs"));
            writer.AddDocument(document);
            writer.Commit();
        }

        using var searcher = new IndexSearcher(directory);
        Assert.Equal(1, searcher.Search(new TermQuery("body", "dev@example.com"), 10).TotalHits);
        Assert.Equal(1, searcher.Search(new TermQuery("body", "https://example.com/docs"), 10).TotalHits);
    }

    [Fact(DisplayName = "MediaWiki Tokeniser With Type Filter: Indexes Category Terms Only")]
    public void MediaWikiTokeniser_WithTypeFilter_IndexesCategoryTermsOnly()
    {
        using var directory = new MMapDirectory(SubDir("mediawiki_category_only"));
        var analyser = new Analyser(
            new MediaWikiTokeniser(),
            new TypeTokenFilter([MediaWikiTokeniser.CategoryType]),
            new LowercaseFilter());

        using (var writer = new IndexWriter(directory, new IndexWriterConfig { DefaultAnalyser = analyser }))
        {
            var document = new LeanDocument();
            document.Add(new TextField("body", "[[Category:Search Engines]] [[Main Page|Ignored Body]]"));
            writer.AddDocument(document);
            writer.Commit();
        }

        using var searcher = new IndexSearcher(directory);
        Assert.Equal(1, searcher.Search(new TermQuery("body", "search"), 10).TotalHits);
        Assert.Equal(0, searcher.Search(new TermQuery("body", "ignored"), 10).TotalHits);
    }

    [Fact(DisplayName = "Metaphone Filter: Phonetic Alternate Is Searchable")]
    public void MetaphoneFilter_PhoneticAlternate_IsSearchable()
    {
        using var directory = new MMapDirectory(SubDir("metaphone_index"));
        var analyser = new Analyser(new Tokeniser(), new LowercaseFilter(), new MetaphoneFilter(), new FlattenGraphFilter());

        using (var writer = new IndexWriter(directory, new IndexWriterConfig { DefaultAnalyser = analyser }))
        {
            var document = new LeanDocument();
            document.Add(new TextField("body", "phone"));
            writer.AddDocument(document);
            writer.Commit();
        }

        using var searcher = new IndexSearcher(directory);
        Assert.Equal(1, searcher.Search(new TermQuery("body", PhoneticEncoding.EncodeMetaphone("phone")), 10).TotalHits);
    }

    [Fact(DisplayName = "Metaphone Filter: Same Position Alternate Preserves Phrase Matching")]
    public void MetaphoneFilter_SamePositionAlternate_PreservesPhraseMatching()
    {
        using var directory = new MMapDirectory(SubDir("metaphone_phrase"));
        var analyser = new Analyser(new Tokeniser(), new LowercaseFilter(), new MetaphoneFilter(), new FlattenGraphFilter());

        using (var writer = new IndexWriter(directory, new IndexWriterConfig { DefaultAnalyser = analyser }))
        {
            var document = new LeanDocument();
            document.Add(new TextField("body", "phone book"));
            writer.AddDocument(document);
            writer.Commit();
        }

        using var searcher = new IndexSearcher(directory);
        var phrase = new PhraseQuery("body", PhoneticEncoding.EncodeMetaphone("phone"), "book");
        Assert.Equal(1, searcher.Search(phrase, 10).TotalHits);
    }

    [Fact(DisplayName = "Hunspell Stem Filter: Stemmed Term Matches Inflected Document")]
    public void HunspellStemFilter_StemmedTerm_MatchesInflectedDocument()
    {
        const string aff = """
SET UTF-8
PFX R Y 1
PFX R 0 re .
SFX D Y 1
SFX D 0 ing .
""";
        const string dic = """
1
play/RD
""";
        using var directory = new MMapDirectory(SubDir("hunspell_index"));
        var analyser = new Analyser(
            new Tokeniser(),
            new LowercaseFilter(),
            new HunspellStemFilter(HunspellDictionary.Parse(aff, dic)),
            new FlattenGraphFilter());

        using (var writer = new IndexWriter(directory, new IndexWriterConfig { DefaultAnalyser = analyser }))
        {
            var document = new LeanDocument();
            document.Add(new TextField("body", "replaying"));
            writer.AddDocument(document);
            writer.Commit();
        }

        using var searcher = new IndexSearcher(directory);
        Assert.Equal(1, searcher.Search(new TermQuery("body", "play"), 10).TotalHits);
    }

    [Fact(DisplayName = "Limit Token Count Filter: Drops Terms Beyond Limit")]
    public void LimitTokenCountFilter_DropsTermsBeyondLimit()
    {
        using var directory = new MMapDirectory(SubDir("limit_token_count"));
        var analyser = new Analyser(new Tokeniser(), new LowercaseFilter(), new LimitTokenCountFilter(2));

        using (var writer = new IndexWriter(directory, new IndexWriterConfig { DefaultAnalyser = analyser }))
        {
            var document = new LeanDocument();
            document.Add(new TextField("body", "alpha beta gamma"));
            writer.AddDocument(document);
            writer.Commit();
        }

        using var searcher = new IndexSearcher(directory);
        Assert.Equal(1, searcher.Search(new TermQuery("body", "alpha"), 10).TotalHits);
        Assert.Equal(0, searcher.Search(new TermQuery("body", "gamma"), 10).TotalHits);
    }

    private string SubDir(string name)
    {
        var path = Path.Combine(_fixture.Path, name);
        Directory.CreateDirectory(path);
        return path;
    }
}
