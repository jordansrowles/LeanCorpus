using Rowles.LeanCorpus.Search.Geo;
using Rowles.LeanCorpus.Search.Ranking;
using Rowles.LeanCorpus.Search.Scoring;
using Rowles.LeanCorpus.Search.Searcher;
using Rowles.LeanCorpus.Search.XY;
using Xunit;

namespace Rowles.LeanCorpus.Tests.Core.Search.Sorting;

[Category(TestCategory.Unit)]
[Area(TestArea.Search)]
public sealed class SpatialDistanceSortFactoryTests
{
    [Fact]
    public void GeoDistanceFactoryRetainsTypedOriginAndDirection()
    {
        var origin = new GeoPoint(51.5074, -0.1278);
        SortField sort = SortField.GeoDistance("location", origin, descending: true);

        Assert.Equal(SortFieldType.GeoDistance, sort.Type);
        Assert.Equal("location", sort.FieldName);
        Assert.True(sort.Descending);
        Assert.Equal(origin, sort.GeoOrigin);
        Assert.Null(sort.XYOrigin);
    }

    [Fact]
    public void XYDistanceFactoryRetainsTypedOriginAndDirection()
    {
        var origin = new XYPoint(-12.5f, 8.25f);
        SortField sort = SortField.XYDistance("position", origin);

        Assert.Equal(SortFieldType.XYDistance, sort.Type);
        Assert.Equal("position", sort.FieldName);
        Assert.False(sort.Descending);
        Assert.Equal(origin, sort.XYOrigin);
        Assert.Null(sort.GeoOrigin);
    }

    [Theory]
    [InlineData(SortFieldType.GeoDistance)]
    [InlineData(SortFieldType.XYDistance)]
    public void UntypedConstructorRejectsSpatialSorts(SortFieldType type)
        => Assert.Throws<ArgumentException>(() => new SortField(type, "position"));

    [Theory]
    [InlineData(SortFieldType.GeoDistance)]
    [InlineData(SortFieldType.XYDistance)]
    public void IndexSortRejectsSpatialDistanceSorts(SortFieldType type)
    {
        SortField field = type == SortFieldType.GeoDistance
            ? SortField.GeoDistance("location", new GeoPoint(0, 0))
            : SortField.XYDistance("position", new XYPoint(0, 0));

        Assert.Throws<ArgumentException>(() => new Rowles.LeanCorpus.Index.Indexer.IndexSort(field));
    }

    [Theory]
    [InlineData(SortFieldType.GeoDistance)]
    [InlineData(SortFieldType.XYDistance)]
    public void CursorCodecRoundTripsSpatialNumericSortValues(SortFieldType type)
    {
        var codec = new SearchCursorCodec(4096, null);
        var cursor = new SearchCursorData(
            "session",
            "index",
            3,
            "query",
            "sort-with-origin",
            "ranking",
            new ScoreDoc(12, 1),
            [CursorSortValue.FromNumeric(type, 123.75)]);

        SearchCursorData decoded = codec.Decode(codec.Encode(cursor));

        Assert.Single(decoded.SortValues);
        Assert.Equal(type, decoded.SortValues[0].Type);
        Assert.Equal(123.75, decoded.SortValues[0].Numeric);
    }

    [Fact]
    public void SortIdentityIncludesSpatialOriginUsingInvariantFormatting()
    {
        var originalCulture = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            SortField sort = SortField.GeoDistance("location", new GeoPoint(51.5074, -0.1278));
            System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo("fr-FR");
            string frenchIdentity = SearchSessionManager.CreateSortIdentity([sort]);

            System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo("en-GB");
            string britishIdentity = SearchSessionManager.CreateSortIdentity([sort]);
            string differentOriginIdentity = SearchSessionManager.CreateSortIdentity(
                [SortField.GeoDistance("location", new GeoPoint(48.8566, 2.3522))]);

            Assert.Equal(frenchIdentity, britishIdentity);
            Assert.NotEqual(frenchIdentity, differentOriginIdentity);
            Assert.Equal(
                RankingProfile.FingerprintOf("Numeric:price:False:Min"),
                SearchSessionManager.CreateSortIdentity([SortField.Numeric("price")]));
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = originalCulture;
        }
    }
}
