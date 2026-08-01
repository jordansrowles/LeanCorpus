using Rowles.LeanCorpus.Analysis;
using Rowles.LeanCorpus.Analysis.Analysers;

var analyser = new StandardAnalyser();
analyser.Analyse("LeanCorpus makes local search practical.", new ConsoleTokenSink());

file sealed class ConsoleTokenSink : ISpanTokenSink
{
    public void Add(ReadOnlySpan<char> text, int startOffset, int endOffset, string type = Token.DefaultType, int positionIncrement = 1, byte[]? payload = null)
    {
        Console.WriteLine($"{startOffset}-{endOffset}: {text}");
    }
}
