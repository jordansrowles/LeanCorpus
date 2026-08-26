namespace Rowles.LeanCorpus.Server.Abstractions.Contracts.Indexing;

/// <summary>Opaque, server-owned identity for one physical local index copy.</summary>
public readonly record struct PhysicalIndexId
{
    /// <summary>Initialises a validated physical index identifier.</summary>
    public PhysicalIndexId(string value)
    {
        if (!Guid.TryParseExact(value, "N", out _))
            throw new ArgumentException("Physical index IDs must be GUID values in N format.", nameof(value));
        Value = value;
    }

    /// <summary>Gets the opaque identifier value.</summary>
    public string Value { get; }

    /// <summary>Creates a new opaque physical identifier.</summary>
    public static PhysicalIndexId New() => new(Guid.NewGuid().ToString("N"));

    /// <inheritdoc />
    public override string ToString() => Value;
}
