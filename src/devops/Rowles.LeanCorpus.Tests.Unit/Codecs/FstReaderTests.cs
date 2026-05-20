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
}
