using System.Net;

namespace Rowles.LeanCorpus.Search.Queries;

/// <summary>Matches IPv4 or IPv6 values equal to any supplied address.</summary>
public sealed class InetAddressPointInSetQuery : Query
{
    private readonly IPAddress[] _addresses;

    /// <inheritdoc/>
    public override string Field { get; }

    /// <summary>Gets the supplied addresses.</summary>
    public IReadOnlyList<IPAddress> Addresses => _addresses;

    /// <summary>Initialises an IP address point-in-set query.</summary>
    public InetAddressPointInSetQuery(string field, params IPAddress[] addresses)
    {
        if (string.IsNullOrWhiteSpace(field))
            throw new ArgumentException("Field must be a non-empty value.", nameof(field));
        ArgumentNullException.ThrowIfNull(addresses);
        if (addresses.Length == 0)
            throw new ArgumentException(
                "InetAddressPointInSetQuery requires at least one address.",
                nameof(addresses));

        Field = field;
        _addresses = addresses.ToArray();
        for (int i = 0; i < _addresses.Length; i++)
            ArgumentNullException.ThrowIfNull(_addresses[i]);
    }

    /// <inheritdoc/>
    public override Query Rewrite()
    {
        var points = new byte[_addresses.Length][];
        for (int i = 0; i < _addresses.Length; i++)
            points[i] = InetAddressEncoding.Encode(_addresses[i]);
        return new BinaryPointInSetQuery(Field, points) { Boost = Boost };
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj) =>
        obj is InetAddressPointInSetQuery other &&
        string.Equals(Field, other.Field, StringComparison.Ordinal) &&
        Boost == other.Boost &&
        _addresses.AsSpan().SequenceEqual(other._addresses);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(nameof(InetAddressPointInSetQuery));
        hash.Add(Field);
        foreach (var address in _addresses)
            hash.Add(address);
        return CombineBoost(hash.ToHashCode());
    }
}
