namespace Rowles.LeanCorpus.Document.Fields;

/// <summary>64-bit signed integer field for exact-match and range filters.</summary>
public sealed class Int64Field : IField
{
    /// <summary>
    /// Initialises a new <see cref="Int64Field"/> with the specified name and value.
    /// </summary>
    /// <param name="name">The field name. Must be a valid LeanCorpus field name.</param>
    /// <param name="value">The integer value to index and store.</param>
    public Int64Field(string name, long value)
        : this(name, value, stored: true, boost: 1.0f)
    {
    }

    /// <summary>
    /// Initialises a new <see cref="Int64Field"/> with the specified name, value, and stored-field behaviour.
    /// </summary>
    /// <param name="name">The field name. Must not be null.</param>
    /// <param name="value">The integer value to index.</param>
    /// <param name="stored">Whether the value should be persisted in stored fields.</param>
    public Int64Field(string name, long value, bool stored)
        : this(name, value, stored, 1.0f, storeDocValues: true)
    {
    }

    /// <summary>
    /// Initialises a new <see cref="Int64Field"/> with the specified name, value, stored-field behaviour, and index-time boost.
    /// </summary>
    /// <param name="name">The field name. Must not be null.</param>
    /// <param name="value">The integer value to index.</param>
    /// <param name="stored">Whether the value should be persisted in stored fields.</param>
    /// <param name="boost">Index-time boost applied to scoring queries against this field.</param>
    /// <param name="storeDocValues">
    /// Whether to populate DocValues for this field. Defaults to <c>true</c>.
    /// Set to <c>false</c> to reduce indexing overhead when range queries can rely on the BKD index alone.
    /// </param>
    /// <param name="indexOptions">
    /// Controls which postings data is written to the inverted index.
    /// Defaults to <see cref="FieldIndexOptions.DocsOnly"/> — integer fields use
    /// the BKD tree for range queries and do not participate in the postings index.
    /// </param>
    public Int64Field(string name, long value, bool stored, float boost, bool storeDocValues = true,
        FieldIndexOptions indexOptions = FieldIndexOptions.DocsOnly)
    {
        Name = FieldNameValidator.Validate(name, nameof(name));
        Value = value;
        IsStored = stored;
        Boost = FieldBoostValidator.Validate(boost, nameof(boost));
        StoreDocValues = storeDocValues;
        IndexOptions = indexOptions;
    }

    /// <inheritdoc/>
    public string Name { get; }

    /// <summary>Gets the integer value of this field.</summary>
    public long Value { get; }

    /// <inheritdoc/>
    public FieldType FieldType => FieldType.Int64;

    /// <inheritdoc/>
    public bool IsStored { get; }

    /// <inheritdoc/>
    public bool IsIndexed => true;

    /// <inheritdoc/>
    public float Boost { get; }

    /// <inheritdoc/>
    public bool StoreDocValues { get; }

    /// <inheritdoc/>
    public FieldIndexOptions IndexOptions { get; }
}
