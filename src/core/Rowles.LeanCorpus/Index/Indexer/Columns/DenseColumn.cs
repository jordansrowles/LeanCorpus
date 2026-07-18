namespace Rowles.LeanCorpus.Index.Indexer.Columns;

/// <summary>
/// Dense single-valued column indexed by document ID. Replaces
/// <c>Dictionary&lt;string, List&lt;T&gt;&gt;</c> for fields like NumericDocValues.
/// </summary>
internal sealed class DenseColumn<T> where T : unmanaged
{
    private T[] _values = [];
    private int _count;

    internal int Count => _count;

    internal void Set(int docId, T value)
    {
        if (docId >= _values.Length)
        {
            int newLen = Math.Max(_values.Length * 2, docId + 1);
            if (newLen < 4) newLen = 4;
            Array.Resize(ref _values, newLen);
        }
        _values[docId] = value;
        if (docId >= _count) _count = docId + 1;
    }

    internal ReadOnlySpan<T> GetValues(int count) => _values.AsSpan(0, Math.Min(count, _count));

    internal void Clear()
    {
        _values = [];
        _count = 0;
    }
}
