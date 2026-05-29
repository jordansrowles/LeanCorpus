using Rowles.LeanCorpus.Codecs;
using Rowles.LeanCorpus.Codecs.TermDictionary;
using System.Text;

namespace Rowles.LeanCorpus.Tests.Unit.Codecs;

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

    [Fact(DisplayName = "TermDictionaryReader: V1 Dictionary Is Rejected With Migrate Hint")]
    public void V1Dictionary_IsRejectedWithMigrateHint()
    {
        var path = WriteV1Dictionary(
            "v1_rejected",
            ("body\0alpha", 11L),
            ("body\0beta", 22L));

        var ex = Assert.Throws<InvalidDataException>(() => TermDictionaryReader.Open(path));
        Assert.Contains("migrate", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(DisplayName = "TermDictionaryReader: V2 Dictionary Is Rejected With Migrate Hint")]
    public void V2Dictionary_IsRejectedWithMigrateHint()
    {
        var path = Path.Combine(_dir, "v2_rejected.dic");
        using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write))
        using (var writer = new BinaryWriter(fs))
        {
            CodecConstants.WriteHeader(writer, version: 2);
            writer.Write(0);
            writer.Write(0);
        }

        var ex = Assert.Throws<InvalidDataException>(() => TermDictionaryReader.Open(path));
        Assert.Contains("migrate", ex.Message, StringComparison.OrdinalIgnoreCase);
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
