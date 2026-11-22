using System.Text;
using Rowles.LeanCorpus.Analysis;
using Rowles.LeanCorpus.Analysis.Analysers;
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index;
using Rowles.LeanCorpus.Search;
using Rowles.LeanCorpus.Search.Parsing;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Tests.Shared.Fixtures;
using Rowles.LeanCorpus.Tests.Shared.Infrastructure;

namespace Rowles.LeanCorpus.Tests.Core;

/// <summary>
/// Slovak retrieval through Core's source-included analysis (ADR021): each form of a judged family
/// finds exactly its own family's documents on a reopened index, through the public analysed-query
/// path. The families come from <c>fixtures/slovak/judgements.tsv</c>.
/// </summary>
[Category(TestCategory.Integration)]
[Area(TestArea.TextIntegration)]
public sealed class SlovakRetrievalTests : IClassFixture<TestDirectoryFixture>
{
    private readonly TestDirectoryFixture _fixture;

    public SlovakRetrievalTests(TestDirectoryFixture fixture) => _fixture = fixture;

    [Fact(DisplayName = "Slovak: every form of a judged family finds exactly its family")]
    public void JudgedFamilies_FindExactlyThemselves()
    {
        var families = LoadJoinFamilies();
        string path = SubDir("slovak-families");
        using (var directory = new MMapDirectory(path))
        using (var writer = new IndexWriter(directory, new IndexWriterConfig { DefaultAnalyser = AnalyserFactory.Create("sk") }))
        {
            foreach (var (family, forms) in families)
            {
                foreach (var form in forms)
                    writer.AddDocument(Doc($"{family}/{form}", form));
            }
            writer.Commit();
        }

        using var reopened = new MMapDirectory(path);
        using var searcher = new IndexSearcher(reopened);
        var parser = new QueryParser("body", AnalyserFactory.Create("sk"));
        var failures = new List<string>();
        foreach (var (family, forms) in families)
        {
            var expected = forms.Select(form => $"{family}/{form}").Order(StringComparer.Ordinal).ToArray();
            foreach (var form in forms)
            {
                var hits = searcher.Search(parser.Parse(form), 1_000, TestContext.Current.CancellationToken);
                var actual = hits.ScoreDocs.Select(hit => searcher.GetStoredFields(hit.DocId)["id"][0]).Order(StringComparer.Ordinal).ToArray();
                if (!expected.SequenceEqual(actual, StringComparer.Ordinal))
                    failures.Add($"{form}: expected {expected.Length} {family} hits, got [{string.Join(", ", actual)}]");
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact(DisplayName = "Slovak: sud and súd, and automat and automatický, stay apart in an index")]
    public void NegativePairs_StayApart()
    {
        using var directory = new MMapDirectory(SubDir("slovak-negatives"));
        using (var writer = new IndexWriter(directory, new IndexWriterConfig { DefaultAnalyser = AnalyserFactory.Create("sk") }))
        {
            writer.AddDocument(Doc("barrel", "Na dvore stojí sud a v sude je víno."));
            writer.AddDocument(Doc("court", "Súd rozhodol a v súde bolo ticho."));
            writer.AddDocument(Doc("machine", "Automat na kávu a bankomat pri automate."));
            writer.AddDocument(Doc("automatic", "Automatické dvere a automatická prevodovka."));
            writer.Commit();
        }

        using var searcher = new IndexSearcher(directory);
        var parser = new QueryParser("body", AnalyserFactory.Create("sk"));
        Assert.Equal(["barrel"], Ids(searcher, parser.Parse("sudu")));
        Assert.Equal(["court"], Ids(searcher, parser.Parse("súdu")));
        Assert.Equal(["machine"], Ids(searcher, parser.Parse("automatoch")));
        Assert.Equal(["automatic"], Ids(searcher, parser.Parse("automatický")));
    }

    [Fact(DisplayName = "Slovak: replacing or removing the stemmer keeps tokenisation and stop words")]
    public void StemmerOverride_KeepsTokenisationAndStopWords()
    {
        const string text = "Veľké domy sú v meste a na školách.";
        var stemmed = Analyse(AnalyserFactory.Create("sk"), text);
        var unstemmed = Analyse(AnalyserFactory.Create("sk", stemmer: null), text);

        Assert.Equal(stemmed.Select(static t => (t.StartOffset, t.EndOffset, t.PositionIncrement)), unstemmed.Select(static t => (t.StartOffset, t.EndOffset, t.PositionIncrement)));
        Assert.Equal(["veľké", "domy", "meste", "školách"], unstemmed.Select(static t => t.Text));
        Assert.Equal(["veľk", "dom", "mest", "škol"], stemmed.Select(static t => t.Text));
    }

    private static List<(string Family, string[] Forms)> LoadJoinFamilies()
    {
        string path = Path.Combine(RepositoryTemporaryStorage.RepositoryRoot, "src", "devops", "tests", "fixtures", "slovak", "judgements.tsv");
        return File.ReadAllLines(path, new UTF8Encoding(false, true))
            .Where(static line => line.Length > 0 && line[0] != '#' && !line.StartsWith("!apart", StringComparison.Ordinal))
            .Select(static line => line.Split('\t'))
            .Where(static cells => cells[4] == "join")
            .GroupBy(static cells => cells[0], StringComparer.Ordinal)
            .Select(static group => (group.Key, group.Select(static cells => cells[5]).ToArray()))
            .ToList();
    }

    private static string[] Ids(IndexSearcher searcher, Query query)
        => searcher.Search(query, 100, TestContext.Current.CancellationToken).ScoreDocs
            .Select(hit => searcher.GetStoredFields(hit.DocId)["id"][0]).Order(StringComparer.Ordinal).ToArray();

    private static List<Token> Analyse(IAnalyser analyser, string text)
    {
        var sink = new MaterialisingTokenSink();
        analyser.Analyse(text, sink);
        return sink.Tokens;
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
