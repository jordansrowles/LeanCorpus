using System.Security.Cryptography;
using System.Text;
using Rowles.LeanCorpus.Analysis.Stemmers;
using Xunit;

namespace Rowles.Text.Tests;

/// <summary>
/// Whole-word-list output of the Snowball-style suffix stemmers. Each language's word list is
/// built from short and long stems joined with every declared rule suffix, one and two deep,
/// plus seeded random words over the language alphabet and all three-letter endings of each stem.
/// The hashes were recorded from the linear rule scan before rules were grouped by final
/// character, so any change to any stem changes the hash.
/// </summary>
[Category(TestCategory.Unit)]
[Area(TestArea.Stemmers)]
public sealed class SnowballStemmerWordListTests
{
    private static readonly Dictionary<string, LanguageWords> Languages = new(StringComparer.Ordinal)
    {
        ["Dutch"] = new LanguageWords(new DutchStemmer(), ["b", "ka", "huis", "werk", "loop", "bevrijd", "gemeent"], ["achtig", "baar", "dde", "dden", "de", "den", "en", "ende", "enden", "ens", "eren", "erij", "ers", "es", "heden", "heid", "ing", "ingen", "isme", "ist", "lijk", "s", "ster", "te", "ten", "tte", "tten", "zaam"], "abcdefghijklmnopqrstuvwxyzèéëïöü"),
        ["French"] = new LanguageWords(new FrenchStemmer(), ["p", "ch", "parl", "chant", "maison", "nation", "heureu"], ["aient", "ais", "ait", "ation", "ations", "aux", "ement", "ements", "ent", "er", "erez", "erons", "eront", "es", "eur", "eurs", "euse", "euses", "eux", "ez", "if", "ifs", "ir", "irent", "issait", "issant", "issement", "issements", "issent", "issez", "issons", "it", "ité", "ités", "ive", "ives", "s", "é", "ée", "ées", "és"], "abcdefghijklmnopqrstuvwxyzàâçéèêëîïôûùüÿœ"),
        ["German"] = new LanguageWords(new GermanStemmer(), ["h", "ha", "haus", "kind", "freund", "arbeit", "schön"], ["e", "em", "en", "ens", "er", "ern", "erns", "ers", "erung", "erungen", "es", "est", "et", "etet", "heit", "heiten", "heits", "ig", "ige", "igem", "igen", "iger", "iges", "innen", "isch", "ische", "ischem", "ischen", "ischer", "isches", "ismus", "ist", "isten", "keit", "keiten", "lich", "liche", "lichem", "lichen", "licher", "liches", "nd", "s", "schaft", "schaften", "te", "test", "tet", "ung", "ungen"], "abcdefghijklmnopqrstuvwxyzäöüß"),
        ["Italian"] = new LanguageWords(new ItalianStemmer(), ["c", "pa", "parl", "cant", "nazion", "felic", "bell"], ["a", "abile", "abili", "amenti", "amento", "ando", "andosi", "anno", "are", "arono", "arsi", "ate", "ati", "ato", "avano", "azione", "azioni", "e", "endo", "endosi", "ere", "erei", "erono", "ersi", "evano", "i", "ibile", "ibili", "imenti", "imento", "ire", "irei", "irono", "irsi", "ismi", "ismo", "ista", "isti", "ita", "ite", "iti", "ito", "ità", "ivano", "mente", "o", "ose", "osi", "uta", "ute", "uti", "uto"], "abcdefghijklmnopqrstuvwxyzàèéìíîòóùú"),
        ["Portuguese"] = new LanguageWords(new PortugueseStemmer(), ["c", "fa", "fal", "cant", "nacion", "feliz", "bel"], ["a", "ada", "adas", "ado", "ados", "amento", "amentos", "amos", "ando", "ar", "aram", "as", "ava", "avam", "ação", "ações", "emos", "endo", "er", "eram", "es", "ia", "ias", "ida", "idade", "idades", "idas", "ido", "idos", "imento", "imentos", "imos", "indo", "ir", "iram", "ismo", "ismos", "ista", "istas", "mente", "o", "os", "áveis", "ável", "íveis", "ível"], "abcdefghijklmnopqrstuvwxyzáâãàçéêíóôõú"),
        ["Russian"] = new LanguageWords(new RussianStemmer(), ["к", "до", "кни", "говор", "красив", "работ", "счаст"], ["а", "ал", "ала", "али", "ам", "ами", "ание", "аний", "анию", "ания", "ать", "ах", "ают", "ая", "вши", "вшись", "е", "ев", "евать", "ей", "ейте", "ем", "ение", "ений", "ению", "ения", "ешь", "и", "ивайте", "ивать", "иваю", "ивши", "ившись", "ие", "ий", "им", "ими", "ите", "ить", "их", "ишь", "ов", "ованн", "ованная", "ованно", "ованного", "ованное", "ованной", "ованному", "ованную", "ованные", "ованный", "ованным", "ованными", "ованных", "овать", "ого", "ой", "ом", "ому", "ости", "ость", "тся", "ться", "у", "уйте", "ут", "ую", "ующая", "ующего", "ующее", "ующей", "ующему", "ующие", "ующий", "ующим", "ующими", "ующих", "ующую", "ы", "ывайте", "ывать", "ываю", "ывши", "ывшись", "ые", "ый", "ым", "ыми", "ых", "ь", "ю", "ют", "я", "ял", "яла", "яли", "ям", "ями", "ять", "ях", "яют", "яя"], "абвгдеёжзийклмнопрстуфхцчшщъыьэюя"),
        ["Spanish"] = new LanguageWords(new SpanishStemmer(), ["c", "ha", "habl", "cant", "nacion", "feliz", "bell"], ["a", "aba", "aban", "abas", "able", "ables", "aciones", "ación", "ada", "adas", "ado", "ados", "amiento", "amientos", "amos", "ando", "ar", "aron", "arán", "aré", "as", "emos", "er", "erán", "eré", "es", "ible", "ibles", "ida", "idad", "idades", "idas", "ido", "idos", "iendo", "ieron", "imiento", "imientos", "imos", "ir", "irán", "iré", "ismo", "ismos", "ista", "istas", "iéndose", "mente", "o", "os", "uciones", "ución", "ándome", "ándose", "ía", "ían", "ías"], "abcdefghijklmnopqrstuvwxyzáéíñóúü"),
    };

    // Recorded at c16f3e4e2 with the linear suffix scan.
    private static readonly Dictionary<string, (int Words, string Sha256)> Golden = new(StringComparer.Ordinal)
    {
        ["Dutch"] = (255067, "bb05486cbe8cf73eb68e0e773f707f7f74db9fbba8a18c7115695da223fe01d7"),
        ["French"] = (514508, "7ebd11dc05b33986846a225b310a30809f7b6131e7c202f737cbed2ff66f25a0"),
        ["German"] = (226857, "0fdb1dd99ec7d7b0d7c634a09f0be92d07aa03035c0ec827ff5dcac8ff961509"),
        ["Italian"] = (365891, "5fce2af8a6c897980dbfa7c933fb13677560b473697744ad032600ed3a028bf0"),
        ["Portuguese"] = (419245, "b9dfe72779392b3e530a4ac9a1ad0c8066f661381c9714cbe156ff080df32f0c"),
        ["Russian"] = (346550, "11a86f4224defd439e6106d6f586613997bb712ac74169de77592aaa9e6af5db"),
        ["Spanish"] = (294708, "10874bc26a2a01e8de774b08a14626f5c92f7ad9e43c131f14a2d56948482be8"),
    };

    public static TheoryData<string> LanguageNames() => new(Languages.Keys);

    [Theory(DisplayName = "Snowball stemmers: word list output is unchanged")]
    [MemberData(nameof(LanguageNames))]
    public void WordListOutput_IsUnchanged(string language)
    {
        var words = Languages[language].BuildWords();
        var stemmer = Languages[language].Stemmer;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<char> buffer = stackalloc char[256];
        foreach (string word in words)
        {
            string viaString = stemmer.Stem(word);
            int length = stemmer.Stem(word.AsSpan(), buffer);
            Assert.Equal(viaString, length < 0 ? word : new string(buffer[..length]));
            hash.AppendData(Encoding.UTF8.GetBytes(word + "\t" + viaString + "\n"));
        }

        string actual = Convert.ToHexStringLower(hash.GetHashAndReset());
        Assert.True(Golden.TryGetValue(language, out var golden) && golden == (words.Count, actual), $"{language}: {words.Count} words, SHA-256 {actual}");
    }

    private sealed record LanguageWords(SnowballStemmer Stemmer, string[] Stems, string[] Endings, string Alphabet)
    {
        public List<string> BuildWords()
        {
            var words = new List<string>();
            foreach (string stem in Stems)
            {
                words.Add(stem);
                foreach (string ending in Endings)
                {
                    words.Add(stem + ending);
                    foreach (string second in Endings)
                        words.Add(stem + ending + second);
                }
                foreach (char a in Alphabet)
                    foreach (char b in Alphabet)
                        foreach (char c in Alphabet)
                            words.Add(string.Concat(stem, a.ToString(), b.ToString(), c.ToString()));
            }

            var random = new Random(Alphabet.Length * 7_919); // NOSONAR: S2245, a seeded generator makes the word list reproducible; nothing here is security-sensitive.
            for (int i = 0; i < 20_000; i++)
            {
                var chars = new char[random.Next(1, 15)];
                for (int c = 0; c < chars.Length; c++)
                    chars[c] = Alphabet[random.Next(Alphabet.Length)];
                words.Add(new string(chars));
            }
            return words;
        }
    }
}
