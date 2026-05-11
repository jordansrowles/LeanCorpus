using System.Text.Json;
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;

namespace Rowles.LeanCorpus.Example.JsonApi;

/// <summary>
/// Converts a <see cref="JsonElement"/> (object) into a <see cref="LeanDocument"/>.
/// Strings become <see cref="TextField"/>, numbers become <see cref="NumericField"/>,
/// booleans become <see cref="StringField"/>, nested objects are flattened with dot notation.
/// The original JSON is stored verbatim in the reserved field <c>_raw</c>.
/// </summary>
public static class JsonMapper
{
    public const string RawField = "_raw";

    public static LeanDocument ToDocument(JsonElement obj, string rawJson)
    {
        var doc = new LeanDocument();
        MapObject(doc, obj, prefix: null);
        doc.Add(new StringField(RawField, rawJson));
        return doc;
    }

    private static void MapObject(LeanDocument doc, JsonElement obj, string? prefix)
    {
        foreach (var prop in obj.EnumerateObject())
        {
            string key = prefix is null ? prop.Name : $"{prefix}.{prop.Name}";
            MapValue(doc, key, prop.Value);
        }
    }

    private static void MapValue(LeanDocument doc, string key, JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.String:
                doc.Add(new TextField(key, value.GetString() ?? string.Empty));
                break;

            case JsonValueKind.Number:
                if (value.TryGetInt64(out long lval))
                    doc.Add(new NumericField(key, lval));
                else
                    doc.Add(new NumericField(key, value.GetDouble()));
                break;

            case JsonValueKind.True:
            case JsonValueKind.False:
                doc.Add(new StringField(key, value.GetBoolean() ? "true" : "false"));
                break;

            case JsonValueKind.Array:
                foreach (var item in value.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String)
                        doc.Add(new TextField(key, item.GetString() ?? string.Empty));
                    else if (item.ValueKind == JsonValueKind.Object)
                        MapObject(doc, item, key);
                }
                break;

            case JsonValueKind.Object:
                MapObject(doc, value, key);
                break;
        }
    }

    /// <summary>
    /// Reconstructs the stored fields dict into a JSON-friendly dictionary,
    /// parsing numbers back from the <c>_raw</c> field where present.
    /// </summary>
    public static Dictionary<string, object?> ToResultFields(IReadOnlyDictionary<string, IReadOnlyList<string>> stored)
    {
        var result = new Dictionary<string, object?>(stored.Count);
        foreach (var (k, vals) in stored)
        {
            if (k == RawField) continue;
            result[k] = vals.Count == 1 ? (object?)vals[0] : vals.ToList();
        }
        return result;
    }
}
