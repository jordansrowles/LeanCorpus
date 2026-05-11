using System.Text.Json.Serialization;

namespace Rowles.LeanCorpus.Cli;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(CliIndexCheckResultDto))]
[JsonSerializable(typeof(CliIndexFormatInventoryDto))]
[JsonSerializable(typeof(CliCompatibilityResultDto))]
[JsonSerializable(typeof(CliMigrationResultDto))]
[JsonSerializable(typeof(CliBackupResultDto))]
[JsonSerializable(typeof(CliRestoreResultDto))]
internal sealed partial class IndexCheckCliJsonContext : JsonSerializerContext;
