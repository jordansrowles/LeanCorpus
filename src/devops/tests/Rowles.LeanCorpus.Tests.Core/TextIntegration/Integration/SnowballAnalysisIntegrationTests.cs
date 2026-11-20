using Rowles.LeanCorpus.Analysis;
using Rowles.LeanCorpus.Analysis.Analysers;
using Rowles.LeanCorpus.Analysis.Stemmers;
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index;
using Rowles.LeanCorpus.Search;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Tests.Shared.Fixtures;
using Rowles.LeanCorpus.Tests.Shared.Infrastructure;
using Rowles.LeanCorpus.Tests.Stemming;

namespace Rowles.LeanCorpus.Tests.Core;

/// <summary>
/// Issue #122 through Core, which compiles the analysis source itself (ADR021): the official
/// Snowball 3.2.0 stems reach the index, and Core's build matches the pinned oracle exactly.
/// </summary>
[Category(TestCategory.Integration)]
[Area(TestArea.TextIntegration)]
public sealed class SnowballAnalysisIntegrationTests : IClassFixture<TestDirectoryFixture>
{
    private readonly TestDirectoryFixture _fixture;

    public SnowballAnalysisIntegrationTests(TestDirectoryFixture fixture) => _fixture = fixture;

    public static TheoryData<string> PinnedAlgorithms() => new(SnowballFixtures.WordListAlgorithms);

    [Fact(DisplayName = "Snowball German: singular and plural recipes find each other (#122)")]
    public void German_SingularAndPlural_FindEachOther()
    {
        using var directory = new MMapDirectory(SubDir("snowball-german"));
        using (var writer = new IndexWriter(directory, new IndexWriterConfig { DefaultAnalyser = AnalyserFactory.Create("de") }))
        {
            writer.AddDocument(Doc("1", "Suppe mit Tomaten und Zwiebeln"));
            writer.AddDocument(Doc("2", "Eine Tomate, eine Zwiebel"));
            writer.AddDocument(Doc("3", "Kürbis"));
            writer.AddDocument(Doc("4", "Kürbisse"));
            writer.Commit();
        }

        using var searcher = new IndexSearcher(directory);
        Assert.Equal(2, searcher.Search(new TermQuery("body", "tomat"), 10, TestContext.Current.CancellationToken).TotalHits);
        Assert.Equal(2, searcher.Search(new TermQuery("body", "zwiebel"), 10, TestContext.Current.CancellationToken).TotalHits);

        // The official Snowball output keeps these distinct; conformance does not mean every plural conflates.
        Assert.Equal(1, searcher.Search(new TermQuery("body", "kurbis"), 10, TestContext.Current.CancellationToken).TotalHits);
        Assert.Equal(1, searcher.Search(new TermQuery("body", "kurbiss"), 10, TestContext.Current.CancellationToken).TotalHits);
    }

    [Theory(DisplayName = "Snowball: Core's source-included stemmers match the pinned word lists")]
    [MemberData(nameof(PinnedAlgorithms))]
    public void CoreBuild_MatchesThePinnedWordList(string algorithm)
    {
        ISpanStemmer stemmer = algorithm switch
        {
            "english" => new EnglishStemmer(),
            "french" => new FrenchStemmer(),
            "german" => new GermanStemmer(),
            "spanish" => new SpanishStemmer(),
            "italian" => new ItalianStemmer(),
            "portuguese" => new PortugueseStemmer(),
            "dutch" => new DutchStemmer(),
            "russian" => new RussianStemmer(),
            "arabic" => new ArabicStemmer(),
            _ => throw new ArgumentException(algorithm, nameof(algorithm)),
        };
        Assert.Equal(typeof(IndexWriter).Assembly, stemmer.GetType().Assembly);

        var buffer = new char[256];
        var mismatches = SnowballFixtures.Vectors(algorithm)
            .Where(pair =>
            {
                int length = stemmer.Stem(pair.Input, buffer);
                return length < 0 || !pair.Expected.AsSpan().SequenceEqual(buffer.AsSpan(0, length));
            })
            .Take(10)
            .Select(static pair => pair.Input)
            .ToArray();
        Assert.Empty(mismatches);
    }

    [Fact(DisplayName = "Snowball: concurrent analysers produce the serial output")]
    public void ConcurrentAnalysers_ProduceTheSerialOutput()
    {
        var words = SnowballFixtures.Vectors("german").Take(3_000).Select(static pair => pair.Input).ToArray();
        string text = string.Join(' ', words);
        var shared = AnalyserFactory.Create("de");
        var serial = Analyse(shared, text);

        var results = new string[16][];
        Parallel.For(0, results.Length, new ParallelOptions { MaxDegreeOfParallelism = 16 }, i =>
        {
            var owned = shared is IThreadLocalAnalyser threadLocal ? threadLocal.CreateThreadLocalAnalyser() : shared;
            results[i] = Analyse(owned, text);
        });

        Assert.All(results, result => Assert.Equal(serial, result));
    }

    private static string[] Analyse(IAnalyser analyser, string text)
    {
        var sink = new MaterialisingTokenSink();
        analyser.Analyse(text, sink);
        return sink.Tokens.Select(static token => token.Text).ToArray();
    }

    private static LeanDocument Doc(string id, string body)
    {
        var document = new LeanDocument();
        document.Add(new StringField("id", id, stored: true));
        document.Add(new TextField("body", body));
        return document;
    }

    private string SubDir(string name)
    {
        var path = Path.Combine(_fixture.Path, name);
        Directory.CreateDirectory(path);
        return path;
    }
}
