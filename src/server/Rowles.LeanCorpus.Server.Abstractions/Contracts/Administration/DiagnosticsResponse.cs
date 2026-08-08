using System.Text.Json;

namespace Rowles.LeanCorpus.Server.Abstractions.Contracts.Administration;

/// <summary>Contains a redacted diagnostic report.</summary>
public sealed record DiagnosticsResponse(JsonElement Data, bool IsTruncated);
