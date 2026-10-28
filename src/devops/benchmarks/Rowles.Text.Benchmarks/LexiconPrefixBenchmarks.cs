using System.Runtime.CompilerServices;
using BenchmarkDotNet.Attributes;
using Rowles.LeanCorpus.Analysis;
using Rowles.LeanCorpus.Analysis.Tokenisers;

namespace Rowles.Text.Benchmarks;

/// <summary>
/// Measures Thai and Chinese longest-prefix lookup across known and unknown
/// runs, word lengths, input lengths and lexicon sizes.
/// </summary>
[MemoryDiagnoser]
[HtmlExporter]
[JsonExporterAttribute.Full]
[MarkdownExporterAttribute.GitHub]
public class LexiconPrefixBenchmarks
{
    [Params(
        "Thai-R256-Known-W16-C512",
        "Thai-R2048-Known-W16-C512",
        "Thai-R2048-Unknown-W16-C512",
        "Thai-R2048-Unknown-W256-C512",
        "Thai-R2048-Unknown-W256-C2048",
        "Chinese-R256-Known-W16-C512",
        "Chinese-R2048-Known-W16-C512",
        "Chinese-R2048-Unknown-W16-C512",
        "Chinese-R2048-Unknown-W256-C512",
        "Chinese-R2048-Unknown-W256-C2048")]
    public string Scenario { get; set; } = "Thai-R256-Known-W16-C512";

    private ISpanTokeniser _tokeniser = null!;
    private string _input = string.Empty;
    private CountingSink _sink = null!;

    [GlobalSetup]
    public void Setup()
    {
        (string language, int inputRunLength, bool knownHeavy, int maximumWordLength, int lexiconCardinality) = Scenario switch
        {
            "Thai-R256-Known-W16-C512" => ("Thai", 256, true, 16, 512),
            "Thai-R2048-Known-W16-C512" => ("Thai", 2048, true, 16, 512),
            "Thai-R2048-Unknown-W16-C512" => ("Thai", 2048, false, 16, 512),
            "Thai-R2048-Unknown-W256-C512" => ("Thai", 2048, false, 256, 512),
            "Thai-R2048-Unknown-W256-C2048" => ("Thai", 2048, false, 256, 2048),
            "Chinese-R256-Known-W16-C512" => ("Chinese", 256, true, 16, 512),
            "Chinese-R2048-Known-W16-C512" => ("Chinese", 2048, true, 16, 512),
            "Chinese-R2048-Unknown-W16-C512" => ("Chinese", 2048, false, 16, 512),
            "Chinese-R2048-Unknown-W256-C512" => ("Chinese", 2048, false, 256, 512),
            "Chinese-R2048-Unknown-W256-C2048" => ("Chinese", 2048, false, 256, 2048),
            _ => throw new InvalidOperationException($"Unknown workload scenario '{Scenario}'.")
        };

        bool isThai = language == "Thai";
        char knownCharacter = isThai ? '\u0E01' : '\u4E2D';
        char unknownCharacter = isThai ? '\u0E2E' : '\u6F22';
        string[] words = BuildLexicon(
            maximumWordLength,
            lexiconCardinality,
            knownCharacter,
            unknownCharacter);

        _tokeniser = isThai
            ? new ThaiTokeniser(words)
            : new ChineseLexiconTokeniser(words);
        _input = new string(knownHeavy ? knownCharacter : unknownCharacter, inputRunLength);
        _sink = new CountingSink();
    }

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int Tokenise()
    {
        _sink.Reset();
        _tokeniser.Tokenise(_input.AsSpan(), _sink);
        return _sink.Count;
    }

    private static string[] BuildLexicon(
        int maximumWordLength,
        int cardinality,
        char knownCharacter,
        char unknownCharacter)
    {
        if (maximumWordLength < 2)
            throw new ArgumentOutOfRangeException(nameof(maximumWordLength));
        if (cardinality < 2 || cardinality >= 0x9FFF - 0x4E00)
            throw new ArgumentOutOfRangeException(nameof(cardinality));

        var words = new string[cardinality];
        words[0] = new string(knownCharacter, maximumWordLength);

        // Unknown inputs share a non-terminal prefix with every filler word.
        // Distinct terminal characters vary fan-out as cardinality grows.
        var unknownPrefix = new string(unknownCharacter, maximumWordLength - 1);
        for (int index = 1; index < cardinality; index++)
        {
            char terminalCharacter = (char)(0x4E00 + index);
            words[index] = string.Concat(unknownPrefix, terminalCharacter);
        }

        return words;
    }

    private sealed class CountingSink : ISpanTokenSink
    {
        internal int Count { get; private set; }

        public void Add(
            ReadOnlySpan<char> text,
            int startOffset,
            int endOffset,
            string type = Token.DefaultType,
            int positionIncrement = 1,
            byte[]? payload = null)
            => Count++;

        internal void Reset() => Count = 0;
    }
}
