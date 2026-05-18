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
    public TextField(string name, string value, bool stored, float boost)
    {
        Name = FieldNameValidator.Validate(name, nameof(name));
        Value = value ?? throw new ArgumentNullException(nameof(value));
        IsStored = stored;
        Boost = FieldBoostValidator.Validate(boost, nameof(boost));
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
}
