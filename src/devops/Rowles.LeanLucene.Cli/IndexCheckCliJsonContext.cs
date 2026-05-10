using System.Text.Json.Serialization;

namespace Rowles.LeanLucene.Cli;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(CliIndexCheckResultDto))]
[JsonSerializable(typeof(CliIndexFormatInventoryDto))]
[JsonSerializable(typeof(CliCompatibilityResultDto))]
[JsonSerializable(typeof(CliMigrationResultDto))]
internal sealed partial class IndexCheckCliJsonContext : JsonSerializerContext;
