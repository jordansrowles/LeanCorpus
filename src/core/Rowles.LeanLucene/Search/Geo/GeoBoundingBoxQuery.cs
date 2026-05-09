namespace Rowles.LeanLucene.Search.Geo;

/// <summary>
/// Query matching documents within a geographic bounding box.
/// </summary>
public sealed class GeoBoundingBoxQuery : Query
{
    /// <summary>
    /// Initialises a new <see cref="GeoBoundingBoxQuery"/> for the specified bounding box.
    /// </summary>
    /// <param name="field">The geo-point field name (base name; lat and lon sub-fields are appended automatically).</param>
    /// <param name="minLat">The minimum latitude in decimal degrees.</param>
    /// <param name="maxLat">The maximum latitude in decimal degrees.</param>
    /// <param name="minLon">The minimum longitude in decimal degrees.</param>
    /// <param name="maxLon">The maximum longitude in decimal degrees.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="field"/> is null.</exception>
    public GeoBoundingBoxQuery(string field, double minLat, double maxLat, double minLon, double maxLon)
    {
        Field = field ?? throw new ArgumentNullException(nameof(field));
        MinLat = minLat; MaxLat = maxLat;
        MinLon = minLon; MaxLon = maxLon;
    }

    /// <inheritdoc/>
    public override string Field { get; }

    /// <summary>Gets the minimum latitude of the bounding box in decimal degrees.</summary>
    public double MinLat { get; }

    /// <summary>Gets the maximum latitude of the bounding box in decimal degrees.</summary>
    public double MaxLat { get; }

    /// <summary>Gets the minimum longitude of the bounding box in decimal degrees.</summary>
    public double MinLon { get; }

    /// <summary>Gets the maximum longitude of the bounding box in decimal degrees.</summary>
    public double MaxLon { get; }

    /// <inheritdoc/>
    public override bool Equals(object? obj)
        => obj is GeoBoundingBoxQuery q &&
           q.Field == Field && q.MinLat == MinLat && q.MaxLat == MaxLat &&
           q.MinLon == MinLon && q.MaxLon == MaxLon;

    /// <inheritdoc/>
    public override int GetHashCode()
        => CombineBoost(HashCode.Combine(nameof(GeoBoundingBoxQuery), Field, MinLat, MaxLat, MinLon, MaxLon));
}
