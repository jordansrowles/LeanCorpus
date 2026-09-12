namespace Rowles.LeanCorpus.Analysis.Analysers;

/// <summary>
/// Explicit ownership contract for analysers used by concurrent indexing. Implementations
/// return an independent instance with equivalent configuration and no shared mutable state.
/// </summary>
public interface IThreadLocalAnalyser : IAnalyser
{
    IAnalyser CreateThreadLocalAnalyser();
}
