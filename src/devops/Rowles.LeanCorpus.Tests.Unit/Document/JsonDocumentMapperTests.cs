using System.Text.Json;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Document.Json;

namespace Rowles.LeanCorpus.Tests.Unit.Document;

/// <summary>
/// Contains unit tests for JSON Document Mapper.
/// </summary>
public sealed class JsonDocumentMapperTests
{
    /// <summary>
    /// Verifies the Flat Object: Maps String And Numeric Fields scenario.
    /// </summary>
    [Fact(DisplayName = "Flat Object: Maps String And Numeric Fields")]
    public void FlatObject_MapsStringAndNumericFields()
    {
        var json = """{"name": "Alice", "age": 30}""";
        var doc = JsonDocumentMapper.FromJsonString(json);

        Assert.Equal(2, doc.Fields.Count);
        Assert.NotNull(doc.GetField("name"));
        Assert.NotNull(doc.GetField("age"));
        Assert.IsType<StringField>(doc.GetField("name"));
        Assert.IsType<Int64Field>(doc.GetField("age"));
    }

    /// <summary>
    /// Verifies the Nested Object: Produces Prefixed Field Names scenario.
    /// </summary>
    [Fact(DisplayName = "Nested Object: Produces Prefixed Field Names")]
    public void NestedObject_ProducesPrefixedFieldNames()
    {
        var json = """{"address": {"city": "London", "zip": "SW1A"}}""";
        var doc = JsonDocumentMapper.FromJsonString(json);

        Assert.NotNull(doc.GetField("address.city"));
        Assert.NotNull(doc.GetField("address.zip"));
    }

    /// <summary>
    /// Verifies the Array: Produces Multi Valued Fields scenario.
    /// </summary>
    [Fact(DisplayName = "Array: Produces Multi Valued Fields")]
    public void Array_ProducesMultiValuedFields()
    {
        var json = """{"tags": ["red", "blue", "green"]}""";
        var doc = JsonDocumentMapper.FromJsonString(json);

        var fields = doc.GetFields("tags");
        Assert.Equal(3, fields.Count);
    }

    /// <summary>
    /// Verifies the Boolean Values: Mapped As String Fields scenario.
    /// </summary>
    [Fact(DisplayName = "Boolean Values: Mapped As String Fields")]
    public void BooleanValues_MappedAsStringFields()
    {
        var json = """{"active": true, "deleted": false}""";
        var doc = JsonDocumentMapper.FromJsonString(json);

        var active = doc.GetField("active") as StringField;
        var deleted = doc.GetField("deleted") as StringField;
        Assert.NotNull(active);
        Assert.NotNull(deleted);
        Assert.Equal("true", active!.Value);
        Assert.Equal("false", deleted!.Value);
    }

    /// <summary>
    /// Verifies the Null Values: Are Skipped scenario.
    /// </summary>
    [Fact(DisplayName = "Null Values: Are Skipped")]
    public void NullValues_AreSkipped()
    {
        var json = """{"name": "Alice", "bio": null}""";
        var doc = JsonDocumentMapper.FromJsonString(json);

        Assert.Null(doc.GetField("bio"));
        Assert.NotNull(doc.GetField("name"));
    }

    /// <summary>
    /// Verifies that JSON strings now default to StringField rather than TextField.
    /// </summary>
    [Fact(DisplayName = "Long Strings: Default To String Fields")]
    public void LongStrings_DefaultToStringFields()
    {
        var longText = new string('x', 100);
        var json = $$"""{"body": "{{longText}}"}""";
        var doc = JsonDocumentMapper.FromJsonString(json);

        Assert.IsType<StringField>(doc.GetField("body"));
    }

    /// <summary>
    /// Verifies the Custom Separator: Used For Nested Names scenario.
    /// </summary>
    [Fact(DisplayName = "Custom Separator: Used For Nested Names")]
    public void CustomSeparator_UsedForNestedNames()
    {
        var json = """{"address": {"city": "London"}}""";
        var opts = new JsonMappingOptions { FieldNameSeparator = "/" };
        var doc = JsonDocumentMapper.FromJsonString(json, opts);

        Assert.NotNull(doc.GetField("address/city"));
    }

    /// <summary>
    /// Verifies the Empty Object: Returns Empty Document scenario.
    /// </summary>
    [Fact(DisplayName = "Empty Object: Returns Empty Document")]
    public void EmptyObject_ReturnsEmptyDocument()
    {
        var doc = JsonDocumentMapper.FromJsonString("{}");
        Assert.Empty(doc.Fields);
    }

    /// <summary>
    /// Verifies that large JSON integers are preserved as Int64Field.
    /// </summary>
    [Fact(DisplayName = "Large Integers: Preserved As Int64")]
    public void LargeIntegers_PreservedAsInt64()
    {
        var json = """{"id": 9223372036854775807}""";
        var doc = JsonDocumentMapper.FromJsonString(json);

        var field = Assert.IsType<Int64Field>(doc.GetField("id"));
        Assert.Equal(long.MaxValue, field.Value);
    }

    /// <summary>
    /// Verifies that non-integer JSON numbers are mapped as NumericField.
    /// </summary>
    [Fact(DisplayName = "Floating Point Numbers: Mapped As NumericField")]
    public void FloatingPointNumbers_MappedAsNumericField()
    {
        var json = """{"score": 3.14}""";
        var doc = JsonDocumentMapper.FromJsonString(json);

        Assert.IsType<NumericField>(doc.GetField("score"));
    }

    /// <summary>
    /// Verifies that exceeding MaxDepth throws instead of silently dropping data.
    /// </summary>
    [Fact(DisplayName = "Max Depth Exceeded: Throws")]
    public void MaxDepthExceeded_Throws()
    {
        var json = """{"a": {"b": {"c": 1}}}""";
        var opts = new JsonMappingOptions { MaxDepth = 1 };

        Assert.Throws<InvalidOperationException>(() => JsonDocumentMapper.FromJsonString(json, opts));
    }

    /// <summary>
    /// Verifies that invalid options are rejected on construction.
    /// </summary>
    [Fact(DisplayName = "Invalid Options: Rejected")]
    public void InvalidOptions_Rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new JsonMappingOptions { MaxDepth = -1 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new JsonMappingOptions { StringFieldMaxLength = -1 });
    }
}
