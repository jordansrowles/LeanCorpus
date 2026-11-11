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
/// Reduced reproduction of the benchmark's <c>SpanNot(near(government, people, ~10), market)</c>
/// difference, checked against a hand-derived result from the <see cref="SpanNotQuery"/> contract:
/// an include span survives unless it overlaps an exclude span.
/// </summary>
[Category(TestCategory.Integration)]
[Area(TestArea.Search)]
public sealed class SpanNotExclusionTests : IClassFixture<TestDirectoryFixture>
{
    private readonly TestDirectoryFixture _fixture;

    public SpanNotExclusionTests(TestDirectoryFixture fixture) => _fixture = fixture;

    [Fact(DisplayName = "SpanNot: Excludes Only Overlapping Spans, Matching A Derived Result")]
    public void SpanNot_ExcludesOnlyOverlappingSpans()
    {
        // Positions: the ordered near span runs from "government" to "people" inclusive.
        (string Id, string Body)[] documents =
        [
            ("d0", "government people market"),   // span [0,2), market at 2: no overlap, matches
            ("d1", "government market people"),   // span [0,3) contains market at 1: excluded
            ("d2", "market government people"),   // span [1,3), market at 0: no overlap, matches
            ("d3", "government people"),          // no exclude term: matches
            ("d4", "people government market"),   // no ordered span: no match
            ("d5", "market only")                 // no include span: no match
        ];
        string[] expected = ["d0", "d2", "d3"];

        string path = Path.Combine(_fixture.Path, "span_not_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        using var directory = new MMapDirectory(path);
        using (var writer = new IndexWriter(directory, new IndexWriterConfig()))
        {
            foreach (var (id, body) in documents)
            {
                var document = new LeanDocument();
                document.Add(new StringField("id", id, stored: true));
                document.Add(new TextField("body", body));
                writer.AddDocument(document);
            }
            writer.Commit();
        }

        using var searcher = new IndexSearcher(directory);
        var query = new SpanNotQuery(
            new SpanNearQuery([new SpanTermQuery("body", "government"), new SpanTermQuery("body", "people")], slop: 10),
            new SpanTermQuery("body", "market"));
        var results = searcher.Search(query, 10, TestContext.Current.CancellationToken);

        string[] actual = results.ScoreDocs.Select(hit => searcher.GetStoredFields(hit.DocId)["id"][0]).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(expected, actual);
        Assert.Equal(expected.Length, results.TotalHits);
    }
}
