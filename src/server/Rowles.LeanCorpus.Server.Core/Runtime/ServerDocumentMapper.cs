using System.Globalization;
using System.Text;
using System.Text.Json;
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using ServerFieldType = Rowles.LeanCorpus.Server.Abstractions.Contracts.Indexing.IndexFieldType;

namespace Rowles.LeanCorpus.Server.Core.Runtime;

/// <summary>Maps Community JSON documents using the persisted index schema.</summary>
internal static class ServerDocumentMapper
{
    internal const string DocumentIdField = "_id";
    internal const string RawDocumentField = "_raw";

    internal static bool TryMap(
        string documentId,
        JsonElement document,
        CompiledIndexSchema schema,
        long maximumDocumentBytes,
        out LeanDocument? result,
        out string code,
        out string message)
    {
        result = null;
        code = string.Empty;
        message = string.Empty;
        if (string.IsNullOrWhiteSpace(documentId))
            return Fail("invalid_document_id", "Document IDs are required.", out code, out message);
        if (document.ValueKind != JsonValueKind.Object)
            return Fail("invalid_document", "Documents must be JSON objects.", out code, out message);
        if (Encoding.UTF8.GetByteCount(document.GetRawText()) > maximumDocumentBytes)
            return Fail("document_too_large", "The document exceeds the configured size limit.", out code, out message);

        LeanDocument mapped = new();
        mapped.Add(new StringField(DocumentIdField, documentId));
        foreach (JsonProperty property in document.EnumerateObject())
        {
            if (!schema.Fields.TryGetValue(property.Name, out CompiledFieldDefinition? field))
                return Fail("unknown_field", $"Field '{property.Name}' is not present in the index schema.", out code, out message);
            if (!TryMapValue(mapped, property.Name, property.Value, field, out code, out message))
                return false;
        }

        // Community 0.1 retains source JSON for bounded document browsing and IncludeDocuments responses.
        mapped.Add(new TextField(RawDocumentField, document.GetRawText(), stored: true));
        result = mapped;
        return true;
    }

    private static bool TryMapValue(LeanDocument target, string name, JsonElement value, CompiledFieldDefinition field, out string code, out string message)
    {
        code = string.Empty;
        message = string.Empty;
        if (value.ValueKind == JsonValueKind.Array && field.Source.Type != ServerFieldType.Vector)
        {
            if (!field.Source.MultiValued)
                return Fail("multi_value_not_allowed", $"Field '{name}' does not accept arrays.", out code, out message);
            foreach (JsonElement item in value.EnumerateArray())
            {
                if (!TryMapScalar(target, name, item, field, out code, out message))
                    return false;
            }
            return true;
        }
        return TryMapScalar(target, name, value, field, out code, out message);
    }

    private static bool TryMapScalar(LeanDocument target, string name, JsonElement value, CompiledFieldDefinition field, out string code, out string message)
    {
        code = string.Empty;
        message = string.Empty;
        try
        {
            bool indexed = field.Source.Indexed;
            bool stored = field.Source.Stored;
            switch (field.Source.Type)
            {
                case ServerFieldType.Text:
                    if (value.ValueKind != JsonValueKind.String)
                        return Fail("schema_validation", $"Field '{name}' requires a JSON string.", out code, out message);
                    AddText(target, name, value.GetString() ?? string.Empty, indexed, stored);
                    return true;
                case ServerFieldType.Keyword:
                    if (value.ValueKind != JsonValueKind.String)
                        return Fail("schema_validation", $"Field '{name}' requires a JSON string.", out code, out message);
                    AddKeyword(target, name, value.GetString() ?? string.Empty, indexed, stored);
                    return true;
                case ServerFieldType.Int64:
                    if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out long integer))
                        return Fail("schema_validation", $"Field '{name}' requires a JSON integer.", out code, out message);
                    AddInt64(target, name, integer, indexed, stored);
                    return true;
                case ServerFieldType.Double:
                    if (value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out double number) || !double.IsFinite(number))
                        return Fail("schema_validation", $"Field '{name}' requires a finite JSON number.", out code, out message);
                    AddDouble(target, name, number, indexed, stored);
                    return true;
                case ServerFieldType.Boolean:
                    if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                        return Fail("schema_validation", $"Field '{name}' requires a JSON Boolean.", out code, out message);
                    AddKeyword(target, name, value.GetBoolean() ? "true" : "false", indexed, stored);
                    return true;
                case ServerFieldType.DateTime:
                    if (value.ValueKind != JsonValueKind.String || !DateTimeOffset.TryParse(value.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTimeOffset timestamp))
                        return Fail("schema_validation", $"Field '{name}' requires an ISO 8601 date-time string.", out code, out message);
                    AddInt64(target, name, timestamp.ToUnixTimeMilliseconds(), indexed, stored);
                    return true;
                case ServerFieldType.Binary:
                    if (value.ValueKind != JsonValueKind.String)
                        return Fail("schema_validation", $"Field '{name}' requires a base64 JSON string.", out code, out message);
                    byte[] bytes = Convert.FromBase64String(value.GetString()!);
                    if (field.Source.Stored)
                        target.Add(new BinaryField(name, bytes));
                    return true;
                case ServerFieldType.Vector:
                    if (value.ValueKind != JsonValueKind.Array)
                        return Fail("schema_validation", $"Vector field '{name}' requires a JSON number array.", out code, out message);
                    float[] vector = value.EnumerateArray().Select(item => item.TryGetSingle(out float component) && float.IsFinite(component) ? component : float.NaN).ToArray();
                    if (vector.Length != field.Source.VectorDimensions || vector.Any(float.IsNaN))
                        return Fail("schema_validation", $"Vector field '{name}' requires exactly {field.Source.VectorDimensions} finite numbers.", out code, out message);
                    target.Add(new VectorField(name, vector));
                    return true;
                default:
                    return Fail("schema_validation", $"Field '{name}' has an unsupported type.", out code, out message);
            }
        }
        catch (Exception exception) when (exception is FormatException or OverflowException or ArgumentException)
        {
            return Fail("schema_validation", $"Field '{name}' is invalid: {exception.Message}", out code, out message);
        }
    }

    private static void AddText(LeanDocument target, string name, string value, bool indexed, bool stored)
    {
        if (indexed)
            target.Add(new TextField(name, value, stored));
        else if (stored)
            target.Add(new StoredField(name, value));
    }

    private static void AddKeyword(LeanDocument target, string name, string value, bool indexed, bool stored)
    {
        if (indexed)
            target.Add(new StringField(name, value, stored));
        else if (stored)
            target.Add(new StoredField(name, value));
    }

    private static void AddInt64(LeanDocument target, string name, long value, bool indexed, bool stored)
    {
        if (indexed)
            target.Add(new Int64Field(name, value, stored));
        else if (stored)
            target.Add(new StoredField(name, value));
    }

    private static void AddDouble(LeanDocument target, string name, double value, bool indexed, bool stored)
    {
        if (indexed)
            target.Add(new NumericField(name, value, stored));
        else if (stored)
            target.Add(new StoredField(name, value));
    }

    private static bool Fail(string failureCode, string failureMessage, out string code, out string message)
    {
        code = failureCode;
        message = failureMessage;
        return false;
    }
}
