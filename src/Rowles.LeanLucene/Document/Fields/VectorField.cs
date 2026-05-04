namespace Rowles.LeanLucene.Document.Fields;

/// <summary>Dense float vector for semantic and RAG workloads.</summary>
public sealed class VectorField : IField
{
    /// <summary>
    /// Initialises a new <see cref="VectorField"/> with the specified name and float vector.
    /// </summary>
    /// <param name="name">The field name. Must be a valid LeanLucene field name.</param>
    /// <param name="value">The non-empty dense float vector to store. Not included in the inverted index.</param>
    public VectorField(string name, ReadOnlyMemory<float> value)
    {
        if (value.Length == 0)
            throw new ArgumentException("Vector fields must contain at least one dimension.", nameof(value));
        foreach (float component in value.Span)
        {
            if (!float.IsFinite(component))
                throw new ArgumentException("Vector fields must contain only finite values.", nameof(value));
        }

        Name = FieldNameValidator.Validate(name, nameof(name));
        Value = value;
    }

    /// <inheritdoc/>
    public string Name { get; }

    /// <summary>Gets the dense float vector stored in this field.</summary>
    public ReadOnlyMemory<float> Value { get; }

    /// <inheritdoc/>
    public FieldType FieldType => FieldType.Vector;

    /// <inheritdoc/>
    public bool IsStored => true;

    /// <inheritdoc/>
    public bool IsIndexed => false;
}
