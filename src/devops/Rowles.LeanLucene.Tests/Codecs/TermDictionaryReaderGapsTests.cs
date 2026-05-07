using Rowles.LeanLucene.Codecs;
using Rowles.LeanLucene.Codecs.Fst;
using Rowles.LeanLucene.Codecs.TermDictionary;
using System.Text;
using System.Text.RegularExpressions;

namespace Rowles.LeanLucene.Tests.Codecs;

/// <summary>
/// Gap coverage for <see cref="TermDictionaryReader"/> header validation,
/// disposal, and small v2 dictionary lookups.
/// </summary>
[Trait("Category", "Codecs")]
[Trait("Category", "UnitTest")]
public sealed class TermDictionaryReaderGapsTests : IDisposable
{
    private readonly string _dir;

    public TermDictionaryReaderGapsTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "ll_tdr_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    [Fact(DisplayName = "TermDictionaryReader: Open Rejects Bad Magic")]
    public void Open_BadMagic_ThrowsInvalidDataException()
    {
        var path = Path.Combine(_dir, "bad_magic.dic");
        using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write))
        using (var writer = new BinaryWriter(fs))
        {
            writer.Write(0x12345678);
            writer.Write((byte)CodecConstants.TermDictionaryVersion);
        }

        Assert.Throws<InvalidDataException>(() => TermDictionaryReader.Open(path));
    }

    [Fact(DisplayName = "TermDictionaryReader: Open Rejects Unsupported Version")]
    public void Open_UnsupportedVersion_ThrowsInvalidDataException()
    {
        var path = Path.Combine(_dir, "future.dic");
        using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write))
        using (var writer = new BinaryWriter(fs))
        {
            writer.Write(CodecConstants.Magic);
            writer.Write((byte)99);
        }

        Assert.Throws<InvalidDataException>(() => TermDictionaryReader.Open(path));
    }

    [Fact(DisplayName = "TermDictionaryReader: V2 Exact Lookup Returns Offset")]
    public void V2ExactLookup_ReturnsOffset()
    {
        var path = WriteDictionary();
        using var reader = TermDictionaryReader.Open(path);
        string term = string.Concat("body", "\0", "alpha");

        bool found = reader.TryGetPostingsOffset(term, out long offset);

        Assert.True(found);
        Assert.Equal(11L, offset);
    }

    [Fact(DisplayName = "TermDictionaryReader: V2 Missing Lookup Returns False")]
    public void V2MissingLookup_ReturnsFalse()
    {
        var path = WriteDictionary();
        using var reader = TermDictionaryReader.Open(path);

        bool found = reader.TryGetPostingsOffset("body\0missing", out long offset);

        Assert.False(found);
        Assert.Equal(0L, offset);
    }

    [Fact(DisplayName = "TermDictionaryReader: V2 Enumerate All Terms Returns Sorted Entries")]
    public void V2EnumerateAllTerms_ReturnsSortedEntries()
    {
        var path = WriteDictionary();
        using var reader = TermDictionaryReader.Open(path);

        var entries = reader.EnumerateAllTerms();

        Assert.Equal(3, entries.Count);
        Assert.Equal("body\0alpha", entries[0].Term);
        Assert.Equal(11L, entries[0].Offset);
        Assert.Equal("body\0beta", entries[1].Term);
        Assert.Equal(22L, entries[1].Offset);
        Assert.Equal("title\0alpha", entries[2].Term);
        Assert.Equal(33L, entries[2].Offset);
    }

    [Fact(DisplayName = "TermDictionaryReader: Dispose Is Idempotent")]
    public void Dispose_IsIdempotent()
    {
        var path = WriteDictionary();
        var reader = TermDictionaryReader.Open(path);

        reader.Dispose();
        var ex = Record.Exception(reader.Dispose);

        Assert.Null(ex);
    }

    [Fact(DisplayName = "TermDictionaryReader: V1 Exact Lookup Returns Offset")]
    public void V1ExactLookup_ReturnsOffset()
    {
        var path = WriteV1Dictionary(
            "v1_exact",
            ("body\0alpha", 11L),
            ("body\0beta", 22L),
            ("title\0alpha", 33L));
        using var reader = TermDictionaryReader.Open(path);

        var found = reader.TryGetPostingsOffset("body\0beta".AsSpan(), out long offset);
        var missing = reader.TryGetPostingsOffset("body\0missing".AsSpan(), out long missingOffset);

        Assert.True(found);
        Assert.Equal(22L, offset);
        Assert.False(missing);
        Assert.Equal(0L, missingOffset);
    }

    [Fact(DisplayName = "TermDictionaryReader: V1 Prefix And Field Enumeration Return Matches")]
    public void V1PrefixAndFieldEnumeration_ReturnMatches()
    {
        var path = WriteV1Dictionary(
            "v1_prefix",
            ("body\0alpha", 10L),
            ("body\0alphabet", 20L),
            ("body\0beta", 30L),
            ("title\0alpha", 40L));
        using var reader = TermDictionaryReader.Open(path);

        var alphaTerms = reader.GetTermsWithPrefix("body\0alph".AsSpan());
        var bodyTerms = reader.GetAllTermsForField("body\0");
        var allTerms = reader.EnumerateAllTerms();

        Assert.Equal([("body\0alpha", 10L), ("body\0alphabet", 20L)], alphaTerms);
        Assert.Equal(3, bodyTerms.Count);
        Assert.Equal(4, allTerms.Count);
        Assert.DoesNotContain(allTerms, entry => entry.Term == "skip\0ignored");
    }

    [Fact(DisplayName = "TermDictionaryReader: V1 Wildcard Matching Returns Terms And Offsets")]
    public void V1WildcardMatching_ReturnsTermsAndOffsets()
    {
        var path = WriteV1Dictionary(
            "v1_wildcard",
            ("body\0cart", 10L),
            ("body\0cat", 20L),
            ("body\0cot", 30L),
            ("body\0cut", 40L),
            ("title\0cat", 50L));
        using var reader = TermDictionaryReader.Open(path);

        var terms = reader.GetTermsMatching("body\0", "c?t".AsSpan());
        var offsets = reader.GetTermOffsetsMatching("body\0", "c?t".AsSpan());

        Assert.Equal([("body\0cat", 20L), ("body\0cot", 30L), ("body\0cut", 40L)], terms);
        Assert.Equal([20L, 30L, 40L], offsets);
    }

    [Fact(DisplayName = "TermDictionaryReader: V1 Fuzzy Matching Returns Closest Expansions")]
    public void V1FuzzyMatching_ReturnsClosestExpansions()
    {
        var path = WriteV1Dictionary(
            "v1_fuzzy",
            ("body\0bar", 10L),
            ("body\0book", 20L),
            ("body\0books", 30L),
            ("body\0boon", 40L),
            ("body\0cook", 50L),
            ("title\0book", 60L));
        using var reader = TermDictionaryReader.Open(path);

        var matches = reader.GetFuzzyMatches("body\0", "book".AsSpan(), maxEdits: 1, maxExpansions: 2);

        Assert.Equal(2, matches.Count);
        Assert.Contains(matches, entry => entry.Term == "body\0book" && entry.Offset == 20L && entry.Distance == 0);
        Assert.All(matches, entry => Assert.InRange(entry.Distance, 0, 1));
        Assert.DoesNotContain(matches, entry => entry.Term.StartsWith("title\0", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "TermDictionaryReader: V1 Range Matching Honours Inclusive Flags")]
    public void V1RangeMatching_HonoursInclusiveFlags()
    {
        var path = WriteV1Dictionary(
            "v1_range",
            ("body\0apple", 10L),
            ("body\0apricot", 20L),
            ("body\0banana", 30L),
            ("body\0berry", 40L),
            ("title\0apricot", 50L));
        using var reader = TermDictionaryReader.Open(path);

        var inclusive = reader.GetTermsInRange("body\0", "apple", "banana");
        var lowerExclusive = reader.GetTermsInRange("body\0", "apple", "banana", includeLower: false);
        var upperExclusive = reader.GetTermsInRange("body\0", "apple", "banana", includeUpper: false);
        var openLower = reader.GetTermsInRange("body\0", null, "apricot", includeUpper: true);

        Assert.Equal([("body\0apple", 10L), ("body\0apricot", 20L), ("body\0banana", 30L)], inclusive);
        Assert.Equal([("body\0apricot", 20L), ("body\0banana", 30L)], lowerExclusive);
        Assert.Equal([("body\0apple", 10L), ("body\0apricot", 20L)], upperExclusive);
        Assert.Equal([("body\0apple", 10L), ("body\0apricot", 20L)], openLower);
    }

    [Fact(DisplayName = "TermDictionaryReader: V1 Regex And Automaton Matching Return Matches")]
    public void V1RegexAndAutomatonMatching_ReturnMatches()
    {
        var path = WriteV1Dictionary(
            "v1_regex_automaton",
            ("body\0apple", 10L),
            ("body\0application", 20L),
            ("body\0banana", 30L),
            ("title\0apple", 40L));
        using var reader = TermDictionaryReader.Open(path);

        var regexMatches = reader.GetTermsMatchingRegex("body\0", new Regex("^app.+e$", RegexOptions.CultureInvariant));
        var automatonMatches = reader.IntersectAutomaton("body\0", new PrefixAutomaton("app"));

        Assert.Equal([("body\0apple", 10L)], regexMatches);
        Assert.Equal([("body\0apple", 10L), ("body\0application", 20L)], automatonMatches);
    }

    private string WriteDictionary()
    {
        var path = Path.Combine(_dir, "tiny.dic");
        var terms = new List<string>
        {
            "body\0alpha",
            "body\0beta",
            "title\0alpha"
        };
        var offsets = new Dictionary<string, long>(StringComparer.Ordinal)
        {
            ["body\0alpha"] = 11L,
            ["body\0beta"] = 22L,
            ["title\0alpha"] = 33L
        };

        TermDictionaryWriter.Write(path, terms, offsets);
        return path;
    }

    private string WriteV1Dictionary(string name, params (string Term, long Offset)[] entries)
    {
        var path = Path.Combine(_dir, name + ".dic");
        var sortedEntries = entries.OrderBy(entry => entry.Term, StringComparer.Ordinal).ToArray();

        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        using var writer = new BinaryWriter(fs, Encoding.UTF8);
        CodecConstants.WriteHeader(writer, version: 1);
        writer.Write(1);
        WriteV1Entry(writer, "skip\0ignored", -1L);

        foreach (var (term, offset) in sortedEntries)
            WriteV1Entry(writer, term, offset);

        return path;
    }

    private static void WriteV1Entry(BinaryWriter writer, string term, long offset)
    {
        var bytes = Encoding.UTF8.GetBytes(term);
        writer.Write(bytes.Length);
        writer.Write(bytes);
        writer.Write(offset);
    }
}
