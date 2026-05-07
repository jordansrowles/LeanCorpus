namespace Rowles.LeanLucene.Search.Geo;

/// <summary>
/// Query matching documents within a specified distance from a centre point.
/// </summary>
public sealed class GeoDistanceQuery : Query
{
    /// <summary>
    /// Initialises a new <see cref="GeoDistanceQuery"/> for the specified centre and radius.
    /// </summary>
    /// <param name="field">The geo-point field name (base name; lat and lon sub-fields are appended automatically).</param>
    /// <param name="centreLat">The centre latitude in decimal degrees.</param>
    /// <param name="centreLon">The centre longitude in decimal degrees.</param>
    /// <param name="radiusMetres">The search radius in metres.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="field"/> is null.</exception>
    public GeoDistanceQuery(string field, double centreLat, double centreLon, double radiusMetres)
    {
        Field = field ?? throw new ArgumentNullException(nameof(field));
        CentreLat = centreLat;
        CentreLon = centreLon;
        RadiusMetres = radiusMetres;
    }

    /// <inheritdoc/>
    public override string Field { get; }

    /// <summary>Gets the centre latitude of the distance query in decimal degrees.</summary>
    public double CentreLat { get; }

    /// <summary>Gets the centre longitude of the distance query in decimal degrees.</summary>
    public double CentreLon { get; }

    /// <summary>Gets the search radius in metres.</summary>
    public double RadiusMetres { get; }

    /// <inheritdoc/>
    public override bool Equals(object? obj)
        => obj is GeoDistanceQuery q &&
           q.Field == Field && q.CentreLat == CentreLat &&
           q.CentreLon == CentreLon && q.RadiusMetres == RadiusMetres;

    /// <inheritdoc/>
    public override int GetHashCode()
        => CombineBoost(HashCode.Combine(nameof(GeoDistanceQuery), Field, CentreLat, CentreLon, RadiusMetres));
}
