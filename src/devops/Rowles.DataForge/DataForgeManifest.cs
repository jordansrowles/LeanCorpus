using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;

namespace Rowles.DataForge;

public sealed record DataForgeManifest(
    int SchemaVersion,
    int CanonicalFormatVersion,
    int DataForgeVersion,
    DataForgeSourceKind SourceKind,
    string? ProfileId,
    int? ProfileVersion,
    ulong? Seed,
    int RecordCount,
    IReadOnlyList<DataForgeParameter> Parameters,
    IReadOnlyList<DataForgeDependencyVersion> Dependencies,
    long LogicalByteCount,
    string ContentSha256,
    string ArtefactSha256,
    IReadOnlyList<DataForgeSummary> Summaries,
    IReadOnlyDictionary<string, string>? Source);
