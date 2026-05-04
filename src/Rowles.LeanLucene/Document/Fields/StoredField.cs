using System.Globalization;

namespace Rowles.LeanLucene.Document.Fields;

/// <summary>
/// Stored-only field whose value can be retrieved from stored fields but is not indexed.
/// </summary>
public sealed class StoredField : IField
{
    /// <summary>
    /// Initialises a new <see cref="StoredField"/> with the specified name and string value.
    /// </summary>
    /// <param name="name">The field name. Must be a valid LeanLucene field name.</param>
    /// <param name="value">The value to persist in stored fields. Must not be null.</param>
    public StoredField(string name, string value)
    {
        Name = FieldNameValidator.Validate(name, nameof(name));
        Value = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>
    /// Initialises a new <see cref="StoredField"/> with the specified name and integer value.
    /// </summary>
    /// <param name="name">The field name. Must be a valid LeanLucene field name.</param>
    /// <param name="value">The integer value to persist in stored fields.</param>
    public StoredField(string name, int value)
        : this(name, value.ToString(CultureInfo.InvariantCulture))
    {
    }

    /// <summary>
    /// Initialises a new <see cref="StoredField"/> with the specified name and long integer value.
    /// </summary>
    /// <param name="name">The field name. Must be a valid LeanLucene field name.</param>
    /// <param name="value">The long integer value to persist in stored fields.</param>
    public StoredField(string name, long value)
        : this(name, value.ToString(CultureInfo.InvariantCulture))
    {
    }

    /// <summary>
    /// Initialises a new <see cref="StoredField"/> with the specified name and floating-point value.
    /// </summary>
    /// <param name="name">The field name. Must be a valid LeanLucene field name.</param>
    /// <param name="value">The floating-point value to persist in stored fields.</param>
    public StoredField(string name, double value)
        : this(name, value.ToString(CultureInfo.InvariantCulture))
    {
    }

    /// <inheritdoc/>
    public string Name { get; }

    /// <summary>Gets the string value persisted in stored fields.</summary>
    public string Value { get; }

    /// <inheritdoc/>
    public FieldType FieldType => FieldType.Stored;

    /// <inheritdoc/>
    public bool IsStored => true;

    /// <inheritdoc/>
    public bool IsIndexed => false;
}
