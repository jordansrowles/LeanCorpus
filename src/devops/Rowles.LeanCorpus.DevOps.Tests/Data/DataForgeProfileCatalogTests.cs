using Rowles.LeanCorpus.DevOps.Data;

namespace Rowles.LeanCorpus.DevOps.Tests.Data;

public sealed class DataForgeProfileCatalogTests
{
    [Fact]
    public void ReadsProfileDescriptorsAfterDotnetRunOutput()
    {
        using var directory = TemporaryDirectory.Create("DataForge profile catalogue ");
        File.WriteAllText(Path.Combine(directory.Path, "process.stdout.log"), "Build succeeded.\n" + """
            [{"profileId":"leancorpus-search","profileVersion":1},{"profileId":"rowles-text-multilingual","profileVersion":1}]
            """);

        var result = DataForgeQualificationEvidence.ReadProfileCatalog(directory.Path);

        Assert.Null(result.Failure);
        Assert.Equal(new[]
        {
            new DataForgeAvailableProfile("leancorpus-search", 1),
            new DataForgeAvailableProfile("rowles-text-multilingual", 1)
        }, result.Profiles);
    }

    [Fact]
    public void RejectsDuplicateAndInvalidProfileDescriptors()
    {
        using var directory = TemporaryDirectory.Create("DataForge invalid profile catalogue ");
        File.WriteAllText(Path.Combine(directory.Path, "process.stdout.log"), """
            [{"profileId":"leancorpus-search","profileVersion":1},{"profileId":"leancorpus-search","profileVersion":1}]
            """);

        var result = DataForgeQualificationEvidence.ReadProfileCatalog(directory.Path);

        Assert.Empty(result.Profiles);
        Assert.Contains("duplicate profile ID", result.Failure, StringComparison.Ordinal);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private TemporaryDirectory(string path) => Path = path;

        public string Path { get; }

        public static TemporaryDirectory Create(string prefix)
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), prefix + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return new TemporaryDirectory(path);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
    }
}
