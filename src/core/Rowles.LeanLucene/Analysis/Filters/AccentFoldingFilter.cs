using System.Globalization;
using System.Text;

namespace Rowles.LeanLucene.Analysis.Filters;

/// <summary>
/// Normalises accented/diacritic characters to their ASCII base form
/// (e.g., é→e, ñ→n, ü→u) for language-neutral matching.
/// Uses Unicode canonical decomposition followed by stripping combining marks.
/// </summary>
public sealed class AccentFoldingFilter : ITokenFilter
{
    /// <inheritdoc/>
    public void Apply(List<Token> tokens)
    {
        for (int i = 0; i < tokens.Count; i++)
        {
            var t = tokens[i];
            var folded = Fold(t.Text);
            if (!ReferenceEquals(folded, t.Text))
                tokens[i] = new Token(folded, t.StartOffset, t.EndOffset);
        }
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
