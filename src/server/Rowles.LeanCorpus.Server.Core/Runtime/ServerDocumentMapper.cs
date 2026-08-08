using System.Text.Json;
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;

namespace Rowles.LeanCorpus.Server.Core.Runtime;

/// <summary>Maps transport JSON to engine documents while retaining the caller-visible identifier.</summary>
internal static class ServerDocumentMapper
{
    internal const string DocumentIdField = "_id";
    internal const string RawDocumentField = "_raw";

    internal static LeanDocument Map(string documentId, JsonElement document)
    {
        LeanDocument result = new();
        result.Add(new StringField(DocumentIdField, documentId));
        MapObject(result, document, null);
        result.Add(new StringField(RawDocumentField, document.GetRawText()));
        return result;
    }

    private static void MapObject(LeanDocument document, JsonElement value, string? prefix)
    {
        foreach (JsonProperty property in value.EnumerateObject())
            MapValue(document, prefix is null ? property.Name : $"{prefix}.{property.Name}", property.Value);
    }

    private static void MapValue(LeanDocument document, string name, JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.String:
                document.Add(new TextField(name, value.GetString() ?? string.Empty));
                break;
            case JsonValueKind.Number when value.TryGetInt64(out long integer):
                document.Add(new NumericField(name, integer));
                break;
            case JsonValueKind.Number:
                document.Add(new NumericField(name, value.GetDouble()));
                break;
            case JsonValueKind.True:
            case JsonValueKind.False:
                document.Add(new StringField(name, value.GetBoolean() ? "true" : "false"));
                break;
            case JsonValueKind.Object:
                MapObject(document, value, name);
                break;
            case JsonValueKind.Array:
                foreach (JsonElement item in value.EnumerateArray())
                    if (item.ValueKind is JsonValueKind.Object)
                        MapObject(document, item, name);
                    else
                        MapValue(document, name, item);
                break;
        }
    }
}
