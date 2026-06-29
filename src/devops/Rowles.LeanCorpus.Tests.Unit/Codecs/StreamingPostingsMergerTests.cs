using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Codecs.CodecKit.Formats;
using Rowles.LeanCorpus.Codecs.Postings;
using Rowles.LeanCorpus.Codecs.TermDictionary;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Tests.Unit.Codecs;

/// <summary>
/// Gap coverage for <see cref="StreamingPostingsMerger"/> using real v3
/// postings and term dictionary files.
/// </summary>
[Trait("Category", "Codecs")]
[Trait("Category", "UnitTest")]
public sealed class StreamingPostingsMergerTests : IDisposable
{
    private readonly string _dir;

    public StreamingPostingsMergerTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "ll_spm_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    [Fact(DisplayName = "StreamingPostingsMerger: Single Source Copies Terms And Offsets")]
    public void Merge_SingleSource_CopiesTermsAndOffsets()
    {
        var source = WriteSource("s1", new Dictionary<string, int[]>(StringComparer.Ordinal)
        {
            ["body\0alpha"] = [0, 2],
            ["body\0beta"] = [1]
        }, new Dictionary<int, int>
        {
            [0] = 10,
            [1] = 11,
            [2] = 12
        });
        var outPos = Path.Combine(_dir, "merged.pos");
        var outDic = Path.Combine(_dir, "merged.dic");

        var result = StreamingPostingsMerger.Merge([source], outPos, outDic);

        Assert.Equal(["body\0alpha", "body\0beta"], result.SortedTerms);
        Assert.Equal([10, 12], ReadDocIds(outDic, outPos, "body\0alpha"));
        Assert.Equal([11], ReadDocIds(outDic, outPos, "body\0beta"));
    }

    [Fact(DisplayName = "StreamingPostingsMerger: Two Sources Remap And Sort Terms")]
    public void Merge_TwoSources_RemapAndSortTerms()
    {
        var source1 = WriteSource("s1", new Dictionary<string, int[]>(StringComparer.Ordinal)
        {
            ["body\0alpha"] = [0, 1],
            ["body\0gamma"] = [2]
        }, new Dictionary<int, int>
        {
            [0] = 0,
            [1] = 2,
            [2] = 3
        });
        var source2 = WriteSource("s2", new Dictionary<string, int[]>(StringComparer.Ordinal)
        {
            ["body\0alpha"] = [0],
            ["body\0beta"] = [1]
        }, new Dictionary<int, int>
        {
            [0] = 4,
            [1] = 5
        });
        var outPos = Path.Combine(_dir, "merged_two.pos");
        var outDic = Path.Combine(_dir, "merged_two.dic");

        var result = StreamingPostingsMerger.Merge([source1, source2], outPos, outDic);

        Assert.Equal(["body\0alpha", "body\0beta", "body\0gamma"], result.SortedTerms);
        Assert.Equal([0, 2, 4], ReadDocIds(outDic, outPos, "body\0alpha"));
        Assert.Equal([5], ReadDocIds(outDic, outPos, "body\0beta"));
        Assert.Equal([3], ReadDocIds(outDic, outPos, "body\0gamma"));
    }

    [Fact(DisplayName = "StreamingPostingsMerger: Drops Unmapped Docs And Empty Terms")]
    public void Merge_DropsUnmappedDocsAndEmptyTerms()
    {
        var source = WriteSource("s1", new Dictionary<string, int[]>(StringComparer.Ordinal)
        {
            ["body\0kept"] = [0, 1],
            ["body\0dropped"] = [2]
        }, new Dictionary<int, int>
        {
            [1] = 7
        });
        var outPos = Path.Combine(_dir, "merged_drop.pos");
        var outDic = Path.Combine(_dir, "merged_drop.dic");

        var result = StreamingPostingsMerger.Merge([source], outPos, outDic);

        Assert.Equal(["body\0kept"], result.SortedTerms);
        Assert.Equal([7], ReadDocIds(outDic, outPos, "body\0kept"));
        Assert.Empty(ReadDocIds(outDic, outPos, "body\0dropped"));
    }

    private StreamingPostingsMerger.Source WriteSource(
        string name,
        Dictionary<string, int[]> postings,
        IReadOnlyDictionary<int, int> docIdMap)
    {
        var posPath = Path.Combine(_dir, name + ".pos");
        var dicPath = Path.Combine(_dir, name + ".dic");
        var offsets = new Dictionary<string, long>(StringComparer.Ordinal);
        var terms = postings.Keys.OrderBy(term => term, StringComparer.Ordinal).ToList();
        // Build body into a temporary file first, then write the final file
        // with a CodecKit header.
        var bodyPath = Path.Combine(_dir, name + ".tmp");
        byte[] body;
        using (var bodyOut = new IndexOutput(bodyPath))
        {
            using var blockWriter = new BlockPostingsWriter(bodyOut);
            foreach (var term in terms)
            {
                long headerPos = bodyOut.Position;
                bodyOut.WriteInt32(0);
                bodyOut.WriteInt64(0L);
                bodyOut.WriteBoolean(true);
                bodyOut.WriteBoolean(false);
                bodyOut.WriteBoolean(false);

                blockWriter.StartTerm();
                foreach (int docId in postings[term].OrderBy(docId => docId))
                    blockWriter.AddPosting(docId, 1);

                var meta = blockWriter.FinishTerm();
                long endPos = bodyOut.Position;
                bodyOut.Seek(headerPos);
                bodyOut.WriteInt32(meta.DocFreq);
                bodyOut.WriteInt64(meta.SkipOffset);
                bodyOut.Seek(endPos);
                offsets[term] = headerPos;
            }
        }
        body = File.ReadAllBytes(bodyPath);
        File.Delete(bodyPath);

        // Adjust skipOffset values inside body for CodecKit envelope
        int headerSize = 1 + VarInt64Size(body.Length);
        foreach (var term in terms)
        {
            int termBodyOffset = (int)offsets[term];
            // skipOffset is at termBodyOffset + 4 (after docFreq Int32)
            long skipOffset = BitConverter.ToInt64(body, termBodyOffset + 4);
            byte[] patched = BitConverter.GetBytes(skipOffset + headerSize);
            patched.CopyTo(body, termBodyOffset + 4);
        }

        // Write final .pos file with CodecKit header
        using (var output = new IndexOutput(posPath))
            CodecFileHeader.Write(output, CodecFormats.Postings, body);

        // Adjust offsets to account for the CodecKit header
        foreach (var term in terms)
            offsets[term] += headerSize;

        TermDictionaryWriter.Write(dicPath, terms, offsets);
        int maxOldId = -1;
        foreach (var k in docIdMap.Keys) if (k > maxOldId) maxOldId = k;
        var docMapArr = new int[maxOldId + 1];
        for (int i = 0; i < docMapArr.Length; i++) docMapArr[i] = -1;
        foreach (var kv in docIdMap) docMapArr[kv.Key] = kv.Value;
        return new StreamingPostingsMerger.Source
        {
            DicPath = dicPath,
            PosPath = posPath,
            DocIdMap = docMapArr
        };
    }

    private static int VarInt64Size(long value)
    {
        int size = 0;
        do { size++; value >>= 7; } while (value != 0);
        return size;
    }

    private static int[] ReadDocIds(string dicPath, string posPath, string term)
    {
        using var dic = TermDictionaryReader.Open(dicPath);
        if (!dic.TryGetPostingsOffset(term, out long offset))
            return [];

        using var input = new IndexInput(posPath);
        var postings = PostingsEnum.Create(input, offset);
        try
        {
            var docIds = new List<int>();
            while (postings.MoveNext())
                docIds.Add(postings.DocId);
            return [.. docIds];
        }
        finally
        {
            postings.Dispose();
        }
    }
}
