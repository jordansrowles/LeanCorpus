namespace Rowles.LeanLucene.Document.Json;

/// <summary>
/// Options controlling how <see cref="JsonDocumentMapper"/> maps JSON to fields.
/// </summary>
public sealed class JsonMappingOptions
{
    /// <summary>Separator between nested field name segments. Default: ".".</summary>
    public string FieldNameSeparator { get; init; } = ".";

    /// <summary>Maximum nesting depth to recurse into. Default: 10.</summary>
    public int MaxDepth { get; init; } = 10;

    /// <summary>
    /// String length threshold: strings shorter than this become <see cref="Fields.StringField"/>,
    /// longer become <see cref="Fields.TextField"/>. Default: 64.
    /// </summary>
    public int StringFieldMaxLength { get; init; } = 64;
}
