using Rowles.LeanCorpus.Codecs.StoredFields;

namespace Rowles.LeanCorpus.Index.Indexer;

/// <summary>
/// Read-only view of an owned detached batch consumed by the flush path.
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
    Dictionary<string, Dictionary<int, long>> Int64Index { get; }
    Dictionary<string, Dictionary<int, ReadOnlyMemory<float>>> Vectors { get; }
    Dictionary<string, List<double>> NumericDocValues { get; }
    Dictionary<string, List<long>> Int64DocValues { get; }
    Dictionary<string, List<string?>> SortedDocValues { get; }
    Dictionary<string, Dictionary<int, List<string>>> SortedSetDocValues { get; }
    Dictionary<string, Dictionary<int, List<double>>> SortedNumericDocValues { get; }
    Dictionary<string, Dictionary<int, List<long>>> Int64SortedDocValues { get; }
    Dictionary<string, Dictionary<int, List<byte[]>>> BinaryDocValues { get; }
    HashSet<int>? ParentDocIds { get; }
    List<PostingAccumulator> PostingAccumulators { get; }
    int PostingsCount { get; }
    /// <summary>
    /// Copies (UTF-8 term bytes, accumulator) pairs into <paramref name="target"/>.
    /// The bytes are owned by the caller; implementations must not retain references.
    /// </summary>
    void CopySortedPostingsUtf8((byte[] TermUtf8, PostingAccumulator Acc)[] target);
}
