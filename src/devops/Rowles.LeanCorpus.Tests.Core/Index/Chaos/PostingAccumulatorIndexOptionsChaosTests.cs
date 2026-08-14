using FsCheck.Xunit;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index.Indexer;

namespace Rowles.LeanCorpus.Tests.Core.Index;
[Category(TestCategory.Chaos)]
[Area(TestArea.Index)]
public sealed class PostingAccumulatorIndexOptionsChaosTests
{
    [Property(DisplayName = "For any FieldIndexOptions, HasFreqs/HasPositions match the option")]
    public void IndexOptions_FlagsConsistent(FieldIndexOptions options, int docId, int position)
    {
        var acc = new PostingAccumulator();
        acc.Add(docId, position, options);

        bool expectedFreqs = (int)options >= (int)FieldIndexOptions.DocsAndFreqs;
        bool expectedPositions = (int)options >= (int)FieldIndexOptions.DocsAndFreqsAndPositions;

        Assert.Equal(expectedFreqs, acc.HasFreqs);
        Assert.Equal(expectedPositions, acc.HasPositions);
    }
}
