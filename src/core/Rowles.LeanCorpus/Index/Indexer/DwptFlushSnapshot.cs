using Rowles.LeanCorpus.Codecs.StoredFields;

namespace Rowles.LeanCorpus.Index.Indexer;

/// <summary>
/// Immutable snapshot of a <see cref="DocumentsWriterPerThread"/> taken under its lock.
/// Owns all captured mutable collections (swapped out of the DWPT), so the DWPT can be
/// reused for new documents while the snapshot is flushed independently.
/// </summary>
internal sealed class DwptFlushSnapshot
{
    internal required int DocCount { get; init; }
    internal required HashSet<string> FieldNames { get; init; }
    internal required Dictionary<string, int[]> DocTokenCounts { get; init; }
    internal required Dictionary<string, Dictionary<int, float>> FieldBoosts { get; init; }
    internal required List<int> StoredDocStarts { get; init; }
    internal required List<int> StoredFieldIds { get; init; }
    internal required List<StoredFieldValue> StoredValues { get; init; }
    internal required List<string> StoredFieldIdToName { get; init; }
    internal required Dictionary<string, Dictionary<int, double>> NumericIndex { get; init; }
    internal required Dictionary<string, Dictionary<int, long>> Int64Index { get; init; }
    internal required Dictionary<string, Dictionary<int, ReadOnlyMemory<float>>> Vectors { get; init; }
    internal required Dictionary<string, List<double>> NumericDocValues { get; init; }
    internal required Dictionary<string, List<long>> Int64DocValues { get; init; }
    internal required Dictionary<string, List<string?>> SortedDocValues { get; init; }
    internal required Dictionary<string, Dictionary<int, List<string>>> SortedSetDocValues { get; init; }
    internal required Dictionary<string, Dictionary<int, List<double>>> SortedNumericDocValues { get; init; }
    internal required Dictionary<string, Dictionary<int, List<long>>> Int64SortedDocValues { get; init; }
    internal required Dictionary<string, Dictionary<int, List<byte[]>>> BinaryDocValues { get; init; }
    internal required BytesRefHash TermHash { get; init; }
    internal required List<PostingAccumulator> PostingAccumulators { get; init; }
    internal HashSet<int>? ParentDocIds { get; init; }

    /// <summary>
    /// Captures an immutable snapshot of <paramref name="dwpt"/> by swapping its mutable
    /// collections with fresh empty instances. The caller must hold <c>lock(dwpt)</c>.
    /// After this returns, the DWPT is ready for new documents and <see cref="DocumentsWriterPerThread.ClearAll"/>
    /// has been called on its replaced state.
    /// </summary>
    internal static DwptFlushSnapshot CaptureFrom(DocumentsWriterPerThread dwpt)
    {
        var snapshot = new DwptFlushSnapshot
        {
            DocCount = dwpt.DocCount,
            FieldNames = dwpt.FieldNames,
            DocTokenCounts = dwpt.DocTokenCounts,
            FieldBoosts = dwpt.FieldBoosts,
            StoredDocStarts = dwpt.StoredDocStarts,
            StoredFieldIds = dwpt.StoredFieldIds,
            StoredValues = dwpt.StoredValues,
            StoredFieldIdToName = new List<string>(dwpt.StoredFieldIdToName),
            NumericIndex = dwpt.NumericIndex,
            Int64Index = dwpt.Int64Index,
            Vectors = dwpt.Vectors,
            NumericDocValues = dwpt.NumericDocValues,
            Int64DocValues = dwpt.Int64DocValues,
            SortedDocValues = dwpt.SortedDocValues,
            SortedSetDocValues = dwpt.SortedSetDocValues,
            SortedNumericDocValues = dwpt.SortedNumericDocValues,
            Int64SortedDocValues = dwpt.Int64SortedDocValues,
            BinaryDocValues = dwpt.BinaryDocValues,
            TermHash = dwpt.TermHash,
            PostingAccumulators = dwpt.PostingAccumulators,
            ParentDocIds = dwpt.ParentDocIds,
        };

        dwpt.ResetAfterSnapshot();

        return snapshot;
    }

    /// <summary>
    /// Enumerates (qualified term string, posting accumulator) pairs for term vector writing.
    /// </summary>
    internal IEnumerable<(string Term, PostingAccumulator Acc)> EnumeratePostings()
    {
        for (int i = 0; i < TermHash.Count; i++)
            yield return (TermHash.GetTermString(i), PostingAccumulators[i]);
    }

    /// <summary>Applies the configured physical document order before detached flush.</summary>
    internal void ApplyIndexSort(IndexSort sort)
    {
        ArgumentNullException.ThrowIfNull(sort);
        int[] permutation = ComputeSortPermutation(sort);
        int[] inverse = new int[DocCount];
        for (int newDocId = 0; newDocId < DocCount; newDocId++)
            inverse[permutation[newDocId]] = newDocId;

        foreach (var accumulator in PostingAccumulators)
            accumulator.RemapDocIds(inverse);

        RemapStoredFields(permutation);
        RemapDenseArrays(DocTokenCounts, permutation);
        RemapSparse(FieldBoosts, inverse);
        RemapDenseLists(NumericDocValues, permutation, 0d);
        RemapDenseLists(Int64DocValues, permutation, 0L);
        RemapDenseLists(SortedDocValues, permutation, null);
        RemapSparse(SortedSetDocValues, inverse);
        RemapSparse(SortedNumericDocValues, inverse);
        RemapSparse(Int64SortedDocValues, inverse);
        RemapSparse(BinaryDocValues, inverse);
        RemapSparse(NumericIndex, inverse);
        RemapSparse(Int64Index, inverse);
        RemapSparse(Vectors, inverse);

        if (ParentDocIds is not null)
        {
            var remappedParents = new HashSet<int>();
            foreach (int oldDocId in ParentDocIds)
                if ((uint)oldDocId < (uint)inverse.Length)
                    remappedParents.Add(inverse[oldDocId]);
            ParentDocIds.Clear();
            ParentDocIds.UnionWith(remappedParents);
        }
    }

    private int[] ComputeSortPermutation(IndexSort sort)
    {
        int[] permutation = Enumerable.Range(0, DocCount).ToArray();
        int fieldCount = sort.Fields.Count;
        var numericKeys = new double[fieldCount][];
        var stringKeys = new string?[fieldCount][];
        var sortTypes = new SortFieldType[fieldCount];
        var descending = new bool[fieldCount];

        for (int fieldIndex = 0; fieldIndex < fieldCount; fieldIndex++)
        {
            var field = sort.Fields[fieldIndex];
            sortTypes[fieldIndex] = field.Type;
            descending[fieldIndex] = field.Descending;
            if (field.Type == SortFieldType.Numeric)
            {
                var values = new double[DocCount];
                if (NumericDocValues.TryGetValue(field.FieldName, out var source))
                    for (int docId = 0; docId < Math.Min(DocCount, source.Count); docId++)
                        values[docId] = source[docId];
                numericKeys[fieldIndex] = values;
            }
            else if (field.Type == SortFieldType.String)
            {
                var values = new string?[DocCount];
                if (SortedDocValues.TryGetValue(field.FieldName, out var source))
                    for (int docId = 0; docId < Math.Min(DocCount, source.Count); docId++)
                        values[docId] = source[docId];
                stringKeys[fieldIndex] = values;
            }
        }

        Array.Sort(permutation, (left, right) =>
        {
            for (int fieldIndex = 0; fieldIndex < fieldCount; fieldIndex++)
            {
                int comparison = sortTypes[fieldIndex] switch
                {
                    SortFieldType.Numeric =>
                        numericKeys[fieldIndex][left].CompareTo(numericKeys[fieldIndex][right]),
                    SortFieldType.String =>
                        string.Compare(
                            stringKeys[fieldIndex][left],
                            stringKeys[fieldIndex][right],
                            StringComparison.Ordinal),
                    SortFieldType.DocId => left.CompareTo(right),
                    _ => 0,
                };
                if (descending[fieldIndex])
                    comparison = -comparison;
                if (comparison != 0)
                    return comparison;
            }
            return left.CompareTo(right);
        });

        return permutation;
    }

    private void RemapStoredFields(int[] permutation)
    {
        var fieldIds = new List<int>(StoredFieldIds.Count);
        var values = new List<StoredFieldValue>(StoredValues.Count);
        var starts = new List<int>(DocCount);
        for (int newDocId = 0; newDocId < DocCount; newDocId++)
        {
            int oldDocId = permutation[newDocId];
            starts.Add(fieldIds.Count);
            int start = StoredDocStarts[oldDocId];
            int end = oldDocId + 1 < StoredDocStarts.Count
                ? StoredDocStarts[oldDocId + 1]
                : StoredFieldIds.Count;
            for (int index = start; index < end; index++)
            {
                fieldIds.Add(StoredFieldIds[index]);
                values.Add(StoredValues[index]);
            }
        }
        StoredDocStarts.Clear();
        StoredDocStarts.AddRange(starts);
        StoredFieldIds.Clear();
        StoredFieldIds.AddRange(fieldIds);
        StoredValues.Clear();
        StoredValues.AddRange(values);
    }

    private static void RemapDenseArrays(
        Dictionary<string, int[]> fields,
        int[] permutation)
    {
        foreach (var (field, source) in fields)
        {
            var remapped = new int[permutation.Length];
            for (int newDocId = 0; newDocId < permutation.Length; newDocId++)
            {
                int oldDocId = permutation[newDocId];
                if ((uint)oldDocId < (uint)source.Length)
                    remapped[newDocId] = source[oldDocId];
            }
            fields[field] = remapped;
        }
    }

    private static void RemapDenseLists<T>(
        Dictionary<string, List<T>> fields,
        int[] permutation,
        T missing)
    {
        foreach (var (field, source) in fields)
        {
            var remapped = new List<T>(permutation.Length);
            foreach (int oldDocId in permutation)
                remapped.Add(oldDocId < source.Count ? source[oldDocId] : missing);
            fields[field] = remapped;
        }
    }

    private static void RemapSparse<T>(
        Dictionary<string, Dictionary<int, T>> fields,
        int[] inverse)
    {
        foreach (var (field, source) in fields)
        {
            var remapped = new Dictionary<int, T>(source.Count);
            foreach (var (oldDocId, value) in source)
                if ((uint)oldDocId < (uint)inverse.Length)
                    remapped[inverse[oldDocId]] = value;
            fields[field] = remapped;
        }
    }
}
