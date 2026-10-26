using Rowles.LeanCorpus.Analysis;
using Rowles.LeanCorpus.Analysis.Analysers;

var sink = new MaterialisingSink();
new StandardAnalyser().Analyse("The QUICK fox", sink);
if (!sink.Tokens.SequenceEqual(["quick", "fox"]))
    throw new InvalidDataException($"Unexpected token sequence: {string.Join(",", sink.Tokens)}");
Console.WriteLine("TEXT " + string.Join("|", sink.Tokens));

sealed class MaterialisingSink : ISpanTokenSink
{
    public List<string> Tokens { get; } = [];
    public void Add(ReadOnlySpan<char> text, int startOffset, int endOffset, string type = Token.DefaultType,
        int positionIncrement = 1, byte[]? payload = null) => Tokens.Add(text.ToString());
}