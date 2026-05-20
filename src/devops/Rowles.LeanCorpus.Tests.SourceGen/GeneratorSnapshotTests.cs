using System.Linq;
using Xunit;

namespace Rowles.LeanCorpus.Tests.SourceGen;

public sealed class GeneratorSnapshotTests
{
    [Fact]
    public void Emits_Index_class_with_Map_property_and_Fields()
    {
        const string source = """
            using Rowles.LeanCorpus.Mapping.Attributes;
            namespace Sample;
            [LeanDocument]
            public partial class Product
            {
                [LeanString("id", Required = true)] public required string Id { get; init; }
                [LeanText("title")] public string? Title { get; init; }
            }
            """;

        var result = GeneratorTestHarness.Run(source);

        Assert.Empty(result.GeneratorDiagnostics);
        Assert.Empty(result.CompilationErrors);
        var src = result.CombinedSource;
        Assert.Contains("public static class ProductIndex", src);
        Assert.Contains("public static LeanDocumentMap<global::Sample.Product> Map { get; }", src);
        Assert.Contains("public static class Fields", src);
        Assert.Contains("public static readonly LeanField<global::Sample.Product, string> Id", src);
        Assert.Contains("public static LeanDocument ToDocument(global::Sample.Product value)", src);
        Assert.Contains("public static IndexSchema CreateSchema(bool strict = true)", src);
        Assert.Contains("public static global::Sample.Product FromStoredDocument(StoredDocument document)", src);
        Assert.Contains("file sealed class ProductDocumentMap", src);
    }

    [Fact]
    public void Emits_null_guards_for_optional_string_fields()
    {
        const string source = """
            using Rowles.LeanCorpus.Mapping.Attributes;
            namespace Sample;
            [LeanDocument]
            public partial class Article
            {
                [LeanString("id", Required = true)] public required string Id { get; init; }
                [LeanText("body")] public string? Body { get; init; }
            }
            """;
        var result = GeneratorTestHarness.Run(source);
        Assert.Empty(result.GeneratorDiagnostics);
        var src = result.CombinedSource;
        Assert.Contains("if (value.Body is not null)", src);
        Assert.Contains("new TextField(Fields.Body.Name, value.Body, true)", src);
    }

    [Fact]
    public void Emits_collection_loop_for_repeated_text_fields()
    {
        const string source = """
            using System.Collections.Generic;
            using Rowles.LeanCorpus.Mapping.Attributes;
            namespace Sample;
            [LeanDocument]
            public partial class Bag
            {
                [LeanString("id", Required = true)] public required string Id { get; init; }
                [LeanText("tag")] public IReadOnlyList<string>? Tags { get; init; }
            }
            """;
        var result = GeneratorTestHarness.Run(source);
        Assert.Empty(result.GeneratorDiagnostics);
        var src = result.CombinedSource;
        Assert.Contains("foreach (var s in value.Tags)", src);
        Assert.Contains("new TextField(Fields.Tags.Name, s, true)", src);
    }

    [Fact]
    public void Emits_numeric_encoder_call_for_DateTimeOffset()
    {
        const string source = """
            using System;
            using Rowles.LeanCorpus.Mapping;
            using Rowles.LeanCorpus.Mapping.Attributes;
            namespace Sample;
            [LeanDocument]
            public partial class Event
            {
                [LeanString("id", Required = true)] public required string Id { get; init; }
                [LeanNumeric("at", Encoding = LeanNumericEncoding.UnixMilliseconds)] public DateTimeOffset At { get; init; }
            }
            """;
        var result = GeneratorTestHarness.Run(source);
        Assert.Empty(result.GeneratorDiagnostics);
        Assert.Contains("LeanNumericEncoders.ToUnixMilliseconds(value.At)", result.CombinedSource);
    }

    [Fact]
    public void Emits_schema_with_field_mappings()
    {
        const string source = """
            using Rowles.LeanCorpus.Mapping.Attributes;
            namespace Sample;
            [LeanDocument]
            public partial class Product
            {
                [LeanString("id", Required = true)] public required string Id { get; init; }
                [LeanText("title")] public string? Title { get; init; }
            }
            """;
        var result = GeneratorTestHarness.Run(source);
        var src = result.CombinedSource;
        Assert.Contains("schema.Add(new FieldMapping(\"id\", FieldType.String)", src);
        Assert.Contains("schema.Add(new FieldMapping(\"title\", FieldType.Text)", src);
        Assert.Contains("IsRequired = true", src);
    }
}
