using System;
using System.Collections.Generic;
using Rowles.LeanCorpus.Mapping;
using Rowles.LeanCorpus.Mapping.Attributes;

namespace Rowles.LeanCorpus.Tests.SourceGen.Models;

[LeanDocument]
public partial class Product
{
    [LeanString("id", Required = true)]
    public required string Id { get; init; }

    [LeanText("title")]
    public string? Title { get; init; }

    [LeanText("tag")]
    public IReadOnlyList<string>? Tags { get; init; }

    [LeanNumeric("price")]
    public double Price { get; init; }

    [LeanNumeric("count")]
    public int? Count { get; init; }

    [LeanNumeric("at", Encoding = LeanNumericEncoding.UnixMilliseconds)]
    public DateTimeOffset CreatedAt { get; init; }

    [LeanNumeric("amount", Encoding = LeanNumericEncoding.DecimalAsString)]
    public decimal Amount { get; init; }

    [LeanStored("blob")]
    public byte[]? Blob { get; init; }
}
