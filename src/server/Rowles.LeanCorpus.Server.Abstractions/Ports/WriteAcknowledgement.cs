namespace Rowles.LeanCorpus.Server.Abstractions.Ports;

/// <summary>Describes the acknowledgement returned for a write.</summary>
public sealed record WriteAcknowledgement(bool IsAcknowledged, WriteDurability Durability, string? Reason = null);
