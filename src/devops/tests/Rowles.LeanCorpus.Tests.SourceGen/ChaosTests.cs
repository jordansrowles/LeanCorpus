using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Mapping;
using Rowles.LeanCorpus.Tests.SourceGen.Models;
using Xunit;

namespace Rowles.LeanCorpus.Tests.SourceGen;

public sealed class ChaosTests
{
    [Fact]
    public void Empty_document_with_no_mapped_fields_produces_no_generated_source()
    {
        const string source = """
            using Rowles.LeanCorpus.Mapping.Attributes;
            namespace Sample;
            [LeanDocument]
            public partial class Empty
            {
                // no mapped properties
            }
            """;
        var result = GeneratorTestHarness.Run(source);
        Assert.Empty(result.CompilationErrors);
        Assert.Empty(result.GeneratedSources);
    }

    [Fact]
    public void Document_with_only_ignored_fields_produces_no_generated_source()
    {
        const string source = """
            using Rowles.LeanCorpus.Mapping.Attributes;
            namespace Sample;
            [LeanDocument]
            public partial class AllIgnored
            {
                [LeanIgnore] public required string Id { get; init; }
                [LeanIgnore] public string? Name { get; init; }
            }
            """;
        var result = GeneratorTestHarness.Run(source);
        Assert.Empty(result.GeneratedSources);
    }

    [Fact]
    public void Max_dimension_vector()
    {
        const string source = """
            using Rowles.LeanCorpus.Mapping.Attributes;
            namespace Sample;
            [LeanDocument]
            public partial class HugeVec
            {
                [LeanString("id", Required = true)] public required string Id { get; init; }
                [LeanVector("v", Dimension = 1536)] public float[]? V { get; init; }
            }
            """;
        var result = GeneratorTestHarness.Run(source);
        Assert.Contains(result.GeneratorDiagnostics, d => d.Id == "LCGEN004");
        Assert.Contains("value.V.Length != 1536", result.CombinedSource);
    }

    [Fact]
    public void Many_fields_of_all_types()
    {
        const string source = """
            using System;
            using System.Collections.Generic;
            using Rowles.LeanCorpus.Mapping;
            using Rowles.LeanCorpus.Mapping.Attributes;
            namespace Sample;
            [LeanDocument(StrictSchema = false)]
            public partial class FullDoc
            {
                [LeanString("id", Required = true)] public required string Id { get; init; }
                [LeanText("title")] public string? Title { get; init; }
                [LeanText("tags")] public IReadOnlyList<string>? Tags { get; init; }
                [LeanText("aliases")] public string[]? Aliases { get; init; }
                [LeanNumeric("score")] public double Score { get; init; }
                [LeanNumeric("count")] public long Count { get; init; }
                [LeanNumeric("flag")] public short? Flag { get; init; }
                [LeanNumeric("at", Encoding = LeanNumericEncoding.UnixMilliseconds)] public DateTimeOffset At { get; init; }
                [LeanNumeric("date", Encoding = LeanNumericEncoding.DateOnlyDayNumber)] public DateOnly Date { get; init; }
                [LeanNumeric("time", Encoding = LeanNumericEncoding.TimeOnlyTicks)] public TimeOnly Time { get; init; }
                [LeanNumeric("money", Encoding = LeanNumericEncoding.DecimalAsString)] public decimal Money { get; init; }
                [LeanVector("embedding", Dimension = 4)] public float[]? Embedding { get; init; }
                [LeanGeoPoint("loc")] public LeanGeoLocation? Loc { get; init; }
                [LeanStored("note")] public string? Note { get; init; }
                [LeanStored("blob")] public byte[]? Blob { get; init; }
            }
            """;
        var result = GeneratorTestHarness.Run(source);
        Assert.Contains(result.GeneratorDiagnostics, d => d.Id == "LCGEN004");
        Assert.Empty(result.CompilationErrors);
        var src = result.CombinedSource;
        Assert.Contains("public static class FullDocIndex", src);
        Assert.Contains("FullDocDocumentMap", src);
    }

    [Fact]
    public void Numeric_overflow_handling_for_integral_types()
    {
        // sbyte is the smallest integral type; test it's in the NumericKind switch
        const string source = """
            using Rowles.LeanCorpus.Mapping.Attributes;
            namespace Sample;
            [LeanDocument]
            public partial class SByteDoc
            {
                [LeanString("id", Required = true)] public required string Id { get; init; }
                [LeanNumeric("val")] public sbyte Val { get; init; }
            }
            """;
        var result = GeneratorTestHarness.Run(source);
        Assert.Empty(result.GeneratorDiagnostics);
        Assert.Empty(result.CompilationErrors);
        Assert.Contains("checked((sbyte)(long.Parse", result.CombinedSource);
    }

    [Fact]
    public void All_unsigned_integral_types_are_supported()
    {
        const string source = """
            using Rowles.LeanCorpus.Mapping.Attributes;
            namespace Sample;
            [LeanDocument]
            public partial class AllUIntDoc
            {
                [LeanString("id", Required = true)] public required string Id { get; init; }
                [LeanNumeric("a")] public byte A { get; init; }
                [LeanNumeric("b")] public ushort B { get; init; }
                [LeanNumeric("c")] public uint C { get; init; }
                [LeanNumeric("d")] public ulong D { get; init; }
            }
            """;
        var result = GeneratorTestHarness.Run(source);
        Assert.Empty(result.GeneratorDiagnostics);
        Assert.Empty(result.CompilationErrors);
    }

    [Fact]
    public void Numeric_with_Required_false_emits_null_check_in_FromStored()
    {
        const string source = """
            using Rowles.LeanCorpus.Mapping.Attributes;
            namespace Sample;
            [LeanDocument]
            public partial class OptNum
            {
                [LeanString("id", Required = true)] public required string Id { get; init; }
                [LeanNumeric("val", Required = false)] public int? Val { get; init; }
            }
            """;
        var result = GeneratorTestHarness.Run(source);
        Assert.Empty(result.GeneratorDiagnostics);
        Assert.Contains("default(int?)", result.CombinedSource);
    }

    [Fact]
    public void FromStoredDocument_round_trips_integral_edge_values()
    {
        var p = new Product
        {
            Id = "edge-1",
            Price = 42.5,
            Amount = 0m,
            Count = int.MaxValue
        };
        var stored = new Dictionary<string, IReadOnlyList<string>>
        {
            ["id"] = new[] { p.Id },
            ["price"] = new[] { p.Price.ToString(CultureInfo.InvariantCulture) },
            ["count"] = new[] { p.Count!.Value.ToString(CultureInfo.InvariantCulture) },
            ["at"] = new[] { "0" },
            ["amount"] = new[] { LeanNumericEncoders.ToDecimalString(0m) }
        };
        var snapshot = StoredDocument.Create(stored, null);
        var revived = ProductIndex.FromStoredDocument(snapshot);

        Assert.Equal(int.MaxValue, revived.Count);
    }

    [Fact]
    public void FromStoredDocument_round_trips_negative_numeric()
    {
        var p = new Product
        {
            Id = "neg-1",
            Price = -123.456,
            Amount = 0m
        };
        var stored = new Dictionary<string, IReadOnlyList<string>>
        {
            ["id"] = new[] { p.Id },
            ["price"] = new[] { p.Price.ToString(CultureInfo.InvariantCulture) },
            ["at"] = new[] { "0" },
            ["amount"] = new[] { LeanNumericEncoders.ToDecimalString(0m) }
        };
        var snapshot = StoredDocument.Create(stored, null);
        var revived = ProductIndex.FromStoredDocument(snapshot);

        Assert.Equal(p.Price, revived.Price, precision: 9);
    }

    [Fact]
    public void FromStoredDocument_handles_mixed_presence_of_optional_collection_fields()
    {
        // One collection present, the other missing
        var stored = new Dictionary<string, IReadOnlyList<string>>
        {
            ["id"] = new[] { "p-1" },
            ["tag"] = new[] { "red", "blue" }
            // "alias" is missing, should be null
        };
        var snapshot = StoredDocument.Create(stored, null);
        var revived = CollectionProductIndex.FromStoredDocument(snapshot);

        Assert.Equal(new[] { "red", "blue" }, revived.Tags);
        Assert.Null(revived.Aliases);
    }

    [Fact]
    public void ToDocument_creates_document_with_all_string_representations_present_in_fields()
    {
        var fields = ProductIndex.Map.Fields;
        Assert.Equal(8, fields.Count);
        Assert.Equal("id", fields[0].Name);
        Assert.Equal("title", fields[1].Name);
        Assert.Equal("tag", fields[2].Name);
        Assert.Equal("price", fields[3].Name);
        Assert.Equal("count", fields[4].Name);
        Assert.Equal("at", fields[5].Name);
        Assert.Equal("amount", fields[6].Name);
        Assert.Equal("blob", fields[7].Name);
    }

    [Fact]
    public void Schema_uses_correct_field_type_for_each_kind()
    {
        var schema = ProductIndex.CreateSchema(strict: false);
        Assert.Equal(Document.Fields.FieldType.String, schema.Mappings["id"].FieldType);
        Assert.Equal(Document.Fields.FieldType.Text, schema.Mappings["title"].FieldType);
        Assert.Equal(Document.Fields.FieldType.Text, schema.Mappings["tag"].FieldType);
        Assert.Equal(Document.Fields.FieldType.Numeric, schema.Mappings["price"].FieldType);
        Assert.Equal(Document.Fields.FieldType.Int64, schema.Mappings["count"].FieldType);
        Assert.Equal(Document.Fields.FieldType.Numeric, schema.Mappings["at"].FieldType);
        Assert.Equal(Document.Fields.FieldType.Stored, schema.Mappings["amount"].FieldType);
        Assert.Equal(Document.Fields.FieldType.Binary, schema.Mappings["blob"].FieldType);
    }

    [Fact]
    public void FromStoredDocument_with_null_binary_field()
    {
        var stored = new Dictionary<string, IReadOnlyList<string>>
        {
            ["id"] = new[] { "p-1" },
            ["title"] = new[] { "test" },
            ["price"] = new[] { "0.0" },
            ["at"] = new[] { "0" },
            ["amount"] = new[] { LeanNumericEncoders.ToDecimalString(0m) }
        };
        var snapshot = StoredDocument.Create(stored, null);
        var revived = ProductIndex.FromStoredDocument(snapshot);

        Assert.Null(revived.Tags);
        Assert.Null(revived.Count);
        Assert.Equal(0m, revived.Amount);
    }
}
