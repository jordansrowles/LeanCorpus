using System.Net;
using System.Net.Sockets;

namespace Rowles.LeanCorpus.Document.Fields;

/// <summary>Stored binary field for an IPv4 or IPv6 address.</summary>
public sealed class InetAddressField : IField
{
    /// <summary>Initialises an IP address field.</summary>
    public InetAddressField(string name, IPAddress value)
    {
        Name = FieldNameValidator.Validate(name, nameof(name));
        ArgumentNullException.ThrowIfNull(value);
        Address = value;
        Value = value.AddressFamily == AddressFamily.InterNetwork
            ? value.MapToIPv6().GetAddressBytes()
            : value.GetAddressBytes();
    }

    /// <inheritdoc/>
    public string Name { get; }

    /// <summary>Gets the source address.</summary>
    public IPAddress Address { get; }

    /// <summary>Gets the normalised 16-byte address used by binary queries.</summary>
    public ReadOnlyMemory<byte> Value { get; }

    /// <inheritdoc/>
    public FieldType FieldType => FieldType.Binary;

    /// <inheritdoc/>
    public bool IsStored => true;

    /// <inheritdoc/>
    public bool IsIndexed => false;

    /// <inheritdoc/>
    public float Boost => 1.0f;

    /// <inheritdoc/>
    public bool StoreDocValues => true;

    /// <inheritdoc/>
    public FieldIndexOptions IndexOptions => FieldIndexOptions.DocsOnly;
}
