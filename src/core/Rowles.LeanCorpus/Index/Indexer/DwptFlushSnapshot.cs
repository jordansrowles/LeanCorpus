using Rowles.LeanCorpus.Codecs.StoredFields;
using Rowles.LeanCorpus.Codecs.PackedBkd.Internal;
using Rowles.LeanCorpus.Index.Indexer.Postings;

namespace Rowles.LeanCorpus.Index.Indexer;

/// <summary>
/// Detached, owned snapshot taken from a <see cref="DocumentsWriterPerThread"/> under its lock.
/// The snapshot owns every transferred buffer until <see cref="Dispose"/> is called, allowing
/// the DWPT to accept new documents while physical flush work is in progress.
/// </summary>
internal sealed class DwptFlushSnapshot : IDisposable
{
    internal required long EstimatedBytes { get; init; }
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
    internal required Dictionary<string, PackedBkdFieldBuffer> PackedBkdFields { get; init; }
    internal required Dictionary<string, List<double>> NumericDocValues { get; init; }
    internal required Dictionary<string, List<long>> Int64DocValues { get; init; }
    internal required Dictionary<string, List<string?>> SortedDocValues { get; init; }
    internal required Dictionary<string, Dictionary<int, List<string>>> SortedSetDocValues { get; init; }
    internal required Dictionary<string, Dictionary<int, List<double>>> SortedNumericDocValues { get; init; }
    internal required Dictionary<string, Dictionary<int, List<long>>> Int64SortedDocValues { get; init; }
    internal required Dictionary<string, Dictionary<int, List<byte[]>>> BinaryDocValues { get; init; }
    internal required PostingsStore Postings { get; init; }
    internal HashSet<int>? ParentDocIds { get; init; }
    internal bool PendingBytesAccounted { get; set; }
    private int _disposed;
    internal int CleanupCountForTests { get; private set; }

    /// <summary>
    /// Detaches the owned mutable state from <paramref name="dwpt"/> by swapping its
    /// collections with fresh empty instances. The caller must hold <c>lock(dwpt)</c>.
    /// After this returns, the DWPT is ready for new documents and <see cref="DocumentsWriterPerThread.ResetAfterSnapshot"/>
    /// has been called on its replaced state.
    /// </summary>
    internal static DwptFlushSnapshot CaptureFrom(DocumentsWriterPerThread dwpt)
    {
        dwpt.Postings.Freeze();
        var snapshot = new DwptFlushSnapshot
        {
            EstimatedBytes = dwpt.EstimatedRamBytes,
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
            PackedBkdFields = dwpt.PackedBkdFields,
            NumericDocValues = dwpt.NumericDocValues,
            Int64DocValues = dwpt.Int64DocValues,
            SortedDocValues = dwpt.SortedDocValues,
            SortedSetDocValues = dwpt.SortedSetDocValues,
            SortedNumericDocValues = dwpt.SortedNumericDocValues,
            Int64SortedDocValues = dwpt.Int64SortedDocValues,
            BinaryDocValues = dwpt.BinaryDocValues,
            Postings = dwpt.Postings,
            ParentDocIds = dwpt.ParentDocIds,
        };

        dwpt.ResetAfterSnapshot();

        return snapshot;
    }

    /// <summary>Returns transferred pooled buffers exactly once.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        CleanupCountForTests++;
        Postings.Dispose();
        foreach (var buffer in PackedBkdFields.Values)
            buffer.Dispose();
        PackedBkdFields.Clear();
    }
}
