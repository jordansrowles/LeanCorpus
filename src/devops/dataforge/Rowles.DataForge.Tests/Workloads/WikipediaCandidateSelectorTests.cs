using System.Text;
using Rowles.DataForge.Tool;
using Rowles.DataForge.Workloads;
using SharpCompress.Compressors;
using SharpCompress.Compressors.BZip2;

namespace Rowles.DataForge.Tests.Workloads;

public sealed class WikipediaCandidateSelectorTests
{
    [Fact]
    public void Candidate_selection_matches_brute_force_and_is_independent_of_index_line_order()
    {
        var entries = Enumerable.Range(1, 300)
            .Select(value => new WikipediaIndexEntry(0, (ulong)value, $"Title:{value}"))
            .ToArray();
        var expected = entries.Select(static entry => new WikipediaCandidate(entry))
            .OrderBy(static candidate => candidate)
            .Take(37)
            .Select(static candidate => candidate.Entry.PageId)
            .ToArray();
        var first = WikipediaCandidateSelector.Select(new StringReader(string.Join('\n', entries.Select(Format))), 37);
        var second = WikipediaCandidateSelector.Select(new StringReader(string.Join('\n', entries.Reverse().Select(Format))), 37);

        Assert.Equal(expected, first.Candidates.Select(static candidate => candidate.Entry.PageId));
        Assert.Equal(expected, second.Candidates.Select(static candidate => candidate.Entry.PageId));
        Assert.Equal(300, first.IndexEntriesScanned);
        Assert.Equal(new long[] { 0 }, first.UniqueOffsets);
    }

    [Fact]
    public void Parses_only_the_first_two_colons_as_separators()
    {
        var entry = WikipediaCandidateSelector.ParseIndexLine("1024:987:Title: with: colons", 7);

        Assert.Equal(1024, entry.Offset);
        Assert.Equal(987UL, entry.PageId);
        Assert.Equal("Title: with: colons", entry.Title);
    }

    [Theory]
    [InlineData("x:2:title")]
    [InlineData("-1:2:title")]
    [InlineData("0:0:title")]
    [InlineData("0::title")]
    [InlineData("0:2:")]
    public void Rejects_malformed_index_lines(string line)
    {
        Assert.Throws<InvalidDataException>(() => WikipediaCandidateSelector.ParseIndexLine(line, 4));
    }

    [Fact]
    public void Rejects_decreasing_offsets_and_oversized_lines()
    {
        Assert.Throws<InvalidDataException>(() => WikipediaCandidateSelector.Select(new StringReader("10:1:a\n9:2:b"), 2));
        Assert.Throws<InvalidDataException>(() => WikipediaCandidateSelector.Select(new StringReader("0:1:" + new string('x', 70_000)), 2));
    }

    [Fact]
    public void Rejects_invalid_utf8_in_the_compressed_index()
    {
        var path = Path.Combine(Path.GetTempPath(), "dataforge-index-" + Guid.NewGuid().ToString("N") + ".bz2");
        try
        {
            File.WriteAllBytes(path, Compress([.. Encoding.ASCII.GetBytes("0:1:"), 0xC3]));

            Assert.Throws<DecoderFallbackException>(() => WikipediaReferenceSource.ReadIndex(path, 2));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private static string Format(WikipediaIndexEntry entry) => $"{entry.Offset}:{entry.PageId}:{entry.Title}";

    private static byte[] Compress(byte[] bytes)
    {
        using var output = new MemoryStream();
        using (var compressor = BZip2Stream.Create(output, CompressionMode.Compress, decompressConcatenated: false, leaveOpen: true))
            compressor.Write(bytes);
        return output.ToArray();
    }
}
