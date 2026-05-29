using System;
using Rowles.LeanCorpus.Mapping;
using Rowles.LeanCorpus.Mapping.Attributes;

namespace Rowles.LeanCorpus.Tests.SourceGen.Models;

[LeanDocument]
public partial class UnixSecondsProduct
{
    [LeanString("id", Required = true)]
    public required string Id { get; init; }

    [LeanNumeric("at", Encoding = LeanNumericEncoding.UnixSeconds)]
    public DateTimeOffset At { get; init; }
}

[LeanDocument]
public partial class UtcTicksProduct
{
    [LeanString("id", Required = true)]
    public required string Id { get; init; }

    [LeanNumeric("at", Encoding = LeanNumericEncoding.UtcTicks)]
    public DateTimeOffset At { get; init; }
}

[LeanDocument(Name = "RenamedProduct")]
public partial class NamedProduct
{
    [LeanString("id", Required = true)]
    public required string Id { get; init; }
}

[LeanDocument]
public partial class StoredStringProduct
{
    [LeanString("id", Required = true)]
    public required string Id { get; init; }

    [LeanStored("note")]
    public string? Note { get; init; }
}

[LeanDocument]
public partial struct StructProduct
{
    [LeanString("id", Required = true)]
    public required string Id { get; init; }

    [LeanNumeric("price")]
    public double Price { get; init; }
}

[LeanDocument]
public partial class NonPartialProduct
{
    [LeanString("id", Required = true)]
    public required string Id { get; init; }

    [LeanNumeric("price")]
    public double Price { get; init; }
}
