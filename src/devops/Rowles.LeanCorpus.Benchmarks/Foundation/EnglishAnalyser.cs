using Rowles.LeanCorpus.Analysis;
using Rowles.LeanCorpus.Analysis.Analysers;
using Rowles.LeanCorpus.Analysis.Filters;
using Rowles.LeanCorpus.Analysis.Tokenisers;

namespace Rowles.LeanCorpus.Benchmarks;

/// <summary>
/// English analysis pipeline used by LeanCorpus indexing benchmarks.
/// </summary>
internal sealed class EnglishAnalyser : IAnalyser
{
    private readonly Analyser _pipeline = new(
        new Tokeniser(),
        new LowercaseFilter(),
        new StopWordFilter(),
        new PorterStemmerFilter());

    public void Analyse(ReadOnlySpan<char> input, ISpanTokenSink sink) => _pipeline.Analyse(input, sink);
}
