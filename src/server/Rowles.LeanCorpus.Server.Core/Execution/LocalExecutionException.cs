using Rowles.LeanCorpus.Server.Abstractions.Contracts.Common;

namespace Rowles.LeanCorpus.Server.Core.Execution;

internal sealed class LocalExecutionException(ApiFailure failure) : Exception(failure.Message)
{
    internal ApiFailure Failure { get; } = failure;
}
