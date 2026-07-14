using Rowles.LeanCorpus.Analysis;

namespace Rowles.LeanCorpus.Tests.AOTSmoke;

/// <summary>Captures tokens into a list for assertions.</summary>
internal sealed class MaterialisingTokenSink : ISpanTokenSink
{
    public List<Token> Tokens { get; } = [];
    public void Add(ReadOnlySpan<char> text, int startOffset, int endOffset,
        string type = Token.DefaultType, int positionIncrement = 1, byte[]? payload = null)
        => Tokens.Add(new Token(text.ToString(), startOffset, endOffset, type, positionIncrement, payload));
}

/// <summary>Captures tokens into a list via IAnalyser.Analyse.</summary>
internal sealed class CapturingTokenSink : ISpanTokenSink
{
    public List<Token> Tokens { get; } = [];
    public int Count => Tokens.Count;
    public void Add(ReadOnlySpan<char> text, int startOffset, int endOffset,
        string type = Token.DefaultType, int positionIncrement = 1, byte[]? payload = null)
        => Tokens.Add(new Token(text.ToString(), startOffset, endOffset, type, positionIncrement, payload));
}
