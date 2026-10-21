namespace Rowles.LeanCorpus.DevOps.Infrastructure;

internal sealed record ProcessProgressEvent(TimeSpan Elapsed, int StandardOutputLineCount, int StandardErrorLineCount);
