namespace Rowles.LeanCorpus.Server.Abstractions.Contracts.Administration;

/// <summary>Requests a bounded diagnostic report.</summary>
public sealed record DiagnosticsRequest(int MaximumEntries = 100);
