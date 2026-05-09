using System.Text.Json;

namespace Rowles.LeanLucene.Document.Json;

/// <summary>
/// Maps a <see cref="JsonElement"/> tree to a <see cref="LeanDocument"/> by emitting
/// typed fields based on <see cref="JsonValueKind"/>. Nested objects become prefixed
/// field names; arrays become multi-valued fields.
/// </summary>
public static class JsonDocumentMapper
{
    private static readonly JsonMappingOptions DefaultOptions = new();

    /// <summary>
    /// Creates a <see cref="LeanDocument"/> from a JSON element tree.
    /// </summary>
    public static LeanDocument FromJson(JsonElement root, JsonMappingOptions? options = null)
    {
        options ??= DefaultOptions;
        var doc = new LeanDocument();
        MapElement(doc, root, prefix: string.Empty, options, depth: 0);
        return doc;
    }

    /// <summary>
    /// Convenience overload that parses a JSON string first.
    /// </summary>
    public static LeanDocument FromJsonString(string json, JsonMappingOptions? options = null)
    {
        using var jsonDoc = System.Text.Json.JsonDocument.Parse(json);
        return FromJson(jsonDoc.RootElement, options);
    }

    private static void MapElement(LeanDocument doc, JsonElement element, string prefix, JsonMappingOptions options, int depth)
    {
        if (depth > options.MaxDepth) return;

        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var prop in element.EnumerateObject())
                {
                    var fieldName = string.IsNullOrEmpty(prefix)
                        ? prop.Name
                        : string.Concat(prefix, options.FieldNameSeparator, prop.Name);
                    MapElement(doc, prop.Value, fieldName, options, depth + 1);
                }
                break;

            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                    MapElement(doc, item, prefix, options, depth + 1);
                break;

            case JsonValueKind.String:
                var str = element.GetString() ?? string.Empty;
                if (str.Length <= options.StringFieldMaxLength)
                    doc.Add(new StringField(prefix, str));
                else
                    doc.Add(new TextField(prefix, str));
                break;

            case JsonValueKind.Number:
                doc.Add(new NumericField(prefix, element.GetDouble()));
                break;

            case JsonValueKind.True:
                doc.Add(new StringField(prefix, "true"));
                break;

            case JsonValueKind.False:
                doc.Add(new StringField(prefix, "false"));
                break;

                // Null and Undefined are intentionally skipped
        }
    }
}
