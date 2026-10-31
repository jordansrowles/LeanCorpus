namespace Rowles.LeanCorpus.Codecs.Vectors;

/// <summary>Fixed canonical Int8 metadata stored beside each vector's unsigned codes.</summary>
internal readonly record struct CanonicalInt8VectorMetadata(
    float Offset,
    float Scale,
    int SumOfCodes,
    float DecodedSquaredNorm)
{
    internal const int Size = sizeof(float) + sizeof(float) + sizeof(int) + sizeof(float);
}

/// <summary>
/// Provides the decoded vectors and their immutable canonical Int8 representation.
/// The raw record copy is for v3 merge preservation and must not be requantised.
/// </summary>
internal interface ICanonicalVectorSource : IVectorSource
{
    CanonicalInt8VectorMetadata GetCanonicalMetadata(int docId);

    ReadOnlySpan<byte> GetCanonicalCodes(int docId);

    /// <summary>Copies the raw metadata and code bytes into caller-provided record storage.</summary>
    void CopyCanonicalRecordTo(int docId, Span<byte> destination);
}
