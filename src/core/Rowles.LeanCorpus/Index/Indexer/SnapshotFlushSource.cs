using Rowles.LeanCorpus.Codecs.StoredFields;

namespace Rowles.LeanCorpus.Index.Indexer;

/// <summary>
/// Implements <see cref="IFlushSource"/> over a <see cref="DwptFlushSnapshot"/>,
/// allowing the shared flush path (<see cref="SegmentFlusher.FlushCore"/>) to read
/// from an immutable snapshot rather than a live <see cref="DocumentsWriterPerThread"/>.
/// </summary>
internal sealed class SnapshotFlushSource : IFlushSource
{
    private readonly DwptFlushSnapshot _s;

    public SnapshotFlushSource(DwptFlushSnapshot snapshot) => _s = snapshot;

    public int DocCount => _s.DocCount;
    public HashSet<string> FieldNames => _s.FieldNames;
    public Dictionary<string, int[]> DocTokenCounts => _s.DocTokenCounts;
    public Dictionary<string, Dictionary<int, float>> FieldBoosts => _s.FieldBoosts;
    public List<int> StoredDocStarts => _s.StoredDocStarts;
    public List<int> StoredFieldIds => _s.StoredFieldIds;
    public List<StoredFieldValue> StoredFieldValues => _s.StoredValues;
    public List<string> StoredFieldIdToName => _s.StoredFieldIdToName;
    public Dictionary<string, Dictionary<int, double>> NumericIndex => _s.NumericIndex;
    public Dictionary<string, Dictionary<int, long>> Int64Index => _s.Int64Index;
    public Dictionary<string, Dictionary<int, ReadOnlyMemory<float>>> Vectors => _s.Vectors;
    public Dictionary<string, List<double>> NumericDocValues => _s.NumericDocValues;
    public Dictionary<string, List<long>> Int64DocValues => _s.Int64DocValues;
    public Dictionary<string, List<string?>> SortedDocValues => _s.SortedDocValues;
    public Dictionary<string, Dictionary<int, List<string>>> SortedSetDocValues => _s.SortedSetDocValues;
    public Dictionary<string, Dictionary<int, List<double>>> SortedNumericDocValues => _s.SortedNumericDocValues;
    public Dictionary<string, Dictionary<int, List<long>>> Int64SortedDocValues => _s.Int64SortedDocValues;
    public Dictionary<string, Dictionary<int, List<byte[]>>> BinaryDocValues => _s.BinaryDocValues;
    public int PostingsCount => _s.TermHash.Count;

    public void CopySortedPostings((string Term, PostingAccumulator Acc)[] target)
    {
        int idx = 0;
        for (int i = 0; i < _s.TermHash.Count; i++)
            target[idx++] = (_s.TermHash.GetTermString(i), _s.PostingAccumulators[i]);
    }

    public void CopySortedPostingsUtf8((byte[] TermUtf8, PostingAccumulator Acc)[] target)
    {
        int idx = 0;
        for (int i = 0; i < _s.TermHash.Count; i++)
            target[idx++] = (_s.TermHash.GetTerm(i).ToArray(), _s.PostingAccumulators[i]);
    }
}
