using System.Numerics;
using Rowles.LeanCorpus.Codecs.CodecKit.Checksum.Providers;

namespace Rowles.LeanCorpus.Search.Aggregations;

/// <summary>
/// Mergeable HyperLogLog++ cardinality sketch using deterministic xxHash64 input hashing.
/// It uses sparse register storage until one quarter of registers are populated, then
/// switches to a fixed dense register array. The raw estimator uses linear counting
/// for small cardinalities; this mathematically justified correction avoids static
/// empirical bias tables while retaining deterministic, bounded dense memory.
/// </summary>
public sealed class HyperLogLogPlusPlus
{
    /// <summary>Default precision, giving 16,384 registers and approximately 0.81% relative standard error.</summary>
    public const int DefaultPrecision = 14;
    private readonly Dictionary<int, byte>? _sparse;
    private byte[]? _dense;

    /// <summary>Initialises a sketch with precision from 4 to 18.</summary>
    public HyperLogLogPlusPlus(int precision = DefaultPrecision)
    {
        if (precision is < 4 or > 18)
            throw new ArgumentOutOfRangeException(nameof(precision), "HLL++ precision must be from 4 to 18.");
        Precision = precision;
        _sparse = [];
    }

    /// <summary>Gets the register precision.</summary>
    public int Precision { get; }

    /// <summary>Gets the number of registers.</summary>
    public int RegisterCount => 1 << Precision;

    /// <summary>Gets the expected relative standard error, approximately 1.04 / sqrt(m).</summary>
    public double ExpectedRelativeError => 1.04 / Math.Sqrt(RegisterCount);

    /// <summary>Gets whether the sketch is currently using sparse register storage.</summary>
    public bool IsSparse => _dense is null;

    /// <summary>Adds an already stable 64-bit hash.</summary>
    public void AddHash(ulong hash)
    {
        int index = (int)(hash >> (64 - Precision));
        ulong remaining = hash << Precision;
        byte rank = (byte)Math.Min(64 - Precision + 1, BitOperations.LeadingZeroCount(remaining) + 1);
        SetRegister(index, rank);
    }

    /// <summary>Adds a numeric value using its stable IEEE-754 binary representation.</summary>
    public void Add(double value)
    {
        Span<byte> bytes = stackalloc byte[8];
        BitConverter.TryWriteBytes(bytes, BitConverter.DoubleToInt64Bits(value));
        AddHash(XxHash64.Compute(bytes));
    }

    /// <summary>Adds an Int64 value using its stable binary representation.</summary>
    public void Add(long value)
    {
        Span<byte> bytes = stackalloc byte[8];
        BitConverter.TryWriteBytes(bytes, value);
        AddHash(XxHash64.Compute(bytes));
    }

    /// <summary>Estimates the distinct cardinality.</summary>
    public double Estimate()
    {
        double sum = 0;
        int zeroes = 0;
        for (int i = 0; i < RegisterCount; i++)
        {
            byte value = GetRegister(i);
            sum += Math.ScaleB(1.0, -value);
            if (value == 0) zeroes++;
        }
        if (zeroes == RegisterCount)
            return 0;
        double m = RegisterCount;
        double alpha = RegisterCount switch { 16 => 0.673, 32 => 0.697, 64 => 0.709, _ => 0.7213 / (1 + 1.079 / m) };
        double estimate = alpha * m * m / sum;
        return estimate <= 2.5 * m && zeroes > 0 ? m * Math.Log(m / zeroes) : estimate;
    }

    /// <summary>Merges another sketch with identical precision.</summary>
    public void MergeFrom(HyperLogLogPlusPlus other)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (Precision != other.Precision)
            throw new ArgumentException("HLL++ precisions must match before merging.", nameof(other));
        for (int i = 0; i < RegisterCount; i++)
            SetRegister(i, other.GetRegister(i));
    }

    private byte GetRegister(int index) => _dense is null ? _sparse!.GetValueOrDefault(index) : _dense[index];

    private void SetRegister(int index, byte rank)
    {
        if (_dense is not null)
        {
            if (rank > _dense[index]) _dense[index] = rank;
            return;
        }
        var sparse = _sparse!;
        if (rank > sparse.GetValueOrDefault(index)) sparse[index] = rank;
        if (sparse.Count >= RegisterCount / 4)
        {
            _dense = new byte[RegisterCount];
            foreach (var entry in sparse) _dense[entry.Key] = entry.Value;
            sparse.Clear();
        }
    }
}
