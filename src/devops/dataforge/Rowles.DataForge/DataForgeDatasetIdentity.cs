using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;

namespace Rowles.DataForge;

public sealed record DataForgeDatasetIdentity(
    DataForgeSourceKind SourceKind,
    int DataForgeVersion,
    string? ProfileId,
    int? ProfileVersion,
    string? DatasetId,
    int? DatasetVersion,
    ulong? Seed,
    int RecordCount,
    IReadOnlyDictionary<string, string> Parameters,
    IReadOnlyList<DataForgeDependencyVersion> Dependencies,
    string ContentSha256)
{
    public byte[] GetCanonicalBytes()
    {
        ValidateDependencies(Dependencies);
        using var stream = new MemoryStream();
        using var writer = new CanonicalJsonWriter(stream);
        writer.WriteStartObject();
        writer.WritePropertyName("sourceKind");
        writer.WriteStringValue(SourceKind == DataForgeSourceKind.Generated ? "Generated" : "Imported");
        writer.WritePropertyName("dataForgeVersion");
        writer.WriteInt32Value(DataForgeVersion);
        WriteNullableString(writer, "profileId", ProfileId);
        WriteNullableInt32(writer, "profileVersion", ProfileVersion);
        WriteNullableString(writer, "datasetId", DatasetId);
        WriteNullableInt32(writer, "datasetVersion", DatasetVersion);
        writer.WritePropertyName("seed");
        if (Seed is ulong seed)
            writer.WriteUInt64Value(seed);
        else
            writer.WriteNullValue();
        writer.WritePropertyName("recordCount");
        writer.WriteInt32Value(RecordCount);
        writer.WritePropertyName("parameters");
        writer.WriteStartObject();
        foreach (var parameter in Parameters.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
        {
            writer.WritePropertyName(parameter.Key);
            writer.WriteStringValue(parameter.Value);
        }
        writer.WriteEndObject();
        writer.WritePropertyName("dependencies");
        writer.WriteStartArray();
        foreach (var dependency in Dependencies.OrderBy(static item => item.Name, StringComparer.Ordinal)
                     .ThenBy(static item => item.Version, StringComparer.Ordinal))
        {
            writer.WriteStartObject();
            writer.WritePropertyName("name");
            writer.WriteStringValue(dependency.Name);
            writer.WritePropertyName("version");
            writer.WriteStringValue(dependency.Version);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
        writer.WritePropertyName("contentSha256");
        writer.WriteStringValue(ContentSha256);
        writer.WriteEndObject();
        return stream.ToArray();
    }

    public string GetShortKey()
    {
        var hash = SHA256.HashData(GetCanonicalBytes());
        return Convert.ToHexString(hash.AsSpan(0, 8)).ToLowerInvariant();
    }

    public static DataForgeDatasetIdentity FromManifest(DataForgeManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        string? datasetId = null;
        int? datasetVersion = null;
        if (manifest.SourceKind == DataForgeSourceKind.Imported && manifest.Source is not null)
        {
            manifest.Source.TryGetValue("datasetId", out datasetId);
            if (manifest.Source.TryGetValue("datasetVersion", out var sourceVersion) &&
                int.TryParse(sourceVersion, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedVersion))
                datasetVersion = parsedVersion;
        }

        return new DataForgeDatasetIdentity(
            manifest.SourceKind,
            manifest.DataForgeVersion,
            manifest.SourceKind == DataForgeSourceKind.Generated ? manifest.ProfileId : null,
            manifest.SourceKind == DataForgeSourceKind.Generated ? manifest.ProfileVersion : null,
            datasetId,
            datasetVersion,
            manifest.Seed,
            manifest.RecordCount,
            manifest.Parameters.ToDictionary(static item => item.Name, static item => item.Value, StringComparer.Ordinal),
            manifest.Dependencies.ToArray(),
            manifest.ContentSha256);
    }

    private static void ValidateDependencies(IReadOnlyList<DataForgeDependencyVersion> dependencies)
    {
        ArgumentNullException.ThrowIfNull(dependencies);
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var dependency in dependencies)
        {
            if (dependency is null || string.IsNullOrWhiteSpace(dependency.Name) || string.IsNullOrWhiteSpace(dependency.Version))
                throw new InvalidDataException("DataForge identity dependencies require non-empty names and versions.");
            if (!names.Add(dependency.Name))
                throw new InvalidDataException($"DataForge identity contains duplicate dependency '{dependency.Name}'.");
        }
    }

    private static void WriteNullableString(CanonicalJsonWriter writer, string name, string? value)
    {
        writer.WritePropertyName(name);
        if (value is null)
            writer.WriteNullValue();
        else
            writer.WriteStringValue(value);
    }

    private static void WriteNullableInt32(CanonicalJsonWriter writer, string name, int? value)
    {
        writer.WritePropertyName(name);
        if (value is int number)
            writer.WriteInt32Value(number);
        else
            writer.WriteNullValue();
    }
}
