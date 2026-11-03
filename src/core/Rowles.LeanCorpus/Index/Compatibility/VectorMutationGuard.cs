using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Codecs.Vectors;
using Rowles.LeanCorpus.Index.Segment;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Index.Compatibility;

/// <summary>Checks every quantised source before any index mutation, including deleted fields.</summary>
internal static class VectorMutationGuard
{
    internal static void EnsureCanRewrite(MMapDirectory directory, IEnumerable<SegmentInfo> segments)
    {
        foreach (var segment in segments)
        {
            using ISegmentFileSource files = segment.IsCompoundFile
                ? new CompoundSegmentFileSource(directory, segment.SegmentId)
                : new LooseSegmentFileSource(directory, segment.SegmentId);
            var declaredFiles = new HashSet<string>(StringComparer.Ordinal);
            foreach (var field in segment.VectorFields.Where(static field => field.Quantisation != VectorQuantisation.None))
            {
                string name = Path.GetFileName(VectorFilePaths.QuantisedVectorFile(segment.SegmentId, field.FieldName));
                declaredFiles.Add(name);
                if (!files.FileExists(name))
                    throw new InvalidDataException($"Declared quantised vector file '{name}' is missing from segment '{segment.SegmentId}'.");
            }

            foreach (string name in files.EnumerateFiles())
            {
                if (!VectorCodecFiles.Quantised.FileMatcher.IsMatch(name))
                    continue;
                declaredFiles.Remove(name);
                using var reader = QuantisedVectorReader.Open(files.OpenInput(name));
                var decision = VectorCodecFiles.Quantised.Evaluate(CodecLifecycleOperation.RewriteSource, reader.LifecycleIdentity);
                if (!decision.IsAllowed)
                    throw new InvalidOperationException($"{decision.ReasonCode}: segment '{segment.SegmentId}', file '{name}': {decision.Explanation}");
            }

            if (declaredFiles.Count > 0)
                throw new InvalidDataException($"Declared quantised vector file '{declaredFiles.Order(StringComparer.Ordinal).First()}' is missing from segment '{segment.SegmentId}'.");
        }
    }
}
