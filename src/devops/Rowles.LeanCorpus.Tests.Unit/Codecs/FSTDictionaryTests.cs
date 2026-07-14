using Rowles.LeanCorpus.Codecs;
using Rowles.LeanCorpus.Codecs.Hnsw;
using Rowles.LeanCorpus.Codecs.Fst;
using Rowles.LeanCorpus.Codecs.Bkd;
using Rowles.LeanCorpus.Codecs.Vectors;
using Rowles.LeanCorpus.Codecs.TermVectors;
using Rowles.LeanCorpus.Codecs.TermDictionary;
using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Codecs.CodecKit.Formats;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Tests.Shared.Fixtures;
using Xunit.Abstractions;

namespace Rowles.LeanCorpus.Tests.Unit.Codecs;

/// <summary>
/// Tests the v2 byte-keyed term dictionary (FSTBuilder → TermDictionaryReader) round-trip
/// and all consumer methods: exact lookup, prefix, wildcard, fuzzy, range, regex, field enum.
/// </summary>
[Trait("Category", "Codecs")]
public sealed class FSTDictionaryTests : IClassFixture<TestDirectoryFixture>
{
    private readonly TestDirectoryFixture _fixture;
    private readonly ITestOutputHelper _output;

    public FSTDictionaryTests(TestDirectoryFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    private string DicPath(string name) => Path.Combine(_fixture.Path, name + ".dic");

    private static void WriteDictionary(string path, List<string> terms, Dictionary<string, long> offsets)
    {
        TermDictionaryWriter.Write(path, terms, offsets);
    }

    // ── Empty dictionary ────────────────────────────────────────────────────

    /// <summary>
    /// Verifies the Empty Dictionary: Exact Lookup Returns False scenario.
    /// </summary>
    [Fact(DisplayName = "Empty Dictionary: Exact Lookup Returns False")]
    public void EmptyDictionary_ExactLookupReturnsFalse()
    {
        var path = DicPath("empty");
        WriteDictionary(path, [], []);
        using var reader = TermDictionaryReader.Open(path);
        Assert.False(reader.TryGetPostingsOffset("anything", out _));
    }

    /// <summary>
    /// Verifies the Empty Dictionary: Prefix Returns Empty scenario.
    /// </summary>
    [Fact(DisplayName = "Empty Dictionary: Prefix Returns Empty")]
    public void EmptyDictionary_PrefixReturnsEmpty()
    {
        var path = DicPath("empty_prefix");
        WriteDictionary(path, [], []);
        using var reader = TermDictionaryReader.Open(path);
        Assert.Empty(reader.GetTermsWithPrefix("body\0".AsSpan()));
    }

    // ── Single term ─────────────────────────────────────────────────────────

    /// <summary>
    /// Verifies the Single Term: Exact Lookup Succeeds scenario.
    /// </summary>
    [Fact(DisplayName = "Single Term: Exact Lookup Succeeds")]
    public void SingleTerm_ExactLookupSucceeds()
    {
        var path = DicPath("single");
        var terms = new List<string> { "body\0hello" };
        var offsets = new Dictionary<string, long> { ["body\0hello"] = 42L };
        WriteDictionary(path, terms, offsets);

        using var reader = TermDictionaryReader.Open(path);
        Assert.True(reader.TryGetPostingsOffset("body\0hello", out long offset));
        Assert.Equal(42L, offset);
        Assert.False(reader.TryGetPostingsOffset("body\0world", out _));
    }

    // ── Multi-term round-trip ───────────────────────────────────────────────

    /// <summary>
    /// Verifies the Multi Term: All Terms Round-trip scenario.
    /// </summary>
    [Fact(DisplayName = "Multi Term: All Terms Round-trip")]
    public void MultiTerm_AllTermsRoundTrip()
    {
        var path = DicPath("multi");
        var terms = new List<string>
        {
            "body\0apple",
            "body\0banana",
            "body\0cherry",
            "title\0doc1",
            "title\0doc2"
        };
        var offsets = new Dictionary<string, long>();
        for (int i = 0; i < terms.Count; i++)
            offsets[terms[i]] = (i + 1) * 100L;

        WriteDictionary(path, terms, offsets);
        using var reader = TermDictionaryReader.Open(path);

        foreach (var (term, expected) in offsets)
        {
            Assert.True(reader.TryGetPostingsOffset(term, out long actual), $"Missing: {term}");
            Assert.Equal(expected, actual);
        }
    }

    // ── Unicode terms (CJK, emoji) ──────────────────────────────────────────

    /// <summary>
    /// Verifies the Unicode: CJK And Emoji Round-trip scenario.
    /// </summary>
    [Fact(DisplayName = "Unicode: CJK And Emoji Round-trip")]
    public void Unicode_CJKAndEmoji_RoundTrip()
    {
        var path = DicPath("unicode");
        var terms = new List<string>
        {
            "body\0café",
            "body\0日本語",
            "body\0🎉"
        };
        terms.Sort(StringComparer.Ordinal);
        var offsets = new Dictionary<string, long>();
        for (int i = 0; i < terms.Count; i++)
            offsets[terms[i]] = i * 10L;

        WriteDictionary(path, terms, offsets);
        using var reader = TermDictionaryReader.Open(path);

        foreach (var (term, expected) in offsets)
        {
            Assert.True(reader.TryGetPostingsOffset(term, out long actual), $"Missing: {term}");
            Assert.Equal(expected, actual);
        }
    }

    // ── Prefix scan ─────────────────────────────────────────────────────────

    /// <summary>
    /// Verifies the Prefix Scan: Returns Matching Terms scenario.
    /// </summary>
    [Fact(DisplayName = "Prefix Scan: Returns Matching Terms")]
    public void PrefixScan_ReturnsMatchingTerms()
    {
        var path = DicPath("prefix");
        var terms = new List<string>
        {
            "body\0apple",
            "body\0application",
            "body\0banana",
            "title\0apple"
        };
        var offsets = new Dictionary<string, long>();
        for (int i = 0; i < terms.Count; i++) offsets[terms[i]] = i;

        WriteDictionary(path, terms, offsets);
        using var reader = TermDictionaryReader.Open(path);

        var bodyApple = reader.GetTermsWithPrefix("body\0app".AsSpan());
        Assert.Equal(2, bodyApple.Count);
        Assert.Equal("body\0apple", bodyApple[0].Term);
        Assert.Equal("body\0application", bodyApple[1].Term);
    }

    // ── Wildcard matching ───────────────────────────────────────────────────

    /// <summary>
    /// Verifies the Wildcard Scan: Matches Pattern scenario.
    /// </summary>
    [Fact(DisplayName = "Wildcard Scan: Matches Pattern")]
    public void WildcardScan_MatchesPattern()
    {
        var path = DicPath("wildcard");
        var terms = new List<string>
        {
            "body\0cat",
            "body\0car",
            "body\0cart",
            "body\0dog"
        };
        var offsets = new Dictionary<string, long>();
        for (int i = 0; i < terms.Count; i++) offsets[terms[i]] = i;

        WriteDictionary(path, terms, offsets);
        using var reader = TermDictionaryReader.Open(path);

        var matches = reader.GetTermsMatching("body\0", "ca*".AsSpan());
        Assert.Equal(3, matches.Count);
    }

    /// <summary>
    /// Verifies the Wildcard Scan: Mid Pattern Short Prefix Returns Expected Matches scenario.
    /// </summary>
    [Fact(DisplayName = "Wildcard Scan: Mid Pattern Short Prefix Returns Expected Matches")]
    public void WildcardScan_MidPatternShortPrefix_ReturnsExpectedMatches()
    {
        var path = DicPath("wildcard_mid_short_prefix");
        var terms = new List<string>
        {
            "body\0market",
            "body\0markets",
            "body\0markup",
            "body\0muppet",
            "body\0preexisting",
            "body\0president"
        };
        terms.Sort(StringComparer.Ordinal);
        var offsets = new Dictionary<string, long>();
        for (int i = 0; i < terms.Count; i++) offsets[terms[i]] = i;

        WriteDictionary(path, terms, offsets);
        using var reader = TermDictionaryReader.Open(path);

        var matches = reader.GetTermsMatching("body\0", "m*rket".AsSpan());
        var offsetsOnly = reader.GetTermOffsetsMatching("body\0", "m*rket".AsSpan());

        var match = Assert.Single(matches);
        Assert.Equal("body\0market", match.Term);
        Assert.Equal([offsets["body\0market"]], offsetsOnly);
    }

    /// <summary>
    /// Verifies the Wildcard Scan: Trailing Wildcard Equals Prefix Scan scenario.
    /// </summary>
    [Fact(DisplayName = "Wildcard Scan: Trailing Wildcard Equals Prefix Scan")]
    public void WildcardScan_TrailingWildcard_EqualsPrefixScan()
    {
        var path = DicPath("wildcard_prefix_equivalent");
        var terms = new List<string>
        {
            "body\0government",
            "body\0governor",
            "body\0govt",
            "body\0growth",
            "title\0government"
        };
        terms.Sort(StringComparer.Ordinal);
        var offsets = new Dictionary<string, long>();
        for (int i = 0; i < terms.Count; i++) offsets[terms[i]] = i;

        WriteDictionary(path, terms, offsets);
        using var reader = TermDictionaryReader.Open(path);

        var wildcardMatches = reader.GetTermsMatching("body\0", "gov*".AsSpan()).Select(x => x.Term).ToArray();
        var prefixMatches = reader.GetTermsWithPrefix("body\0gov".AsSpan()).Select(x => x.Term).ToArray();

        Assert.Equal(prefixMatches, wildcardMatches);
    }

    // ── Fuzzy matching ──────────────────────────────────────────────────────

    /// <summary>
    /// Verifies the Fuzzy Matches: Finds Within Edit Distance scenario.
    /// </summary>
    [Fact(DisplayName = "Fuzzy Matches: Finds Within Edit Distance")]
    public void FuzzyMatches_FindsWithinEditDistance()
    {
        var path = DicPath("fuzzy");
        var terms = new List<string>
        {
            "body\0cat",
            "body\0bat",
            "body\0car",
            "body\0dog"
        };
        var offsets = new Dictionary<string, long>();
        for (int i = 0; i < terms.Count; i++) offsets[terms[i]] = i;

        WriteDictionary(path, terms, offsets);
        using var reader = TermDictionaryReader.Open(path);

        var matches = reader.GetFuzzyMatches("body\0", "cat".AsSpan(), 1);
        Assert.Contains(matches, m => m.Term == "body\0cat");
        Assert.Contains(matches, m => m.Term == "body\0bat");
        Assert.Contains(matches, m => m.Term == "body\0car");
        Assert.DoesNotContain(matches, m => m.Term == "body\0dog");

        // Verify edit distances are returned correctly
        var catMatch = matches.First(m => m.Term == "body\0cat");
        Assert.Equal(0, catMatch.Distance);
        var batMatch = matches.First(m => m.Term == "body\0bat");
        Assert.Equal(1, batMatch.Distance);
    }

    /// <summary>
    /// Verifies the Fuzzy Matches: Large Term Set Prefix Sharing Prunes scenario.
    /// </summary>
    [Fact(DisplayName = "Fuzzy Matches: Large Term Set Prefix Sharing Prunes")]
    public void FuzzyMatches_LargeTermSet_PrefixSharingPrunes()
    {
        var path = DicPath("fuzzy_scale");
        // Generate 10K terms to exercise prefix-sharing + dead-prefix skipping
        var random = new Random(42);
        var terms = new HashSet<string>();
        foreach (var w in new[] { "search", "serch", "surch", "seach" })
            terms.Add($"body\0{w}");
        while (terms.Count < 10_000)
        {
            int len = random.Next(3, 12);
            var chars = new char[len];
            for (int i = 0; i < len; i++) chars[i] = (char)('a' + random.Next(26));
            terms.Add($"body\0{new string(chars)}");
        }
        var sorted = terms.OrderBy(t => t, StringComparer.Ordinal).ToList();
        var offsets = new Dictionary<string, long>();
        for (int i = 0; i < sorted.Count; i++) offsets[sorted[i]] = i;

        WriteDictionary(path, sorted, offsets);
        using var reader = TermDictionaryReader.Open(path);

        var matches = reader.GetFuzzyMatches("body\0", "search".AsSpan(), 2);
        Assert.Contains(matches, m => m.Term == "body\0search" && m.Distance == 0);
        Assert.Contains(matches, m => m.Term == "body\0serch" && m.Distance == 1);
        Assert.True(matches.All(m => m.Distance <= 2), "All matches within edit distance 2");

        // Performance: 10K terms should complete quickly with prefix pruning
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < 100; i++)
            reader.GetFuzzyMatches("body\0", "search".AsSpan(), 2);
        sw.Stop();
        _output.WriteLine($"100 fuzzy queries over 10K terms: {sw.ElapsedMilliseconds}ms ({sw.ElapsedMilliseconds / 100.0:F2}ms/query)");
        Assert.True(sw.ElapsedMilliseconds < 2000, $"100 fuzzy queries took {sw.ElapsedMilliseconds}ms (expected < 2000ms)");
    }

    /// <summary>
    /// Verifies the Fuzzy Matches: Max Edits 2 Correct Distances scenario.
    /// </summary>
    [Fact(DisplayName = "Fuzzy Matches: Max Edits 2 Correct Distances")]
    public void FuzzyMatches_MaxEdits2_CorrectDistances()
    {
        var path = DicPath("fuzzy_dist2");
        var terms = new List<string>
        {
            "body\0abc",     // dist 0 from "abc"
            "body\0ab",      // dist 1 (deletion)
            "body\0abcd",    // dist 1 (insertion)
            "body\0axc",     // dist 1 (substitution)
            "body\0a",       // dist 2 (2 deletions)
            "body\0abcde",   // dist 2 (2 insertions)
            "body\0xyz",     // dist 3 — should NOT match
        };
        terms.Sort(StringComparer.Ordinal);
        var offsets = new Dictionary<string, long>();
        for (int i = 0; i < terms.Count; i++) offsets[terms[i]] = i;

        WriteDictionary(path, terms, offsets);
        using var reader = TermDictionaryReader.Open(path);

        var matches = reader.GetFuzzyMatches("body\0", "abc".AsSpan(), 2);
        Assert.Equal(0, matches.First(m => m.Term == "body\0abc").Distance);
        Assert.Equal(1, matches.First(m => m.Term == "body\0ab").Distance);
        Assert.Equal(1, matches.First(m => m.Term == "body\0abcd").Distance);
        Assert.Equal(1, matches.First(m => m.Term == "body\0axc").Distance);
        Assert.Equal(2, matches.First(m => m.Term == "body\0a").Distance);
        Assert.Equal(2, matches.First(m => m.Term == "body\0abcde").Distance);
        Assert.DoesNotContain(matches, m => m.Term == "body\0xyz");
    }

    // ── Range scan ──────────────────────────────────────────────────────────

    /// <summary>
    /// Verifies the Range Scan: Returns Terms In Range scenario.
    /// </summary>
    [Fact(DisplayName = "Range Scan: Returns Terms In Range")]
    public void RangeScan_ReturnsTermsInRange()
    {
        var path = DicPath("range");
        var terms = new List<string>
        {
            "body\0alpha",
            "body\0beta",
            "body\0gamma",
            "body\0delta"
        };
        terms.Sort(StringComparer.Ordinal);
        var offsets = new Dictionary<string, long>();
        for (int i = 0; i < terms.Count; i++) offsets[terms[i]] = i;

        WriteDictionary(path, terms, offsets);
        using var reader = TermDictionaryReader.Open(path);

        var range = reader.GetTermsInRange("body\0", "beta", "gamma", includeLower: true, includeUpper: true);
        Assert.Equal(3, range.Count); // beta, delta, gamma (lexicographic)
    }

    // ── Regex matching ──────────────────────────────────────────────────────

    /// <summary>
    /// Verifies the Regex Scan: Matches Pattern scenario.
    /// </summary>
    [Fact(DisplayName = "Regex Scan: Matches Pattern")]
    public void RegexScan_MatchesPattern()
    {
        var path = DicPath("regex");
        var terms = new List<string>
        {
            "body\0abc123",
            "body\0abc456",
            "body\0xyz789"
        };
        var offsets = new Dictionary<string, long>();
        for (int i = 0; i < terms.Count; i++) offsets[terms[i]] = i;

        WriteDictionary(path, terms, offsets);
        using var reader = TermDictionaryReader.Open(path);

        var regex = new System.Text.RegularExpressions.Regex(@"^abc\d+$");
        var matches = reader.GetTermsMatchingRegex("body\0", regex);
        Assert.Equal(2, matches.Count);
    }

    // ── Field enumeration ───────────────────────────────────────────────────

    /// <summary>
    /// Verifies the Get All Terms For Field: Returns All Field Terms scenario.
    /// </summary>
    [Fact(DisplayName = "Get All Terms For Field: Returns All Field Terms")]
    public void GetAllTermsForField_ReturnsAllFieldTerms()
    {
        var path = DicPath("fieldall");
        var terms = new List<string>
        {
            "body\0one",
            "body\0two",
            "title\0three"
        };
        var offsets = new Dictionary<string, long>();
        for (int i = 0; i < terms.Count; i++) offsets[terms[i]] = i;

        WriteDictionary(path, terms, offsets);
        using var reader = TermDictionaryReader.Open(path);

        var bodyTerms = reader.GetAllTermsForField("body\0");
        Assert.Equal(2, bodyTerms.Count);

        var titleTerms = reader.GetAllTermsForField("title\0");
        Assert.Single(titleTerms);
    }

    // ── Large-scale (10K terms) ─────────────────────────────────────────────

    /// <summary>
    /// Verifies the Large Scale: 10 K Terms All Lookup Succeeds scenario.
    /// </summary>
    [Fact(DisplayName = "Large Scale: 10 K Terms All Lookup Succeeds")]
    public void LargeScale_10KTerms_AllLookupSucceeds()
    {
        var path = DicPath("large10k");
        var terms = new List<string>();
        var offsets = new Dictionary<string, long>();
        for (int i = 0; i < 10_000; i++)
        {
            var term = $"body\0term_{i:D6}";
            terms.Add(term);
            offsets[term] = i * 8L;
        }
        terms.Sort(StringComparer.Ordinal);

        WriteDictionary(path, terms, offsets);
        using var reader = TermDictionaryReader.Open(path);

        // Spot-check first, middle, last
        Assert.True(reader.TryGetPostingsOffset("body\0term_000000", out long o0));
        Assert.Equal(0L, o0);

        Assert.True(reader.TryGetPostingsOffset("body\0term_005000", out long o5k));
        Assert.Equal(5000L * 8, o5k);

        Assert.True(reader.TryGetPostingsOffset("body\0term_009999", out long o9k));
        Assert.Equal(9999L * 8, o9k);

        Assert.False(reader.TryGetPostingsOffset("body\0term_099999", out _));

        _output.WriteLine("✓ 10K terms: all lookups correct");
    }

    // ── Phase-1 fuzzy DP (maxEdits > 2) ─────────────────────────────────────

    /// <summary>
    /// Verifies that GetFuzzyMatches with maxEdits greater than 2 uses the Phase-1 DP loop.
    /// </summary>
    [Fact(DisplayName = "Fuzzy Matches: Max Edits 3 Uses Phase-1 DP")]
    public void FuzzyMatches_MaxEdits3_UsesPhase1Dp()
    {
        // maxEdits == 3 bypasses the single-edit and maxEdits==2 fast paths,
        // routing through the prefix-sharing DP loop (Phase 1).
        var path = DicPath("fuzzy_phase1");
        var terms = new List<string>
        {
            "body\0abcdef",   // dist 0
            "body\0abcdex",   // dist 1 (substitution)
            "body\0abcxyz",   // dist 3 (3 substitutions)
            "body\0axxxxxx",  // dist > 3 — must not appear
        };
        terms.Sort(StringComparer.Ordinal);
        var offsets = new Dictionary<string, long>();
        for (int i = 0; i < terms.Count; i++) offsets[terms[i]] = i * 10L;

        WriteDictionary(path, terms, offsets);
        using var reader = TermDictionaryReader.Open(path);

        var matches = reader.GetFuzzyMatches("body\0", "abcdef".AsSpan(), 3);

        Assert.Contains(matches, m => m.Term == "body\0abcdef" && m.Distance == 0);
        Assert.Contains(matches, m => m.Term == "body\0abcdex" && m.Distance == 1);
        Assert.Contains(matches, m => m.Term == "body\0abcxyz" && m.Distance == 3);
        Assert.DoesNotContain(matches, m => m.Term == "body\0axxxxxx");
        Assert.True(matches.All(m => m.Distance <= 3));
    }

    /// <summary>
    /// Verifies that the Phase-1 dead-prefix optimisation skips terms ahead of a dead prefix,
    /// exercising LowerBound(key, lo) and TryBuildNextPrefixKey.
    /// </summary>
    [Fact(DisplayName = "Fuzzy Matches: Phase-1 Dead Prefix Skips Ahead Via LowerBound")]
    public void FuzzyMatches_Phase1_DeadPrefixSkipsAheadViaLowerBound()
    {
        // Query "cat" vs terms beginning "zzzz": after 4 bytes the DP row minimum
        // exceeds maxEdits=3, declaring a dead prefix. The skip-ahead increments the
        // prefix key via TryBuildNextPrefixKey and advances the scan pointer via
        // LowerBound(nextPrefix, idx + 1), bypassing all "zzzz*" variants.
        var path = DicPath("fuzzy_dead_skip");
        var terms = new List<string>
        {
            "body\0bat",
            "body\0cat",
            "body\0zzzz",
            "body\0zzzza",
            "body\0zzzzb",
            "body\0zzzzc",
            "body\0zzzzd",
            "body\0zzzze",
        };
        terms.Sort(StringComparer.Ordinal);
        var offsets = new Dictionary<string, long>();
        for (int i = 0; i < terms.Count; i++) offsets[terms[i]] = i * 10L;

        WriteDictionary(path, terms, offsets);
        using var reader = TermDictionaryReader.Open(path);

        var matches = reader.GetFuzzyMatches("body\0", "cat".AsSpan(), 3);

        Assert.Contains(matches, m => m.Term == "body\0cat" && m.Distance == 0);
        Assert.Contains(matches, m => m.Term == "body\0bat" && m.Distance == 1);
        Assert.DoesNotContain(matches, m => m.Term.StartsWith("body\0zzzz", StringComparison.Ordinal));
    }

    /// <summary>
    /// Verifies the early-out path when the field prefix sorts past all terms in the dictionary.
    /// </summary>
    [Fact(DisplayName = "Fuzzy Matches: Returns Empty When Prefix Sorts Past All Terms")]
    public void FuzzyMatches_ReturnsEmpty_WhenPrefixSortsPastAllTerms()
    {
        // LowerBound("zzz\0") returns _termCount, triggering the early return [].
        var path = DicPath("fuzzy_past_end");
        var terms = new List<string> { "aaa\0word", "bbb\0term" };
        var offsets = new Dictionary<string, long> { ["aaa\0word"] = 1L, ["bbb\0term"] = 2L };

        WriteDictionary(path, terms, offsets);
        using var reader = TermDictionaryReader.Open(path);

        var matches = reader.GetFuzzyMatches("zzz\0", "word".AsSpan(), 3);

        Assert.Empty(matches);
    }

    // ── GetFuzzyMatchesFallback (non-ASCII query) ────────────────────────────

    /// <summary>
    /// Verifies that a non-ASCII query term routes through GetFuzzyMatchesFallback.
    /// </summary>
    [Fact(DisplayName = "Fuzzy Matches: Non-ASCII Query Uses Byte Levenshtein")]
    public void FuzzyMatches_NonAsciiQuery_UsesByteLevenshtein()
    {
        // Distance is reported in UTF-8 bytes under the FST-backed v3 reader (byte-level
        // Levenshtein DFA). For ASCII queries this is identical to char-level; for
        // multi-byte queries it differs. Here "日本" (6 UTF-8 bytes) is 3 byte-deletions
        // away from "日本語" (9 UTF-8 bytes), so it is not reachable at maxEdits=1.
        var path = DicPath("fuzzy_noascii");
        var terms = new List<string>
        {
            "body\0english",
            "body\0日本",
            "body\0日本語",
        };
        terms.Sort(StringComparer.Ordinal);
        var offsets = new Dictionary<string, long>();
        for (int i = 0; i < terms.Count; i++) offsets[terms[i]] = i * 10L;

        WriteDictionary(path, terms, offsets);
        using var reader = TermDictionaryReader.Open(path);

        var matches = reader.GetFuzzyMatches("body\0", "日本語".AsSpan(), 1);

        Assert.Contains(matches, m => m.Term == "body\0日本語" && m.Distance == 0);
        Assert.DoesNotContain(matches, m => m.Term == "body\0english");

        var widerMatches = reader.GetFuzzyMatches("body\0", "日本語".AsSpan(), 3);
        Assert.Contains(widerMatches, m => m.Term == "body\0日本" && m.Distance == 3);
    }

    // ── FuzzyCacheKey (cache hit / eviction) ─────────────────────────────────

    /// <summary>
    /// Verifies that a maxEdits==2 result is returned from cache on a repeated call.
    /// </summary>
    [Fact(DisplayName = "Fuzzy Matches: MaxEdits 2 Result Is Served From Cache On Second Call")]
    public void FuzzyMatches_MaxEdits2_ResultIsServedFromCacheOnSecondCall()
    {
        var path = DicPath("fuzzy_cache_hit");
        var terms = new List<string> { "body\0hello", "body\0helo", "body\0world" };
        terms.Sort(StringComparer.Ordinal);
        var offsets = new Dictionary<string, long>();
        for (int i = 0; i < terms.Count; i++) offsets[terms[i]] = i;

        WriteDictionary(path, terms, offsets);
        using var reader = TermDictionaryReader.Open(path);

        var first = reader.GetFuzzyMatches("body\0", "hello".AsSpan(), 2);
        var second = reader.GetFuzzyMatches("body\0", "hello".AsSpan(), 2);

        Assert.Equal(first.Count, second.Count);
        foreach (var m in first)
            Assert.Contains(second, s => s.Term == m.Term && s.Distance == m.Distance);
    }

    /// <summary>
    /// Verifies that the fuzzy cache evicts all entries once MaxFuzzyCacheEntries is exceeded,
    /// and that subsequent calls still return correct results.
    /// </summary>
    [Fact(DisplayName = "Fuzzy Matches: Cache Evicts When Max Entries Exceeded")]
    public void FuzzyMatches_Cache_EvictsWhenMaxEntriesExceeded()
    {
        // MaxFuzzyCacheEntries == 128. The 129th unique (fieldPrefix, query, maxEdits, maxExpansions)
        // key triggers _fuzzyCache.Clear() before inserting the new entry.
        var path = DicPath("fuzzy_cache_evict");
        var terms = new List<string>();
        var offsets = new Dictionary<string, long>();
        for (int i = 0; i < 10; i++)
        {
            var t = $"body\0term{i:D2}";
            terms.Add(t);
            offsets[t] = i;
        }
        terms.Sort(StringComparer.Ordinal);

        WriteDictionary(path, terms, offsets);
        using var reader = TermDictionaryReader.Open(path);

        for (int i = 0; i < 130; i++)
        {
            // 130 distinct query strings → 130 unique FuzzyCacheKey values.
            var query = $"term{i:D3}";
            var results = reader.GetFuzzyMatches("body\0", query.AsSpan(), 2);
            Assert.True(results.All(m => m.Distance <= 2), $"Query {query}: result exceeded maxEdits");
        }
    }

    // ── GetAllTermsForField edge cases ────────────────────────────────────────

    /// <summary>
    /// Verifies that GetAllTermsForField returns empty for a field with no entries.
    /// </summary>
    [Fact(DisplayName = "Get All Terms For Field: Unknown Field Returns Empty")]
    public void GetAllTermsForField_UnknownField_ReturnsEmpty()
    {
        var path = DicPath("fieldall_unknown");
        var terms = new List<string> { "body\0foo", "body\0bar", "title\0baz" };
        terms.Sort(StringComparer.Ordinal);
        var offsets = new Dictionary<string, long>();
        for (int i = 0; i < terms.Count; i++) offsets[terms[i]] = i;

        WriteDictionary(path, terms, offsets);
        using var reader = TermDictionaryReader.Open(path);

        Assert.Empty(reader.GetAllTermsForField("nofield\0"));
    }

    /// <summary>
    /// Verifies that GetAllTermsForField on an empty dictionary returns empty.
    /// </summary>
    [Fact(DisplayName = "Get All Terms For Field: Empty Dictionary Returns Empty")]
    public void GetAllTermsForField_EmptyDictionary_ReturnsEmpty()
    {
        var path = DicPath("fieldall_empty_dict");
        WriteDictionary(path, [], []);
        using var reader = TermDictionaryReader.Open(path);

        Assert.Empty(reader.GetAllTermsForField("body\0"));
    }

    // ── GetTermsMatching: fullTerm decoding path ──────────────────────────────

    /// <summary>
    /// Verifies that wildcard matching decodes the full term when bareTerm bytes are non-ASCII.
    /// </summary>
    [Fact(DisplayName = "Wildcard Scan: Non-ASCII Terms Use Full Term Decode Path")]
    public void WildcardScan_NonAsciiTerms_UseFullTermDecodePath()
    {
        // "café" contains é (0xC3 0xA9 in UTF-8), so IsAscii(bareTerm) is false.
        // The ASCII fast path is skipped; the full term is decoded and matched
        // via WildcardQuery.Matches. ASCII terms ("caffe", "cat") take the fast path.
        var path = DicPath("wildcard_noascii_term");
        var terms = new List<string>
        {
            "body\0café",
            "body\0caffe",
            "body\0cat",
            "body\0dog",
        };
        terms.Sort(StringComparer.Ordinal);
        var offsets = new Dictionary<string, long>();
        for (int i = 0; i < terms.Count; i++) offsets[terms[i]] = i;

        WriteDictionary(path, terms, offsets);
        using var reader = TermDictionaryReader.Open(path);

        var matches = reader.GetTermsMatching("body\0", "ca*".AsSpan());
        var matched = matches.Select(m => m.Term).ToArray();

        Assert.Contains("body\0café", matched);
        Assert.Contains("body\0caffe", matched);
        Assert.Contains("body\0cat", matched);
        Assert.DoesNotContain("body\0dog", matched);
    }

    /// <summary>
    /// Verifies that a non-ASCII wildcard pattern routes all terms through the full term decode path.
    /// </summary>
    [Fact(DisplayName = "Wildcard Scan: Non-ASCII Pattern Uses Full Term Decode Path")]
    public void WildcardScan_NonAsciiPattern_UseFullTermDecodePath()
    {
        // patternIsAscii == false for "日*", so every candidate goes through the
        // fullTerm decode branch regardless of whether bareTerm bytes are ASCII.
        var path = DicPath("wildcard_noascii_pattern");
        var terms = new List<string>
        {
            "body\0english",
            "body\0日本",
            "body\0日本語",
        };
        terms.Sort(StringComparer.Ordinal);
        var offsets = new Dictionary<string, long>();
        for (int i = 0; i < terms.Count; i++) offsets[terms[i]] = i;

        WriteDictionary(path, terms, offsets);
        using var reader = TermDictionaryReader.Open(path);

        var matches = reader.GetTermsMatching("body\0", "日*".AsSpan());

        Assert.Equal(2, matches.Count);
        Assert.Contains(matches, m => m.Term == "body\0日本");
        Assert.Contains(matches, m => m.Term == "body\0日本語");
        Assert.DoesNotContain(matches, m => m.Term == "body\0english");
    }

    // ── Format version in file ──────────────────────────────────────────────

    /// <summary>
    /// Verifies the Written File: Has Version Three scenario.
    /// </summary>
    [Fact(DisplayName = "Written File: Has Version Three")]
    public void WrittenFile_HasVersionThree()
    {
        var path = DicPath("version_check");
        WriteDictionary(path, ["body\0test"], new Dictionary<string, long> { ["body\0test"] = 1 });

        using var input = new IndexInput(path);
        byte version = CodecFileHeader.ReadVersion(input, CodecFormats.TermDictionary);
        Assert.Equal(CodecConstants.TermDictionaryVersion, version);
    }
}
