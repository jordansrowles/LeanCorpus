using Rowles.LeanCorpus.Codecs.StoredFields;

namespace Rowles.LeanCorpus.Index.Indexer;

/// <summary>
/// Read-only view of a document buffer consumed by the shared flush path.
/// Implemented by wrapper structs over <see cref="DocumentBufferState"/>
/// and <see cref="DocumentsWriterPerThread"/> so the shared flush path writes
/// segments without knowing which buffer type produced the data.
/// </summary>
internal interface IFlushSource
{
    int DocCount { get; }
    HashSet<string> FieldNames { get; }
    Dictionary<string, int[]> DocTokenCounts { get; }
    Dictionary<string, Dictionary<int, float>> FieldBoosts { get; }
    List<int> StoredDocStarts { get; }
    List<int> StoredFieldIds { get; }
    List<StoredFieldValue> StoredFieldValues { get; }
    List<string> StoredFieldIdToName { get; }
    Dictionary<string, Dictionary<int, double>> NumericIndex { get; }
    Dictionary<string, Dictionary<int, ReadOnlyMemory<float>>> Vectors { get; }
    Dictionary<string, List<double>> NumericDocValues { get; }
    Dictionary<string, List<string?>> SortedDocValues { get; }
    Dictionary<string, Dictionary<int, List<string>>> SortedSetDocValues { get; }
    Dictionary<string, Dictionary<int, List<double>>> SortedNumericDocValues { get; }
    Dictionary<string, Dictionary<int, List<byte[]>>> BinaryDocValues { get; }
    int PostingsCount { get; }
    void CopySortedPostings((string Term, PostingAccumulator Acc)[] target);
}

/// <summary>
/// Wraps <see cref="DocumentBufferState"/> for the shared flush path.
/// </summary>
internal readonly struct BufferFlushSource : IFlushSource
{
    private readonly DocumentBufferState _b;

    public BufferFlushSource(DocumentBufferState buffer) => _b = buffer;

    public int DocCount => _b.DocCount;
    public HashSet<string> FieldNames => _b.FieldNames;
    public Dictionary<string, int[]> DocTokenCounts => _b.DocTokenCounts;
    public Dictionary<string, Dictionary<int, float>> FieldBoosts => _b.FieldBoosts;
    public List<int> StoredDocStarts => _b.StoredDocStarts;
    public List<int> StoredFieldIds => _b.StoredFieldIds;
    public List<StoredFieldValue> StoredFieldValues => _b.StoredFieldValues;
    public List<string> StoredFieldIdToName => _b.StoredFieldIdToName;
    public Dictionary<string, Dictionary<int, double>> NumericIndex => _b.NumericIndex;
    public Dictionary<string, Dictionary<int, ReadOnlyMemory<float>>> Vectors => _b.Vectors;
    public Dictionary<string, List<double>> NumericDocValues => _b.NumericDocValues;
    public Dictionary<string, List<string?>> SortedDocValues => _b.SortedDocValues;
    public Dictionary<string, Dictionary<int, List<string>>> SortedSetDocValues => _b.SortedSetDocValues;
    public Dictionary<string, Dictionary<int, List<double>>> SortedNumericDocValues => _b.SortedNumericDocValues;
    public Dictionary<string, Dictionary<int, List<byte[]>>> BinaryDocValues => _b.BinaryDocValues;
    public int PostingsCount => _b.PostingsCount;

    public void CopySortedPostings((string Term, PostingAccumulator Acc)[] target)
    {
        for (int i = 0; i < target.Length; i++)
            target[i] = (_b.GetTermString(i), _b.PostingAccumulators[i]);
    }
}

/// <summary>
/// Wraps <see cref="DocumentsWriterPerThread"/> for the shared flush path.
/// </summary>
internal readonly struct DwptFlushSource : IFlushSource
{
    private readonly DocumentsWriterPerThread _d;

    public DwptFlushSource(DocumentsWriterPerThread dwpt) => _d = dwpt;

    public int DocCount => _d.DocCount;
    public HashSet<string> FieldNames => _d.FieldNames;
    public Dictionary<string, int[]> DocTokenCounts => _d.DocTokenCounts;
    public Dictionary<string, Dictionary<int, float>> FieldBoosts => _d.FieldBoosts;
    public List<int> StoredDocStarts => _d.StoredDocStarts;
    public List<int> StoredFieldIds => _d.StoredFieldIds;
    public List<StoredFieldValue> StoredFieldValues => _d.StoredValues;
    public List<string> StoredFieldIdToName => _d.StoredFieldIdToName;
    public Dictionary<string, Dictionary<int, double>> NumericIndex => _d.NumericIndex;
    public Dictionary<string, Dictionary<int, ReadOnlyMemory<float>>> Vectors => _d.Vectors;
    public Dictionary<string, List<double>> NumericDocValues => _d.NumericDocValues;
    public Dictionary<string, List<string?>> SortedDocValues => _d.SortedDocValues;
    public Dictionary<string, Dictionary<int, List<string>>> SortedSetDocValues => _d.SortedSetDocValues;
    public Dictionary<string, Dictionary<int, List<double>>> SortedNumericDocValues => _d.SortedNumericDocValues;
    public Dictionary<string, Dictionary<int, List<byte[]>>> BinaryDocValues => _d.BinaryDocValues;
    public int PostingsCount => _d.Postings.Count;

    public void CopySortedPostings((string Term, PostingAccumulator Acc)[] target)
    {
        int idx = 0;
        foreach (var (term, acc) in _d.Postings)
            target[idx++] = (term, acc);
    }
}
