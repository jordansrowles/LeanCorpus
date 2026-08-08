namespace Rowles.LeanCorpus.Analysis.Tokenisers.Japanese;

internal readonly ref struct CharacterDefinition
{
    internal const int CharacterCount = 0x10000;
    internal const int ClassCount = 12;
    internal const byte Ngram = 0;
    internal const byte Default = 1;
    internal const byte Space = 2;
    internal const byte Symbol = 3;
    internal const byte Numeric = 4;
    internal const byte Alpha = 5;
    internal const byte Cyrillic = 6;
    internal const byte Greek = 7;
    internal const byte Hiragana = 8;
    internal const byte Katakana = 9;
    internal const byte Kanji = 10;
    internal const byte KanjiNumeric = 11;

    private readonly ReadOnlySpan<byte> _data;

    internal CharacterDefinition(ReadOnlySpan<byte> data)
    {
        if (data.Length != CharacterCount + ClassCount)
            throw new InvalidDataException("Japanese character definition section has an invalid length.");

        _data = data;
    }

    internal byte GetClass(char value) => _data[value];

    internal bool IsInvoke(char value) => (_data[CharacterCount + GetClass(value)] & 1) != 0;

    internal bool IsGroup(char value) => (_data[CharacterCount + GetClass(value)] & 2) != 0;
}
