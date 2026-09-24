using System.Globalization;
using Rowles.LeanCorpus.Search.Geo;
using Rowles.LeanCorpus.Search.XY;

namespace Rowles.LeanCorpus.Search.Spatial;

/// <summary>Parses a bounded subset of two-dimensional Well-Known Text.</summary>
public static class WktReader
{
    /// <summary>Parses Geo WKT whose coordinates are longitude followed by latitude.</summary>
    public static IGeoGeometry ParseGeo(string wkt)
    {
        ArgumentNullException.ThrowIfNull(wkt);
        return ParseGeo(wkt.AsSpan());
    }

    /// <summary>Parses Geo WKT whose coordinates are longitude followed by latitude.</summary>
    public static IGeoGeometry ParseGeo(ReadOnlySpan<char> wkt)
    {
        try
        {
            return new Parser(wkt, geo: true).ParseGeo();
        }
        catch (FormatException)
        {
            throw;
        }
        catch (ArgumentException exception)
        {
            throw new FormatException("The WKT geometry is invalid for the Geo coordinate model.", exception);
        }
    }

    /// <summary>Parses XY WKT whose coordinates are X followed by Y.</summary>
    public static IXYGeometry ParseXY(string wkt)
    {
        ArgumentNullException.ThrowIfNull(wkt);
        return ParseXY(wkt.AsSpan());
    }

    /// <summary>Parses XY WKT whose coordinates are X followed by Y.</summary>
    public static IXYGeometry ParseXY(ReadOnlySpan<char> wkt)
    {
        try
        {
            return new Parser(wkt, geo: false).ParseXY();
        }
        catch (FormatException)
        {
            throw;
        }
        catch (ArgumentException exception)
        {
            throw new FormatException("The WKT geometry is invalid for the XY coordinate model.", exception);
        }
    }

    private ref struct Parser
    {
        private const int MaximumCoordinatePositions = 1_000_000;
        private const int MaximumGeometryCollectionDepth = 32;
        private readonly ReadOnlySpan<char> _text;
        private readonly bool _geo;
        private int _position;
        private int _coordinatePositions;

        internal Parser(ReadOnlySpan<char> text, bool geo)
        {
            _text = text;
            _geo = geo;
            _position = 0;
            _coordinatePositions = 0;
        }

        internal IGeoGeometry ParseGeo()
        {
            if (!_geo)
                throw new InvalidOperationException("This WKT parser is configured for XY coordinates.");
            object geometry = ParseGeometry(collectionDepth: 0);
            SkipWhitespace();
            if (_position != _text.Length)
                Fail("Unexpected trailing text follows the WKT geometry.");
            return (IGeoGeometry)geometry;
        }

        internal IXYGeometry ParseXY()
        {
            if (_geo)
                throw new InvalidOperationException("This WKT parser is configured for Geo coordinates.");
            object geometry = ParseGeometry(collectionDepth: 0);
            SkipWhitespace();
            if (_position != _text.Length)
                Fail("Unexpected trailing text follows the WKT geometry.");
            return (IXYGeometry)geometry;
        }

        private object ParseGeometry(int collectionDepth)
        {
            string keyword = ReadKeyword();
            return keyword switch
            {
                "POINT" => ParsePoint(),
                "LINESTRING" => BuildLine(ParseCoordinateSequence()),
                "POLYGON" => ParsePolygonBody(),
                "MULTIPOINT" => ParseMultiPoint(),
                "MULTILINESTRING" => ParseMultiLineString(),
                "MULTIPOLYGON" => ParseMultiPolygon(),
                "GEOMETRYCOLLECTION" => ParseGeometryCollection(collectionDepth),
                _ => throw Error($"Unsupported WKT geometry keyword '{keyword}'."),
            };
        }

        private object ParsePoint()
        {
            Expect('(');
            Coordinate coordinate = ReadCoordinate();
            Expect(')');
            return BuildPoint(coordinate);
        }

        private object ParseMultiPoint()
        {
            Expect('(');
            var points = new List<object>();
            do
            {
                SkipWhitespace();
                bool parenthesised = Peek() == '(';
                if (parenthesised)
                    Expect('(');
                Coordinate coordinate = ReadCoordinate();
                if (parenthesised)
                    Expect(')');
                points.Add(BuildPoint(coordinate));
            }
            while (ConsumeComma());
            Expect(')');
            return BuildCollection(points);
        }

        private object ParseMultiLineString()
        {
            Expect('(');
            var lines = new List<object>();
            do
            {
                lines.Add(BuildLine(ParseCoordinateSequence()));
            }
            while (ConsumeComma());
            Expect(')');
            return BuildCollection(lines);
        }

        private object ParseMultiPolygon()
        {
            Expect('(');
            var polygons = new List<object>();
            do
            {
                polygons.Add(ParsePolygonBody());
            }
            while (ConsumeComma());
            Expect(')');
            return BuildCollection(polygons);
        }

        private object ParseGeometryCollection(int collectionDepth)
        {
            if (collectionDepth >= MaximumGeometryCollectionDepth)
                Fail($"WKT geometry collection nesting exceeds {MaximumGeometryCollectionDepth}.");

            Expect('(');
            var geometries = new List<object>();
            do
            {
                geometries.Add(ParseGeometry(collectionDepth + 1));
            }
            while (ConsumeComma());
            Expect(')');
            return BuildCollection(geometries);
        }

        private object ParsePolygonBody()
        {
            Expect('(');
            var rings = new List<List<Coordinate>>();
            do
            {
                List<Coordinate> ring = ParseCoordinateSequence();
                if (ring.Count < 4)
                    Fail("A WKT polygon ring must contain at least four positions including its closing point.");
                if (ring[0] != ring[^1])
                    Fail("WKT polygon rings must be explicitly closed.");
                rings.Add(ring);
            }
            while (ConsumeComma());
            Expect(')');
            return BuildPolygon(rings);
        }

        private List<Coordinate> ParseCoordinateSequence()
        {
            Expect('(');
            var coordinates = new List<Coordinate>();
            do
            {
                coordinates.Add(ReadCoordinate());
            }
            while (ConsumeComma());
            Expect(')');
            if (coordinates.Count < 2)
                Fail("A WKT line string must contain at least two positions.");
            return coordinates;
        }

        private Coordinate ReadCoordinate()
        {
            SkipWhitespace();
            double x = ReadNumber();
            int beforeWhitespace = _position;
            SkipWhitespace();
            if (_position == beforeWhitespace)
                Fail("A WKT coordinate must contain exactly two whitespace-separated ordinates.");
            double y = ReadNumber();
            SkipWhitespace();
            if (Peek() is not (',' or ')' or '\0'))
                Fail("A WKT coordinate must contain exactly two ordinates.");

            _coordinatePositions++;
            if (_coordinatePositions > MaximumCoordinatePositions)
                Fail($"A WKT geometry exceeds {MaximumCoordinatePositions} coordinate positions.");

            if (_geo)
            {
                if (x is < -180d or > 180d || y is < -90d or > 90d)
                    Fail("Geo WKT coordinates must be longitude in [-180,180] followed by latitude in [-90,90].");
                return new Coordinate(x, y);
            }

            float floatX = (float)x;
            float floatY = (float)y;
            if (!float.IsFinite(floatX) || !float.IsFinite(floatY))
                Fail("XY WKT coordinates must be finite single-precision values.");
            return new Coordinate(floatX, floatY);
        }

        private double ReadNumber()
        {
            SkipWhitespace();
            int start = _position;
            while (_position < _text.Length && !IsWhitespace(_text[_position])
                && _text[_position] is not (',' or '(' or ')'))
            {
                _position++;
            }

            double value = default;
            bool parsed = start != _position
                && double.TryParse(_text[start.._position], NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                && double.IsFinite(value);
            if (!parsed)
                Fail("A WKT ordinate must be a finite invariant-culture number.");
            return value;
        }

        private object BuildPoint(Coordinate coordinate)
            => _geo
                ? new GeoPoint(coordinate.Y, coordinate.X)
                : new XYPoint((float)coordinate.X, (float)coordinate.Y);

        private object BuildLine(List<Coordinate> coordinates)
        {
            if (_geo)
            {
                var points = new GeoPoint[coordinates.Count];
                for (int i = 0; i < points.Length; i++)
                    points[i] = new GeoPoint(coordinates[i].Y, coordinates[i].X);
                return new GeoLineString(points);
            }

            var xyPoints = new XYPoint[coordinates.Count];
            for (int i = 0; i < xyPoints.Length; i++)
                xyPoints[i] = new XYPoint((float)coordinates[i].X, (float)coordinates[i].Y);
            return new XYLineString(xyPoints);
        }

        private object BuildPolygon(List<List<Coordinate>> rings)
        {
            if (_geo)
            {
                GeoPoint[] shell = ToGeoPoints(rings[0]);
                var holes = new GeoPoint[rings.Count - 1][];
                for (int i = 1; i < rings.Count; i++)
                    holes[i - 1] = ToGeoPoints(rings[i]);
                return new GeoPolygon(shell, holes);
            }

            XYPoint[] xyShell = ToXYPoints(rings[0]);
            var xyHoles = new XYPoint[rings.Count - 1][];
            for (int i = 1; i < rings.Count; i++)
                xyHoles[i - 1] = ToXYPoints(rings[i]);
            return new XYPolygon(xyShell, xyHoles);
        }

        private object BuildCollection(List<object> geometries)
        {
            var flat = new List<object>();
            foreach (object geometry in geometries)
            {
                if (_geo && geometry is GeoGeometryCollection geoCollection)
                {
                    foreach (IGeoGeometry component in geoCollection.Geometries)
                        flat.Add(component);
                }
                else if (!_geo && geometry is XYGeometryCollection xyCollection)
                {
                    foreach (IXYGeometry component in xyCollection.Geometries)
                        flat.Add(component);
                }
                else
                {
                    flat.Add(geometry);
                }
            }

            return _geo
                ? new GeoGeometryCollection(flat.Cast<IGeoGeometry>())
                : new XYGeometryCollection(flat.Cast<IXYGeometry>());
        }

        private static GeoPoint[] ToGeoPoints(List<Coordinate> coordinates)
        {
            var points = new GeoPoint[coordinates.Count];
            for (int i = 0; i < points.Length; i++)
                points[i] = new GeoPoint(coordinates[i].Y, coordinates[i].X);
            return points;
        }

        private static XYPoint[] ToXYPoints(List<Coordinate> coordinates)
        {
            var points = new XYPoint[coordinates.Count];
            for (int i = 0; i < points.Length; i++)
                points[i] = new XYPoint((float)coordinates[i].X, (float)coordinates[i].Y);
            return points;
        }

        private string ReadKeyword()
        {
            SkipWhitespace();
            int start = _position;
            while (_position < _text.Length && IsAsciiLetter(_text[_position]))
                _position++;
            if (_position == start || _position - start > 32)
                Fail("A WKT geometry keyword is missing or too long.");
            return _text[start.._position].ToString().ToUpperInvariant();
        }

        private bool ConsumeComma()
        {
            SkipWhitespace();
            if (Peek() != ',')
                return false;
            _position++;
            return true;
        }

        private void Expect(char expected)
        {
            SkipWhitespace();
            if (Peek() != expected)
                Fail($"Expected '{expected}' in WKT.");
            _position++;
        }

        private void SkipWhitespace()
        {
            while (_position < _text.Length && IsWhitespace(_text[_position]))
                _position++;
        }

        private char Peek() => _position < _text.Length ? _text[_position] : '\0';

        private static bool IsWhitespace(char value)
            => value is ' ' or '\t' or '\r' or '\n' or '\f' or '\v';

        private static bool IsAsciiLetter(char value)
            => value is >= 'A' and <= 'Z' or >= 'a' and <= 'z';

        private void Fail(string message) => throw Error(message);

        private FormatException Error(string message)
            => new($"{message} Position {_position}.");

        private readonly record struct Coordinate(double X, double Y);
    }
}
