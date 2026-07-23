namespace Rowles.LeanCorpus.Index.Indexer.Columns;

/// <summary>
/// Sparse single-valued column. Replaces <c>Dictionary&lt;int, T&gt;</c>
/// for fields like NumericIndex and Vectors.
/// </summary>
internal sealed class SparseColumn<T>
{
    private int[] _docIds = [];
    private T[] _values = [];
    private int _count;

    internal int Count => _count;

    internal void Set(int docId, T value)
    {
        if (_count == _docIds.Length)
        {
            int newLen = Math.Max(_docIds.Length * 2, 4);
            Array.Resize(ref _docIds, newLen);
            Array.Resize(ref _values, newLen);
        }
        _docIds[_count] = docId;
        _values[_count] = value;
        _count++;
    }

    internal (int[] DocIds, T[] Values) GetEntries() => (_docIds, _values);

    internal int GetCount() => _count;

    internal void Clear()
    {
        _docIds = [];
        _values = [];
        _count = 0;
    }
}
