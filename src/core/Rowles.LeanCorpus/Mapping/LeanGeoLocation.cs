namespace Rowles.LeanCorpus.Mapping;

/// <summary>
/// Canonical geo-point payload used by the LeanCorpus source generator when mapping
/// properties annotated with <c>[LeanGeoPoint]</c>. Generated mappers project this type
/// to a <see cref="GeoPointField"/>.
/// </summary>
/// <param name="Latitude">Latitude in decimal degrees in the range [-90, 90].</param>
/// <param name="Longitude">Longitude in decimal degrees in the range [-180, 180].</param>
public readonly record struct LeanGeoLocation(double Latitude, double Longitude);
