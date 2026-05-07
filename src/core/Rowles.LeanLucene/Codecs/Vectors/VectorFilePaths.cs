using System.Security.Cryptography;
using System.Text;

namespace Rowles.LeanLucene.Codecs.Vectors;

/// <summary>
/// Helpers for resolving per-field vector and HNSW file paths within a segment.
/// Convention: "<c>{segmentId}_v_{sanitisedFieldName}.{ext}</c>" so multiple vector fields
/// can co-exist in one segment without colliding with the legacy single-vector layout.
/// </summary>
internal static class VectorFilePaths
{
    /// <summary>Returns the per-field <c>.vec</c> file path.</summary>
    public static string VectorFile(string basePath, string fieldName)
        => $"{basePath}_v_{Sanitise(fieldName)}.vec";

    /// <summary>Returns the per-field <c>.hnsw</c> file path.</summary>
    public static string HnswFile(string basePath, string fieldName)
        => $"{basePath}_v_{Sanitise(fieldName)}.hnsw";

    /// <summary>
    /// Returns a filesystem-safe representation of <paramref name="fieldName"/>.
    /// Unsafe names receive a hash suffix so different names cannot collapse to the same path.
    /// </summary>
    public static string Sanitise(string fieldName)
    {
        ArgumentException.ThrowIfNullOrEmpty(fieldName);
        Span<char> buf = fieldName.Length <= 256 ? stackalloc char[fieldName.Length] : new char[fieldName.Length];
        bool changed = false;
        for (int i = 0; i < fieldName.Length; i++)
        {
            char c = fieldName[i];
            if (char.IsAsciiLetterOrDigit(c) || c == '_')
            {
                buf[i] = c;
            }
            else
            {
                buf[i] = '_';
                changed = true;
            }
        }

        string safe = new(buf);
        if (!changed)
            return safe;

        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(fieldName));
        return $"{safe}_{Convert.ToHexString(hash, 0, 8)}";
    }
}
