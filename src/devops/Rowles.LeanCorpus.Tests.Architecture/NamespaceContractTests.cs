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
using Rowles.LeanCorpus.Tests.Architecture.Infrastructure;

namespace Rowles.LeanCorpus.Tests.Architecture;

public sealed class NamespaceContractTests
{
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
}
