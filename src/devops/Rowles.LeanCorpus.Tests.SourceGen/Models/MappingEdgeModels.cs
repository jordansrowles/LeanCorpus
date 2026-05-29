using System;
using System.Collections.Generic;
using Rowles.LeanCorpus.Mapping;
using Rowles.LeanCorpus.Mapping.Attributes;

namespace Rowles.LeanCorpus.Tests.SourceGen.Models;

[LeanDocument(StrictSchema = false)]
public partial class LooseProduct
{
    [LeanString("id", Required = true)]
    public required string Id { get; init; }
}

[LeanDocument]
public partial class CollectionProduct
{
    [LeanString("id", Required = true)]
    public required string Id { get; init; }

    [LeanText("tag", Required = true)]
    public required IReadOnlyList<string> Tags { get; init; }

    [LeanText("alias")]
    public IReadOnlyList<string>? Aliases { get; init; }
}

[LeanDocument]
public partial class GeoProduct
{
    [LeanString("id", Required = true)]
    public required string Id { get; init; }

    [LeanGeoPoint("loc")]
    public LeanGeoLocation? Location { get; init; }
}

[LeanDocument]
public partial class TemporalProduct
{
    [LeanString("id", Required = true)]
    public required string Id { get; init; }

    [LeanNumeric("date", Encoding = LeanNumericEncoding.DateOnlyDayNumber)]
    public DateOnly Date { get; init; }

    [LeanNumeric("time", Encoding = LeanNumericEncoding.TimeOnlyTicks)]
    public TimeOnly Time { get; init; }
}
