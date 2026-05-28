using Rowles.LeanCorpus.Codecs;
using Rowles.LeanCorpus.Codecs.Hnsw;
using Rowles.LeanCorpus.Codecs.Fst;
using Rowles.LeanCorpus.Codecs.Bkd;
using Rowles.LeanCorpus.Codecs.Vectors;
using Rowles.LeanCorpus.Codecs.TermVectors;
using Rowles.LeanCorpus.Codecs.TermDictionary;
using Rowles.LeanCorpus.Tests.Shared.Fixtures;
using Xunit.Abstractions;

namespace Rowles.LeanCorpus.Tests.Unit.Codecs;

/// <summary>
/// Tests IAutomaton implementations (Prefix, Wildcard, Levenshtein) and their intersection
/// with the FSTReader term dictionary.
/// </summary>
[Trait("Category", "Codecs")]
public sealed class FSTAutomatonTests : IClassFixture<TestDirectoryFixture>
{
    private readonly TestDirectoryFixture _fixture;
    private readonly ITestOutputHelper _output;

    public FSTAutomatonTests(TestDirectoryFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    private string DicPath(string name) => Path.Combine(_fixture.Path, name + ".dic");

    private TermDictionaryReader BuildDictionary(string name, params string[] bareTerms)
    {
        var path = DicPath(name);
        var fieldPrefix = "body\0";
        var terms = bareTerms.Select(t => fieldPrefix + t).Order().ToList();
        var offsets = new Dictionary<string, long>();
        long offset = 100;
        foreach (var t in terms)
            offsets[t] = offset++;

        TermDictionaryWriter.Write(path, terms, offsets);
        return TermDictionaryReader.Open(path);
    }

    // ── Prefix Automaton ─────────────────────────────────────────────────

    /// <summary>
    /// Verifies the Prefix Automaton: Matches Correct Terms scenario.
    /// </summary>
    [Fact(DisplayName = "Prefix Automaton: Matches Correct Terms")]
    public void PrefixAutomaton_MatchesCorrectTerms()
    {
        using var reader = BuildDictionary("prefix_test",
            "sea", "search", "searching", "set", "seal", "zebra");

        var automaton = new PrefixAutomaton("sea");
        var results = FSTAutomaton.Intersect(reader, "body\0", automaton);
        var terms = results.Select(r => r.Term.Replace("body\0", "")).ToList();

        Assert.Contains("sea", terms);
        Assert.Contains("search", terms);
        Assert.Contains("searching", terms);
        Assert.Contains("seal", terms);
        Assert.DoesNotContain("set", terms);
        Assert.DoesNotContain("zebra", terms);
    }

    /// <summary>
    /// Verifies the Prefix Automaton: Empty Prefix Matches All scenario.
    /// </summary>
    [Fact(DisplayName = "Prefix Automaton: Empty Prefix Matches All")]
    public void PrefixAutomaton_EmptyPrefix_MatchesAll()
    {
        using var reader = BuildDictionary("prefix_empty",
            "apple", "banana", "cherry");

        var automaton = new PrefixAutomaton("");
        var results = FSTAutomaton.Intersect(reader, "body\0", automaton);

        Assert.Equal(3, results.Count);
    }

    // ── Wildcard Automaton ───────────────────────────────────────────────

    /// <summary>
    /// Verifies the Wildcard Automaton: Star Pattern Matches Correctly scenario.
    /// </summary>
    [Fact(DisplayName = "Wildcard Automaton: Star Pattern Matches Correctly")]
    public void WildcardAutomaton_StarPattern_MatchesCorrectly()
    {
        using var reader = BuildDictionary("wildcard_star",
            "search", "stitch", "seat", "scratch", "such");

        var automaton = new WildcardAutomaton("s*ch");
        var results = FSTAutomaton.Intersect(reader, "body\0", automaton);
        var terms = results.Select(r => r.Term.Replace("body\0", "")).ToList();

        Assert.Contains("search", terms);
        Assert.Contains("stitch", terms);
        Assert.Contains("such", terms);
        Assert.DoesNotContain("seat", terms);
    }

    /// <summary>
    /// Verifies the Wildcard Automaton: Question Mark Matches Single Char scenario.
    /// </summary>
    [Fact(DisplayName = "Wildcard Automaton: Question Mark Matches Single Char")]
    public void WildcardAutomaton_QuestionMark_MatchesSingleChar()
    {
        using var reader = BuildDictionary("wildcard_question",
            "cat", "cot", "cut", "cart", "coat");

        var automaton = new WildcardAutomaton("c?t");
        var results = FSTAutomaton.Intersect(reader, "body\0", automaton);
        var terms = results.Select(r => r.Term.Replace("body\0", "")).ToList();

        Assert.Contains("cat", terms);
        Assert.Contains("cot", terms);
        Assert.Contains("cut", terms);
        Assert.DoesNotContain("cart", terms);
        Assert.DoesNotContain("coat", terms);
    }

    /// <summary>
    /// Verifies the Wildcard Automaton: Star Only Matches All scenario.
    /// </summary>
    [Fact(DisplayName = "Wildcard Automaton: Star Only Matches All")]
    public void WildcardAutomaton_StarOnly_MatchesAll()
    {
        using var reader = BuildDictionary("wildcard_all",
            "alpha", "beta", "gamma");

        var automaton = new WildcardAutomaton("*");
        var results = FSTAutomaton.Intersect(reader, "body\0", automaton);

        Assert.Equal(3, results.Count);
    }

    // ── Levenshtein Automaton ────────────────────────────────────────────

    /// <summary>
    /// Verifies the Levenshtein Automaton: Edit 1 Matches Insertion Only scenario.
    /// </summary>
    [Fact(DisplayName = "Levenshtein Automaton: Edit 1 Matches Insertion Only")]
    public void LevenshteinAutomaton_Edit1_MatchesInsertionOnly()
    {
        using var reader = BuildDictionary("lev1",
            "search", "scratch", "seat", "serch");

        var automaton = new LevenshteinAutomaton("serch", 1);
        var results = FSTAutomaton.Intersect(reader, "body\0", automaton);
        var terms = results.Select(r => r.Term.Replace("body\0", "")).ToList();

        // "search" is edit distance 1 from "serch" (insert 'a')
        Assert.Contains("search", terms);
        // "serch" is edit distance 0 (exact)
        Assert.Contains("serch", terms);
        // "scratch" is too far
        Assert.DoesNotContain("scratch", terms);
    }

    /// <summary>
    /// Verifies the Levenshtein Automaton: Edit 2 Matches Substitution And Insertion scenario.
    /// </summary>
    [Fact(DisplayName = "Levenshtein Automaton: Edit 2 Matches Substitution And Insertion")]
    public void LevenshteinAutomaton_Edit2_MatchesSubstitutionAndInsertion()
    {
        using var reader = BuildDictionary("lev2",
            "vector", "vectir", "victor", "venture", "very");

        var automaton = new LevenshteinAutomaton("vectr", 2);
        var results = FSTAutomaton.Intersect(reader, "body\0", automaton);
        var terms = results.Select(r => r.Term.Replace("body\0", "")).ToList();

        // "vector" = insert 'o' (edit 1)
        Assert.Contains("vector", terms);
        // "vectir" = substitute 'r'→'i' + insert 'r' (edit 2)
        Assert.Contains("vectir", terms);
    }

    /// <summary>
    /// Verifies the Levenshtein Automaton: Exact Match Edit 0 scenario.
    /// </summary>
    [Fact(DisplayName = "Levenshtein Automaton: Exact Match Edit 0")]
    public void LevenshteinAutomaton_ExactMatch_Edit0()
    {
        using var reader = BuildDictionary("lev_exact",
            "hello", "world");

        var automaton = new LevenshteinAutomaton("hello", 0);
        var results = FSTAutomaton.Intersect(reader, "body\0", automaton);
        var terms = results.Select(r => r.Term.Replace("body\0", "")).ToList();

        Assert.Single(terms);
        Assert.Equal("hello", terms[0]);
    }

    // ── Integration: Large Dictionary ───────────────────────────────────

    /// <summary>
    /// Verifies the Large Dictionary: Prefix Intersect Matches Brute Force scenario.
    /// </summary>
    [Fact(DisplayName = "Large Dictionary: Prefix Intersect Matches Brute Force")]
    public void LargeDictionary_PrefixIntersect_MatchesBruteForce()
    {
        // Generate 1000 terms
        var bareTerms = Enumerable.Range(0, 1000)
            .Select(i => $"term{i:D4}")
            .ToArray();

        using var reader = BuildDictionary("large_prefix", bareTerms);

        // Prefix "term00" should match term0000..term0099
        var automaton = new PrefixAutomaton("term00");
        var results = FSTAutomaton.Intersect(reader, "body\0", automaton);

        // Brute force
        var expected = bareTerms.Where(t => t.StartsWith("term00")).ToList();
        Assert.Equal(expected.Count, results.Count);
    }

    /// <summary>
    /// Verifies the Large Dictionary: Wildcard Intersect Matches Brute Force scenario.
    /// </summary>
    [Fact(DisplayName = "Large Dictionary: Wildcard Intersect Matches Brute Force")]
    public void LargeDictionary_WildcardIntersect_MatchesBruteForce()
    {
        var bareTerms = Enumerable.Range(0, 500)
            .Select(i => $"word{i:D3}")
            .ToArray();

        using var reader = BuildDictionary("large_wildcard", bareTerms);

        // "word?5?" should match word050, word051, ..., word059, word150, ..., word459
        var automaton = new WildcardAutomaton("word?5?");
        var results = FSTAutomaton.Intersect(reader, "body\0", automaton);
        var terms = results.Select(r => r.Term.Replace("body\0", "")).ToHashSet();

        var expected = bareTerms.Where(t =>
            t.Length == 7 && t[4] != '\0' && t[5] == '5' && t[6] != '\0').ToHashSet();

        Assert.Equal(expected, terms);
    }

    // ── Edge Cases ──────────────────────────────────────────────────────

    /// <summary>
    /// Verifies the Empty Dictionary: Intersect Returns Empty scenario.
    /// </summary>
    [Fact(DisplayName = "Empty Dictionary: Intersect Returns Empty")]
    public void EmptyDictionary_IntersectReturnsEmpty()
    {
        var path = DicPath("automaton_empty");
        TermDictionaryWriter.Write(path, [], []);
        using var reader = TermDictionaryReader.Open(path);

        var automaton = new PrefixAutomaton("any");
        var results = FSTAutomaton.Intersect(reader, "body\0", automaton);
        Assert.Empty(results);
    }

    /// <summary>
    /// Verifies the Prefix Automaton: No Matches Returns Empty scenario.
    /// </summary>
    [Fact(DisplayName = "Prefix Automaton: No Matches Returns Empty")]
    public void PrefixAutomaton_NoMatches_ReturnsEmpty()
    {
        using var reader = BuildDictionary("no_match", "apple", "banana");

        var automaton = new PrefixAutomaton("xyz");
        var results = FSTAutomaton.Intersect(reader, "body\0", automaton);
        Assert.Empty(results);
    }

    // ── Phase 4: FstAutomaton edge cases ──────────────────────────────────

    [Fact(DisplayName = "Levenshtein Automaton: Large Edit Distance Matches Brute Force")]
    public void LevenshteinAutomaton_LargeEditDistance_MatchesBruteForce()
    {
        // Generate 200+ terms, test with maxEdits=5 against brute-force Levenshtein.
        var bareTerms = Enumerable.Range(0, 220)
            .Select(i =>
            {
                // Generate varied-length terms
                int len = 4 + (i % 8);
                var sb = new System.Text.StringBuilder();
                for (int j = 0; j < len; j++)
                    sb.Append((char)('a' + ((i * 7 + j * 13) % 26)));
                return sb.ToString();
            })
            .Distinct()
            .Order()
            .ToArray();

        using var reader = BuildDictionary("lev_large_edits", bareTerms);

        const string query = "hello";
        const int maxEdits = 5;
        var automaton = new LevenshteinAutomaton(query, maxEdits);
        var results = FSTAutomaton.Intersect(reader, "body\0", automaton);
        var matchedTerms = results.Select(r => r.Term.Replace("body\0", "")).ToHashSet();

        // Brute-force Levenshtein
        int LevDistance(string a, string b)
        {
            if (a.Length == 0) return b.Length;
            if (b.Length == 0) return a.Length;
            var d = new int[a.Length + 1, b.Length + 1];
            for (int i = 0; i <= a.Length; i++) d[i, 0] = i;
            for (int j = 0; j <= b.Length; j++) d[0, j] = j;
            for (int i = 1; i <= a.Length; i++)
                for (int j = 1; j <= b.Length; j++)
                    d[i, j] = Math.Min(
                        Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                        d[i - 1, j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            return d[a.Length, b.Length];
        }

        var expected = bareTerms.Where(t => LevDistance(query, t) <= maxEdits).ToHashSet();

        Assert.Equal(expected, matchedTerms);
    }

    [Fact(DisplayName = "Wildcard Automaton: Complex Patterns Match Correctly")]
    public void WildcardAutomaton_ComplexPatterns_MatchCorrectly()
    {
        var bareTerms = new[]
        {
            "ab", "abc", "abbc", "abbbc", "axbyc", "ac",           // a*b*c patterns
            "xaybz", "aabb", "cad", "bad", "apple", "banana",      // ?a?b? patterns
            "alphabetical", "cat", "cart", "cast", "cost",          // a??c*d patterns
            "café", "cafeteria", "cafe", "coffee",                 // unicode prefix
        };

        using var reader = BuildDictionary("wc_complex", bareTerms);

        // "a*b*c" — any sequence with a-b-c in order
        var w1 = new WildcardAutomaton("a*b*c");
        var r1 = FSTAutomaton.Intersect(reader, "body\0", w1)
            .Select(r => r.Term.Replace("body\0", "")).ToHashSet();
        Assert.Contains("abc", r1);
        Assert.Contains("abbc", r1);
        Assert.Contains("abbbc", r1);
        Assert.Contains("axbyc", r1);
        Assert.DoesNotContain("ac", r1); // no 'b' between a and c

        // "?a?b?" — any 5-char string with a at pos 1 and b at pos 3
        var w2 = new WildcardAutomaton("?a?b?");
        var r2 = FSTAutomaton.Intersect(reader, "body\0", w2)
            .Select(r => r.Term.Replace("body\0", "")).ToHashSet();
        Assert.Contains("xaybz", r2);

        // "a??c*d" — a, then 2 chars, then c, then any, then d
        var w3 = new WildcardAutomaton("a??c*d");
        var r3 = FSTAutomaton.Intersect(reader, "body\0", w3)
            .Select(r => r.Term.Replace("body\0", "")).ToHashSet();
        // "alphabetical" — 'a', 'l', 'p', 'h', 'a', 'b', 'e', 't', 'i', 'c', 'a', 'l'
        // After a??c — "alphabetic..." has a-l-p-h-a-b-e-t-i-c... matches "a??c" (a-l-p... but c is later)
        // Actually "alphabetical": "alphabetical" - a(lp)habetical - wait, a??c needs pos after a, then 2 chars, then c.
        // "alphabetical": a-l-p-h-a-b-e-t-i-c-a-l
        // a = matches, l = char 1, p = char 2, h ≠ c. So no match.
        // Let's verify with simpler test data:
        Assert.DoesNotContain("cat", r3); // no d after c
        Assert.DoesNotContain("cart", r3); // "cart": a-r-t, c not at position for a??c
        Assert.DoesNotContain("cast", r3); // similarly
        Assert.DoesNotContain("cost", r3); // doesn't start with 'a'

        // Unicode pattern: "caf*" should match "café", "cafeteria", "cafe"
        var w4 = new WildcardAutomaton("caf*");
        var r4 = FSTAutomaton.Intersect(reader, "body\0", w4)
            .Select(r => r.Term.Replace("body\0", "")).ToHashSet();
        Assert.Contains("cafe", r4);

        // Note: "caf\u00e9" contains a multi-byte UTF-8 char (é = 0xC3 0xA9).
        // The WildcardAutomaton operates on bytes, so "caf*" matches "caf" + any bytes.
        Assert.Contains("caf\u00e9", r4);
        Assert.Contains("cafeteria", r4);
        Assert.DoesNotContain("coffee", r4);
    }

    [Fact(DisplayName = "Prefix Automaton: Multi-byte UTF-8 Boundary")]
    public void PrefixAutomaton_MultiByteUtf8Boundary()
    {
        // "café" in UTF-8: c(0x63) a(0x61) f(0x66) é(0xC3 0xA9)
        // "cafe" in UTF-8: c(0x63) a(0x61) f(0x66) e(0x65)
        var bareTerms = new[] { "café", "cafeteria", "cafe", "coffee" };

        using var reader = BuildDictionary("prefix_mb", bareTerms);

        // Prefix "caf" — operates on bytes c-a-f, so it matches all of "café", "cafeteria", "cafe"
        var automaton = new PrefixAutomaton("caf");
        var results = FSTAutomaton.Intersect(reader, "body\0", automaton)
            .Select(r => r.Term.Replace("body\0", "")).ToHashSet();

        Assert.Contains("cafe", results);
        Assert.Contains("caf\u00e9", results);
        Assert.Contains("cafeteria", results);
        Assert.DoesNotContain("coffee", results);
    }

    [Fact(DisplayName = "Levenshtein Automaton: MinDistance On Dead State Returns IntMax")]
    public void LevenshteinAutomaton_MinDistance_OnDeadState_ReturnsIntMax()
    {
        var automaton = new LevenshteinAutomaton("hello", 1);

        // Step through for "xyzzy" — should end in dead state (-1)
        int state = automaton.Start;
        foreach (byte b in System.Text.Encoding.UTF8.GetBytes("xyzzy"))
        {
            if (state < 0) break;
            state = automaton.Step(state, b);
        }

        // Should be dead
        Assert.True(state < 0 || !automaton.CanMatch(state));

        // MinDistance on -1 returns int.MaxValue
        int dist = automaton.MinDistance(-1);
        Assert.Equal(int.MaxValue, dist);

        // MinDistance on a dead (non-negative but non-matching) state also returns int.MaxValue
        if (state >= 0)
        {
            Assert.False(automaton.CanMatch(state));
            Assert.Equal(int.MaxValue, automaton.MinDistance(state));
        }
    }
}
