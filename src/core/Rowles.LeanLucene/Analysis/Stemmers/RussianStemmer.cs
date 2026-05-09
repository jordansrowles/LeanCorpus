namespace Rowles.LeanLucene.Analysis.Stemmers;

/// <summary>
/// Russian Snowball-inspired stemmer. Strips common Russian inflectional endings
/// written in Cyrillic. Based on the Dovgal/Snowball Russian algorithm.
/// Expects lowercased input (е and ё are NOT equated — normalise upstream).
/// </summary>
public sealed class RussianStemmer : IStemmer
{
    /// <inheritdoc/>
    public string Stem(string word)
    {
        if (word.Length <= 2) return word;

        Span<char> buf = word.Length <= 64
            ? stackalloc char[word.Length]
            : new char[word.Length];
        word.AsSpan().CopyTo(buf);
        int len = buf.Length;

        // Step 1: Perfective gerunds (must be tried before reflexive)
        int? pg = RemoveSuffix(buf, len, "ывшись", "")
               ?? RemoveSuffix(buf, len, "ившись", "")
               ?? RemoveSuffix(buf, len, "ывши", "")
               ?? RemoveSuffix(buf, len, "ивши", "")
               ?? RemoveSuffix(buf, len, "вшись", "")
               ?? RemoveSuffix(buf, len, "вши", "");
        if (pg.HasValue) len = pg.Value;

        // Step 2: Reflexive endings
        len = RemoveSuffix(buf, len, "ться", "")
           ?? RemoveSuffix(buf, len, "тся", "")
           ?? len;

        // Step 3: Adjective / participle endings
        int? adj = RemoveSuffix(buf, len, "ующего", "")
                ?? RemoveSuffix(buf, len, "ующему", "")
                ?? RemoveSuffix(buf, len, "ующими", "")
                ?? RemoveSuffix(buf, len, "ующих", "")
                ?? RemoveSuffix(buf, len, "ующим", "")
                ?? RemoveSuffix(buf, len, "ующей", "")
                ?? RemoveSuffix(buf, len, "ующую", "")
                ?? RemoveSuffix(buf, len, "ующее", "")
                ?? RemoveSuffix(buf, len, "ующие", "")
                ?? RemoveSuffix(buf, len, "ующий", "")
                ?? RemoveSuffix(buf, len, "ующая", "")
                ?? RemoveSuffix(buf, len, "ованного", "")
                ?? RemoveSuffix(buf, len, "ованному", "")
                ?? RemoveSuffix(buf, len, "ованными", "")
                ?? RemoveSuffix(buf, len, "ованных", "")
                ?? RemoveSuffix(buf, len, "ованным", "")
                ?? RemoveSuffix(buf, len, "ованной", "")
                ?? RemoveSuffix(buf, len, "ованную", "")
                ?? RemoveSuffix(buf, len, "ованное", "")
                ?? RemoveSuffix(buf, len, "ованные", "")
                ?? RemoveSuffix(buf, len, "ованный", "")
                ?? RemoveSuffix(buf, len, "ованная", "")
                ?? RemoveSuffix(buf, len, "ованно", "")
                ?? RemoveSuffix(buf, len, "ованн", "")
                ?? RemoveSuffix(buf, len, "ого", "")
                ?? RemoveSuffix(buf, len, "ому", "")
                ?? RemoveSuffix(buf, len, "ыми", "")
                ?? RemoveSuffix(buf, len, "ими", "")
                ?? RemoveSuffix(buf, len, "ых", "")
                ?? RemoveSuffix(buf, len, "их", "")
                ?? RemoveSuffix(buf, len, "ым", "")
                ?? RemoveSuffix(buf, len, "им", "")
                ?? RemoveSuffix(buf, len, "ей", "")
                ?? RemoveSuffix(buf, len, "ой", "")
                ?? RemoveSuffix(buf, len, "ую", "")
                ?? RemoveSuffix(buf, len, "ые", "")
                ?? RemoveSuffix(buf, len, "ие", "")
                ?? RemoveSuffix(buf, len, "ый", "")
                ?? RemoveSuffix(buf, len, "ий", "")
                ?? RemoveSuffix(buf, len, "ая", "")
                ?? RemoveSuffix(buf, len, "яя", "");
        if (adj.HasValue) len = adj.Value;

        // Step 4: Verb endings
        int? verb = RemoveSuffix(buf, len, "ывайте", "")
                 ?? RemoveSuffix(buf, len, "ивайте", "")
                 ?? RemoveSuffix(buf, len, "ывать", "")
                 ?? RemoveSuffix(buf, len, "ивать", "")
                 ?? RemoveSuffix(buf, len, "ываю", "")
                 ?? RemoveSuffix(buf, len, "иваю", "")
                 ?? RemoveSuffix(buf, len, "овать", "")
                 ?? RemoveSuffix(buf, len, "евать", "")
                 ?? RemoveSuffix(buf, len, "уйте", "")
                 ?? RemoveSuffix(buf, len, "ейте", "")
                 ?? RemoveSuffix(buf, len, "ите", "")
                 ?? RemoveSuffix(buf, len, "ешь", "")
                 ?? RemoveSuffix(buf, len, "ишь", "")
                 ?? RemoveSuffix(buf, len, "ают", "")
                 ?? RemoveSuffix(buf, len, "яют", "")
                 ?? RemoveSuffix(buf, len, "ают", "")
                 ?? RemoveSuffix(buf, len, "ют", "")
                 ?? RemoveSuffix(buf, len, "ут", "")
                 ?? RemoveSuffix(buf, len, "ал", "")
                 ?? RemoveSuffix(buf, len, "ял", "")
                 ?? RemoveSuffix(buf, len, "ала", "")
                 ?? RemoveSuffix(buf, len, "яла", "")
                 ?? RemoveSuffix(buf, len, "али", "")
                 ?? RemoveSuffix(buf, len, "яли", "")
                 ?? RemoveSuffix(buf, len, "ать", "")
                 ?? RemoveSuffix(buf, len, "ять", "")
                 ?? RemoveSuffix(buf, len, "ить", "");
        if (verb.HasValue) len = verb.Value;

        // Step 5: Noun endings
        len = RemoveSuffix(buf, len, "ости", "")
           ?? RemoveSuffix(buf, len, "ость", "")
           ?? RemoveSuffix(buf, len, "ений", "")
           ?? RemoveSuffix(buf, len, "ения", "")
           ?? RemoveSuffix(buf, len, "ению", "")
           ?? RemoveSuffix(buf, len, "ение", "")
           ?? RemoveSuffix(buf, len, "аний", "")
           ?? RemoveSuffix(buf, len, "ания", "")
           ?? RemoveSuffix(buf, len, "анию", "")
           ?? RemoveSuffix(buf, len, "ание", "")
           ?? RemoveSuffix(buf, len, "ами", "")
           ?? RemoveSuffix(buf, len, "ями", "")
           ?? RemoveSuffix(buf, len, "ах", "")
           ?? RemoveSuffix(buf, len, "ях", "")
           ?? RemoveSuffix(buf, len, "ам", "")
           ?? RemoveSuffix(buf, len, "ям", "")
           ?? RemoveSuffix(buf, len, "ов", "")
           ?? RemoveSuffix(buf, len, "ев", "")
           ?? RemoveSuffix(buf, len, "ей", "")
           ?? RemoveSuffix(buf, len, "ий", "")
           ?? RemoveSuffix(buf, len, "ая", "")
           ?? RemoveSuffix(buf, len, "яя", "")
           ?? RemoveSuffix(buf, len, "ом", "")
           ?? RemoveSuffix(buf, len, "ем", "")
           ?? RemoveSuffix(buf, len, "и", "")
           ?? RemoveSuffix(buf, len, "е", "")
           ?? RemoveSuffix(buf, len, "а", "")
           ?? RemoveSuffix(buf, len, "я", "")
           ?? RemoveSuffix(buf, len, "ы", "")
           ?? RemoveSuffix(buf, len, "у", "")
           ?? RemoveSuffix(buf, len, "ю", "")
           ?? len;

        // Step 6: Strip derivational suffix -ость / -ть if still present
        len = RemoveSuffix(buf, len, "ость", "")
           ?? RemoveSuffix(buf, len, "ь", "")
           ?? len;

        var result = buf[..len];
        return result.SequenceEqual(word.AsSpan()) ? word : new string(result);
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
