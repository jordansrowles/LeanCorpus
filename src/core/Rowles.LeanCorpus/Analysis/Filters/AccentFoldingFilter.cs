using System.Globalization;
using System.Text;

namespace Rowles.LeanCorpus.Analysis.Filters;

/// <summary>
/// Normalises accented/diacritic characters to their ASCII base form
/// (e.g., é→e, ñ→n, ü→u) for language-neutral matching.
/// Uses Unicode canonical decomposition followed by stripping combining marks.
/// </summary>
public sealed class AccentFoldingFilter : ISpanTokenFilter
{
    /// <inheritdoc/>
    public void Apply(
        ReadOnlySpan<char> text,
        int startOffset,
        int endOffset,
        string type,
        int positionIncrement,
        byte[]? payload,
        ISpanTokenSink sink)
    {
        var folded = Fold(new string(text));
        sink.Add(folded.AsSpan(), startOffset, endOffset, type, positionIncrement, payload);
    }

    /// <summary>
    /// Folds accents and diacritics from the input string.
    /// Returns the original reference if no changes were needed.
    /// </summary>
    internal static string Fold(string input)
    {
        var normalised = input.Normalize(NormalizationForm.FormD);
        if (normalised.Length == input.Length)
        {
            // Quick check: if lengths match, likely no combining chars
            bool hasCombining = false;
            for (int i = 0; i < normalised.Length; i++)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(normalised[i]) == UnicodeCategory.NonSpacingMark)
                {
                    hasCombining = true;
                    break;
                }
            }
            if (!hasCombining) return input;
        }

        Span<char> buffer = normalised.Length <= 128
            ? stackalloc char[normalised.Length]
            : new char[normalised.Length];

        int writePos = 0;
        for (int i = 0; i < normalised.Length; i++)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(normalised[i]) != UnicodeCategory.NonSpacingMark)
                buffer[writePos++] = normalised[i];
        }

        return new string(buffer[..writePos]).Normalize(NormalizationForm.FormC);
    }
}
