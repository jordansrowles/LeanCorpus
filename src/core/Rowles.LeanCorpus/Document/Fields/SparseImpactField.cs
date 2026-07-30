using System.Buffers.Binary;
using System.Text;

namespace Rowles.LeanCorpus.Document.Fields;

/// <summary>One externally generated learned-sparse term impact.</summary>
public readonly record struct SparseImpact(string Term, float Weight);

/// <summary>
/// Stored learned-sparse impacts. These are intentionally not ordinary term
/// frequencies: the supplied weights are preserved in a versioned binary
/// DocValues payload for the learned-sparse scorer.
/// </summary>
public sealed class SparseImpactField : IField
{
    private readonly SparseImpact[] _impacts;

    /// <summary>Initialises a learned-sparse field from externally generated impacts.</summary>
    public SparseImpactField(string name, IEnumerable<SparseImpact> impacts, float boost = 1f)
    {
        Name = FieldNameValidator.Validate(name, nameof(name));
        ArgumentNullException.ThrowIfNull(impacts);
        _impacts = impacts.ToArray();
        if (_impacts.Length == 0)
            throw new ArgumentException("Sparse impact fields must contain at least one impact.", nameof(impacts));

        Array.Sort(_impacts, static (left, right) => StringComparer.Ordinal.Compare(left.Term, right.Term));
        for (int i = 0; i < _impacts.Length; i++)
        {
            var impact = _impacts[i];
            if (string.IsNullOrEmpty(impact.Term) || !float.IsFinite(impact.Weight) || impact.Weight <= 0f)
                throw new ArgumentException("Sparse impacts require non-empty terms and positive finite weights.", nameof(impacts));
            if (i > 0 && string.Equals(_impacts[i - 1].Term, impact.Term, StringComparison.Ordinal))
                throw new ArgumentException("Sparse impact terms must be unique.", nameof(impacts));
        }
        Boost = FieldBoostValidator.Validate(boost, nameof(boost));
    }

    /// <inheritdoc/>
    public string Name { get; }

    /// <summary>Sorted immutable learned-sparse impacts.</summary>
    public IReadOnlyList<SparseImpact> Impacts => _impacts;

    /// <inheritdoc/>
    public FieldType FieldType => FieldType.Binary;
    /// <inheritdoc/>
    public bool IsStored => true;
    /// <inheritdoc/>
    public bool IsIndexed => false;
    /// <inheritdoc/>
    public float Boost { get; }
    /// <inheritdoc/>
    public bool StoreDocValues => true;
    /// <inheritdoc/>
    public FieldIndexOptions IndexOptions => FieldIndexOptions.DocsOnly;

    internal byte[] Encode() => SparseImpactPayload.Encode(_impacts);
}

/// <summary>Versioned binary payload shared by learned-sparse fields and queries.</summary>
internal static class SparseImpactPayload
{
    private const uint Magic = 0x3150_534C; // LSP1, little endian
    private const byte Version = 1;

    internal static byte[] Encode(ReadOnlySpan<SparseImpact> impacts)
    {
        var encodedTerms = new byte[impacts.Length][];
        int size = sizeof(uint) + sizeof(byte) + sizeof(ushort);
        for (int i = 0; i < impacts.Length; i++)
        {
            encodedTerms[i] = Encoding.UTF8.GetBytes(impacts[i].Term);
            if (encodedTerms[i].Length > ushort.MaxValue)
                throw new ArgumentException("Sparse impact terms may not exceed 65,535 UTF-8 bytes.", nameof(impacts));
            size = checked(size + sizeof(ushort) + encodedTerms[i].Length + sizeof(float));
        }

        var payload = new byte[size];
        var destination = payload.AsSpan();
        BinaryPrimitives.WriteUInt32LittleEndian(destination, Magic);
        destination[sizeof(uint)] = Version;
        BinaryPrimitives.WriteUInt16LittleEndian(destination[(sizeof(uint) + sizeof(byte))..], checked((ushort)impacts.Length));
        int offset = sizeof(uint) + sizeof(byte) + sizeof(ushort);
        for (int i = 0; i < impacts.Length; i++)
        {
            byte[] term = encodedTerms[i];
            BinaryPrimitives.WriteUInt16LittleEndian(destination[offset..], checked((ushort)term.Length));
            offset += sizeof(ushort);
            term.CopyTo(destination[offset..]);
            offset += term.Length;
            BinaryPrimitives.WriteSingleLittleEndian(destination[offset..], impacts[i].Weight);
            offset += sizeof(float);
        }
        return payload;
    }

    internal static float Score(ReadOnlySpan<byte> payload, IReadOnlyList<SparseImpact> query)
    {
        if (payload.Length < sizeof(uint) + sizeof(byte) + sizeof(ushort) ||
            BinaryPrimitives.ReadUInt32LittleEndian(payload) != Magic ||
            payload[sizeof(uint)] != Version)
        {
            throw new InvalidDataException("Invalid learned-sparse impact payload.");
        }

        int count = BinaryPrimitives.ReadUInt16LittleEndian(payload[(sizeof(uint) + sizeof(byte))..]);
        int offset = sizeof(uint) + sizeof(byte) + sizeof(ushort);
        int queryIndex = 0;
        float score = 0f;
        for (int i = 0; i < count; i++)
        {
            if (offset + sizeof(ushort) > payload.Length)
                throw new InvalidDataException("Truncated learned-sparse impact term length.");
            int length = BinaryPrimitives.ReadUInt16LittleEndian(payload[offset..]);
            offset += sizeof(ushort);
            if (offset + length + sizeof(float) > payload.Length)
                throw new InvalidDataException("Truncated learned-sparse impact payload.");
            string term = Encoding.UTF8.GetString(payload.Slice(offset, length));
            offset += length;
            float documentWeight = BinaryPrimitives.ReadSingleLittleEndian(payload[offset..]);
            offset += sizeof(float);
            if (!float.IsFinite(documentWeight) || documentWeight <= 0f)
                throw new InvalidDataException("Invalid learned-sparse document impact.");

            while (queryIndex < query.Count &&
                   StringComparer.Ordinal.Compare(query[queryIndex].Term, term) < 0)
                queryIndex++;
            if (queryIndex < query.Count &&
                string.Equals(query[queryIndex].Term, term, StringComparison.Ordinal))
                score += query[queryIndex].Weight * documentWeight;
        }
        if (offset != payload.Length)
            throw new InvalidDataException("Trailing bytes in learned-sparse impact payload.");
        return score;
    }

    /// <summary>
    /// Returns a safe document-level score upper bound for a positive learned-sparse
    /// query without decoding UTF-8 terms. It is intentionally conservative and is
    /// used only once the top-N collector has a competitive threshold.
    /// </summary>
    internal static float UpperBound(ReadOnlySpan<byte> payload, float maximumQueryWeight)
    {
        if (payload.Length < sizeof(uint) + sizeof(byte) + sizeof(ushort) ||
            BinaryPrimitives.ReadUInt32LittleEndian(payload) != Magic ||
            payload[sizeof(uint)] != Version)
        {
            throw new InvalidDataException("Invalid learned-sparse impact payload.");
        }

        int count = BinaryPrimitives.ReadUInt16LittleEndian(payload[(sizeof(uint) + sizeof(byte))..]);
        int offset = sizeof(uint) + sizeof(byte) + sizeof(ushort);
        float sum = 0f;
        for (int i = 0; i < count; i++)
        {
            if (offset + sizeof(ushort) > payload.Length)
                throw new InvalidDataException("Truncated learned-sparse impact term length.");
            int length = BinaryPrimitives.ReadUInt16LittleEndian(payload[offset..]);
            offset += sizeof(ushort);
            if (offset + length + sizeof(float) > payload.Length)
                throw new InvalidDataException("Truncated learned-sparse impact payload.");
            offset += length;
            float weight = BinaryPrimitives.ReadSingleLittleEndian(payload[offset..]);
            if (!float.IsFinite(weight) || weight <= 0f)
                throw new InvalidDataException("Invalid learned-sparse document impact.");
            sum += weight;
            offset += sizeof(float);
        }
        if (offset != payload.Length)
            throw new InvalidDataException("Trailing bytes in learned-sparse impact payload.");
        return sum * maximumQueryWeight;
    }
}
