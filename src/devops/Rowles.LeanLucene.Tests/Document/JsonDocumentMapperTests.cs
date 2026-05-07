using System.Text.Json;
using Rowles.LeanLucene.Document.Fields;
using Rowles.LeanLucene.Document.Json;

namespace Rowles.LeanLucene.Tests.Document;

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
        Assert.IsType<NumericField>(doc.GetField("age"));
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
    /// Verifies the Long Strings: Become Text Fields scenario.
    /// </summary>
    [Fact(DisplayName = "Long Strings: Become Text Fields")]
    public void LongStrings_BecomeTextFields()
    {
        var longText = new string('x', 100);
        var json = $$"""{"body": "{{longText}}"}""";
        var doc = JsonDocumentMapper.FromJsonString(json);

        Assert.IsType<TextField>(doc.GetField("body"));
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
}
