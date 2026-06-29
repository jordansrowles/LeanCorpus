namespace Rowles.LeanCorpus.Analysis.Stemmers;

/// <summary>
/// Russian Snowball-inspired stemmer. Strips common Russian inflectional endings
/// written in Cyrillic. Based on the Dovgal/Snowball Russian algorithm.
/// Expects lowercased input (е and ё are NOT equated — normalise upstream).
/// </summary>
public sealed class RussianStemmer : ISpanStemmer
{
    /// <inheritdoc/>
    public string Stem(string word)
    {
        Span<char> buf = word.Length <= 64
            ? stackalloc char[word.Length]
            : new char[word.Length];
        int len = Stem(word.AsSpan(), buf);
        return len < 0 ? word : new string(buf[..len]);
    }

    /// <inheritdoc/>
    public int Stem(ReadOnlySpan<char> word, Span<char> output)
    {
        if (word.Length <= 2)
        {
            if (output.Length < word.Length) return -1;
            word.CopyTo(output);
            return word.Length;
        }
        if (output.Length < word.Length) return -1;
        word.CopyTo(output);
        int len = word.Length;

        // Step 1: Perfective gerunds (must be tried before reflexive)
        int? pg = RemoveSuffix(output, len, "ывшись", "")
               ?? RemoveSuffix(output, len, "ившись", "")
               ?? RemoveSuffix(output, len, "ывши", "")
               ?? RemoveSuffix(output, len, "ивши", "")
               ?? RemoveSuffix(output, len, "вшись", "")
               ?? RemoveSuffix(output, len, "вши", "");
        if (pg.HasValue) len = pg.Value;

        // Step 2: Reflexive endings
        len = RemoveSuffix(output, len, "ться", "")
           ?? RemoveSuffix(output, len, "тся", "")
           ?? len;

        // Step 3: Adjective / participle endings
        int? adj = RemoveSuffix(output, len, "ующего", "")
                ?? RemoveSuffix(output, len, "ующему", "")
                ?? RemoveSuffix(output, len, "ующими", "")
                ?? RemoveSuffix(output, len, "ующих", "")
                ?? RemoveSuffix(output, len, "ующим", "")
                ?? RemoveSuffix(output, len, "ующей", "")
                ?? RemoveSuffix(output, len, "ующую", "")
                ?? RemoveSuffix(output, len, "ующее", "")
                ?? RemoveSuffix(output, len, "ующие", "")
                ?? RemoveSuffix(output, len, "ующий", "")
                ?? RemoveSuffix(output, len, "ующая", "")
                ?? RemoveSuffix(output, len, "ованного", "")
                ?? RemoveSuffix(output, len, "ованному", "")
                ?? RemoveSuffix(output, len, "ованными", "")
                ?? RemoveSuffix(output, len, "ованных", "")
                ?? RemoveSuffix(output, len, "ованным", "")
                ?? RemoveSuffix(output, len, "ованной", "")
                ?? RemoveSuffix(output, len, "ованную", "")
                ?? RemoveSuffix(output, len, "ованное", "")
                ?? RemoveSuffix(output, len, "ованные", "")
                ?? RemoveSuffix(output, len, "ованный", "")
                ?? RemoveSuffix(output, len, "ованная", "")
                ?? RemoveSuffix(output, len, "ованно", "")
                ?? RemoveSuffix(output, len, "ованн", "")
                ?? RemoveSuffix(output, len, "ого", "")
                ?? RemoveSuffix(output, len, "ому", "")
                ?? RemoveSuffix(output, len, "ыми", "")
                ?? RemoveSuffix(output, len, "ими", "")
                ?? RemoveSuffix(output, len, "ых", "")
                ?? RemoveSuffix(output, len, "их", "")
                ?? RemoveSuffix(output, len, "ым", "")
                ?? RemoveSuffix(output, len, "им", "")
                ?? RemoveSuffix(output, len, "ей", "")
                ?? RemoveSuffix(output, len, "ой", "")
                ?? RemoveSuffix(output, len, "ую", "")
                ?? RemoveSuffix(output, len, "ые", "")
                ?? RemoveSuffix(output, len, "ие", "")
                ?? RemoveSuffix(output, len, "ый", "")
                ?? RemoveSuffix(output, len, "ий", "")
                ?? RemoveSuffix(output, len, "ая", "")
                ?? RemoveSuffix(output, len, "яя", "");
        if (adj.HasValue) len = adj.Value;

        // Step 4: Verb endings
        int? verb = RemoveSuffix(output, len, "ывайте", "")
                 ?? RemoveSuffix(output, len, "ивайте", "")
                 ?? RemoveSuffix(output, len, "ывать", "")
                 ?? RemoveSuffix(output, len, "ивать", "")
                 ?? RemoveSuffix(output, len, "ываю", "")
                 ?? RemoveSuffix(output, len, "иваю", "")
                 ?? RemoveSuffix(output, len, "овать", "")
                 ?? RemoveSuffix(output, len, "евать", "")
                 ?? RemoveSuffix(output, len, "уйте", "")
                 ?? RemoveSuffix(output, len, "ейте", "")
                 ?? RemoveSuffix(output, len, "ите", "")
                 ?? RemoveSuffix(output, len, "ешь", "")
                 ?? RemoveSuffix(output, len, "ишь", "")
                 ?? RemoveSuffix(output, len, "ают", "")
                 ?? RemoveSuffix(output, len, "яют", "")
                 ?? RemoveSuffix(output, len, "ают", "")
                 ?? RemoveSuffix(output, len, "ют", "")
                 ?? RemoveSuffix(output, len, "ут", "")
                 ?? RemoveSuffix(output, len, "ал", "")
                 ?? RemoveSuffix(output, len, "ял", "")
                 ?? RemoveSuffix(output, len, "ала", "")
                 ?? RemoveSuffix(output, len, "яла", "")
                 ?? RemoveSuffix(output, len, "али", "")
                 ?? RemoveSuffix(output, len, "яли", "")
                 ?? RemoveSuffix(output, len, "ать", "")
                 ?? RemoveSuffix(output, len, "ять", "")
                 ?? RemoveSuffix(output, len, "ить", "");
        if (verb.HasValue) len = verb.Value;

        // Step 5: Noun endings
        len = RemoveSuffix(output, len, "ости", "")
           ?? RemoveSuffix(output, len, "ость", "")
           ?? RemoveSuffix(output, len, "ений", "")
           ?? RemoveSuffix(output, len, "ения", "")
           ?? RemoveSuffix(output, len, "ению", "")
           ?? RemoveSuffix(output, len, "ение", "")
           ?? RemoveSuffix(output, len, "аний", "")
           ?? RemoveSuffix(output, len, "ания", "")
           ?? RemoveSuffix(output, len, "анию", "")
           ?? RemoveSuffix(output, len, "ание", "")
           ?? RemoveSuffix(output, len, "ами", "")
           ?? RemoveSuffix(output, len, "ями", "")
           ?? RemoveSuffix(output, len, "ах", "")
           ?? RemoveSuffix(output, len, "ях", "")
           ?? RemoveSuffix(output, len, "ам", "")
           ?? RemoveSuffix(output, len, "ям", "")
           ?? RemoveSuffix(output, len, "ов", "")
           ?? RemoveSuffix(output, len, "ев", "")
           ?? RemoveSuffix(output, len, "ей", "")
           ?? RemoveSuffix(output, len, "ий", "")
           ?? RemoveSuffix(output, len, "ая", "")
           ?? RemoveSuffix(output, len, "яя", "")
           ?? RemoveSuffix(output, len, "ом", "")
           ?? RemoveSuffix(output, len, "ем", "")
           ?? RemoveSuffix(output, len, "и", "")
           ?? RemoveSuffix(output, len, "е", "")
           ?? RemoveSuffix(output, len, "а", "")
           ?? RemoveSuffix(output, len, "я", "")
           ?? RemoveSuffix(output, len, "ы", "")
           ?? RemoveSuffix(output, len, "у", "")
           ?? RemoveSuffix(output, len, "ю", "")
           ?? len;

        // Step 6: Strip derivational suffix -ость / -ть if still present
        len = RemoveSuffix(output, len, "ость", "")
           ?? RemoveSuffix(output, len, "ь", "")
           ?? len;

        return len;
    }

    private static int? RemoveSuffix(Span<char> buf, int len, ReadOnlySpan<char> suffix, ReadOnlySpan<char> replacement)
    {
        if (len < suffix.Length + 2) return null;
        if (!buf.Slice(len - suffix.Length, suffix.Length).SequenceEqual(suffix)) return null;
        int stemLen = len - suffix.Length;
        if (replacement.Length > 0)
        {
            replacement.CopyTo(buf[stemLen..]);
            return stemLen + replacement.Length;
        }
        return stemLen;
    }
}
