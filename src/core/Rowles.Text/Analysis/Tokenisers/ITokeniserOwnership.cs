namespace Rowles.LeanCorpus.Analysis.Tokenisers;

/// <summary>Marks a tokeniser whose instances contain no mutable per-call state and may be shared.</summary>
public interface IShareableSpanTokeniser : ISpanTokeniser
{
}

/// <summary>Creates an independent tokeniser with equivalent configuration.</summary>
public interface IThreadLocalSpanTokeniser : ISpanTokeniser
{
    ISpanTokeniser CreateThreadLocalTokeniser();
}
