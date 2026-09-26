using System.Collections;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Util;

namespace Rowles.LeanCorpus.Codecs.DocValues;

/// <summary>Immutable, document-addressable view over one packed numeric DocValues field.</summary>
internal sealed class NumericDocValuesColumn
{
    private readonly IndexInput _input;
    private readonly long _minimumBits;
    private readonly int _bitsPerValue;
    private readonly long _packedDataOffset;

    internal NumericDocValuesColumn(
        IndexInput input,
        int documentCount,
        long minimumBits,
        int bitsPerValue,
        long packedDataOffset,
        RoaringBitmap? presence)
    {
        _input = input;
        DocumentCount = documentCount;
        _minimumBits = minimumBits;
        _bitsPerValue = bitsPerValue;
        _packedDataOffset = packedDataOffset;
        Presence = presence;
    }

    internal int DocumentCount { get; }
    internal RoaringBitmap? Presence { get; }

    internal bool TryGetValue(int documentId, out double value)
    {
        if ((uint)documentId >= (uint)DocumentCount || Presence is not null && !Presence.Contains(documentId))
        {
            value = default;
            return false;
        }

        value = GetValue(documentId);
        return true;
    }

    internal double GetValue(int documentId)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(documentId);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(documentId, DocumentCount);
        ulong delta = _bitsPerValue == 0
            ? 0
            : _input.ReadPackedUnsigned(_packedDataOffset, documentId, _bitsPerValue);
        long bits = unchecked((long)((ulong)_minimumBits + delta));
        return BitConverter.Int64BitsToDouble(bits);
    }

    internal double[] Materialise()
    {
        var values = new double[DocumentCount];
        for (int documentId = 0; documentId < values.Length; documentId++)
            values[documentId] = GetValue(documentId);
        return values;
    }
}

/// <summary>Immutable, document-addressable view over one packed Int64 DocValues field.</summary>
internal sealed class Int64DocValuesColumn
{
    private readonly IndexInput _input;
    private readonly long _minimum;
    private readonly int _bitsPerValue;
    private readonly long _packedDataOffset;

    internal Int64DocValuesColumn(
        IndexInput input,
        int documentCount,
        long minimum,
        int bitsPerValue,
        long packedDataOffset,
        RoaringBitmap? presence)
    {
        _input = input;
        DocumentCount = documentCount;
        _minimum = minimum;
        _bitsPerValue = bitsPerValue;
        _packedDataOffset = packedDataOffset;
        Presence = presence;
    }

    internal int DocumentCount { get; }
    internal RoaringBitmap? Presence { get; }

    internal bool TryGetValue(int documentId, out long value)
    {
        if ((uint)documentId >= (uint)DocumentCount || Presence is not null && !Presence.Contains(documentId))
        {
            value = default;
            return false;
        }

        value = GetValue(documentId);
        return true;
    }

    internal long GetValue(int documentId)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(documentId);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(documentId, DocumentCount);
        ulong delta = _bitsPerValue == 0
            ? 0
            : _input.ReadPackedUnsigned(_packedDataOffset, documentId, _bitsPerValue);
        return unchecked((long)((ulong)_minimum + delta));
    }

    internal long[] Materialise()
    {
        var values = new long[DocumentCount];
        for (int documentId = 0; documentId < values.Length; documentId++)
            values[documentId] = GetValue(documentId);
        return values;
    }
}

/// <summary>Immutable sorted DocValues column retaining packed local ordinals.</summary>
internal sealed class SortedDocValuesColumn
{
    private readonly IndexInput _input;
    private readonly string[] _terms;
    private readonly int _bitsPerOrdinal;
    private readonly long _packedDataOffset;

    internal SortedDocValuesColumn(
        IndexInput input,
        int documentCount,
        string[] terms,
        int bitsPerOrdinal,
        long packedDataOffset,
        RoaringBitmap? presence)
    {
        _input = input;
        DocumentCount = documentCount;
        _terms = terms;
        _bitsPerOrdinal = bitsPerOrdinal;
        _packedDataOffset = packedDataOffset;
        Presence = presence;
    }

    internal int DocumentCount { get; }
    internal int ValueCount => _terms.Length;
    internal RoaringBitmap? Presence { get; }

    internal bool TryGetOrdinal(int documentId, out int ordinal)
    {
        if ((uint)documentId >= (uint)DocumentCount || Presence is not null && !Presence.Contains(documentId))
        {
            ordinal = -1;
            return false;
        }

        ordinal = GetOrdinal(documentId);
        return true;
    }

    internal int GetOrdinal(int documentId)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(documentId);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(documentId, DocumentCount);
        if (_bitsPerOrdinal == 0)
            return 0;

        int ordinal = checked((int)_input.ReadPackedUnsigned(
            _packedDataOffset, documentId, _bitsPerOrdinal));
        if ((uint)ordinal >= (uint)_terms.Length)
            throw new InvalidDataException($"Sorted DocValues ordinal {ordinal} is outside its {_terms.Length}-term dictionary.");
        return ordinal;
    }

    internal string GetValue(int documentId)
    {
        int ordinal = GetOrdinal(documentId);
        return (uint)ordinal < (uint)_terms.Length ? _terms[ordinal] : string.Empty;
    }

    internal string[] CopyTerms() => (string[])_terms.Clone();

    internal string[] Materialise()
    {
        var values = new string[DocumentCount];
        for (int documentId = 0; documentId < values.Length; documentId++)
            values[documentId] = GetValue(documentId);
        return values;
    }
}

/// <summary>Immutable sorted-set column with one term table and a flat local-ordinal vector.</summary>
internal sealed class SortedSetDocValuesColumn
{
    private readonly string[] _terms;
    private readonly int[] _documentStarts;
    private readonly int[] _ordinals;

    internal SortedSetDocValuesColumn(string[] terms, int[] documentStarts, int[] ordinals)
    {
        _terms = terms;
        _documentStarts = documentStarts;
        _ordinals = ordinals;
    }

    internal int DocumentCount => _documentStarts.Length - 1;
    internal int ValueCount => _terms.Length;
    internal bool HasValues(int documentId)
        => (uint)documentId < (uint)DocumentCount
            && _documentStarts[documentId] < _documentStarts[documentId + 1];

    internal IReadOnlyList<int> GetOrdinals(int documentId)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(documentId);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(documentId, DocumentCount);
        int start = _documentStarts[documentId];
        int count = _documentStarts[documentId + 1] - start;
        return count == 0 ? Array.Empty<int>() : new DocValuesSlice<int>(count, index => _ordinals[start + index]);
    }

    internal IReadOnlyList<string> GetValues(int documentId)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(documentId);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(documentId, DocumentCount);
        int start = _documentStarts[documentId];
        int count = _documentStarts[documentId + 1] - start;
        return count == 0
            ? Array.Empty<string>()
            : new DocValuesSlice<string>(count, index => _terms[_ordinals[start + index]]);
    }

    internal string[] CopyTerms() => (string[])_terms.Clone();

    internal string[][] Materialise()
    {
        var values = new string[DocumentCount][];
        for (int documentId = 0; documentId < values.Length; documentId++)
            values[documentId] = GetValues(documentId).ToArray();
        return values;
    }
}

/// <summary>Immutable sorted-numeric column backed by packed values and document offsets.</summary>
internal sealed class SortedNumericDocValuesColumn
{
    private readonly IndexInput _input;
    private readonly int[] _documentStarts;
    private readonly long _minimumBits;
    private readonly int _bitsPerValue;
    private readonly long _packedDataOffset;

    internal SortedNumericDocValuesColumn(
        IndexInput input,
        int[] documentStarts,
        long minimumBits,
        int bitsPerValue,
        long packedDataOffset)
    {
        _input = input;
        _documentStarts = documentStarts;
        _minimumBits = minimumBits;
        _bitsPerValue = bitsPerValue;
        _packedDataOffset = packedDataOffset;
    }

    internal int DocumentCount => _documentStarts.Length - 1;
    internal bool HasValues(int documentId)
        => (uint)documentId < (uint)DocumentCount
            && _documentStarts[documentId] < _documentStarts[documentId + 1];

    internal IReadOnlyList<double> GetValues(int documentId)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(documentId);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(documentId, DocumentCount);
        int start = _documentStarts[documentId];
        int count = _documentStarts[documentId + 1] - start;
        return count == 0
            ? Array.Empty<double>()
            : new DocValuesSlice<double>(count, index => GetFlatValue(start + index));
    }

    internal double GetFlatValue(int valueIndex)
    {
        ulong delta = _bitsPerValue == 0 ? 0 : _input.ReadPackedUnsigned(_packedDataOffset, valueIndex, _bitsPerValue);
        return BitConverter.Int64BitsToDouble(unchecked((long)((ulong)_minimumBits + delta)));
    }

    internal double[][] Materialise()
    {
        var values = new double[DocumentCount][];
        for (int documentId = 0; documentId < values.Length; documentId++)
            values[documentId] = GetValues(documentId).ToArray();
        return values;
    }
}

/// <summary>Immutable sorted Int64 column backed by packed values and document offsets.</summary>
internal sealed class Int64SortedNumericDocValuesColumn
{
    private readonly IndexInput _input;
    private readonly int[] _documentStarts;
    private readonly long _minimum;
    private readonly int _bitsPerValue;
    private readonly long _packedDataOffset;

    internal Int64SortedNumericDocValuesColumn(
        IndexInput input,
        int[] documentStarts,
        long minimum,
        int bitsPerValue,
        long packedDataOffset)
    {
        _input = input;
        _documentStarts = documentStarts;
        _minimum = minimum;
        _bitsPerValue = bitsPerValue;
        _packedDataOffset = packedDataOffset;
    }

    internal int DocumentCount => _documentStarts.Length - 1;
    internal bool HasValues(int documentId)
        => (uint)documentId < (uint)DocumentCount
            && _documentStarts[documentId] < _documentStarts[documentId + 1];

    internal IReadOnlyList<long> GetValues(int documentId)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(documentId);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(documentId, DocumentCount);
        int start = _documentStarts[documentId];
        int count = _documentStarts[documentId + 1] - start;
        return count == 0
            ? Array.Empty<long>()
            : new DocValuesSlice<long>(count, index => GetFlatValue(start + index));
    }

    internal long GetFlatValue(int valueIndex)
    {
        ulong delta = _bitsPerValue == 0 ? 0 : _input.ReadPackedUnsigned(_packedDataOffset, valueIndex, _bitsPerValue);
        return unchecked((long)((ulong)_minimum + delta));
    }

    internal long[][] Materialise()
    {
        var values = new long[DocumentCount][];
        for (int documentId = 0; documentId < values.Length; documentId++)
            values[documentId] = GetValues(documentId).ToArray();
        return values;
    }
}

/// <summary>Immutable binary column backed by mapped offsets and one payload range.</summary>
internal sealed class BinaryDocValuesColumn
{
    private readonly IndexInput _input;
    private readonly int[] _documentStarts;
    private readonly int[] _valueByteOffsets;
    private readonly long _payloadOffset;

    internal BinaryDocValuesColumn(
        IndexInput input,
        int[] documentStarts,
        int[] valueByteOffsets,
        long payloadOffset)
    {
        _input = input;
        _documentStarts = documentStarts;
        _valueByteOffsets = valueByteOffsets;
        _payloadOffset = payloadOffset;
    }

    internal int DocumentCount => _documentStarts.Length - 1;
    internal bool HasValues(int documentId)
        => (uint)documentId < (uint)DocumentCount
            && _documentStarts[documentId] < _documentStarts[documentId + 1];

    internal IReadOnlyList<byte[]> GetValues(int documentId)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(documentId);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(documentId, DocumentCount);
        int start = _documentStarts[documentId];
        int count = _documentStarts[documentId + 1] - start;
        return count == 0
            ? Array.Empty<byte[]>()
            : new DocValuesSlice<byte[]>(count, index => GetValue(start + index));
    }

    internal byte[] GetValue(int valueIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(valueIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(valueIndex, _valueByteOffsets.Length - 1);
        int start = _valueByteOffsets[valueIndex];
        int length = _valueByteOffsets[valueIndex + 1] - start;
        return _input.ReadBytesAt(checked(_payloadOffset + start), length);
    }

    internal bool HasValuesForEveryDocument()
    {
        for (int documentId = 0; documentId < DocumentCount; documentId++)
        {
            if (_documentStarts[documentId] == _documentStarts[documentId + 1])
                return false;
        }

        return true;
    }

    internal byte[][][] Materialise()
    {
        var values = new byte[DocumentCount][][];
        for (int documentId = 0; documentId < values.Length; documentId++)
            values[documentId] = GetValues(documentId).ToArray();
        return values;
    }
}

/// <summary>Read-only list view used for one document's values without retaining a row array.</summary>
internal sealed class DocValuesSlice<T> : IReadOnlyList<T>
{
    private readonly Func<int, T> _getValue;

    internal DocValuesSlice(int count, Func<int, T> getValue)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        _getValue = getValue ?? throw new ArgumentNullException(nameof(getValue));
        Count = count;
    }

    public int Count { get; }

    public T this[int index]
    {
        get
        {
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, Count);
            return _getValue(index);
        }
    }

    public IEnumerator<T> GetEnumerator()
    {
        for (int index = 0; index < Count; index++)
            yield return this[index];
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
