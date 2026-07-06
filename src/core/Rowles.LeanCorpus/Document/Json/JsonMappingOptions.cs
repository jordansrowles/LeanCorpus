namespace Rowles.LeanCorpus.Document.Json;

/// <summary>
/// Options controlling how <see cref="JsonDocumentMapper"/> maps JSON to fields.
/// </summary>
public sealed class JsonMappingOptions
{
    private string _fieldNameSeparator = ".";
    private int _maxDepth = 10;
    private int _stringFieldMaxLength = int.MaxValue;

    /// <summary>Separator between nested field name segments. Default: ".".</summary>
    public string FieldNameSeparator
    {
        get => _fieldNameSeparator;
        init => _fieldNameSeparator = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>Maximum nesting depth to recurse into. Default: 10. Must be non-negative.</summary>
    public int MaxDepth
    {
        get => _maxDepth;
        init => _maxDepth = value >= 0
            ? value
            : throw new ArgumentOutOfRangeException(nameof(value), "MaxDepth must be non-negative.");
    }

    /// <summary>
    /// Maximum length for a JSON string to be mapped as <see cref="Fields.StringField"/>.
    /// Longer strings become <see cref="Fields.TextField"/>. Default: <see cref="int.MaxValue"/>,
    /// so JSON strings are exact-match by default. Must be non-negative.
    /// Set to 0 to map every string as <see cref="Fields.TextField"/>.
    /// </summary>
    public int StringFieldMaxLength
    {
        get => _stringFieldMaxLength;
        init => _stringFieldMaxLength = value >= 0
            ? value
            : throw new ArgumentOutOfRangeException(nameof(value), "StringFieldMaxLength must be non-negative.");
    }
}
