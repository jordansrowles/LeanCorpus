using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;

namespace Rowles.DataForge;

public static class DataForgeManifestCodec
{
    private static readonly string[] PropertyOrder =
    [
        "schemaVersion",
        "canonicalFormatVersion",
        "dataForgeVersion",
        "sourceKind",
        "profileId",
        "profileVersion",
        "seed",
        "recordCount",
        "parameters",
        "dependencies",
        "logicalByteCount",
        "contentSha256",
        "artefactSha256",
        "summaries",
        "source"
    ];

    public static byte[] ToCanonicalBytes(DataForgeManifest manifest)
    {
        Validate(manifest);
        using var stream = new MemoryStream();
        Write(stream, manifest);
        return stream.ToArray();
    }

    public static void Write(Stream stream, DataForgeManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(stream);
        Validate(manifest);
        using var writer = new CanonicalJsonWriter(stream);
        writer.WriteStartObject();
        Property(writer, "schemaVersion", manifest.SchemaVersion);
        Property(writer, "canonicalFormatVersion", manifest.CanonicalFormatVersion);
        Property(writer, "dataForgeVersion", manifest.DataForgeVersion);
        writer.WritePropertyName("sourceKind");
        writer.WriteStringValue(manifest.SourceKind == DataForgeSourceKind.Generated ? "Generated" : "Imported");
        NullableStringProperty(writer, "profileId", manifest.ProfileId);
        NullableIntProperty(writer, "profileVersion", manifest.ProfileVersion);
        writer.WritePropertyName("seed");
        if (manifest.Seed is ulong seed)
            writer.WriteUInt64Value(seed);
        else
            writer.WriteNullValue();
        Property(writer, "recordCount", manifest.RecordCount);

        writer.WritePropertyName("parameters");
        writer.WriteStartObject();
        foreach (var parameter in manifest.Parameters.OrderBy(static item => item.Name, StringComparer.Ordinal))
        {
            writer.WritePropertyName(parameter.Name);
            writer.WriteStringValue(parameter.Value);
        }
        writer.WriteEndObject();

        writer.WritePropertyName("dependencies");
        writer.WriteStartArray();
        foreach (var dependency in manifest.Dependencies.OrderBy(static item => item.Name, StringComparer.Ordinal))
        {
            writer.WriteStartObject();
            writer.WritePropertyName("name");
            writer.WriteStringValue(dependency.Name);
            writer.WritePropertyName("version");
            writer.WriteStringValue(dependency.Version);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();

        Property(writer, "logicalByteCount", manifest.LogicalByteCount);
        writer.WritePropertyName("contentSha256");
        writer.WriteStringValue(manifest.ContentSha256);
        writer.WritePropertyName("artefactSha256");
        writer.WriteStringValue(manifest.ArtefactSha256);

        writer.WritePropertyName("summaries");
        writer.WriteStartArray();
        foreach (var summary in manifest.Summaries)
        {
            writer.WriteStartObject();
            writer.WritePropertyName("name");
            writer.WriteStringValue(summary.Name);
            writer.WritePropertyName("value");
            writer.WriteStringValue(summary.Value);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();

        writer.WritePropertyName("source");
        if (manifest.Source is null)
        {
            writer.WriteNullValue();
        }
        else
        {
            writer.WriteStartObject();
            foreach (var pair in manifest.Source.OrderBy(static item => item.Key, StringComparer.Ordinal))
            {
                writer.WritePropertyName(pair.Key);
                writer.WriteStringValue(pair.Value);
            }
            writer.WriteEndObject();
        }
        writer.WriteEndObject();
        writer.WriteLine();
    }

    public static DataForgeManifest Read(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length > 5 * 1024 * 1024)
            throw new InvalidDataException("Manifest exceeds the 5 MiB safety limit.");

        using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 32 });
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Manifest root must be a JSON object.");

        var propertyNames = root.EnumerateObject().Select(static property => property.Name).ToArray();
        if (!propertyNames.SequenceEqual(PropertyOrder, StringComparer.Ordinal))
            throw new InvalidDataException("Manifest properties are missing, duplicated, unknown or out of canonical order.");

        var sourceKindText = root.GetProperty("sourceKind").GetString();
        var sourceKind = sourceKindText switch
        {
            "Generated" => DataForgeSourceKind.Generated,
            "Imported" => DataForgeSourceKind.Imported,
            _ => throw new InvalidDataException($"Unsupported source kind '{sourceKindText}'.")
        };

        var parametersElement = root.GetProperty("parameters");
        if (parametersElement.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Manifest parameters must be an object.");
        var parameters = parametersElement.EnumerateObject()
            .Select(static property => new DataForgeParameter(property.Name, property.Value.GetString() ?? throw new InvalidDataException("Parameter values must be strings.")))
            .ToArray();

        var dependenciesElement = root.GetProperty("dependencies");
        if (dependenciesElement.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("Manifest dependencies must be an array.");
        var dependencies = dependenciesElement.EnumerateArray()
            .Select(static item => new DataForgeDependencyVersion(
                item.GetProperty("name").GetString() ?? throw new InvalidDataException("Dependency name must be a string."),
                item.GetProperty("version").GetString() ?? throw new InvalidDataException("Dependency version must be a string.")))
            .ToArray();

        var summariesElement = root.GetProperty("summaries");
        if (summariesElement.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("Manifest summaries must be an array.");
        var summaries = summariesElement.EnumerateArray()
            .Select(static item => new DataForgeSummary(
                item.GetProperty("name").GetString() ?? throw new InvalidDataException("Summary name must be a string."),
                item.GetProperty("value").GetString() ?? throw new InvalidDataException("Summary value must be a string.")))
            .ToArray();

        IReadOnlyDictionary<string, string>? source = null;
        var sourceElement = root.GetProperty("source");
        if (sourceElement.ValueKind == JsonValueKind.Object)
        {
            var sourceValues = new SortedDictionary<string, string>(StringComparer.Ordinal);
            foreach (var property in sourceElement.EnumerateObject())
            {
                if (!sourceValues.TryAdd(property.Name, property.Value.GetString() ?? throw new InvalidDataException("Source values must be strings.")))
                    throw new InvalidDataException($"Duplicate source field '{property.Name}'.");
            }
            source = sourceValues;
        }
        else if (sourceElement.ValueKind != JsonValueKind.Null)
        {
            throw new InvalidDataException("Manifest source must be an object or null.");
        }

        var manifest = new DataForgeManifest(
            root.GetProperty("schemaVersion").GetInt32(),
            root.GetProperty("canonicalFormatVersion").GetInt32(),
            root.GetProperty("dataForgeVersion").GetInt32(),
            sourceKind,
            NullableString(root.GetProperty("profileId")),
            NullableInt(root.GetProperty("profileVersion")),
            NullableUInt64(root.GetProperty("seed")),
            root.GetProperty("recordCount").GetInt32(),
            parameters,
            dependencies,
            root.GetProperty("logicalByteCount").GetInt64(),
            root.GetProperty("contentSha256").GetString() ?? string.Empty,
            root.GetProperty("artefactSha256").GetString() ?? string.Empty,
            summaries,
            source);

        Validate(manifest);
        if (!bytes.AsSpan().SequenceEqual(ToCanonicalBytes(manifest)))
            throw new InvalidDataException("Manifest bytes are not canonical.");
        return manifest;
    }

    public static void Validate(DataForgeManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        if (manifest.SchemaVersion != DataForgeVersions.ManifestSchemaVersion)
            throw new InvalidDataException($"Unsupported manifest schema version {manifest.SchemaVersion}.");
        if (manifest.CanonicalFormatVersion != DataForgeVersions.CanonicalFormatVersion)
            throw new InvalidDataException($"Unsupported canonical format version {manifest.CanonicalFormatVersion}.");
        if (manifest.DataForgeVersion != DataForgeVersions.DataForgeVersion)
            throw new InvalidDataException($"Unsupported DataForge version {manifest.DataForgeVersion}.");
        if (manifest.RecordCount is < 1 or > 10_000_000)
            throw new InvalidDataException("Manifest record count is outside the supported range.");
        if (manifest.LogicalByteCount < 1 || manifest.LogicalByteCount > (long)manifest.RecordCount * (4_194_304L + 1))
            throw new InvalidDataException("Manifest logical byte count is outside the supported range.");
        if (manifest.Parameters.Count > 64)
            throw new InvalidDataException("Manifest contains too many parameters.");
        var dependencyNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var dependency in manifest.Dependencies)
        {
            if (dependency is null || string.IsNullOrWhiteSpace(dependency.Name) || string.IsNullOrWhiteSpace(dependency.Version))
                throw new InvalidDataException("Manifest dependencies require non-empty names and versions.");
            if (!dependencyNames.Add(dependency.Name))
                throw new InvalidDataException($"Duplicate dependency name '{dependency.Name}'.");
        }
        ValidateSha256(manifest.ContentSha256, "contentSha256");
        ValidateSha256(manifest.ArtefactSha256, "artefactSha256");

        if (manifest.SourceKind == DataForgeSourceKind.Generated)
        {
            if (manifest.ProfileId is null || manifest.ProfileVersion is null || manifest.ProfileVersion < 1 || manifest.Seed is null)
                throw new InvalidDataException("Generated manifests require profile ID, profile version and seed.");
            if (manifest.Source is not null)
                throw new InvalidDataException("Generated manifests must not contain imported source metadata.");
            var profileIdBytes = DataForgeSeedDerivation.GetStrictUtf8ByteCount(manifest.ProfileId);
            if (profileIdBytes is < 1 or > 128)
                throw new InvalidDataException("Generated manifest profile ID must contain 1 to 128 UTF-8 bytes.");
        }
        else
        {
            if (manifest.ProfileId is not null || manifest.ProfileVersion is not null || manifest.Seed is not null)
                throw new InvalidDataException("Imported manifests must not invent generated profile or seed fields.");
            if (manifest.Source is null || !manifest.Source.TryGetValue("datasetId", out var datasetId) || string.IsNullOrWhiteSpace(datasetId) ||
                !manifest.Source.TryGetValue("datasetVersion", out var datasetVersion) ||
                !int.TryParse(datasetVersion, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedDatasetVersion) || parsedDatasetVersion < 1)
                throw new InvalidDataException("Imported manifests require source datasetId and positive datasetVersion fields.");
            if (manifest.Parameters.Count != 0)
                throw new InvalidDataException("Imported manifests do not use generated profile parameters.");
        }

        var parameterNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var parameter in manifest.Parameters)
        {
            if (!parameterNames.Add(parameter.Name))
                throw new InvalidDataException($"Duplicate parameter key '{parameter.Name}'.");
            var keyLength = DataForgeSeedDerivation.GetStrictUtf8ByteCount(parameter.Name);
            var valueLength = DataForgeSeedDerivation.GetStrictUtf8ByteCount(parameter.Value);
            if (keyLength is < 1 or > 64 || valueLength > 1_024)
                throw new InvalidDataException($"Parameter '{parameter.Name}' exceeds its UTF-8 size limit.");
        }
    }

    private static void ValidateSha256(string value, string field)
    {
        if (value.Length != 64 || value.Any(static ch => ch is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))
            throw new InvalidDataException($"Manifest {field} must be a lowercase 64-character SHA-256 value.");
    }

    private static void Property(CanonicalJsonWriter writer, string name, int value)
    {
        writer.WritePropertyName(name);
        writer.WriteInt32Value(value);
    }

    private static void Property(CanonicalJsonWriter writer, string name, long value)
    {
        writer.WritePropertyName(name);
        writer.WriteInt64Value(value);
    }

    private static void NullableStringProperty(CanonicalJsonWriter writer, string name, string? value)
    {
        writer.WritePropertyName(name);
        if (value is null)
            writer.WriteNullValue();
        else
            writer.WriteStringValue(value);
    }

    private static void NullableIntProperty(CanonicalJsonWriter writer, string name, int? value)
    {
        writer.WritePropertyName(name);
        if (value is int number)
            writer.WriteInt32Value(number);
        else
            writer.WriteNullValue();
    }

    private static string? NullableString(JsonElement element) =>
        element.ValueKind == JsonValueKind.Null ? null : element.GetString();

    private static int? NullableInt(JsonElement element) =>
        element.ValueKind == JsonValueKind.Null ? null : element.GetInt32();

    private static ulong? NullableUInt64(JsonElement element) =>
        element.ValueKind == JsonValueKind.Null ? null : element.GetUInt64();
}
