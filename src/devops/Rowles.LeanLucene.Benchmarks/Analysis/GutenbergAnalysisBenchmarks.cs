using BenchmarkDotNet.Attributes;
using Rowles.LeanLucene.Analysis;
using Rowles.LeanLucene.Analysis.Analysers;

namespace Rowles.LeanLucene.Benchmarks;

/// <summary>
/// Measures analysis pipeline throughput on real Project Gutenberg text.
/// Compares <see cref="StandardAnalyser"/> (tokenise+lowercase+stopwords)
/// against <see cref="EnglishAnalyser"/> (tokenise+lowercase+stopwords+Porter stem).
/// </summary>
[MemoryDiagnoser]
[HtmlExporter]
[JsonExporterAttribute.Full]
[MarkdownExporterAttribute.GitHub]
[SimpleJob]
public class GutenbergAnalysisBenchmarks
{
    private (string Title, string Text)[] _books = [];
    private StandardAnalyser _standard = null!;
    private EnglishAnalyser _english = null!;

    [GlobalSetup]
    public void Setup()
    {
        _books = GutenbergDataLoader.LoadBookTexts();
        _standard = new StandardAnalyser();
        _english = new EnglishAnalyser();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _books = [];
    }

    /// <summary>
    /// Tokenise + lowercase + stop-word removal across all books.
    /// </summary>
    [Benchmark(Baseline = true)]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanLucene_Standard_Analyse()
    {
        int total = 0;
        foreach (var (_, text) in _books)
            total += _standard.Analyse(text.AsSpan()).Count;
        return total;
    }

    /// <summary>
    /// Tokenise + lowercase + stop-word removal + Porter stem across all books.
    /// </summary>
    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanLucene_English_Analyse()
    {
        int total = 0;
        foreach (var (_, text) in _books)
            total += _english.Analyse(text.AsSpan()).Count;
        return total;
    }
}
