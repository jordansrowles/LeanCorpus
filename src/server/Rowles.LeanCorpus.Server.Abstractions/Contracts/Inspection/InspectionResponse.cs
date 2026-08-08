using System.Text.Json;

namespace Rowles.LeanCorpus.Server.Abstractions.Contracts.Inspection;

/// <summary>Contains the result of a bounded inspection operation.</summary>
public sealed record InspectionResponse(InspectionResource Resource, JsonElement Data, bool IsTruncated, long? MetadataEpoch = null);
