using Rowles.LeanLucene.Document.Fields;

namespace Rowles.LeanLucene.Tests.Document;

/// <summary>
/// Contains unit tests for Field Validation.
/// </summary>
public sealed class FieldValidationTests
{
    /// <summary>
    /// Verifies the Fields: Reject Unsafe Field Names scenario.
    /// </summary>
    /// <param name="name">The name value for the test case.</param>
    [Theory(DisplayName = "Fields: Reject Unsafe Field Names")]
    [InlineData("")]
    [InlineData("bad\0name")]
    [InlineData("bad\u0001name")]
    public void Fields_RejectUnsafeFieldNames(string name)
    {
        Assert.ThrowsAny<ArgumentException>(() => new TextField(name, "value"));
        Assert.ThrowsAny<ArgumentException>(() => new StringField(name, "value"));
        Assert.ThrowsAny<ArgumentException>(() => new NumericField(name, 1));
        Assert.ThrowsAny<ArgumentException>(() => new VectorField(name, new float[] { 1, 2 }));
        Assert.ThrowsAny<ArgumentException>(() => new GeoPointField(name, 51.5, -0.1));
    }

    /// <summary>
    /// Verifies the Vector Field: Rejects Empty And Non Finite Vectors scenario.
    /// </summary>
    [Fact(DisplayName = "Vector Field: Rejects Empty And Non Finite Vectors")]
    public void VectorField_RejectsEmptyAndNonFiniteVectors()
    {
        Assert.Throws<ArgumentException>(() => new VectorField("embedding", Array.Empty<float>()));
        Assert.Throws<ArgumentException>(() => new VectorField("embedding", new[] { 1f, float.NaN }));
        Assert.Throws<ArgumentException>(() => new VectorField("embedding", new[] { 1f, float.PositiveInfinity }));
    }

    /// <summary>
    /// Verifies the Geo Point Field: Rejects Out Of Range Coordinates scenario.
    /// </summary>
    /// <param name="latitude">The latitude value for the test case.</param>
    /// <param name="longitude">The longitude value for the test case.</param>
    [Theory(DisplayName = "Geo Point Field: Rejects Out Of Range Coordinates")]
    [InlineData(-90.1, 0)]
    [InlineData(90.1, 0)]
    [InlineData(0, -180.1)]
    [InlineData(0, 180.1)]
    public void GeoPointField_RejectsOutOfRangeCoordinates(double latitude, double longitude)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new GeoPointField("location", latitude, longitude));
    }
}
