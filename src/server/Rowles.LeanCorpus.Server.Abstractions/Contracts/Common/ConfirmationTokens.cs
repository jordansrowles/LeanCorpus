using System.Security.Cryptography;
using System.Text;

namespace Rowles.LeanCorpus.Server.Abstractions.Contracts.Common;

/// <summary>Creates operation-specific Community confirmation tokens.</summary>
public static class ConfirmationTokens
{
    /// <summary>Creates the deterministic token accepted for a destructive operation.</summary>
    public static string Create(string operation, string resource)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        ArgumentException.ThrowIfNullOrWhiteSpace(resource);
        byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes($"leancorpus-community-confirm\0{operation}\0{resource}"));
        return Convert.ToHexString(digest).ToLowerInvariant();
    }

    /// <summary>Checks a supplied token without exposing server secrets.</summary>
    public static bool IsValid(string? token, string operation, string resource)
        => !string.IsNullOrWhiteSpace(token)
            && CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(token),
                Encoding.UTF8.GetBytes(Create(operation, resource)));
}
