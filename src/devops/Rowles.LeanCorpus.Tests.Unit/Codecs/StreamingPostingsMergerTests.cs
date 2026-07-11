using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Codecs;
using Rowles.LeanCorpus.Tests.Shared.Fixtures;
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
        TestDirectoryFixture.TryDeleteDirectory(_dir);
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
        IReadOnlyDictionary<int, int> docIdMap,
        IReadOnlyDictionary<string, float[]>? fieldNorms = null)
    {
        var posPath = Path.Combine(_dir, name + ".pos");
        var dicPath = Path.Combine(_dir, name + ".dic");
        var nrmPath = Path.Combine(_dir, name + ".nrm");
        int docCount = fieldNorms?.Values.FirstOrDefault()?.Length ?? 0;
        NormsWriter.Write(nrmPath, fieldNorms ?? new Dictionary<string, float[]>(), docCount: docCount);
        var offsets = new Dictionary<string, long>(StringComparer.Ordinal);
        var terms = postings.Keys.OrderBy(term => term, StringComparer.Ordinal).ToList();

        // Write v2 postings directly — no temp file, no envelope patching.
        using (var posOutput = new IndexOutput(posPath))
        {
            using var _scope = CodecFileHeader.BeginStreamingWrite(posOutput, CodecConstants.PostingsVersion);

            using var blockWriter = new BlockPostingsWriter(posOutput);
            foreach (var term in terms)
            {
                long headerPos = posOutput.Position;
                posOutput.WriteInt32(0);     // docFreq placeholder
                posOutput.WriteInt64(0L);    // skipOffset placeholder
                posOutput.WriteBoolean(true);
                posOutput.WriteBoolean(false);
                posOutput.WriteBoolean(false);

                blockWriter.StartTerm();
                foreach (int docId in postings[term].OrderBy(docId => docId))
                    blockWriter.AddPosting(docId, 1);

                var meta = blockWriter.FinishTerm();
                long endPos = posOutput.Position;
                posOutput.Seek(headerPos);
                posOutput.WriteInt32(meta.DocFreq);
                posOutput.WriteInt64(meta.SkipOffset);
                posOutput.Seek(endPos);
                offsets[term] = headerPos;
            }
        }

        // v2 has no envelope — offsets are already correct.
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
            NormsPath = nrmPath,
            DocIdMap = docMapArr
        };
    }

    [Fact(DisplayName = "StreamingPostingsMerger: Preserves per-document norms in skip entries")]
    public void Merge_PreservesNormsInSkipEntries()
    {
        // 130 docs creates one full 128-doc block plus a tail, so one skip entry is emitted.
        var docIds = Enumerable.Range(0, 130).ToArray();
        var norms = new byte[130];
        Array.Fill(norms, (byte)50);
        norms[0] = 200;

        var fieldNorms = new Dictionary<string, float[]>(StringComparer.Ordinal)
        {
            ["body"] = norms.Select(n => n / 255f).ToArray()
        };
        var postings = new Dictionary<string, int[]>(StringComparer.Ordinal)
        {
            ["body\0term"] = docIds
        };
        var source = WriteSource("norms", postings, docIds.ToDictionary(id => id), fieldNorms);

        var outPos = Path.Combine(_dir, "merged_norms.pos");
        var outDic = Path.Combine(_dir, "merged_norms.dic");
        var result = StreamingPostingsMerger.Merge([source], outPos, outDic);

        Assert.Equal(["body\0term"], result.SortedTerms);

        using var input = new IndexInput(outPos);
        _ = PostingsEnum.ValidateFileHeader(input);
        using var dic = TermDictionaryReader.Open(outDic);
        long offset = dic.EnumerateAllTerms().Single(t => t.Term == "body\0term").Offset;

        input.Seek(offset);
        int docFreq = input.ReadInt32();
        long skipOffset = input.ReadInt64();
        input.ReadBoolean(); // hasFreqs
        input.ReadBoolean(); // hasPositions
        input.ReadBoolean(); // hasPayloads
        long docStart = input.Position;

        var enumv = BlockPostingsEnum.Create(input, docStart, skipOffset, docFreq);
        Assert.Equal(1, enumv.SkipEntries.Length);
        Assert.Equal((byte)200, enumv.SkipEntries[0].MaxNormInBlock);
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
