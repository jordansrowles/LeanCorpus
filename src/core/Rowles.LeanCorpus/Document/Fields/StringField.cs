namespace Rowles.LeanCorpus.Document.Fields;

/// <summary>Exact-match field stored as-is, not passed through the analyser.</summary>
public sealed class StringField : IField
{
    /// <summary>
    /// Initialises a new <see cref="StringField"/> with the specified name and value.
    /// </summary>
    /// <param name="name">The field name. Must be a valid LeanCorpus field name.</param>
    /// <param name="value">The exact string value to index and store. Must not be null.</param>
    public StringField(string name, string value)
        : this(name, value, stored: true, boost: 1.0f)
    {
    }

    /// <summary>
    /// Initialises a new <see cref="StringField"/> with the specified name, value, and stored-field behaviour.
    /// </summary>
    /// <param name="name">The field name. Must not be null.</param>
    /// <param name="value">The exact string value to index. Must not be null.</param>
    /// <param name="stored">Whether the exact string value should be persisted in stored fields.</param>
    public StringField(string name, string value, bool stored)
        : this(name, value, stored, 1.0f, storeDocValues: true)
    {
    }

    /// <summary>
    /// Initialises a new <see cref="StringField"/> with the specified name, value, stored-field behaviour, and index-time boost.
    /// </summary>
    /// <param name="name">The field name. Must not be null.</param>
    /// <param name="value">The exact string value to index. Must not be null.</param>
    /// <param name="stored">Whether the exact string value should be persisted in stored fields.</param>
    /// <param name="boost">Index-time boost applied to scoring queries against this field.</param>
    /// <param name="storeDocValues">
    /// Whether to populate DocValues (sorted, sorted-set, binary) for this field.
    /// Defaults to <c>true</c>. Set to <c>false</c> to reduce indexing overhead when
    /// faceting, collapsing, or binary DocValues retrieval is not required.
    /// </param>
    /// <param name="indexOptions">
    /// Controls which postings data is written to the inverted index.
    /// Defaults to <see cref="FieldIndexOptions.DocsAndFreqs"/> — exact-match fields
    /// benefit from term frequencies for scoring but typically do not need positions.
    /// Set to <see cref="FieldIndexOptions.DocsOnly"/> for filter-only string fields.
    /// </param>
    public StringField(string name, string value, bool stored, float boost, bool storeDocValues = true,
        FieldIndexOptions indexOptions = FieldIndexOptions.DocsAndFreqs)
        : this(
            name,
            value,
            stored,
            boost,
            storeDocValues
                ? StringDocValues.Sorted | StringDocValues.SortedSet | StringDocValues.Binary
                : StringDocValues.None,
            indexOptions)
    {
    }

    /// <summary>Initialises a string field with explicit DocValues representations.</summary>
    /// <param name="name">The field name. Must not be null.</param>
    /// <param name="value">The exact string value to index. Must not be null.</param>
    /// <param name="stored">Whether the value should be persisted in stored fields.</param>
    /// <param name="boost">Index-time boost applied to scoring queries.</param>
    /// <param name="docValues">The DocValues representations to write.</param>
    /// <param name="indexOptions">Controls which postings data is written.</param>
    public StringField(string name, string value, bool stored, float boost, StringDocValues docValues,
        FieldIndexOptions indexOptions = FieldIndexOptions.DocsAndFreqs)
    {
        const StringDocValues valid = StringDocValues.Sorted | StringDocValues.SortedSet | StringDocValues.Binary;
        if ((docValues & ~valid) != 0)
            throw new ArgumentOutOfRangeException(nameof(docValues), docValues, "Unknown string DocValues representation.");

        Name = FieldNameValidator.Validate(name, nameof(name));
        Value = value ?? throw new ArgumentNullException(nameof(value));
        IsStored = stored;
        Boost = FieldBoostValidator.Validate(boost, nameof(boost));
        DocValues = docValues;
        IndexOptions = indexOptions;
    }

    /// <inheritdoc/>
    public string Name { get; }

    /// <summary>Gets the exact string value of this field.</summary>
    public string Value { get; }

    /// <inheritdoc/>
    public FieldType FieldType => FieldType.String;

    /// <inheritdoc/>
    public bool IsStored { get; }

    /// <inheritdoc/>
    public bool IsIndexed => true;

    /// <inheritdoc/>
    public float Boost { get; }

    /// <inheritdoc/>
    public bool StoreDocValues => DocValues != StringDocValues.None;

    /// <summary>Gets the explicitly selected DocValues representations.</summary>
    public StringDocValues DocValues { get; }

    /// <inheritdoc/>
    public FieldIndexOptions IndexOptions { get; }
}
