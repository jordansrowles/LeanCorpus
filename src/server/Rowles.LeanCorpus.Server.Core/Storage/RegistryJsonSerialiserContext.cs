using System.Text.Json.Serialization;

namespace Rowles.LeanCorpus.Server.Core.Storage;

/// <summary>Provides Native AOT-safe JSON metadata for the local server registry.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(ServerRegistry))]
internal partial class RegistryJsonSerialiserContext : JsonSerializerContext;
