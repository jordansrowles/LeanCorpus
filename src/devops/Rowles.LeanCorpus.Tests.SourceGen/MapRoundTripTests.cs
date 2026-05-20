using System;
using System.Collections.Generic;
using System.Linq;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Mapping;
using Rowles.LeanCorpus.Tests.SourceGen.Models;
using Xunit;

namespace Rowles.LeanCorpus.Tests.SourceGen;

public sealed class MapRoundTripTests
{
    [Fact]
    public void Generated_Map_exposes_field_bindings()
    {
        var map = ProductIndex.Map;
        Assert.NotNull(map);
        Assert.Equal("Product", map.DocumentName);
        Assert.Contains(map.Fields, f => f.Name == "id" && f.IsRequired);
        Assert.Contains(map.Fields, f => f.Name == "title" && !f.IsRequired);
    }

    [Fact]
    public void ToDocument_emits_expected_fields_for_a_full_product()
    {
        var p = new Product
        {
            Id = "p-1",
            Title = "Widget",
            Tags = new[] { "red", "blue" },
            Price = 19.95,
            Count = 7,
            CreatedAt = new DateTimeOffset(2024, 6, 15, 12, 30, 45, TimeSpan.Zero),
            Amount = 100.50m,
            Blob = new byte[] { 1, 2, 3, 4 }
        };

        var doc = ProductIndex.ToDocument(p);
        var allFields = doc.Fields.ToList();

        Assert.Contains(allFields, f => f.Name == "id");
        Assert.Equal(2, allFields.Count(f => f.Name == "tag"));
        Assert.Contains(allFields, f => f.Name == "price");
        Assert.Contains(allFields, f => f.Name == "count");
        Assert.Contains(allFields, f => f.Name == "at");
        Assert.Contains(allFields, f => f.Name == "amount");
        Assert.Contains(allFields, f => f.Name == "blob");
    }

    [Fact]
    public void ToDocument_skips_null_optional_fields()
    {
        var p = new Product { Id = "p-2", Price = 1.0, Amount = 0m };
        var doc = ProductIndex.ToDocument(p);
        var fields = doc.Fields.ToList();

        Assert.DoesNotContain(fields, f => f.Name == "title");
        Assert.DoesNotContain(fields, f => f.Name == "tag");
        Assert.DoesNotContain(fields, f => f.Name == "count");
        Assert.DoesNotContain(fields, f => f.Name == "blob");
    }

    [Fact]
    public void CreateSchema_lists_every_field()
    {
        var schema = ProductIndex.CreateSchema(strict: true);
        Assert.True(schema.StrictMode);
        Assert.True(schema.Mappings.ContainsKey("id"));
        Assert.True(schema.Mappings.ContainsKey("title"));
        Assert.True(schema.Mappings.ContainsKey("tag"));
        Assert.True(schema.Mappings.ContainsKey("price"));
        Assert.True(schema.Mappings.ContainsKey("at"));
        Assert.True(schema.Mappings.ContainsKey("amount"));
        Assert.True(schema.Mappings.ContainsKey("blob"));
    }

    [Fact]
    public void FromStoredDocument_round_trips_string_numeric_and_decimal_fields()
    {
        var original = new Product
        {
            Id = "p-7",
            Title = "Round trip",
            Tags = new[] { "x", "y" },
            Price = 42.5,
            Count = 11,
            CreatedAt = new DateTimeOffset(2024, 1, 2, 3, 4, 5, TimeSpan.Zero),
            Amount = 123.456m,
            Blob = new byte[] { 9, 8, 7 }
        };

        var stored = new Dictionary<string, IReadOnlyList<string>>
        {
            ["id"] = new[] { original.Id },
            ["title"] = new[] { original.Title! },
            ["tag"] = (IReadOnlyList<string>)original.Tags!,
            ["price"] = new[] { original.Price.ToString(System.Globalization.CultureInfo.InvariantCulture) },
            ["count"] = new[] { original.Count!.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) },
            ["at"] = new[] { LeanNumericEncoders.ToUnixMilliseconds(original.CreatedAt).ToString(System.Globalization.CultureInfo.InvariantCulture) },
            ["amount"] = new[] { LeanNumericEncoders.ToDecimalString(original.Amount) }
        };
        var bin = new Dictionary<string, IReadOnlyList<byte[]>>
        {
            ["blob"] = new[] { original.Blob! }
        };
        var snapshot = StoredDocument.Create(stored, bin);

        var revived = ProductIndex.FromStoredDocument(snapshot);

        Assert.Equal(original.Id, revived.Id);
        Assert.Equal(original.Title, revived.Title);
        Assert.Equal(original.Tags, revived.Tags);
        Assert.Equal(original.Price, revived.Price);
        Assert.Equal(original.Count, revived.Count);
        Assert.Equal(original.CreatedAt, revived.CreatedAt);
        Assert.Equal(original.Amount, revived.Amount);
        Assert.Equal(original.Blob, revived.Blob);
    }

    [Fact]
    public void FromStoredDocument_throws_when_required_field_missing()
    {
        var snapshot = StoredDocument.Empty;
        Assert.Throws<InvalidOperationException>(() => ProductIndex.FromStoredDocument(snapshot));
    }
}
