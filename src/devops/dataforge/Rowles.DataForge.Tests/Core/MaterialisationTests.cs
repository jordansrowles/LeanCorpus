using Rowles.DataForge;

namespace Rowles.DataForge.Tests.Core;

public sealed class MaterialisationTests
{
    [Fact]
    public void Materialisation_streams_records_and_writes_manifest_and_ndjson()
    {
        var directory = Path.Combine(Path.GetTempPath(), "dataforge-materialise-" + Guid.NewGuid().ToString("N"));
        try
        {
            var result = DataForgeMaterialiser.Materialise(
                new GoldenProfileTests.GoldenProfile(),
                new DataForgeGenerationOptions(42, 3),
                directory);

            Assert.True(File.Exists(Path.Combine(directory, "manifest.json")));
            Assert.True(File.Exists(Path.Combine(directory, "records.ndjson")));
            Assert.Equal(326, new FileInfo(Path.Combine(directory, "records.ndjson")).Length);
            Assert.Equal(result.Manifest.ContentSha256, result.Manifest.ArtefactSha256);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }
}
