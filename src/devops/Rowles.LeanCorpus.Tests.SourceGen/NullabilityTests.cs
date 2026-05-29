using Xunit;

namespace Rowles.LeanCorpus.Tests.SourceGen;

public sealed class NullabilityTests
{
    [Fact]
    public void Non_nullable_reference_type_defaults_to_required()
    {
        const string source = """
            using Rowles.LeanCorpus.Mapping.Attributes;
            namespace Sample;
            [LeanDocument]
            public partial class A
            {
                [LeanString("id")] public required string Id { get; init; }
            }
            """;
        var result = GeneratorTestHarness.Run(source);
        Assert.Contains("IsRequired = true", result.CombinedSource);
    }

    [Fact]
    public void Nullable_reference_type_is_optional()
    {
        const string source = """
            using Rowles.LeanCorpus.Mapping.Attributes;
            namespace Sample;
            [LeanDocument]
            public partial class B
            {
                [LeanString("id", Required = true)] public required string Id { get; init; }
                [LeanText("title")] public string? Title { get; init; }
            }
            """;
        var result = GeneratorTestHarness.Run(source);
        // Title binding should be optional
        Assert.Contains("\"title\", FieldType.Text, true, true, false", result.CombinedSource);
    }

    [Fact]
    public void Nullable_value_type_is_optional()
    {
        const string source = """
            using Rowles.LeanCorpus.Mapping.Attributes;
            namespace Sample;
            [LeanDocument]
            public partial class C
            {
                [LeanString("id", Required = true)] public required string Id { get; init; }
                [LeanNumeric("count")] public int? Count { get; init; }
            }
            """;
        var result = GeneratorTestHarness.Run(source);
        // Count is nullable value type, so IsRequired should be false.
        Assert.Contains("\"count\", FieldType.Numeric, true, true, false", result.CombinedSource);
    }

    [Fact]
    public void Attribute_Required_false_overrides_non_nullable_value_type()
    {
        const string source = """
            using Rowles.LeanCorpus.Mapping.Attributes;
            namespace Sample;
            [LeanDocument]
            public partial class D
            {
                [LeanString("id", Required = true)] public required string Id { get; init; }
                [LeanNumeric("count", Required = false)] public int Count { get; init; }
            }
            """;
        var result = GeneratorTestHarness.Run(source);
        Assert.Contains("\"count\", FieldType.Numeric, true, true, false", result.CombinedSource);
    }
}
