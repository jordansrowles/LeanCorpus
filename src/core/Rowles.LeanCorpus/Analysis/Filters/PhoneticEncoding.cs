namespace Rowles.LeanCorpus.Analysis.Filters;

internal static class PhoneticEncoding
{
    public static string EncodeMetaphone(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        string value = AccentFoldingFilter.Fold(text).ToUpperInvariant();
        Span<char> scratch = value.Length <= 64 ? stackalloc char[value.Length] : new char[value.Length];

        int compactLength = 0;
        for (int i = 0; i < value.Length; i++)
        {
            if (i > 0 && value[i] == value[i - 1] && value[i] != 'C')
                continue;

            scratch[compactLength++] = value[i];
        }

        var source = scratch[..compactLength];
        if (source.Length >= 2 && source[0] is 'K' or 'G' or 'P' && source[1] == 'N')
            source = source[1..];
        else if (source.Length >= 2 && source[0] == 'A' && source[1] == 'E')
            source = source[1..];
        else if (source.Length >= 2 && source[0] == 'W' && source[1] == 'R')
            source = source[1..];
        else if (source.Length > 0 && source[0] == 'X')
            source[0] = 'S';

        var builder = new System.Text.StringBuilder(source.Length + 4);
        for (int i = 0; i < source.Length; i++)
        {
            char c = source[i];
            char next = i + 1 < source.Length ? source[i + 1] : '\0';
            char next2 = i + 2 < source.Length ? source[i + 2] : '\0';
            char previous = i > 0 ? source[i - 1] : '\0';

            switch (c)
            {
                case 'A':
                case 'E':
                case 'I':
                case 'O':
                case 'U':
                    if (i == 0)
                        builder.Append(c);
                    break;
                case 'B':
                    if (!(i == source.Length - 1 && previous == 'M'))
                        builder.Append('B');
                    break;
                case 'C':
                    if (next == 'H')
                    {
                        builder.Append('X');
                        i++;
                    }
                    else if (next == 'I' && next2 == 'A')
                    {
                        builder.Append('X');
                        i += 2;
                    }
                    else if (next is 'I' or 'E' or 'Y')
                    {
                        builder.Append('S');
                        i++;
                    }
                    else
                    {
                        builder.Append('K');
                    }
                    break;
                case 'D':
                    if (next == 'G' && next2 is 'E' or 'I' or 'Y')
                    {
                        builder.Append('J');
                        i += 2;
                    }
                    else
                    {
                        builder.Append('T');
                    }
                    break;
                case 'F':
                case 'J':
                case 'L':
                case 'M':
                case 'N':
                case 'R':
                    builder.Append(c);
                    break;
                case 'G':
                    if (next == 'H')
                    {
                        if (i + 2 < source.Length && !"AEIOU".Contains(next2))
                        {
                            builder.Append('K');
                        }
                        i++;
                    }
                    else if (next == 'N')
                    {
                        builder.Append('N');
                        i++;
                    }
                    else if (next is 'E' or 'I' or 'Y')
                    {
                        builder.Append('J');
                        i++;
                    }
                    else
                    {
                        builder.Append('K');
                    }
                    break;
                case 'H':
                    if ("AEIOU".Contains(next) && !"CSPTG".Contains(previous))
                        builder.Append('H');
                    break;
                case 'K':
                    if (previous != 'C')
                        builder.Append('K');
                    break;
                case 'P':
                    if (next == 'H')
                    {
                        builder.Append('F');
                        i++;
                    }
                    else
                    {
                        builder.Append('P');
                    }
                    break;
                case 'Q':
                    builder.Append('K');
                    break;
                case 'S':
                    if (next == 'H' || (next == 'I' && next2 is 'O' or 'A'))
                    {
                        builder.Append('X');
                        if (next == 'H')
                            i++;
                        else
                            i += 2;
                    }
                    else
                    {
                        builder.Append('S');
                    }
                    break;
                case 'T':
                    if (next == 'H')
                    {
                        builder.Append('0');
                        i++;
                    }
                    else if (next == 'I' && next2 is 'O' or 'A')
                    {
                        builder.Append('X');
                        i += 2;
                    }
                    else if (!(next == 'C' && next2 == 'H'))
                    {
                        builder.Append('T');
                    }
                    break;
                case 'V':
                    builder.Append('F');
                    break;
                case 'W':
                case 'Y':
                    if ("AEIOU".Contains(next))
                        builder.Append(c);
                    break;
                case 'X':
                    builder.Append("KS");
                    break;
                case 'Z':
                    builder.Append('S');
                    break;
            }
        }

        return builder.ToString();
    }

    public static IReadOnlyList<string> EncodeLatinNameAlternates(string text, int maxExpansions)
    {
        var variants = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<string>();
        string seed = AccentFoldingFilter.Fold(text).ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(seed))
            return Array.Empty<string>();

        queue.Enqueue(seed);
        variants.Add(seed);

        while (queue.Count > 0 && variants.Count < maxExpansions)
        {
            var current = queue.Dequeue();
            foreach (var variant in Expand(current))
            {
                if (variants.Count >= maxExpansions)
                    break;

                if (variants.Add(variant))
                    queue.Enqueue(variant);
            }
        }

        return variants
            .Select(EncodeMetaphone)
            .Where(static code => !string.IsNullOrEmpty(code))
            .Distinct(StringComparer.Ordinal)
            .Take(maxExpansions)
            .ToArray();
    }

    private static IEnumerable<string> Expand(string text)
    {
        if (text.Contains("sch", StringComparison.Ordinal))
        {
            yield return text.Replace("sch", "sk", StringComparison.Ordinal);
            yield return text.Replace("sch", "sh", StringComparison.Ordinal);
        }

        if (text.Contains('w'))
            yield return text.Replace('w', 'v');
        if (text.Contains('j'))
            yield return text.Replace('j', 'y');
        if (text.Contains("cz", StringComparison.Ordinal))
        {
            yield return text.Replace("cz", "ts", StringComparison.Ordinal);
            yield return text.Replace("cz", "ch", StringComparison.Ordinal);
        }
        if (text.Contains("ph", StringComparison.Ordinal))
            yield return text.Replace("ph", "f", StringComparison.Ordinal);
        if (text.Contains("tz", StringComparison.Ordinal))
            yield return text.Replace("tz", "ts", StringComparison.Ordinal);
        if (text.Contains("kh", StringComparison.Ordinal))
            yield return text.Replace("kh", "h", StringComparison.Ordinal);
    }
}
