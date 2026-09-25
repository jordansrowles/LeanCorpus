using System.Globalization;
using System.Text;
using Rowles.LeanCorpus.Search.Geo;
using Rowles.LeanCorpus.Search.XY;

namespace Rowles.LeanCorpus.Search.Spatial;

/// <summary>Writes supported built-in Geo and XY geometries as canonical 2D WKT.</summary>
public static class WktWriter
{
    /// <summary>Writes a supported geographic geometry.</summary>
    public static string Write(IGeoGeometry geometry)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        var builder = new StringBuilder();
        AppendGeo(builder, geometry);
        return builder.ToString();
    }

    /// <summary>Writes a supported Cartesian geometry.</summary>
    public static string Write(IXYGeometry geometry)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        var builder = new StringBuilder();
        AppendXY(builder, geometry);
        return builder.ToString();
    }

    private static void AppendGeo(StringBuilder builder, IGeoGeometry geometry)
    {
        switch (geometry)
        {
            case GeoPoint point:
                builder.Append("POINT (");
                AppendCoordinate(builder, point.Longitude, point.Latitude);
                builder.Append(')');
                break;
            case GeoLineString line:
                builder.Append("LINESTRING ");
                AppendGeoCoordinateSequence(builder, line.Points);
                break;
            case GeoPolygon polygon:
                AppendGeoPolygon(builder, polygon);
                break;
            case GeoRectangle rectangle:
                AppendGeoRectangle(builder, rectangle);
                break;
            case GeoGeometryCollection collection:
                builder.Append("GEOMETRYCOLLECTION (");
                for (int i = 0; i < collection.Geometries.Count; i++)
                {
                    if (i > 0)
                        builder.Append(", ");
                    AppendGeo(builder, collection.Geometries[i]);
                }
                builder.Append(')');
                break;
            case GeoCircle:
                throw new NotSupportedException("Geo circles have no supported 2D WKT representation.");
            default:
                throw new NotSupportedException($"Geo geometry type '{geometry.GetType().FullName}' is not supported by the WKT writer.");
        }
    }

    private static void AppendXY(StringBuilder builder, IXYGeometry geometry)
    {
        switch (geometry)
        {
            case XYPoint point:
                builder.Append("POINT (");
                AppendCoordinate(builder, point.X, point.Y);
                builder.Append(')');
                break;
            case XYLineString line:
                builder.Append("LINESTRING ");
                AppendXYCoordinateSequence(builder, line.Points);
                break;
            case XYPolygon polygon:
                AppendXYPolygon(builder, polygon);
                break;
            case XYRectangle rectangle:
                AppendXYRectangle(builder, rectangle);
                break;
            case XYGeometryCollection collection:
                builder.Append("GEOMETRYCOLLECTION (");
                for (int i = 0; i < collection.Geometries.Count; i++)
                {
                    if (i > 0)
                        builder.Append(", ");
                    AppendXY(builder, collection.Geometries[i]);
                }
                builder.Append(')');
                break;
            case XYCircle:
                throw new NotSupportedException("XY circles have no supported 2D WKT representation.");
            default:
                throw new NotSupportedException($"XY geometry type '{geometry.GetType().FullName}' is not supported by the WKT writer.");
        }
    }

    private static void AppendGeoPolygon(StringBuilder builder, GeoPolygon polygon)
    {
        builder.Append("POLYGON (");
        AppendGeoCoordinateSequence(builder, polygon.Shell);
        foreach (IReadOnlyList<GeoPoint> hole in polygon.Holes)
        {
            builder.Append(", ");
            AppendGeoCoordinateSequence(builder, hole);
        }
        builder.Append(')');
    }

    private static void AppendGeoRectangle(StringBuilder builder, GeoRectangle rectangle)
    {
        double longitudeSpan = rectangle.East < rectangle.West
            ? rectangle.East + 360d - rectangle.West
            : rectangle.East - rectangle.West;
        bool zeroWidth = longitudeSpan == 0d;
        bool zeroHeight = rectangle.South == rectangle.North;

        if (zeroWidth && zeroHeight)
        {
            builder.Append("POINT (");
            AppendCoordinate(builder, rectangle.West, rectangle.South);
            builder.Append(')');
            return;
        }

        if (zeroHeight || zeroWidth)
        {
            builder.Append("LINESTRING ");
            if (zeroWidth)
            {
                AppendGeoCoordinateSequence(builder,
                [
                    new GeoPoint(rectangle.South, rectangle.West),
                    new GeoPoint(rectangle.North, rectangle.West),
                ]);
            }
            else
            {
                AppendGeoLongitudeLine(builder, rectangle.West, longitudeSpan, rectangle.South);
            }
            return;
        }

        int longitudeSteps = Math.Max(1, (int)Math.Ceiling(longitudeSpan / 90d));
        double unwrappedEast = rectangle.West + longitudeSpan;
        builder.Append("POLYGON ((");
        for (int i = 0; i <= longitudeSteps; i++)
        {
            if (i > 0)
                builder.Append(", ");
            double longitude = rectangle.West + longitudeSpan * i / longitudeSteps;
            AppendCoordinate(builder, NormaliseWktLongitude(longitude), rectangle.South);
        }
        builder.Append(", ");
        AppendCoordinate(builder, NormaliseWktLongitude(unwrappedEast), rectangle.North);
        for (int i = longitudeSteps - 1; i >= 0; i--)
        {
            builder.Append(", ");
            double longitude = rectangle.West + longitudeSpan * i / longitudeSteps;
            AppendCoordinate(builder, NormaliseWktLongitude(longitude), rectangle.North);
        }
        builder.Append(", ");
        AppendCoordinate(builder, NormaliseWktLongitude(rectangle.West), rectangle.South);
        builder.Append("))");
    }

    private static void AppendGeoLongitudeLine(StringBuilder builder, double west, double span, double latitude)
    {
        int steps = Math.Max(1, (int)Math.Ceiling(span / 90d));
        builder.Append('(');
        for (int i = 0; i <= steps; i++)
        {
            if (i > 0)
                builder.Append(", ");
            double longitude = west + span * i / steps;
            AppendCoordinate(builder, NormaliseWktLongitude(longitude), latitude);
        }
        builder.Append(')');
    }

    private static double NormaliseWktLongitude(double longitude)
    {
        if (longitude > 180d)
            longitude -= 360d * Math.Ceiling((longitude - 180d) / 360d);
        else if (longitude < -180d)
            longitude += 360d * Math.Ceiling((-180d - longitude) / 360d);
        return longitude == 0d ? 0d : longitude;
    }

    private static void AppendXYRectangle(StringBuilder builder, XYRectangle rectangle)
    {
        bool zeroWidth = rectangle.MinX == rectangle.MaxX;
        bool zeroHeight = rectangle.MinY == rectangle.MaxY;
        if (zeroWidth && zeroHeight)
        {
            builder.Append("POINT (");
            AppendCoordinate(builder, rectangle.MinX, rectangle.MinY);
            builder.Append(')');
            return;
        }

        if (zeroWidth || zeroHeight)
        {
            builder.Append("LINESTRING ");
            AppendXYCoordinateSequence(builder,
            [
                new XYPoint(rectangle.MinX, rectangle.MinY),
                new XYPoint(rectangle.MaxX, rectangle.MaxY),
            ]);
            return;
        }

        builder.Append("POLYGON (");
        AppendXYCoordinateSequence(builder,
        [
            new XYPoint(rectangle.MinX, rectangle.MinY),
            new XYPoint(rectangle.MaxX, rectangle.MinY),
            new XYPoint(rectangle.MaxX, rectangle.MaxY),
            new XYPoint(rectangle.MinX, rectangle.MaxY),
            new XYPoint(rectangle.MinX, rectangle.MinY),
        ]);
        builder.Append(')');
    }

    private static void AppendXYPolygon(StringBuilder builder, XYPolygon polygon)
    {
        builder.Append("POLYGON (");
        AppendXYCoordinateSequence(builder, polygon.Shell);
        foreach (IReadOnlyList<XYPoint> hole in polygon.Holes)
        {
            builder.Append(", ");
            AppendXYCoordinateSequence(builder, hole);
        }
        builder.Append(')');
    }

    private static void AppendGeoCoordinateSequence(StringBuilder builder, IReadOnlyList<GeoPoint> points)
    {
        builder.Append('(');
        for (int i = 0; i < points.Count; i++)
        {
            if (i > 0)
                builder.Append(", ");
            AppendCoordinate(builder, points[i].Longitude, points[i].Latitude);
        }
        builder.Append(')');
    }

    private static void AppendXYCoordinateSequence(StringBuilder builder, IReadOnlyList<XYPoint> points)
    {
        builder.Append('(');
        for (int i = 0; i < points.Count; i++)
        {
            if (i > 0)
                builder.Append(", ");
            AppendCoordinate(builder, points[i].X, points[i].Y);
        }
        builder.Append(')');
    }

    private static void AppendCoordinate(StringBuilder builder, double x, double y)
    {
        builder.Append(x.ToString("R", CultureInfo.InvariantCulture));
        builder.Append(' ');
        builder.Append(y.ToString("R", CultureInfo.InvariantCulture));
    }

    private static void AppendCoordinate(StringBuilder builder, float x, float y)
    {
        builder.Append(x.ToString("R", CultureInfo.InvariantCulture));
        builder.Append(' ');
        builder.Append(y.ToString("R", CultureInfo.InvariantCulture));
    }
}
