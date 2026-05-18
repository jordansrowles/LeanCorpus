using System.Collections.Frozen;
using System.Globalization;

namespace Rowles.LeanCorpus.Analysis.Tokenisers;

/// <summary>
/// Lightweight Thai tokeniser using greedy longest-match segmentation with a compact
/// built-in lexicon and grapheme-cluster fallback for unknown runs.
/// </summary>
public sealed class ThaiTokeniser : ITokeniser
{
    /// <summary>Token type emitted for Thai segments.</summary>
    public const string ThaiType = "thai";

    private static readonly string[] DefaultLexicon =
    [
        "การ", "ค้นหา", "ข้อมูล", "ภาษา", "ไทย", "ยินดี", "ต้อนรับ", "ระบบ", "ใหม่", "สวัสดี",
        "โลก", "นัก", "พัฒนา", "ซอฟต์แวร์", "เครื่องมือ", "วิเคราะห์", "ข้อความ", "และ", "ของ",
        "ใน", "ที่", "สำหรับ", "ข่าว", "บทความ", "ค้น", "หา", "เล็ก", "ใหญ่", "พร้อม", "ใช้งาน"
    ];

    private readonly FrozenSet<string> _lexicon;
    private readonly int _maxWordLength;

    /// <summary>
    /// Initialises a new <see cref="ThaiTokeniser"/>.
    /// </summary>
    /// <param name="lexicon">Optional Thai lexicon override. When omitted, a compact built-in lexicon is used.</param>
    public ThaiTokeniser(IEnumerable<string>? lexicon = null)
    {
        var words = (lexicon ?? DefaultLexicon)
            .Where(static word => !string.IsNullOrWhiteSpace(word))
            .Select(static word => word.Trim())
            .ToArray();
        _lexicon = words.ToFrozenSet(StringComparer.Ordinal);
        _maxWordLength = words.Length == 0 ? 0 : words.Max(static word => word.Length);
    }

    /// <inheritdoc/>
    public List<Token> Tokenise(ReadOnlySpan<char> input)
    {
        var tokens = new List<Token>();
        int i = 0;

        while (i < input.Length)
        {
            if (UnicodeTokenisation.IsThai(input[i]))
            {
                int runEnd = i + 1;
                while (runEnd < input.Length && UnicodeTokenisation.IsThai(input[runEnd]))
                    runEnd++;

                TokeniseThaiRun(input, i, runEnd, tokens);
                i = runEnd;
                continue;
            }

            if (!UnicodeTokenisation.IsWordStart(input[i]))
            {
                i++;
                continue;
            }

            int start = i;
            i = UnicodeTokenisation.ConsumeWord(input, start);
            var span = input[start..i];
            tokens.Add(new Token(span.ToString(), start, i, UnicodeTokenisation.ClassifyTokenType(span)));
        }

        return tokens;
    }

    private void TokeniseThaiRun(ReadOnlySpan<char> input, int start, int end, List<Token> tokens)
    {
        int i = start;
        while (i < end)
        {
            int matchLength = TryFindLongestLexiconMatch(input, i, end);
            if (matchLength > 0)
            {
                tokens.Add(new Token(input.Slice(i, matchLength).ToString(), i, i + matchLength, ThaiType));
                i += matchLength;
                continue;
            }

            int clusterEnd = ReadThaiCluster(input, i, end);
            tokens.Add(new Token(input[i..clusterEnd].ToString(), i, clusterEnd, ThaiType));
            i = clusterEnd;
        }
    }

    private int TryFindLongestLexiconMatch(ReadOnlySpan<char> input, int start, int end)
    {
        if (_maxWordLength == 0)
            return 0;

        int maxLength = Math.Min(_maxWordLength, end - start);
        for (int length = maxLength; length > 0; length--)
        {
            var candidate = input.Slice(start, length);
            if (_lexicon.GetAlternateLookup<ReadOnlySpan<char>>().Contains(candidate))
                return length;
        }

        return 0;
    }

    private static int ReadThaiCluster(ReadOnlySpan<char> input, int start, int end)
    {
        int i = start + 1;
        while (i < end)
        {
            var category = char.GetUnicodeCategory(input[i]);
            if (category is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark)
            {
                i++;
                continue;
            }

            break;
        }

        return i;
    }
}
