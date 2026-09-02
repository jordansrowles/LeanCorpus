using System.Buffers;
using System.Buffers.Binary;
using System.Numerics;
using Rowles.LeanCorpus.Codecs.CodecKit.Checksum.Providers;

namespace Rowles.LeanCorpus.Search.Aggregations;

/// <summary>
/// Mergeable HyperLogLog++ cardinality sketch using deterministic xxHash64 hashing,
/// higher-precision sparse encoding, empirical bias correction, LinearCounting
/// crossover thresholds and bounded six-bit dense registers.
/// </summary>
public sealed class HyperLogLogPlusPlus
{
    /// <summary>Default precision, giving 16,384 registers and approximately 0.81% relative standard error.</summary>
    public const int DefaultPrecision = 14;
    internal const int SparsePrecision = 25;
    private const int SparseRankBits = 6;
    private const int PendingCapacity = 256;
    private readonly int _denseBudget;
    private readonly uint[] _pending = new uint[PendingCapacity];
    private byte[] _sparseData = [];
    private int _pendingCount;
    private int _sparseCount;
    private byte[]? _dense;

    /// <summary>Initialises a sketch with precision from 4 to 18.</summary>
    public HyperLogLogPlusPlus(int precision = DefaultPrecision)
    {
        if (precision is < 4 or > 18)
            throw new ArgumentOutOfRangeException(nameof(precision), "HLL++ precision must be from 4 to 18.");
        Precision = precision;
        _denseBudget = (RegisterCount * 6 + 7) / 8;
    }

    /// <summary>Gets the configured dense-register precision.</summary>
    public int Precision { get; }
    /// <summary>Gets the number of dense registers.</summary>
    public int RegisterCount => 1 << Precision;
    /// <summary>Gets the expected relative standard error, approximately 1.04 / sqrt(m).</summary>
    public double ExpectedRelativeError => 1.04 / Math.Sqrt(RegisterCount);
    /// <summary>Gets whether the sketch is currently using higher-precision sparse storage.</summary>
    public bool IsSparse => _dense is null;

    /// <summary>Adds an already stable 64-bit hash.</summary>
    public void AddHash(ulong hash)
    {
        if (_dense is not null)
        {
            AddDenseHash(hash);
            return;
        }

        _pending[_pendingCount++] = EncodeSparseHash(hash);
        if (_pendingCount == _pending.Length)
            FlushSparse();
    }

    /// <summary>Adds a numeric value using its deterministic little-endian IEEE-754 representation.</summary>
    public void Add(double value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(long)];
        BinaryPrimitives.WriteInt64LittleEndian(bytes, BitConverter.DoubleToInt64Bits(value));
        AddHash(XxHash64.Compute(bytes));
    }

    /// <summary>Adds an Int64 value using its deterministic little-endian representation.</summary>
    public void Add(long value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(long)];
        BinaryPrimitives.WriteInt64LittleEndian(bytes, value);
        AddHash(XxHash64.Compute(bytes));
    }

    /// <summary>Estimates the distinct cardinality.</summary>
    public double Estimate()
    {
        if (_dense is null)
        {
            FlushSparse();
            if (_sparseCount == 0) return 0;
            const double sparseRegisters = 1 << SparsePrecision;
            return sparseRegisters * Math.Log(sparseRegisters / (sparseRegisters - _sparseCount));
        }

        double sum = 0;
        int zeroRegisters = 0;
        for (int i = 0; i < RegisterCount; i++)
        {
            byte register = GetDenseRegister(i);
            sum += Math.ScaleB(1d, -register);
            if (register == 0) zeroRegisters++;
        }

        double m = RegisterCount;
        double raw = Alpha(RegisterCount) * m * m / sum;
        double corrected = raw <= 5 * m
            ? raw - HyperLogLogPlusPlusData.EstimateBias(Precision, raw)
            : raw;
        if (zeroRegisters == 0) return corrected;
        double linear = m * Math.Log(m / zeroRegisters);
        return linear <= HyperLogLogPlusPlusData.Threshold(Precision) ? linear : corrected;
    }

    /// <summary>Merges another sketch with identical configured precision.</summary>
    public void MergeFrom(HyperLogLogPlusPlus other)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (Precision != other.Precision)
            throw new ArgumentException("HLL++ precisions must match before merging.", nameof(other));

        FlushSparse();
        other.FlushSparse();
        if (_dense is null && other._dense is null)
        {
            MergeSparseData(other);
            return;
        }

        EnsureDense();
        if (other._dense is null)
            other.ApplySparseTo(SetDenseRegister);
        else
            for (int i = 0; i < RegisterCount; i++) SetDenseRegister(i, other.GetDenseRegister(i));
    }

    internal static uint EncodeSparseHash(ulong hash)
    {
        uint index = (uint)(hash >> (64 - SparsePrecision));
        byte rank = (byte)Math.Min(64 - SparsePrecision + 1, BitOperations.LeadingZeroCount(hash << SparsePrecision) + 1);
        return (index << SparseRankBits) | rank;
    }

    internal static int DecodeDenseIndex(uint encoded, int precision)
        => (int)(encoded >> (SparseRankBits + SparsePrecision - precision));

    internal static byte DecodeDenseRank(uint encoded, int precision)
    {
        uint sparseIndex = encoded >> SparseRankBits;
        int additionalBits = SparsePrecision - precision;
        uint additional = sparseIndex & ((1u << additionalBits) - 1);
        if (additional != 0)
            return (byte)(BitOperations.LeadingZeroCount(additional) - (32 - additionalBits) + 1);
        return (byte)(additionalBits + (encoded & 0x3f));
    }

    internal int SparseCountForTesting { get { FlushSparse(); return _sparseCount; } }
    internal int SparsePayloadBytesForTesting { get { FlushSparse(); return _sparseData.Length; } }
    internal byte GetRegisterForTesting(int index) { EnsureDense(); return GetDenseRegister(index); }
    internal bool ContainsSparseValueForTesting(uint expected)
    {
        FlushSparse();
        uint value = 0;
        int offset = 0;
        while (offset < _sparseData.Length)
        {
            value += ReadVarUInt(_sparseData, ref offset);
            if (value == expected) return true;
            if (value > expected) return false;
        }
        return false;
    }

    private void AddDenseHash(ulong hash)
    {
        int index = (int)(hash >> (64 - Precision));
        byte rank = (byte)Math.Min(64 - Precision + 1, BitOperations.LeadingZeroCount(hash << Precision) + 1);
        SetDenseRegister(index, rank);
    }

    private void MergeSparseData(HyperLogLogPlusPlus other)
    {
        uint[] values = ArrayPool<uint>.Shared.Rent(_sparseCount + other._sparseCount);
        try
        {
            int count = DecodeSparse(_sparseData, values);
            count += DecodeSparse(other._sparseData, values.AsSpan(count));
            ReplaceSparse(values, count);
        }
        finally { ArrayPool<uint>.Shared.Return(values); }
    }

    private void FlushSparse()
    {
        if (_dense is not null || _pendingCount == 0) return;
        uint[] values = ArrayPool<uint>.Shared.Rent(_sparseCount + _pendingCount);
        try
        {
            int count = DecodeSparse(_sparseData, values);
            _pending.AsSpan(0, _pendingCount).CopyTo(values.AsSpan(count));
            ReplaceSparse(values, count + _pendingCount);
            _pendingCount = 0;
        }
        finally { ArrayPool<uint>.Shared.Return(values); }
    }

    private void ReplaceSparse(uint[] values, int count)
    {
        Array.Sort(values, 0, count);
        int unique = 0;
        for (int i = 0; i < count; i++)
        {
            uint value = values[i];
            if (unique > 0 && (values[unique - 1] >> SparseRankBits) == (value >> SparseRankBits))
                values[unique - 1] = value;
            else
                values[unique++] = value;
        }

        int byteCount = 0;
        uint previous = 0;
        for (int i = 0; i < unique; i++)
        {
            byteCount += VarUIntLength(values[i] - previous);
            previous = values[i];
        }

        var encoded = new byte[byteCount];
        int offset = 0;
        previous = 0;
        for (int i = 0; i < unique; i++)
        {
            WriteVarUInt(encoded, ref offset, values[i] - previous);
            previous = values[i];
        }

        _sparseData = encoded;
        _sparseCount = unique;
        if (_sparseData.Length > _denseBudget)
            ConvertToDense();
    }

    private void EnsureDense()
    {
        if (_dense is null)
        {
            FlushSparse();
            if (_dense is null) ConvertToDense();
        }
    }

    private void ConvertToDense()
    {
        _dense = new byte[_denseBudget];
        ApplySparseTo(SetDenseRegister);
        _sparseData = [];
        _sparseCount = 0;
        _pendingCount = 0;
    }

    private void ApplySparseTo(Action<int, byte> apply)
    {
        uint value = 0;
        int offset = 0;
        while (offset < _sparseData.Length)
        {
            value += ReadVarUInt(_sparseData, ref offset);
            apply(DecodeDenseIndex(value, Precision), DecodeDenseRank(value, Precision));
        }
    }

    private static int DecodeSparse(ReadOnlySpan<byte> data, Span<uint> destination)
    {
        uint value = 0;
        int offset = 0;
        int count = 0;
        while (offset < data.Length)
        {
            value += ReadVarUInt(data, ref offset);
            destination[count++] = value;
        }
        return count;
    }

    private byte GetDenseRegister(int index)
    {
        int bit = index * 6;
        int offset = bit >> 3;
        int shift = bit & 7;
        uint window = _dense![offset];
        if (offset + 1 < _dense.Length) window |= (uint)_dense[offset + 1] << 8;
        return (byte)((window >> shift) & 0x3f);
    }

    private void SetDenseRegister(int index, byte value)
    {
        if (value <= GetDenseRegister(index)) return;
        int bit = index * 6;
        int offset = bit >> 3;
        int shift = bit & 7;
        uint window = _dense![offset];
        if (offset + 1 < _dense.Length) window |= (uint)_dense[offset + 1] << 8;
        window = (window & ~(0x3fu << shift)) | ((uint)value << shift);
        _dense[offset] = (byte)window;
        if (offset + 1 < _dense.Length) _dense[offset + 1] = (byte)(window >> 8);
    }

    private static int VarUIntLength(uint value)
    {
        int length = 1;
        while (value >= 0x80) { value >>= 7; length++; }
        return length;
    }

    private static void WriteVarUInt(Span<byte> destination, ref int offset, uint value)
    {
        while (value >= 0x80) { destination[offset++] = (byte)(value | 0x80); value >>= 7; }
        destination[offset++] = (byte)value;
    }

    private static uint ReadVarUInt(ReadOnlySpan<byte> source, ref int offset)
    {
        uint value = 0;
        int shift = 0;
        while (true)
        {
            byte current = source[offset++];
            value |= (uint)(current & 0x7f) << shift;
            if ((current & 0x80) == 0) return value;
            shift += 7;
        }
    }

    private static double Alpha(int m) => m switch
    {
        16 => .673,
        32 => .697,
        64 => .709,
        _ => .7213 / (1 + 1.079 / m),
    };
}
