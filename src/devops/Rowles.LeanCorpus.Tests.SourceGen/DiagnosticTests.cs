using System.Linq;
using Xunit;

namespace Rowles.LeanCorpus.Tests.SourceGen;

public sealed class DiagnosticTests
{
    private static bool Has(GeneratorRunResult result, string id)
        => result.GeneratorDiagnostics.Any(d => d.Id == id);

    [Fact]
    public void LCGEN001_unsupported_property_type()
    {
        const string source = """
            using System;
            using Rowles.LeanCorpus.Mapping.Attributes;
            namespace Sample;
            [LeanDocument]
            public partial class Bad
            {
                [LeanString("id", Required = true)] public required string Id { get; init; }
                [LeanString("token")] public Guid Token { get; init; }
            }
            """;
        Assert.True(Has(GeneratorTestHarness.Run(source), "LCGEN001"));
    }

    [Fact]
    public void LCGEN002_duplicate_field_name()
    {
        const string source = """
            using Rowles.LeanCorpus.Mapping.Attributes;
            namespace Sample;
            [LeanDocument]
            public partial class Dup
            {
                [LeanString("id", Required = true)] public required string Id { get; init; }
                [LeanText("id")] public string? AlsoId { get; init; }
            }
            """;
        Assert.True(Has(GeneratorTestHarness.Run(source), "LCGEN002"));
    }

    [Fact]
    public void LCGEN003_invalid_field_name()
    {
        const string source = """
            using Rowles.LeanCorpus.Mapping.Attributes;
            namespace Sample;
            [LeanDocument]
            public partial class BadName
            {
                [LeanString("", Required = true)] public required string Id { get; init; }
            }
            """;
        Assert.True(Has(GeneratorTestHarness.Run(source), "LCGEN003"));
    }

    [Fact]
    public void LCGEN004_from_stored_blocked_by_vector_required()
    {
        const string source = """
            using Rowles.LeanCorpus.Mapping.Attributes;
            namespace Sample;
            [LeanDocument]
            public partial class Vec
            {
                [LeanString("id", Required = true)] public required string Id { get; init; }
                [LeanVector("embedding", Dimension = 4, Required = true)] public required float[] Embedding { get; init; }
            }
            """;
        Assert.True(Has(GeneratorTestHarness.Run(source), "LCGEN004"));
    }

    [Fact]
    public void LCGEN005_conflicting_attributes()
    {
        const string source = """
            using Rowles.LeanCorpus.Mapping.Attributes;
            namespace Sample;
            [LeanDocument]
            public partial class Conflict
            {
                [LeanString("id", Required = true)] public required string Id { get; init; }
                [LeanText("title")][LeanString("title2")] public string? Title { get; init; }
            }
            """;
        Assert.True(Has(GeneratorTestHarness.Run(source), "LCGEN005"));
    }

    [Fact]
    public void LCGEN006_missing_numeric_encoding()
    {
        const string source = """
            using System;
            using Rowles.LeanCorpus.Mapping.Attributes;
            namespace Sample;
            [LeanDocument]
            public partial class MissingEnc
            {
                [LeanString("id", Required = true)] public required string Id { get; init; }
                [LeanNumeric("at")] public DateTimeOffset At { get; init; }
            }
            """;
        Assert.True(Has(GeneratorTestHarness.Run(source), "LCGEN006"));
    }

    [Fact]
    public void LCGEN007_missing_vector_dimension()
    {
        const string source = """
            using Rowles.LeanCorpus.Mapping.Attributes;
            namespace Sample;
            [LeanDocument]
            public partial class NoDim
            {
                [LeanString("id", Required = true)] public required string Id { get; init; }
                [LeanVector("embedding")] public float[]? Embedding { get; init; }
            }
            """;
        Assert.True(Has(GeneratorTestHarness.Run(source), "LCGEN007"));
    }

    [Fact]
    public void LCGEN008_unsupported_collection_shape()
    {
        const string source = """
            using System.Collections.Generic;
            using Rowles.LeanCorpus.Mapping.Attributes;
            namespace Sample;
            [LeanDocument]
            public partial class BadList
            {
                [LeanString("id", Required = true)] public required string Id { get; init; }
                [LeanText("body")] public List<string>? Body { get; init; }
            }
            """;
        Assert.True(Has(GeneratorTestHarness.Run(source), "LCGEN008"));
    }

    [Fact]
    public void LCGEN009_invalid_geo_mapping()
    {
        const string source = """
            using Rowles.LeanCorpus.Mapping.Attributes;
            namespace Sample;
            [LeanDocument]
            public partial class BadGeo
            {
                [LeanString("id", Required = true)] public required string Id { get; init; }
                [LeanGeoPoint("loc")] public string? Loc { get; init; }
            }
            """;
        Assert.True(Has(GeneratorTestHarness.Run(source), "LCGEN009"));
    }

    [Fact]
    public void LCGEN010_unsupported_generic_document_target()
    {
        const string source = """
            using Rowles.LeanCorpus.Mapping.Attributes;
            namespace Sample;
            [LeanDocument]
            public partial class GenericDoc<T>
            {
                [LeanString("id", Required = true)] public required string Id { get; init; }
            }
            """;
        Assert.True(Has(GeneratorTestHarness.Run(source), "LCGEN010"));
    }

    [Fact]
    public void LCGEN010_unsupported_nested_document_target()
    {
        const string source = """
            using Rowles.LeanCorpus.Mapping.Attributes;
            namespace Sample;
            public static class Outer
            {
                [LeanDocument]
                public partial class NestedDoc
                {
                    [LeanString("id", Required = true)] public required string Id { get; init; }
                }
            }
            """;
        Assert.True(Has(GeneratorTestHarness.Run(source), "LCGEN010"));
    }

    [Fact]
    public void LCGEN011_inaccessible_mapped_member()
    {
        const string source = """
            using Rowles.LeanCorpus.Mapping.Attributes;
            namespace Sample;
            [LeanDocument]
            public partial class ProtectedDoc
            {
                [LeanString("id", Required = true)] protected string Id { get; init; } = "1";
            }
            """;
        Assert.True(Has(GeneratorTestHarness.Run(source), "LCGEN011"));
    }

    [Fact]
    public void LCGEN012_missing_parameterless_constructor_blocks_materialiser()
    {
        const string source = """
            using Rowles.LeanCorpus.Mapping.Attributes;
            namespace Sample;
            [LeanDocument]
            public partial class CtorDoc
            {
                public CtorDoc(string id) { Id = id; }
                [LeanString("id", Required = true)] public string Id { get; init; }
            }
            """;
        Assert.True(Has(GeneratorTestHarness.Run(source), "LCGEN012"));
    }

    [Fact]
    public void LCGEN012_getter_only_property_blocks_materialiser()
    {
        const string source = """
            using Rowles.LeanCorpus.Mapping.Attributes;
            namespace Sample;
            [LeanDocument]
            public partial class GetterOnlyDoc
            {
                [LeanString("id", Required = true)] public string Id { get; } = "1";
            }
            """;
        Assert.True(Has(GeneratorTestHarness.Run(source), "LCGEN012"));
    }

    [Fact]
    public void LCGEN012_unmapped_required_member_blocks_materialiser()
    {
        const string source = """
            using Rowles.LeanCorpus.Mapping.Attributes;
            namespace Sample;
            [LeanDocument]
            public partial class RequiredDoc
            {
                [LeanString("id", Required = true)] public required string Id { get; init; }
                public required string Name { get; init; }
            }
            """;
        Assert.True(Has(GeneratorTestHarness.Run(source), "LCGEN012"));
    }

    [Fact]
    public void LCGEN013_decimal_as_string_must_be_stored()
    {
        const string source = """
            using Rowles.LeanCorpus.Mapping;
            using Rowles.LeanCorpus.Mapping.Attributes;
            namespace Sample;
            [LeanDocument]
            public partial class MoneyDoc
            {
                [LeanString("id", Required = true)] public required string Id { get; init; }
                [LeanNumeric("amount", Encoding = LeanNumericEncoding.DecimalAsString, Stored = false)] public decimal Amount { get; init; }
            }
            """;
        Assert.True(Has(GeneratorTestHarness.Run(source), "LCGEN013"));
    }
}
