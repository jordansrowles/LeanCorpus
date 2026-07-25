namespace Rowles.LeanCorpus.Index.Indexer.Columns;

/// <summary>
/// Multi-valued column where each document may have zero or more values.
/// Replaces <c>Dictionary&lt;int, List&lt;T&gt;&gt;</c> for fields
/// like SortedSetDocValues and SortedNumericDocValues.
/// </summary>
internal sealed class MultiValuedColumn<T> where T : unmanaged
{
    private int[] _docStarts = []; // offset into _values per docId
    private T[] _values = [];      // flat concatenated values
    private int _valueCount;
    private int _maxDocId;

    internal void Add(int docId, T value)
    {
        // Ensure docStarts has capacity
        if (docId >= _docStarts.Length)
        {
            int newLen = Math.Max(_docStarts.Length * 2, docId + 1);
            if (newLen < 4) newLen = 4;
            Array.Resize(ref _docStarts, newLen);
            // Fill any gap entries with -1 sentinel
            for (int i = _maxDocId + 1; i < docId; i++)
                _docStarts[i] = -1;
        }

        // Allocate starting offset if first value for this doc
        if (docId >= _maxDocId || _docStarts[docId] < 0)
        {
            _docStarts[docId] = _valueCount;
            if (docId >= _maxDocId)
                _maxDocId = docId;
        }

        // Append value
        if (_valueCount == _values.Length)
        {
            int newLen = Math.Max(_values.Length * 2, 4);
            Array.Resize(ref _values, newLen);
        }
        _values[_valueCount++] = value;
    }

    internal ReadOnlySpan<T> GetValues(int docId)
    {
        if (docId >= _docStarts.Length || _docStarts[docId] < 0)
            return ReadOnlySpan<T>.Empty;

        int start = _docStarts[docId];
        int end = docId + 1 < _docStarts.Length && _docStarts[docId + 1] >= 0
            ? _docStarts[docId + 1]
            : _valueCount;
        return _values.AsSpan(start, end - start);
    }

    /// <summary>
    /// Returns (docStarts, values, valueCount, maxDocId) for flush-writer densification.
    /// </summary>
    internal (int[] DocStarts, T[] Values, int ValueCount, int MaxDocId) GetRawData()
        => (_docStarts, _values, _valueCount, _maxDocId);

    internal void Clear()
    {
        _docStarts = [];
        _values = [];
        _valueCount = 0;
        _maxDocId = 0;
    }
}
