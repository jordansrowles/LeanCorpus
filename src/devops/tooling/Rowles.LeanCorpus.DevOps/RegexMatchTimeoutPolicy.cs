namespace Rowles.LeanCorpus.DevOps;

internal static class RegexMatchTimeoutPolicy
{
    internal const int MatchTimeoutMilliseconds = 1_000;
    internal static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(MatchTimeoutMilliseconds);
}
