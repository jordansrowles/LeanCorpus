namespace Rowles.LeanLucene.Document.Fields;

/// <summary>Exact-match field stored as-is, not passed through the analyser.</summary>
public sealed class StringField : IField
{
    /// <summary>
    /// Initialises a new <see cref="StringField"/> with the specified name and value.
    /// </summary>
    /// <param name="name">The field name. Must be a valid LeanLucene field name.</param>
    /// <param name="value">The exact string value to index and store. Must not be null.</param>
    public StringField(string name, string value)
        : this(name, value, stored: true)
    {
    }

    /// <summary>
    /// Initialises a new <see cref="StringField"/> with the specified name, value, and stored-field behaviour.
    /// </summary>
    /// <param name="name">The field name. Must not be null.</param>
    /// <param name="value">The exact string value to index. Must not be null.</param>
    /// <param name="stored">Whether the exact string value should be persisted in stored fields.</param>
    public StringField(string name, string value, bool stored)
    {
        Name = FieldNameValidator.Validate(name, nameof(name));
        Value = value ?? throw new ArgumentNullException(nameof(value));
        IsStored = stored;
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
}
