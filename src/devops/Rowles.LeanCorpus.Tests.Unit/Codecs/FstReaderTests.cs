using System.Text;
using Rowles.LeanCorpus.Codecs.Fst;

namespace Rowles.LeanCorpus.Tests.Unit.Codecs;

/// <summary>
/// Round-trip tests covering the <see cref="FstReader"/> over data emitted by <see cref="FstBuilder"/>.
/// Verifies exact lookup, prefix enumeration, and automaton intersection (prefix, wildcard, Levenshtein).
/// </summary>
public sealed class FstReaderTests
{
    private static byte[] Build(IEnumerable<(string Key, long Output)> entries)
    {
        var sorted = entries
            .Select(e => (KeyUtf8: Encoding.UTF8.GetBytes(e.Key), e.Output))
            .OrderBy(e => e.KeyUtf8, ByteArrayComparer.Instance)
            .ToList();

        var b = new FstBuilder();
        foreach (var (key, output) in sorted)
            b.Add(key, output);
        return b.Finish();
    }

    private sealed class ByteArrayComparer : IComparer<byte[]>
    {
        public static readonly ByteArrayComparer Instance = new();
        public int Compare(byte[]? x, byte[]? y) => x!.AsSpan().SequenceCompareTo(y);
    }

    [Fact]
    public void Empty_Fst_Has_Zero_Count()
    {
        var blob = new FstBuilder().Finish();
        var reader = FstReader.Open(blob);
        Assert.Equal(0, reader.Count);
        Assert.True(reader.IsEmpty);
        Assert.False(reader.TryGetOutput("any"u8, out _));
        Assert.Empty(reader.EnumerateAll());
    }

    [Fact]
    public void Roundtrip_Exact_Lookups()
    {
        var entries = new (string, long)[]
        {
            ("apple", 100),
            ("application", 200),
            ("apply", 300),
            ("banana", 400),
            ("band", 500),
            ("cat", 600),
        };
        var blob = Build(entries);
        var reader = FstReader.Open(blob);
        Assert.Equal(entries.Length, reader.Count);

        foreach (var (term, output) in entries)
        {
            var bytes = Encoding.UTF8.GetBytes(term);
            Assert.True(reader.TryGetOutput(bytes, out long got), $"missing {term}");
            Assert.Equal(output, got);
        }

        Assert.False(reader.TryGetOutput("missing"u8, out _));
        Assert.False(reader.TryGetOutput("app"u8, out _));
        Assert.False(reader.TryGetOutput("apples"u8, out _));
    }

    [Fact]
    public void Prefix_Of_Another_Key_Has_Independent_Output()
    {
        // "ab" with output 5, "abc" with output 7; both must round-trip.
        var blob = Build([("ab", 5), ("abc", 7)]);
        var reader = FstReader.Open(blob);

        Assert.True(reader.TryGetOutput("ab"u8, out long ab));
        Assert.True(reader.TryGetOutput("abc"u8, out long abc));
        Assert.Equal(5, ab);
        Assert.Equal(7, abc);
    }

    [Fact]
    public void EnumerateAll_Yields_Sorted_Pairs()
    {
        var entries = new (string, long)[]
        {
            ("a", 1), ("ab", 2), ("ac", 3), ("b", 4), ("c", 5),
        };
        var blob = Build(entries);
        var reader = FstReader.Open(blob);

        var got = reader.EnumerateAll()
            .Select(p => (Encoding.UTF8.GetString(p.Key), p.Output))
            .ToList();

        Assert.Equal(entries.Length, got.Count);
        foreach (var (key, output) in entries)
            Assert.Contains((key, output), got);
    }

    [Fact]
    public void EnumerateWithPrefix_Filters_To_Subtree()
    {
        var blob = Build([("alpha", 1), ("alpine", 2), ("apple", 3), ("banana", 4)]);
        var reader = FstReader.Open(blob);

        var got = reader.EnumerateWithPrefix("alp"u8)
            .Select(p => Encoding.UTF8.GetString(p.Key))
            .OrderBy(s => s)
            .ToList();

        Assert.Equal(new[] { "alpha", "alpine" }, got);
    }

    [Fact]
    public void IntersectAutomaton_Prefix_Equivalent_To_EnumerateWithPrefix()
    {
        var blob = Build([("alpha", 1), ("alpine", 2), ("apple", 3), ("banana", 4)]);
        var reader = FstReader.Open(blob);

        var prefix = new PrefixAutomaton("alp");
        var got = reader.IntersectAutomaton(prefix)
            .Select(t => Encoding.UTF8.GetString(t.Key))
            .OrderBy(s => s)
            .ToList();

        Assert.Equal(new[] { "alpha", "alpine" }, got);
    }

    [Fact]
    public void IntersectAutomaton_Levenshtein_Reports_Distance()
    {
        var blob = Build([("kitten", 1), ("sitting", 2), ("kitchen", 3), ("kit", 4)]);
        var reader = FstReader.Open(blob);

        var lev = new LevenshteinAutomaton("kitten", 2);
        var got = reader.IntersectAutomaton(lev)
            .Select(t => (Term: Encoding.UTF8.GetString(t.Key), Distance: lev.MinDistance(t.FinalState)))
            .OrderBy(t => t.Term)
            .ToList();

        Assert.Contains(("kitten", 0), got);
        Assert.Contains(("kitchen", 2), got);
        // "kit" is distance 3 from "kitten", should NOT appear with maxEdits=2.
        Assert.DoesNotContain(got, t => t.Term == "kit");
    }

    // ── Phase 1: Allocation-light output-collector paths ──────────────────

    [Fact]
    public void CollectOutputsWithPrefix_Returns_Correct_Outputs()
    {
        var entries = new (string, long)[]
        {
            ("alpha", 10),
            ("alpine", 20),
            ("apple", 30),
            ("banana", 40),
            ("band", 50),
            ("cat", 60),
        };
        var blob = Build(entries);
        var reader = FstReader.Open(blob);

        // Valid prefix
        var sink = new List<long>();
        reader.CollectOutputsWithPrefix("al"u8, sink);
        Assert.Equal(new long[] { 10, 20 }, sink.OrderBy(x => x));

        // Empty prefix returns all
        sink.Clear();
        reader.CollectOutputsWithPrefix(ReadOnlySpan<byte>.Empty, sink);
        Assert.Equal(entries.Length, sink.Count);

        // Non-existent prefix
        sink.Clear();
        reader.CollectOutputsWithPrefix("zzz"u8, sink);
        Assert.Empty(sink);

        // Prefix that IS an exact key
        sink.Clear();
        reader.CollectOutputsWithPrefix("alpine"u8, sink);
        Assert.Single(sink, 20L);
    }

    [Fact]
    public void CollectIntersectOutputs_Returns_Correct_Outputs()
    {
        var entries = new (string, long)[]
        {
            ("alpha", 10),
            ("alpine", 20),
            ("apple", 30),
            ("banana", 40),
            ("band", 50),
            ("cat", 60),
            ("car", 70),
        };
        var blob = Build(entries);
        var reader = FstReader.Open(blob);

        // Prefix automaton — should match CollectOutputsWithPrefix
        var prefix = new PrefixAutomaton("al");
        var sink = new List<long>();
        reader.CollectIntersectOutputs(prefix, ReadOnlySpan<byte>.Empty, sink);
        Assert.Equal(new long[] { 10, 20 }, sink.OrderBy(x => x));

        // WildcardAutomaton "ca?" — should match "cat", "car".
        // Verify via IntersectAutomaton (known-correct) then test CollectIntersectOutputs
        // with an automaton whose accept state is not a sink (prefix automaton).
        var wildcard = new WildcardAutomaton("ca?");
        var wildcardResults = reader.IntersectAutomaton(wildcard)
            .Select(t => Encoding.UTF8.GetString(t.Key))
            .ToHashSet();
        Assert.Contains("cat", wildcardResults);
        Assert.Contains("car", wildcardResults);
        Assert.Equal(2, wildcardResults.Count);

        // LevenshteinAutomaton "bt" with maxEdits 1 — no matching term.
        var lev = new LevenshteinAutomaton("bt", 1);
        var levResults = reader.IntersectAutomaton(lev).ToList();
        Assert.Empty(levResults);

        // LevenshteinAutomaton "bat" with maxEdits 1 — "cat" is distance 1.
        var lev2 = new LevenshteinAutomaton("bat", 1);
        var lev2Results = reader.IntersectAutomaton(lev2)
            .Select(t => Encoding.UTF8.GetString(t.Key))
            .ToList();
        Assert.Single(lev2Results);
        Assert.Equal("cat", lev2Results[0]);

        // IsSink branch: WildcardAutomaton("*") should collect all
        var all = new WildcardAutomaton("*");
        sink.Clear();
        reader.CollectIntersectOutputs(all, ReadOnlySpan<byte>.Empty, sink);
        Assert.Equal(entries.Length, sink.Count);
    }

    [Fact]
    public void CollectContainsOutputs_Returns_Correct_Outputs()
    {
        var entries = new (string, long)[]
        {
            ("alpha", 10),
            ("alpine", 20),
            ("banana", 40),
            ("band", 50),
            ("canada", 60),
        };
        var blob = Build(entries);
        var reader = FstReader.Open(blob);

        // Needle "na" matches "banana"(40), "canada"(60)
        var sink = new List<long>();
        reader.CollectContainsOutputs(ReadOnlySpan<byte>.Empty, "na"u8, sink);
        Assert.Equal(new long[] { 40, 60 }, sink.OrderBy(x => x));

        // Needle "alp" matches "alpha"(10), "alpine"(20)
        sink.Clear();
        reader.CollectContainsOutputs(ReadOnlySpan<byte>.Empty, "alp"u8, sink);
        Assert.Equal(new long[] { 10, 20 }, sink.OrderBy(x => x));

        // Needle "zz" matches nothing
        sink.Clear();
        reader.CollectContainsOutputs(ReadOnlySpan<byte>.Empty, "zz"u8, sink);
        Assert.Empty(sink);

        // Empty needle matches all
        sink.Clear();
        reader.CollectContainsOutputs(ReadOnlySpan<byte>.Empty, ReadOnlySpan<byte>.Empty, sink);
        Assert.Equal(entries.Length, sink.Count);

        // Needle present in subset
        sink.Clear();
        reader.CollectContainsOutputs(ReadOnlySpan<byte>.Empty, "an"u8, sink);
        Assert.Equal(new long[] { 40, 50, 60 }, sink.OrderBy(x => x));
    }

    [Fact]
    public void EnumerateOutputsWithPrefix_Returns_Correct_Outputs()
    {
        var entries = new (string, long)[]
        {
            ("alpha", 10),
            ("alpine", 20),
            ("apple", 30),
            ("banana", 40),
        };
        var blob = Build(entries);
        var reader = FstReader.Open(blob);

        var got = reader.EnumerateOutputsWithPrefix("al"u8).OrderBy(x => x).ToList();
        Assert.Equal(new long[] { 10, 20 }, got);

        // Empty prefix returns all
        var all = reader.EnumerateOutputsWithPrefix(ReadOnlySpan<byte>.Empty).OrderBy(x => x).ToList();
        Assert.Equal(new long[] { 10, 20, 30, 40 }, all);

        // Non-existent prefix
        var none = reader.EnumerateOutputsWithPrefix("zzz"u8).ToList();
        Assert.Empty(none);
    }

    [Fact]
    public void EnumerateContainsOutputs_Returns_Correct_Outputs()
    {
        var entries = new (string, long)[]
        {
            ("alpha", 10),
            ("alpine", 20),
            ("banana", 40),
            ("band", 50),
        };
        var blob = Build(entries);
        var reader = FstReader.Open(blob);

        // Needle "na" matches "banana"(40)
        var got = reader.EnumerateContainsOutputs(ReadOnlySpan<byte>.Empty, "na"u8).ToList();
        Assert.Single(got, 40L);

        // Needle "alp" matches "alpha"(10), "alpine"(20)
        got = reader.EnumerateContainsOutputs(ReadOnlySpan<byte>.Empty, "alp"u8).OrderBy(x => x).ToList();
        Assert.Equal(new long[] { 10, 20 }, got);

        // Needle "zz" matches nothing
        got = reader.EnumerateContainsOutputs(ReadOnlySpan<byte>.Empty, "zz"u8).ToList();
        Assert.Empty(got);

        // Empty needle matches all
        var all = reader.EnumerateContainsOutputs(ReadOnlySpan<byte>.Empty, ReadOnlySpan<byte>.Empty)
            .OrderBy(x => x).ToList();
        Assert.Equal(new long[] { 10, 20, 40, 50 }, all);
    }

    [Fact]
    public void IntersectAutomaton_With_Qualifier_Filters_By_Qualifier_Then_Automaton()
    {
        // Build FST with field-qualified terms ("body\0term").
        // The qualifier overload applies the automaton to the suffix only.
        var entries = new (string, long)[]
        {
            ("body\0alpha", 10),
            ("body\0alpine", 20),
            ("body\0apple", 30),
            ("title\0alpha", 40),
            ("title\0banana", 50),
        };
        var sorted = entries
            .Select(e => (KeyUtf8: Encoding.UTF8.GetBytes(e.Item1), Output: e.Item2))
            .OrderBy(e => e.KeyUtf8, ByteArrayComparer.Instance)
            .ToList();
        var b = new FstBuilder();
        foreach (var (key, output) in sorted)
            b.Add(key, output);
        var blob = b.Finish();
        var reader = FstReader.Open(blob);

        // Intersect with "body\0" qualifier and prefix "al" — should match body\0alpha, body\0alpine
        var prefix = new PrefixAutomaton("al");
        var results = reader.IntersectAutomaton(prefix, "body\0"u8)
            .Select(t => Encoding.UTF8.GetString(t.Key))
            .OrderBy(s => s)
            .ToList();

        Assert.Equal(new[] { "body\0alpha", "body\0alpine" }, results);
    }

    [Fact]
    public void IntersectAutomatonOutputs_Returns_Correct_Outputs_And_FinalStates()
    {
        var entries = new (string, long)[]
        {
            ("alpha", 10),
            ("alpine", 20),
            ("apple", 30),
            ("banana", 40),
        };
        var blob = Build(entries);
        var reader = FstReader.Open(blob);

        // Intersect with prefix automaton — outputs only
        var prefix = new PrefixAutomaton("al");
        var results = reader.IntersectAutomatonOutputs(prefix, ReadOnlySpan<byte>.Empty).ToList();

        Assert.Equal(2, results.Count);
        var outputs = results.Select(r => r.Output).OrderBy(x => x).ToList();
        Assert.Equal(new long[] { 10, 20 }, outputs);
        // All final states should be accepting
        Assert.All(results, r => Assert.True(prefix.IsAccept(r.FinalState)));
    }

    // ── Phase 2: FstReader edge cases ────────────────────────────────────

    [Fact]
    public void Open_Rejects_Corrupt_Blob()
    {
        // Truncated header (<4 bytes)
        Assert.Throws<InvalidDataException>(() => FstReader.Open([0x46, 0x53]));

        // Wrong magic bytes
        var wrongMagic = new byte[] { 0x46, 0x53, 0x54, 0x32, 0x00, 0x00 };
        Assert.Throws<InvalidDataException>(() => FstReader.Open(wrongMagic));

        // Out-of-bounds node address: rootAddress beyond blob length after header.
        // Open does not validate this, but traversal should fail gracefully.
        var oobBlob = new byte[16];
        "FST1"u8.CopyTo(oobBlob);
        // Write rootAddress VarInt = 1000 (beyond remaining blob), count = 1
        int pos = 4;
        pos += FstBuilder.WriteVarInt(oobBlob, 4, 1000L);
        FstBuilder.WriteVarInt(oobBlob, pos, 1L);
        var reader = FstReader.Open(oobBlob);
        // Traversal with OOB address should not throw, just return false.
        Assert.False(reader.TryGetOutput("any"u8, out _));
        Assert.Empty(reader.EnumerateAll());
    }

    [Fact]
    public void Large_VarInt_Outputs_RoundTrip()
    {
        var entries = new (string, long)[]
        {
            ("a", 127),                    // 1-byte boundary
            ("b", 128),                    // 2-byte start
            ("c", 16383),                  // 2-byte boundary
            ("d", 16384),                  // 3-byte start
            ("e", 2097151),                // 3-byte boundary
            ("f", 2097152),                // 4-byte start
            ("g", long.MaxValue),          // maximum
            ("h", 0),
        };
        var blob = Build(entries);
        var reader = FstReader.Open(blob);

        foreach (var (term, expected) in entries)
        {
            Assert.True(reader.TryGetOutput(Encoding.UTF8.GetBytes(term), out long got),
                $"missing key '{term}'");
            Assert.Equal(expected, got);
        }
    }

    [Fact]
    public void FinalOutput_VirtualArc_RoundTrip()
    {
        // "a" → 100, "ab" → 200, "ac" → 300
        // After output distribution, "a" should have non-zero final output
        // read via the 0xFF virtual arc.
        var entries = new (string, long)[]
        {
            ("a", 100),
            ("ab", 200),
            ("ac", 300),
        };
        var blob = Build(entries);
        var reader = FstReader.Open(blob);

        Assert.True(reader.TryGetOutput("a"u8, out long a));
        Assert.True(reader.TryGetOutput("ab"u8, out long ab));
        Assert.True(reader.TryGetOutput("ac"u8, out long ac));
        Assert.Equal(100, a);
        Assert.Equal(200, ab);
        Assert.Equal(300, ac);
    }

    [Fact]
    public void Deep_FST_With_LongKeys_RoundTrips()
    {
        // 100 keys of length ~1KB each
        var keys = new List<string>();
        var expected = new Dictionary<string, long>();
        var rng = new Random(42);
        for (int i = 0; i < 100; i++)
        {
            var sb = new StringBuilder();
            sb.Append($"prefix_{i:D4}_");
            while (sb.Length < 1000)
                sb.Append((char)('a' + rng.Next(26)));
            var key = sb.ToString();
            keys.Add(key);
            expected[key] = i * 10L;
        }
        keys.Sort(StringComparer.Ordinal);

        var b = new FstBuilder();
        foreach (var key in keys)
            b.Add(Encoding.UTF8.GetBytes(key), expected[key]);
        var blob = b.Finish();
        var reader = FstReader.Open(blob);

        Assert.Equal(100, reader.Count);
        foreach (var key in keys)
        {
            Assert.True(reader.TryGetOutput(Encoding.UTF8.GetBytes(key), out long got),
                $"missing key starting '{key[..20]}...'");
            Assert.Equal(expected[key], got);
        }

        // Enumerate all
        var enumerated = reader.EnumerateAll()
            .Select(p => (Encoding.UTF8.GetString(p.Key), p.Output))
            .ToList();
        Assert.Equal(keys.Count, enumerated.Count);
    }

    // ── Corruption / bounds-checking tests ────────────────────────────────

    [Fact]
    public void Truncated_Node_At_LastByte_Does_Not_Crash()
    {
        // Node section has a single byte — reading flags+label would go OOB.
        var blob = MakeBlob(rootAddress: 0, count: 1, nodes: new byte[] { 0x00 });
        var reader = FstReader.Open(blob);

        // All traversal paths should return empty/false, not throw.
        Assert.False(reader.TryGetOutput("a"u8, out _));
        Assert.Empty(reader.EnumerateAll());
        Assert.Empty(reader.EnumerateWithPrefix("a"u8));
    }

    [Fact]
    public void Truncated_Arc_Missing_VarInt_Does_Not_Crash()
    {
        // A node whose first (and only) arc has flags indicating target + output
        // but the node section ends right after the label byte.
        byte flags = FstBuilder.FlagIsLastArc | FstBuilder.FlagHasTarget | FstBuilder.FlagHasOutput;
        byte label = (byte)'a';
        var nodes = new byte[] { flags, label };
        var blob = MakeBlob(rootAddress: 0, count: 1, nodes: nodes);
        var reader = FstReader.Open(blob);

        // Must not throw — exact results from corrupt data are undefined.
        _ = reader.TryGetOutput("a"u8, out _);
        Assert.Empty(reader.EnumerateWithPrefix("a"u8));
    }

    [Fact]
    public void Arc_With_ContinuationByte_Overflow_Does_Not_Crash()
    {
        // An arc with FlagHasTarget, followed by 12 bytes of 0x80 (continuation),
        // exceeding MaxVarInt64Size. The reader must not loop forever or read past buffer.
        byte flags = FstBuilder.FlagIsLastArc | FstBuilder.FlagHasTarget;
        byte label = (byte)'x';
        var nodes = new byte[2 + 12];
        nodes[0] = flags;
        nodes[1] = label;
        for (int i = 0; i < 12; i++)
            nodes[2 + i] = 0x80;

        var blob = MakeBlob(rootAddress: 0, count: 1, nodes: nodes);
        var reader = FstReader.Open(blob);

        // Must not throw or loop forever.
        _ = reader.TryGetOutput("x"u8, out _);
        Assert.Empty(reader.EnumerateWithPrefix("x"u8));
    }

    [Fact]
    public void Bogus_RootAddress_Does_Not_Crash()
    {
        // rootAddress points into the header area (before node data).
        var blob = MakeBlob(rootAddress: -5, count: 1, nodes: [0x00]);
        var reader = FstReader.Open(blob);

        Assert.False(reader.TryGetOutput("a"u8, out _));
        Assert.Empty(reader.EnumerateAll());
    }

    [Fact]
    public void Arc_With_Both_VarInts_But_Only_Room_For_One_Does_Not_Crash()
    {
        // Flags say target and output. The target VarInt is a single valid byte,
        // but the output byte is a continuation (0x80) with no following bytes,
        // forcing TryReadVarInt to fail at the buffer boundary.
        byte flags = FstBuilder.FlagIsLastArc | FstBuilder.FlagHasTarget | FstBuilder.FlagHasOutput;
        byte label = (byte)'z';
        var nodes = new byte[4];
        nodes[0] = flags;
        nodes[1] = label;
        nodes[2] = 0x01; // target = 1, a single-byte VarInt
        nodes[3] = 0x80; // continuation — expects more bytes but buffer ends

        var blob = MakeBlob(rootAddress: 0, count: 1, nodes: nodes);
        var reader = FstReader.Open(blob);

        // Must not throw.
        _ = reader.TryGetOutput("z"u8, out _);
        Assert.Empty(reader.EnumerateWithPrefix("z"u8));
    }


    /// <summary>
    /// Terms that share common suffixes must survive node deduplication with correct outputs.
    /// The FST builder compiles suffix nodes once and reuses them via hash lookup; this test
    /// ensures that the content comparison guards against any theoretical hash collision.
    /// </summary>
    [Fact]
    public void SuffixSharing_Dedup_Preserves_Outputs()
    {
        // Groups sharing suffixes:
        //   "aaaaa" (1), "baaaa" (2), "caaaa" (3)  -- share "aaaa" suffix
        //   "aaabb" (4), "bbabb" (5), "ccabb" (6)  -- share "abb" suffix
        //   "zzzzz" (7)                             -- unique
        var entries = new (string Key, long Output)[]
        {
            ("aaaaa", 1), ("aaabb", 4), ("baaaa", 2), ("bbabb", 5),
            ("caaaa", 3), ("ccabb", 6), ("zzzzz", 7),
        };
        var blob = Build(entries);
        var reader = FstReader.Open(blob);
        Assert.Equal(entries.Length, reader.Count);

        foreach (var (key, expectedOutput) in entries)
        {
            var keyBytes = Encoding.UTF8.GetBytes(key);
            Assert.True(reader.TryGetOutput(keyBytes, out long got),
                $"Lookup failed for '{key}'");
            Assert.Equal(expectedOutput, got);
        }
    }
    private static byte[] MakeBlob(long rootAddress, long count, byte[] nodes)
    {
        // Build a minimal FST1 blob: [magic 4B][rootAddress VarInt][count VarInt][nodes...]
        int headerSize = 4 + FstBuilder.VarIntSize(rootAddress) + FstBuilder.VarIntSize(count);
        var blob = new byte[headerSize + nodes.Length];
        "FST1"u8.CopyTo(blob);
        int pos = 4;
        pos += FstBuilder.WriteVarInt(blob, pos, rootAddress);
        FstBuilder.WriteVarInt(blob, pos, count);
        Buffer.BlockCopy(nodes, 0, blob, headerSize, nodes.Length);
        return blob;
    }
}
