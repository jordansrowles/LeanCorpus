using Rowles.LeanCorpus.Codecs.Fst;
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Tests.Integration.Index;

/// <summary>
/// Coverage tests for pattern-matching methods on <see cref="SegmentReader"/>:
/// GetTermsMatching and IntersectAutomaton.
/// </summary>
[Trait("Category", "Index")]
[Trait("Category", "UnitTest")]
public sealed class SegmentReaderPatternTests : IDisposable
{
    private readonly string _dir;

    public SegmentReaderPatternTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "ll_sr_pat_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private (MMapDirectory Dir, IndexSearcher Searcher) BuildAndOpen(Action<IndexWriter> populate)
    {
        var mmap = new MMapDirectory(_dir);
        using (var writer = new IndexWriter(mmap, new IndexWriterConfig()))
        {
            populate(writer);
            writer.Commit();
        }
        return (mmap, new IndexSearcher(mmap));
    }

    [Fact(DisplayName = "SegmentReader: GetTermsMatching Wildcard Pattern Returns Matching Terms")]
    public void GetTermsMatching_WildcardPattern_ReturnsMatchingTerms()
    {
        var (dir, searcher) = BuildAndOpen(w =>
        {
            var doc = new LeanDocument();
            doc.Add(new TextField("body", "apple apricot banana"));
            w.AddDocument(doc);
        });
        using (dir) using (searcher)
        {
            var reader = searcher.GetSegmentReaders()[0];
            var matches = reader.GetTermsMatching("body\0", "ap*".AsSpan());
            Assert.Contains(matches, t => t.Term.EndsWith("apple", StringComparison.Ordinal));
            Assert.Contains(matches, t => t.Term.EndsWith("apricot", StringComparison.Ordinal));
            Assert.DoesNotContain(matches, t => t.Term.EndsWith("banana", StringComparison.Ordinal));
        }
    }

    [Fact(DisplayName = "SegmentReader: IntersectAutomaton Prefix Automaton Returns Terms With Prefix")]
    public void IntersectAutomaton_PrefixAutomaton_ReturnsTermsWithPrefix()
    {
        var (dir, searcher) = BuildAndOpen(w =>
        {
            var doc = new LeanDocument();
            doc.Add(new TextField("body", "apple apricot banana"));
            w.AddDocument(doc);
        });
        using (dir) using (searcher)
        {
            var reader = searcher.GetSegmentReaders()[0];
            var automaton = new PrefixAutomaton("ap");
            var matches = reader.IntersectAutomaton("body\0", automaton);
            Assert.NotEmpty(matches);
            Assert.All(matches, t => Assert.Contains("ap", t.Term, StringComparison.Ordinal));
            Assert.DoesNotContain(matches, t => t.Term.EndsWith("banana", StringComparison.Ordinal));
        }
    }
}
