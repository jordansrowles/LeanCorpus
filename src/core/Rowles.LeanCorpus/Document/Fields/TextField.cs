namespace Rowles.LeanCorpus.Document.Fields;

/// <summary>Full-text field passed through the analyser pipeline.</summary>
public sealed class TextField : IField
{
    /// <summary>
    /// Initialises a new <see cref="TextField"/> with the specified name and text value.
    /// </summary>
    /// <param name="name">The field name. Must be a valid LeanCorpus field name.</param>
    /// <param name="value">The text content to analyse, index, and store. Must not be null.</param>
    public TextField(string name, string value)
        : this(name, value, stored: true, boost: 1.0f)
    {
    }

    /// <summary>
    /// Initialises a new <see cref="TextField"/> with the specified name, text value, and stored-field behaviour.
    /// </summary>
    /// <param name="name">The field name. Must be a valid LeanCorpus field name.</param>
    /// <param name="value">The text content to analyse and index. Must not be null.</param>
    /// <param name="stored">Whether the original text value should be persisted in stored fields.</param>
    public TextField(string name, string value, bool stored)
        : this(name, value, stored, 1.0f)
    {
    }

    /// <summary>
    /// Initialises a new <see cref="TextField"/> with the specified name, text value, stored-field behaviour, and index-time boost.
    /// </summary>
    /// <param name="name">The field name. Must be a valid LeanCorpus field name.</param>
    /// <param name="value">The text content to analyse and index. Must not be null.</param>
    /// <param name="stored">Whether the original text value should be persisted in stored fields.</param>
    /// <param name="boost">Index-time boost applied to scoring queries against this field.</param>
    /// <param name="storeDocValues">
    /// Whether to populate DocValues for this field. Defaults to <c>false</c>
    /// because <see cref="TextField"/> does not currently populate DocValues.
    /// </param>
    /// <param name="indexOptions">
    /// Controls which postings data is written to the inverted index.
    /// Defaults to <see cref="FieldIndexOptions.DocsAndFreqsAndPositions"/>.
    /// Use <see cref="FieldIndexOptions.DocsOnly"/> for filter-only fields or
    /// <see cref="FieldIndexOptions.DocsAndFreqs"/> when phrase queries and
    /// highlighting are not required.
    /// </param>
    public TextField(string name, string value, bool stored, float boost, bool storeDocValues = false,
        FieldIndexOptions indexOptions = FieldIndexOptions.DocsAndFreqsAndPositions)
    {
        Name = FieldNameValidator.Validate(name, nameof(name));
        Value = value ?? throw new ArgumentNullException(nameof(value));
        IsStored = stored;
        Boost = FieldBoostValidator.Validate(boost, nameof(boost));
        StoreDocValues = storeDocValues;
        IndexOptions = indexOptions;
    }

    /// <inheritdoc/>
    public string Name { get; }

    /// <summary>Gets the text content of this field.</summary>
    public string Value { get; }

    /// <inheritdoc/>
    public FieldType FieldType => FieldType.Text;

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
