namespace Rowles.LeanCorpus.Server.Abstractions.Contracts.Common;

/// <summary>Represents the result of a transport-neutral service operation.</summary>
/// <typeparam name="T">Successful payload type.</typeparam>
/// <param name="Metadata">Response metadata.</param>
/// <param name="Value">Successful payload, when present.</param>
/// <param name="Failure">Failure payload, when present.</param>
public sealed record ServiceResult<T>(ResponseMetadata Metadata, T? Value, ApiFailure? Failure)
{
    /// <summary>Gets whether the result contains a successful payload.</summary>
    public bool IsSuccess => Failure is null;

    /// <summary>Creates a successful result.</summary>
    public static ServiceResult<T> Success(ResponseMetadata metadata, T value) => new(metadata, value, null);

    /// <summary>Creates a failed result.</summary>
    public static ServiceResult<T> Failed(ResponseMetadata metadata, ApiFailure failure) => new(metadata, default, failure);
}
