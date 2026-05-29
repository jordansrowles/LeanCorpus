using Rowles.LeanCorpus.Codecs.Fst;
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Tests.Integration.Index;

/// <summary>
/// Coverage tests for pattern-matching methods on <see cref="SegmentReader"/>:
/// GetTermsMatching and IntersectAutomaton.
/// </summary>
[Trait("Category", "Index")]
public sealed class SegmentReaderPatternTests: IDisposable
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

    // ── Phase 5: Integration coverage ─────────────────────────────────────

    [Fact(DisplayName = "SegmentReader: GetFuzzyMatches Returns Terms Within Edit Distance")]
    public void GetFuzzyMatches_ReturnsTermsWithinEditDistance()
    {
        var (dir, searcher) = BuildAndOpen(w =>
        {
            var doc = new LeanDocument();
            doc.Add(new TextField("body", "hello help hell heap helper"));
            w.AddDocument(doc);
        });
        using (dir) using (searcher)
        {
            var reader = searcher.GetSegmentReaders()[0];
            var matches = reader.GetFuzzyMatches("body\0", "hell".AsSpan(), 1);

            // "hell" with edit distance 1 should match "hello" (insertion), "hell" (exact), "help" (substitution)
            // "heap" is distance 2, "helper" is distance 2+ (insert 'p', insert 'e', insert 'r')
            Assert.Contains(matches, m => m.Term.EndsWith("hello", StringComparison.Ordinal));
            Assert.Contains(matches, m => m.Term.EndsWith("hell", StringComparison.Ordinal));
            Assert.Contains(matches, m => m.Term.EndsWith("help", StringComparison.Ordinal));
            Assert.DoesNotContain(matches, m => m.Term.EndsWith("heap", StringComparison.Ordinal));
            Assert.DoesNotContain(matches, m => m.Term.EndsWith("helper", StringComparison.Ordinal));
        }
    }

    [Fact(DisplayName = "SegmentReader: GetTermsWithRegex Matches Terms By Pattern")]
    public void GetTermsMatchingRegex_MatchesTermsByPattern()
    {
        var (dir, searcher) = BuildAndOpen(w =>
        {
            var doc = new LeanDocument();
            doc.Add(new TextField("body", "cat cart cast cost coffee"));
            w.AddDocument(doc);
        });
        using (dir) using (searcher)
        {
            var reader = searcher.GetSegmentReaders()[0];
            var regex = new System.Text.RegularExpressions.Regex("^ca");
            var matches = reader.GetTermsMatchingRegex("body\0", regex);

            Assert.Contains(matches, m => m.Term.EndsWith("cat", StringComparison.Ordinal));
            Assert.Contains(matches, m => m.Term.EndsWith("cart", StringComparison.Ordinal));
            Assert.DoesNotContain(matches, m => m.Term.EndsWith("cost", StringComparison.Ordinal));
            Assert.DoesNotContain(matches, m => m.Term.EndsWith("coffee", StringComparison.Ordinal));
        }
    }

    [Fact(DisplayName = "SegmentReader: GetTermsInRange Returns Terms Within Bounds")]
    public void GetTermsInRange_ReturnsTermsWithinBounds()
    {
        var (dir, searcher) = BuildAndOpen(w =>
        {
            var doc = new LeanDocument();
            doc.Add(new TextField("body", "apple banana cherry date elderberry"));
            w.AddDocument(doc);
        });
        using (dir) using (searcher)
        {
            var reader = searcher.GetSegmentReaders()[0];

            // Range ["banana", "date"] inclusive
            var matches = reader.GetTermsInRange("body\0", "banana", "date", true, true);
            Assert.Contains(matches, m => m.Term.EndsWith("banana", StringComparison.Ordinal));
            Assert.Contains(matches, m => m.Term.EndsWith("cherry", StringComparison.Ordinal));
            Assert.Contains(matches, m => m.Term.EndsWith("date", StringComparison.Ordinal));
            Assert.DoesNotContain(matches, m => m.Term.EndsWith("apple", StringComparison.Ordinal));
            Assert.DoesNotContain(matches, m => m.Term.EndsWith("elderberry", StringComparison.Ordinal));
        }
    }
}
