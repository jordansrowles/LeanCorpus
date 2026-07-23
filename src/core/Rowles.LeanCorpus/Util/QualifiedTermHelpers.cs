using System.Runtime.CompilerServices;

namespace Rowles.LeanCorpus.Util;

internal static class QualifiedTermHelpers
{
    /// <summary>Returns the total length of a qualified term (field + '\0' + term).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int QualifiedTermLength(ReadOnlySpan<char> field, ReadOnlySpan<char> term)
        => field.Length + 1 + term.Length;

    /// <summary>Returns the field-name portion of a qualified term, or the whole string if unqualified.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ReadOnlySpan<char> GetFieldName(ReadOnlySpan<char> qualifiedTerm)
    {
        int separator = qualifiedTerm.IndexOf('\0');
        return separator < 0 ? qualifiedTerm : qualifiedTerm[..separator];
    }

    /// <summary>
    /// Writes "field\0term" into the caller-provided buffer.
    /// The buffer MUST be at least <see cref="QualifiedTermLength"/> chars long.
    /// Returns a span over the written portion.
    /// </summary>
    public static ReadOnlySpan<char> BuildQualifiedTerm(
        ReadOnlySpan<char> field, ReadOnlySpan<char> term, Span<char> buffer)
    {
        field.CopyTo(buffer);
        buffer[field.Length] = '\0';
        term.CopyTo(buffer[(field.Length + 1)..]);
        return buffer[..(field.Length + 1 + term.Length)];
    }

    /// <summary>Builds an allocating qualified term string.</summary>
    public static string BuildQualifiedTermString(ReadOnlySpan<char> field, ReadOnlySpan<char> term)
    {
        int length = QualifiedTermLength(field, term);
        Span<char> buffer = length <= 256
            ? stackalloc char[length]
            : new char[length];
        ReadOnlySpan<char> qualifiedTerm = BuildQualifiedTerm(field, term, buffer);
        return new string(qualifiedTerm);
    }
}
