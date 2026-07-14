namespace Rowles.LeanCorpus.Tests.Unit.Mapping;

/// <summary>
/// Unit tests for <see cref="LeanGeoLocation"/> record struct construction,
/// equality, deconstruction, and formatting.
/// </summary>
[Trait("Category", "Mapping")]
[Trait("Category", "UnitTest")]
public sealed class LeanGeoLocationTests
{
    [Fact(DisplayName = "LeanGeoLocation: Positional Constructor Sets Latitude And Longitude")]
    public void PositionalConstructor_SetsLatitudeAndLongitude()
    {
        var loc = new LeanGeoLocation(51.5074, -0.1278);

        Assert.Equal(51.5074, loc.Latitude);
        Assert.Equal(-0.1278, loc.Longitude);
    }

    [Fact(DisplayName = "LeanGeoLocation: Named Parameters Supported")]
    public void NamedParameters_Supported()
    {
        var loc = new LeanGeoLocation(Latitude: 48.8566, Longitude: 2.3522);

        Assert.Equal(48.8566, loc.Latitude);
        Assert.Equal(2.3522, loc.Longitude);
    }

    [Fact(DisplayName = "LeanGeoLocation: Equal Instances Are Equal")]
    public void EqualInstances_AreEqual()
    {
        var a = new LeanGeoLocation(51.5074, -0.1278);
        var b = new LeanGeoLocation(51.5074, -0.1278);

        Assert.Equal(a, b);
        Assert.True(a == b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact(DisplayName = "LeanGeoLocation: Different Instances Are Not Equal")]
    public void DifferentInstances_AreNotEqual()
    {
        var a = new LeanGeoLocation(51.5074, -0.1278);
        var b = new LeanGeoLocation(48.8566, 2.3522);

        Assert.NotEqual(a, b);
        Assert.True(a != b);
    }

    [Fact(DisplayName = "LeanGeoLocation: Deconstruction Produces Expected Values")]
    public void Deconstruction_ProducesExpectedValues()
    {
        var loc = new LeanGeoLocation(40.7128, -74.0060);
        (double lat, double lng) = loc;

        Assert.Equal(40.7128, lat);
        Assert.Equal(-74.0060, lng);
    }

    [Fact(DisplayName = "LeanGeoLocation: ToString Includes Coordinates")]
    public void ToString_IncludesCoordinates()
    {
        var loc = new LeanGeoLocation(51.5074, -0.1278);
        var s = loc.ToString();

        Assert.Contains("51.5074", s);
        Assert.Contains("-0.1278", s);
    }
}