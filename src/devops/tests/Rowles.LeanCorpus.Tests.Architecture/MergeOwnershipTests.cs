using System.Reflection;
using Rowles.LeanCorpus.Index.Segment;
using Rowles.LeanCorpus.Tests.Architecture.Infrastructure;

namespace Rowles.LeanCorpus.Tests.Architecture;

[Trait("Area", "Architecture")]
public sealed class MergeOwnershipTests
{
    private const string MergeNamespace = "Rowles.LeanCorpus.Index.Segment.Merging.";
    private static readonly string[] Components =
    [
        "SegmentMergePlan", "SegmentMergePlanner", "SegmentMergeDocumentMap",
        "SegmentMergePostingsWriter", "SegmentMergeStoredPayloadWriter", "SegmentMergeDocValuesWriter",
        "SegmentMergeVectorWriter", "SegmentMergeSpatialWriter", "SegmentMergeFinaliser",
    ];

    [Fact]
    public void Required_merge_components_are_internal_and_have_separate_sources()
    {
        foreach (string component in Components)
        {
            Type? type = typeof(SegmentMerger).Assembly.GetType(MergeNamespace + component);
            Assert.NotNull(type);
            Assert.False(type.IsPublic);
            Assert.True(File.Exists(RepositoryPaths.FromRoot("src", "core", "Rowles.LeanCorpus",
                "Index", "Segment", "Merging", component + ".cs")));
        }
    }

    [Fact]
    public void Merger_does_not_own_nested_codec_writers_or_serialisation_calls()
    {
        Assert.DoesNotContain(typeof(SegmentMerger).GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic),
            static type => type.Name.EndsWith("Writer", StringComparison.Ordinal)
                || type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .Any(static field => field.FieldType.Namespace?.StartsWith("Rowles.LeanCorpus.Codecs", StringComparison.Ordinal) == true
                        && field.FieldType.Name.EndsWith("Writer", StringComparison.Ordinal)));
        string source = File.ReadAllText(RepositoryPaths.FromRoot("src", "core", "Rowles.LeanCorpus",
            "Index", "Segment", "SegmentMerger.cs"));
        foreach (string writer in new[] { "StoredFieldsStreamWriter", "TermVectorsStreamWriter", "CodecFileWriter",
            "NormsWriter", "FieldLengthWriter", "VectorWriter.Write", "HnswWriter", "BKDWriter.Write", "LiveDocs.Serialise" })
            Assert.DoesNotContain(writer, source, StringComparison.Ordinal);
    }

    [Fact]
    public void Merge_plan_has_no_setters_or_independent_disposable_ownership()
    {
        Type? plan = typeof(SegmentMerger).Assembly.GetType(MergeNamespace + "SegmentMergePlan");
        Assert.NotNull(plan);
        Assert.False(typeof(IDisposable).IsAssignableFrom(plan));
        Assert.DoesNotContain(plan.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic),
            static property => property.SetMethod is not null);
    }
}
