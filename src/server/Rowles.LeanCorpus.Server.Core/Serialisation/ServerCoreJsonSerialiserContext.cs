using System.Text.Json.Serialization;
using Rowles.LeanCorpus.Diagnostics;
using Rowles.LeanCorpus.Server.Abstractions.Contracts.Indexing;

namespace Rowles.LeanCorpus.Server.Core.Serialisation;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(IndexSummary[]))]
[JsonSerializable(typeof(StorageInspectionPayload))]
[JsonSerializable(typeof(ReaderInspectionPayload))]
[JsonSerializable(typeof(BoundedInspectionDocument[]))]
[JsonSerializable(typeof(IndexFieldDefinition[]))]
[JsonSerializable(typeof(SegmentSizeReport[]))]
internal partial class ServerCoreJsonSerialiserContext : JsonSerializerContext;

internal sealed record StorageInspectionPayload(string IndexName, long TotalSizeBytes, int SegmentCount, IReadOnlyList<SegmentSizeReport> Segments);

internal sealed record ReaderInspectionPayload(
    string IndexName,
    int ActiveReaders,
    int ActiveLeases,
    long Refreshes,
    long RefreshFailures,
    long DisposedReaders,
    long ConsecutiveRefreshFailures,
    string? LastRefreshError);

internal sealed record BoundedInspectionDocument(string DocumentId, string Json, bool Truncated);
