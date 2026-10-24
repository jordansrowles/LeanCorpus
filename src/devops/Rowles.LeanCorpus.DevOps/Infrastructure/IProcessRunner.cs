namespace Rowles.LeanCorpus.DevOps.Infrastructure;

internal interface IProcessRunner
{
    Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken = default);
}
