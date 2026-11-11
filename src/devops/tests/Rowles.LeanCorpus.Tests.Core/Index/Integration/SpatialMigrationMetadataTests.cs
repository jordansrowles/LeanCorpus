namespace Rowles.LeanCorpus.Tests.Core.Index.Migration;

[Category(TestCategory.Integration)]
[Area(TestArea.Index)]
public sealed class SpatialMigrationMetadataTests : IDisposable
{
    private readonly string _path = RepositoryTemporaryStorage.NewPath("tests", "spatial-migration-");
    public void Dispose() => TestDirectoryFixture.TryDeleteDirectory(_path);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MigrationRetainsDeclaredSpatialFieldKinds(bool compound)
    {
        Release311Fixture.Extract(compound, _path);
        // Annotate the fixture's existing Geo point data with its known kind. The
        // old codec bodies still require migration; no data or version is rewritten here.
        foreach (string file in Directory.GetFiles(_path, "*.seg"))
        {
            var info = SegmentInfo.ReadFrom(file);
            info.SpatialFields.Add(new SpatialFieldInfo { FieldName = "location", Kind = SpatialFieldKind.GeoPoint });
            info.WriteTo(file);
        }
        using var directory = new MMapDirectory(_path);
        var plan = IndexCodecMigrator.Plan(directory);
        Assert.True(plan.CanExecute);
        Assert.NotEmpty(plan.Actions);
        var result = IndexCodecMigrator.Migrate(directory, new IndexCodecMigrationOptions { DryRun = false });
        Assert.True(result.Succeeded, string.Join("; ", result.Issues));
        foreach (string file in Directory.GetFiles(_path, "*.seg"))
        {
            var field = Assert.Single(SegmentInfo.ReadFrom(file).SpatialFields);
            Assert.Equal("location", field.FieldName);
            Assert.Equal(SpatialFieldKind.GeoPoint, field.Kind);
        }
    }
}
