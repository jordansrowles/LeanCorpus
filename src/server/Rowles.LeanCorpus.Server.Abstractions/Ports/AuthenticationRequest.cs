namespace Rowles.LeanCorpus.Server.Abstractions.Ports;

/// <summary>Provides transport-normalised authentication material to an authentication provider.</summary>
public sealed record AuthenticationRequest(string? Scheme, string? Credential, IReadOnlyDictionary<string, string>? Attributes = null);
