using Rowles.LeanCorpus.Analysis;
using Rowles.LeanCorpus.Analysis.Analysers;
using Rowles.LeanCorpus.Analysis.Filters;
using Rowles.LeanCorpus.Analysis.Stemmers;
using Rowles.LeanCorpus.Analysis.Tokenisers;
using Rowles.LeanCorpus.Codecs.CodecKit.Checksum.Providers;
using Rowles.LeanCorpus.Codecs.CodecKit.Codecs;
using Rowles.LeanCorpus.Codecs.StoredFields;
using Rowles.LeanCorpus.Diagnostics;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Linq;
using Rowles.LeanCorpus.Search.Highlighting;
using Rowles.LeanCorpus.Search.Scoring;
using Rowles.LeanCorpus.Search.Geo;
using Rowles.LeanCorpus.Search.XY;
using Rowles.LeanCorpus.Tests.Architecture.Infrastructure;

namespace Rowles.LeanCorpus.Tests.Architecture;

public sealed class NamespaceContractTests
{
    [Fact]
    public void Public_types_must_not_reside_in_internal_namespaces()
    {
        // CaseDefinition<TBase> is public for historical compatibility. Keep its namespace in 3.x;
        // moving it belongs in the next major release.
        const string legacyCaseDefinition = "Rowles.LeanCorpus.Codecs.CodecKit.Internal.CaseDefinition`1";

        var failures = ArchitectureContext.CoreAssembly.GetExportedTypes()
            .Where(type => type.Namespace?.Contains(".Internal", StringComparison.Ordinal) == true
                && type.FullName != legacyCaseDefinition)
            .Select(static type => type.FullName ?? type.Name);

        RuleAssert.Empty("Public types must not reside in namespaces containing '.Internal':", failures);

        var compatibilityType = ArchitectureContext.CoreAssembly.GetType(legacyCaseDefinition);
        Assert.NotNull(compatibilityType);
        Assert.True(compatibilityType.IsPublic);
        Assert.Equal("Rowles.LeanCorpus.Codecs.CodecKit.Internal", compatibilityType.Namespace);
    }

    [Fact]
    public void Public_spatial_types_must_remain_in_their_public_namespaces()
    {
        AssertPublicSpatialTypes(
            "Rowles.LeanCorpus.Search.Geo",
            typeof(IGeoGeometry), typeof(GeoPoint), typeof(GeoRectangle), typeof(GeoCircle),
            typeof(GeoLineString), typeof(GeoPolygon), typeof(GeoGeometryCollection),
            typeof(GeoEncodingUtils), typeof(GeoBoundingBoxQuery), typeof(GeoDistanceQuery));
        AssertPublicSpatialTypes(
            "Rowles.LeanCorpus.Search.XY",
            typeof(IXYGeometry), typeof(XYPoint), typeof(XYRectangle), typeof(XYCircle),
            typeof(XYLineString), typeof(XYPolygon), typeof(XYGeometryCollection), typeof(XYEncodingUtils));
    }

    [Fact]
    public void Packed_bkd_subsystem_contracts_must_remain_internal_in_the_root_namespace()
    {
        const string namespaceName = "Rowles.LeanCorpus.Codecs.PackedBkd";
        string[] typeNames =
        [
            "PackedBkdConfig",
            "PackedBkdBuildOptions",
            "PackedBkdFieldBuffer",
            "PackedBkdReader",
            "PackedBkdWriter",
            "PackedBkdFieldMetadata",
            "PackedBkdTraversalStats",
            "PackedBkdCellRelation",
            "IPackedBkdIntersectVisitor",
            "PackedBkdCodecFiles",
        ];

        var failures = typeNames
            .Select(typeName => (TypeName: typeName, Type: ArchitectureContext.CoreAssembly.GetType($"{namespaceName}.{typeName}")))
            .Where(static item => item.Type is null || item.Type.IsVisible || item.Type.Namespace != namespaceName)
            .Select(static item => item.Type is null
                ? $"{namespaceName}.{item.TypeName} is missing"
                : $"{item.Type.FullName} must be non-public in {namespaceName}");

        RuleAssert.Empty("Packed BKD subsystem contracts must remain internal:", failures);
    }

    [Fact]
    public void Packed_bkd_implementation_helpers_must_remain_internal()
    {
        const string namespaceName = "Rowles.LeanCorpus.Codecs.PackedBkd.Internal";
        string[] typeNames =
        [
            "IPackedBkdRecordSource",
            "PackedBkdBuildMemoryTracker",
            "PackedBkdBuilder",
            "PackedBkdBuiltField",
            "PackedBkdDocumentCounter",
            "PackedBkdFieldCursor",
            "PackedBkdFieldNameComparer",
            "PackedBkdFormat",
            "PackedBkdLeafDataStore",
            "PackedBkdLeafEncoder",
            "PackedBkdMemoryRecordSource",
            "PackedBkdOrderedMemoryRecordSource",
            "PackedBkdSpillRecordStore",
            "PackedBkdTreeMath",
        ];

        var failures = typeNames
            .Select(typeName => (TypeName: typeName, Type: ArchitectureContext.CoreAssembly.GetType($"{namespaceName}.{typeName}")))
            .Where(static item => item.Type is null || item.Type.IsVisible || item.Type.Namespace != namespaceName)
            .Select(static item => item.Type is null
                ? $"{namespaceName}.{item.TypeName} is missing"
                : $"{item.Type.FullName} must be non-public in {namespaceName}");

        RuleAssert.Empty("Packed BKD implementation helpers must remain internal:", failures);
    }

    [Fact]
    public void Analysers_must_reside_in_Analysers() => AssertImplementations(typeof(IAnalyser), "Analysers");

    [Fact]
    public void Character_filters_must_reside_in_Filters() => AssertImplementations(typeof(ICharFilter), "Filters");

    [Fact]
    public void Token_filters_must_reside_in_Filters() => AssertImplementations(typeof(ISpanTokenFilter), "Filters");

    [Fact]
    public void Stemmers_must_reside_in_Stemmers() => AssertImplementations(typeof(ISpanStemmer), "Stemmers");

    [Fact]
    public void Tokenisers_must_reside_in_Tokenisers() => AssertImplementations(typeof(ISpanTokeniser), "Tokenisers");

    [Fact]
    public void Checksum_providers_must_reside_in_Providers() => AssertImplementations(typeof(IChecksumProvider), "Providers");

    [Fact]
    public void Codecs_must_reside_in_CodecKit() => AssertImplementations(typeof(ICodec<>), "CodecKit");

    [Fact]
    public void Field_compression_codecs_must_reside_in_StoredFields() => AssertImplementations(typeof(IFieldCompressionCodec), "StoredFields");

    [Fact]
    public void Metrics_collectors_must_reside_in_Diagnostics() => AssertImplementations(typeof(IMetricsCollector), "Diagnostics");

    [Fact]
    public void Document_fields_must_reside_in_Fields() => AssertImplementations(typeof(IField), "Fields");

    [Fact]
    public void Deletion_policies_must_reside_in_Indexer() => AssertImplementations(typeof(IIndexDeletionPolicy), "Indexer");

    [Fact]
    public void Merge_policies_must_reside_in_Indexer() => AssertImplementations(typeof(IMergePolicy), "Indexer");

    [Fact]
    public void Field_descriptors_must_reside_in_Mapping() => AssertImplementations(typeof(IFieldDescriptor), "Mapping");

    [Fact]
    public void Highlighters_must_reside_in_Highlighting() => AssertImplementations(typeof(IHighlighter), "Highlighting");

    [Fact]
    public void Collectors_must_reside_in_Scoring() => AssertImplementations(typeof(ICollector), "Scoring");

    [Fact]
    public void Similarities_must_reside_in_Scoring() => AssertImplementations(typeof(ISimilarity), "Scoring");

    private static void AssertImplementations(Type contract, string namespaceSegment)
    {
        var failures = ArchitectureContext.CoreAssembly.GetTypes()
            .Where(static type => !type.IsInterface && !type.IsAbstract)
            .Where(type => Implements(type, contract))
            .Where(type => !NamespaceSegments.Contains(type.Namespace, namespaceSegment))
            .Select(static type => type.FullName ?? type.Name);

        RuleAssert.Empty(
            $"Concrete implementations of {contract.Name} must reside in namespace segment '{namespaceSegment}':",
            failures);
    }

    private static bool Implements(Type type, Type contract)
    {
        if (!contract.IsGenericTypeDefinition)
            return contract.IsAssignableFrom(type);

        return type.GetInterfaces().Any(@interface =>
            @interface.IsGenericType && @interface.GetGenericTypeDefinition() == contract);
    }

    private static void AssertPublicSpatialTypes(string expectedNamespace, params Type[] types)
    {
        var failures = types
            .Where(type => !type.IsPublic || type.Namespace != expectedNamespace)
            .Select(type => type.FullName ?? type.Name);

        RuleAssert.Empty($"Public spatial types must reside in {expectedNamespace}:", failures);
    }
}
