namespace Rowles.LeanLucene.Document.Fields;

/// <summary>Numeric field for range filters and sorting.</summary>
public sealed class NumericField : IField
{
    /// <summary>
    /// Initialises a new <see cref="NumericField"/> with the specified name and numeric value.
    /// </summary>
    /// <param name="name">The field name. Must be a valid LeanLucene field name.</param>
    /// <param name="value">The numeric value to index and store.</param>
    public NumericField(string name, double value)
        : this(name, value, stored: true)
    {
    }

    /// <summary>
    /// Initialises a new <see cref="NumericField"/> with the specified name, numeric value, and stored-field behaviour.
    /// </summary>
    /// <param name="name">The field name. Must not be null.</param>
    /// <param name="value">The numeric value to index.</param>
    /// <param name="stored">Whether the numeric value should be persisted in stored fields.</param>
    public NumericField(string name, double value, bool stored)
    {
        Name = FieldNameValidator.Validate(name, nameof(name));
        Value = value;
        IsStored = stored;
    }

    /// <inheritdoc/>
    public string Name { get; }

    /// <summary>Gets the numeric value of this field.</summary>
    public double Value { get; }

    /// <inheritdoc/>
    public FieldType FieldType => FieldType.Numeric;

    /// <inheritdoc/>
    public bool IsStored { get; }

    /// <inheritdoc/>
    public bool IsIndexed => true;
}
