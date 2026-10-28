using System;
using Rowles.LeanCorpus.Mapping;
using Xunit;

namespace Rowles.LeanCorpus.Tests.SourceGen;

public sealed class EncoderTests
{
    [Fact]
    public void UnixMilliseconds_round_trips()
    {
        var value = new DateTimeOffset(2024, 6, 15, 12, 30, 45, 123, TimeSpan.Zero);
        double encoded = LeanNumericEncoders.ToUnixMilliseconds(value);
        var decoded = LeanNumericEncoders.FromUnixMilliseconds(encoded);
        Assert.Equal(value, decoded);
    }

    [Fact]
    public void UnixSeconds_round_trips_to_second_precision()
    {
        var value = new DateTimeOffset(2024, 6, 15, 12, 30, 45, TimeSpan.Zero);
        double encoded = LeanNumericEncoders.ToUnixSeconds(value);
        var decoded = LeanNumericEncoders.FromUnixSeconds(encoded);
        Assert.Equal(value, decoded);
    }

    [Fact]
    public void UtcTicks_round_trips()
    {
        // Pick a value with limited precision so double->long->DateTimeOffset survives the trip.
        var value = new DateTimeOffset(2024, 6, 15, 12, 30, 45, TimeSpan.Zero);
        double encoded = LeanNumericEncoders.ToUtcTicks(value);
        var decoded = LeanNumericEncoders.FromUtcTicks(encoded);
        Assert.Equal(value.UtcTicks, decoded.UtcTicks);
    }

    [Fact]
    public void DateOnly_round_trips()
    {
        var value = new DateOnly(2024, 6, 15);
        double encoded = LeanNumericEncoders.ToDayNumber(value);
        var decoded = LeanNumericEncoders.FromDayNumber(encoded);
        Assert.Equal(value, decoded);
    }

    [Fact]
    public void TimeOnly_round_trips()
    {
        var value = new TimeOnly(12, 30, 45, 123);
        double encoded = LeanNumericEncoders.ToTimeOnlyTicks(value);
        var decoded = LeanNumericEncoders.FromTimeOnlyTicks(encoded);
        Assert.Equal(value, decoded);
    }

    [Fact]
    public void Decimal_round_trips_through_invariant_string()
    {
        decimal value = 12345.6789012345m;
        string encoded = LeanNumericEncoders.ToDecimalString(value);
        decimal decoded = LeanNumericEncoders.FromDecimalString(encoded);
        Assert.Equal(value, decoded);
    }

    [Fact]
    public void Decimal_negative_round_trips()
    {
        decimal value = -987654321.123456789m;
        Assert.Equal(value, LeanNumericEncoders.FromDecimalString(LeanNumericEncoders.ToDecimalString(value)));
    }
}
