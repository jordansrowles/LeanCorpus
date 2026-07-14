namespace Rowles.LeanCorpus.Document.Fields;

/// <summary>
/// Stored-only field whose value is persisted as raw bytes without any text encoding.
/// </summary>
public sealed class BinaryField : IField
{
    /// <summary>
    /// Initialises a new <see cref="BinaryField"/> with the specified name and binary value.
    /// </summary>
    /// <param name="name">The field name. Must be a valid LeanCorpus field name.</param>
    /// <param name="value">The raw bytes to persist. Must not be null.</param>
    public BinaryField(string name, ReadOnlyMemory<byte> value)
    {
        Name = FieldNameValidator.Validate(name, nameof(name));
        Value = value.ToArray();
    }

    /// <inheritdoc/>
    public string Name { get; }

    /// <summary>Gets the raw stored bytes for this field.</summary>
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
    /// <remarks>
    /// <see cref="BinaryField"/> populates <c>BinaryDocValues</c> with its raw byte payload,
    /// so this defaults to <c>true</c>.
    /// </remarks>
    public bool StoreDocValues => true;

    /// <inheritdoc/>
    public FieldIndexOptions IndexOptions => FieldIndexOptions.DocsOnly;
}
