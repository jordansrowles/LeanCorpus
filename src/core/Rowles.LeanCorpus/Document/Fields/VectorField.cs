namespace Rowles.LeanCorpus.Document.Fields;

/// <summary>Dense float vector for semantic and RAG workloads.</summary>
public sealed class VectorField : IField
{
    /// <summary>
    /// Initialises a new <see cref="VectorField"/> with the specified name and float vector.
    /// </summary>
    /// <param name="name">The field name. Must be a valid LeanCorpus field name.</param>
    /// <param name="value">The non-empty dense float vector to store. Not included in the inverted index.</param>
    /// <param name="boost">Index-time boost applied to vector-scoring queries against this field.</param>
    public VectorField(string name, ReadOnlyMemory<float> value, float boost = 1.0f)
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
        Boost = FieldBoostValidator.Validate(boost, nameof(boost));
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

    /// <inheritdoc/>
    public float Boost { get; }

    /// <inheritdoc/>
    public bool StoreDocValues => false;

    /// <inheritdoc/>
    public FieldIndexOptions IndexOptions => FieldIndexOptions.DocsOnly;
}

/// <summary>
/// Dense unsigned-byte vector for compact externally produced embeddings.
/// Values are converted exactly to Float32 at the current vector-format boundary,
/// so all existing vector codecs, HNSW, merges, and migrations remain applicable.
/// </summary>
public sealed class ByteVectorField : IField
{
    /// <summary>Initialises a byte-vector field.</summary>
    public ByteVectorField(string name, ReadOnlyMemory<byte> value, float boost = 1.0f)
    {
        if (value.IsEmpty)
            throw new ArgumentException("Byte vector fields must contain at least one dimension.", nameof(value));

        Name = FieldNameValidator.Validate(name, nameof(name));
        Value = value;
        Boost = FieldBoostValidator.Validate(boost, nameof(boost));
    }

    /// <inheritdoc/>
    public string Name { get; }

    /// <summary>Gets the unsigned byte values supplied by the application.</summary>
    public ReadOnlyMemory<byte> Value { get; }

    /// <inheritdoc/>
    public FieldType FieldType => FieldType.Vector;

    /// <inheritdoc/>
    public bool IsStored => true;

    /// <inheritdoc/>
    public bool IsIndexed => false;

    /// <inheritdoc/>
    public float Boost { get; }

    /// <inheritdoc/>
    public bool StoreDocValues => false;

    /// <inheritdoc/>
    public FieldIndexOptions IndexOptions => FieldIndexOptions.DocsOnly;

    internal float[] ToFloat32()
    {
        var values = new float[Value.Length];
        for (int i = 0; i < values.Length; i++)
            values[i] = Value.Span[i];
        return values;
    }
}
