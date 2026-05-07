using Rowles.LeanLucene.Codecs;
using Rowles.LeanLucene.Codecs.Postings;
using Rowles.LeanLucene.Codecs.TermDictionary;
using Rowles.LeanLucene.Store;

namespace Rowles.LeanLucene.Tests.Codecs;

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

        using (var output = new IndexOutput(posPath))
        {
            CodecConstants.WriteHeader(output, CodecConstants.PostingsVersion);
            using var blockWriter = new BlockPostingsWriter(output);
            foreach (var term in terms)
            {
                long headerPos = output.Position;
                output.WriteInt32(0);
                output.WriteInt64(0L);
                output.WriteBoolean(true);
                output.WriteBoolean(false);
                output.WriteBoolean(false);

                blockWriter.StartTerm();
                foreach (int docId in postings[term].OrderBy(docId => docId))
                    blockWriter.AddPosting(docId, 1);

                var meta = blockWriter.FinishTerm();
                long endPos = output.Position;
                output.Seek(headerPos);
                output.WriteInt32(meta.DocFreq);
                output.WriteInt64(meta.SkipOffset);
                output.Seek(endPos);
                offsets[term] = headerPos;
            }
        }

        TermDictionaryWriter.Write(dicPath, terms, offsets);
        return new StreamingPostingsMerger.Source
        {
            DicPath = dicPath,
            PosPath = posPath,
            DocIdMap = docIdMap
        };
    }

    private static int[] ReadDocIds(string dicPath, string posPath, string term)
    {
        using var dic = TermDictionaryReader.Open(dicPath);
        if (!dic.TryGetPostingsOffset(term, out long offset))
            return [];

        using var input = new IndexInput(posPath);
        byte version = PostingsEnum.ValidateFileHeader(input);
        var postings = PostingsEnum.Create(input, offset, version);
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
