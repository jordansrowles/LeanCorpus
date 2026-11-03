namespace Rowles.LeanCorpus.Codecs.Vectors;

/// <summary>Metadata stored beside each historical canonical unsigned Int8 vector row.</summary>
internal readonly record struct CanonicalInt8VectorMetadata(
    float Offset,
    float Scale,
    int SumOfCodes,
    float DecodedSquaredNorm)
{
    internal const int Size = sizeof(float) + sizeof(float) + sizeof(int) + sizeof(float);
}
