using Rowles.LeanCorpus.Codecs.CodecKit;

namespace Rowles.LeanCorpus.Codecs.Vectors;

internal static class VectorCodecFiles
{
    internal static CodecFileDescriptor Float32 { get; } =
        CodecCatalog.Default.GetFile("leancorpus.vectors.float32");

    internal static CodecFileDescriptor Quantised { get; } =
        CodecCatalog.Default.GetFile("leancorpus.vectors.quantised");

    internal static CodecFileDescriptor Hnsw { get; } =
        CodecCatalog.Default.GetFile("leancorpus.vectors.hnsw");

    internal static CodecRepresentationIdentity QuantisedIdentity(QuantisedVectorStorageKind storageKind)
        => QuantisedVectorLifecyclePolicy.Identity(Quantised.CurrentFormatVersion!.Value, storageKind);

    internal static CodecLifecycleDecision CanCreate(VectorQuantisation quantisation)
        => quantisation switch
        {
            VectorQuantisation.None => Float32.Evaluate(CodecLifecycleOperation.Create,
                target: new(Float32.FormatId, Float32.CurrentFormatVersion!.Value)),
            VectorQuantisation.Int8 => Quantised.Evaluate(CodecLifecycleOperation.Create,
                target: QuantisedIdentity(QuantisedVectorStorageKind.LegacyInt8)),
            VectorQuantisation.BBQ => Quantised.Evaluate(CodecLifecycleOperation.Create,
                target: QuantisedIdentity(QuantisedVectorStorageKind.BbqOsqCanonicalInt8)),
            _ => CodecLifecycleDecision.Denied("UnknownRepresentation", $"Unknown vector quantisation '{quantisation}'."),
        };

    internal static InvalidOperationException RetiredBbqWrite()
    {
        var decision = CanCreate(VectorQuantisation.BBQ);
        return new InvalidOperationException($"{decision.ReasonCode}: {decision.Explanation}");
    }

    internal static void EnsureCanCreate(VectorQuantisation quantisation)
    {
        var decision = CanCreate(quantisation);
        if (!decision.IsAllowed)
            throw new InvalidOperationException($"{decision.ReasonCode}: {decision.Explanation}");
    }
}
