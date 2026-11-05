using Rowles.LeanCorpus.Codecs.Vectors;

namespace Rowles.LeanCorpus.Codecs.CodecKit;

/// <summary>Representation restrictions for the existing quantised vector file role.</summary>
internal sealed class QuantisedVectorLifecyclePolicy : ICodecLifecyclePolicy
{
    internal static QuantisedVectorLifecyclePolicy Instance { get; } = new();

    internal static CodecRepresentationIdentity Identity(int version, QuantisedVectorStorageKind storageKind)
        => new("leancorpus.vectors.quantised", version, storageKind switch
        {
            QuantisedVectorStorageKind.LegacyInt8 => nameof(QuantisedVectorStorageKind.LegacyInt8),
            QuantisedVectorStorageKind.LegacyBbqSign => nameof(QuantisedVectorStorageKind.LegacyBbqSign),
            QuantisedVectorStorageKind.BbqOsqCanonicalInt8 => nameof(QuantisedVectorStorageKind.BbqOsqCanonicalInt8),
            _ => null,
        });

    public CodecLifecycleDecision Evaluate(CodecLifecycleOperation operation,
        CodecRepresentationIdentity? source, CodecRepresentationIdentity? target)
    {
        foreach (var identity in new[] { source, target })
        {
            if (identity is not { } value)
                continue;
            bool int8 = value.RepresentationId == nameof(QuantisedVectorStorageKind.LegacyInt8);
            bool sign = value.RepresentationId == nameof(QuantisedVectorStorageKind.LegacyBbqSign);
            bool osq = value.RepresentationId == nameof(QuantisedVectorStorageKind.BbqOsqCanonicalInt8) && value.Version == 3;
            if (!int8 && !sign && !osq)
                return CodecLifecycleDecision.Denied("UnknownRepresentation",
                    $"{value.FormatId} v{value.Version}: {operation} requires a validated storage-kind identity.");
            if (int8 && operation is not (CodecLifecycleOperation.Read
                or CodecLifecycleOperation.RewriteSource
                or CodecLifecycleOperation.Migrate))
                return CodecLifecycleDecision.Denied("Int8WriteRetired",
                    $"{value.FormatId} v{value.Version}/{value.RepresentationId}: {operation} is retired for 4.0. " +
                    "Historical Int8 files remain readable, convertible to Float32 during rewrites, and structurally migratable; create new vector indexes with Float32.");
            if (!int8 && operation != CodecLifecycleOperation.Read)
                return CodecLifecycleDecision.Denied("CodecReadOnly",
                    $"{value.FormatId} v{value.Version}/{value.RepresentationId}: {operation} is retired. " +
                    "ReindexFromOriginalVectorsRequired: rebuild a separate Float32 index from original documents and embeddings.");
        }

        if (operation == CodecLifecycleOperation.Migrate
            && !(source is { Version: 1 or 2 or 3, RepresentationId: nameof(QuantisedVectorStorageKind.LegacyInt8) }
                && target is { Version: 3, RepresentationId: nameof(QuantisedVectorStorageKind.LegacyInt8) }))
            return CodecLifecycleDecision.Denied("MigrationNotSupported", "No declared quantised-vector migration route.");
        return CodecLifecycleDecision.Allowed;
    }
}
