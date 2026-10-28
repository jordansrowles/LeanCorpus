using System.Text.Json;
using System.Xml.Linq;
using Rowles.LeanCorpus.DevOps.Repository;

namespace Rowles.LeanCorpus.DevOps.Tests.Repository;

public sealed class SolutionInventoryValidatorTests
{
    [Fact]
    public async Task AcceptsCompleteMasterAndFilterInventories()
    {
        using var checkout = TemporaryCheckout.Create();
        checkout.WriteProject("core/Rowles.LeanCorpus/Rowles.LeanCorpus.csproj");
        checkout.WriteMaster("core/Rowles.LeanCorpus/Rowles.LeanCorpus.csproj");
        checkout.WriteFilter("core.slnf", "../Rowles.LeanCorpus.slnx", "core/Rowles.LeanCorpus/Rowles.LeanCorpus.csproj");

        var result = await new SolutionInventoryValidator().ValidateAsync(checkout.Identity,
            ["src/core/Rowles.LeanCorpus/Rowles.LeanCorpus.csproj"], TestContext.Current.CancellationToken);

        Assert.True(result.IsValid, string.Join(Environment.NewLine, result.Diagnostics));
        Assert.Equal(1, result.TrackedProjectCount);
        Assert.Equal(1, result.MasterProjectCount);
        Assert.Equal(1, result.FilterCount);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public async Task ReportsTrackedProjectMissingFromMaster()
    {
        using var checkout = TemporaryCheckout.Create();
        checkout.WriteProject("core/Missing/Missing.csproj");
        checkout.WriteMaster();

        var result = await new SolutionInventoryValidator().ValidateAsync(checkout.Identity,
            ["src/core/Missing/Missing.csproj"], TestContext.Current.CancellationToken);

        Assert.False(result.IsValid);
        Assert.Contains(result.Diagnostics, message => message.Contains("missing from the master", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ExcludesHistoricalFixtureGeneratorFromTheProductSolutionInventory()
    {
        using var checkout = TemporaryCheckout.Create();
        checkout.WriteProject("devops/tests/fixtures/historical/versions/3.1.1/Generator.csproj");
        checkout.WriteMaster();

        var result = await new SolutionInventoryValidator().ValidateAsync(checkout.Identity,
            ["src/devops/tests/fixtures/historical/versions/3.1.1/Generator.csproj"], TestContext.Current.CancellationToken);

        Assert.True(result.IsValid, string.Join(Environment.NewLine, result.Diagnostics));
        Assert.Equal(0, result.TrackedProjectCount);
        Assert.Equal(0, result.MasterProjectCount);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public async Task ReportsDuplicateMasterProjectEntries()
    {
        using var checkout = TemporaryCheckout.Create();
        checkout.WriteProject("core/One/One.csproj");
        checkout.WriteMaster("core/One/One.csproj", "core/One/One.csproj");

        var result = await new SolutionInventoryValidator().ValidateAsync(checkout.Identity,
            ["src/core/One/One.csproj"], TestContext.Current.CancellationToken);

        Assert.False(result.IsValid);
        Assert.Contains(result.Diagnostics, message => message.Contains("duplicate", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ReportsMasterProjectNotPresentInTrackedInventory()
    {
        using var checkout = TemporaryCheckout.Create();
        checkout.WriteProject("core/Removed/Removed.csproj");
        checkout.WriteMaster("core/Removed/Removed.csproj");

        var result = await new SolutionInventoryValidator().ValidateAsync(checkout.Identity, [], TestContext.Current.CancellationToken);

        Assert.False(result.IsValid);
        Assert.Contains(result.Diagnostics, message => message.Contains("not present in the tracked", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ReportsMasterProjectCaseMismatch()
    {
        using var checkout = TemporaryCheckout.Create();
        checkout.WriteProject("core/Foo/Foo.csproj");
        checkout.WriteMaster("core/foo/Foo.csproj");

        var result = await new SolutionInventoryValidator().ValidateAsync(checkout.Identity,
            ["src/core/Foo/Foo.csproj"], TestContext.Current.CancellationToken);

        Assert.False(result.IsValid);
        Assert.Contains(result.Diagnostics, message => message.Contains("case mismatch", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ReportsMasterProjectPathEscapingRepository()
    {
        using var checkout = TemporaryCheckout.Create();
        checkout.WriteMaster("../../../outside.csproj");

        var result = await new SolutionInventoryValidator().ValidateAsync(checkout.Identity, [], TestContext.Current.CancellationToken);

        Assert.False(result.IsValid);
        Assert.Contains(result.Diagnostics, message => message.Contains("outside the repository", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ReportsFilterThatReferencesDifferentMasterSolution()
    {
        using var checkout = TemporaryCheckout.Create();
        checkout.WriteMaster();
        checkout.WriteFilter("core.slnf", "../Other.slnx");

        var result = await new SolutionInventoryValidator().ValidateAsync(checkout.Identity, [], TestContext.Current.CancellationToken);

        Assert.False(result.IsValid);
        Assert.Contains(result.Diagnostics, message => message.Contains("canonical master solution", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ReportsFilterMemberMissingFromMaster()
    {
        using var checkout = TemporaryCheckout.Create();
        checkout.WriteProject("core/One/One.csproj");
        checkout.WriteProject("core/Two/Two.csproj");
        checkout.WriteMaster("core/One/One.csproj");
        checkout.WriteFilter("core.slnf", "../Rowles.LeanCorpus.slnx", "core/Two/Two.csproj");

        var result = await new SolutionInventoryValidator().ValidateAsync(checkout.Identity,
            ["src/core/One/One.csproj", "src/core/Two/Two.csproj"], TestContext.Current.CancellationToken);

        Assert.False(result.IsValid);
        Assert.Contains(result.Diagnostics, message => message.Contains("not in the master solution", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ReportsInvalidFilterDocument()
    {
        using var checkout = TemporaryCheckout.Create();
        checkout.WriteMaster();
        Directory.CreateDirectory(Path.Combine(checkout.Root, "src", "solutions"));
        await File.WriteAllTextAsync(Path.Combine(checkout.Root, "src", "solutions", "broken.slnf"), "{\"solution\":\"bad\"}",
            TestContext.Current.CancellationToken);

        var result = await new SolutionInventoryValidator().ValidateAsync(checkout.Identity, [], TestContext.Current.CancellationToken);

        Assert.False(result.IsValid);
        Assert.Contains(result.Diagnostics, message => message.Contains("broken.slnf", StringComparison.Ordinal));
    }

    private sealed class TemporaryCheckout : IDisposable
    {
        private TemporaryCheckout(string root)
        {
            Root = root;
            Identity = new CheckoutIdentity(root, null, null);
            Directory.CreateDirectory(Path.Combine(root, "src"));
            File.WriteAllText(Path.Combine(root, "global.json"), "{}");
            File.WriteAllText(Path.Combine(root, "Directory.Build.props"), "<Project />");
        }

        public string Root { get; }

        public CheckoutIdentity Identity { get; }

        public static TemporaryCheckout Create()
            => new(Path.Combine(Path.GetTempPath(), "LeanCorpus solution inventory " + Guid.NewGuid().ToString("N")));

        public void WriteProject(string relativePath)
        {
            var path = Path.Combine(Root, "src", relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "<Project />");
        }

        public void WriteMaster(params string[] projectPaths)
        {
            var solution = new XElement("Solution", projectPaths.Select(path => new XElement("Project", new XAttribute("Path", path))));
            File.WriteAllText(Path.Combine(Root, "src", "Rowles.LeanCorpus.slnx"), solution.ToString(SaveOptions.DisableFormatting));
        }

        public void WriteFilter(string name, string masterPath, params string[] projectPaths)
        {
            var directory = Path.Combine(Root, "src", "solutions");
            Directory.CreateDirectory(directory);
            var filter = new { solution = new { path = masterPath, projects = projectPaths } };
            File.WriteAllText(Path.Combine(directory, name), JsonSerializer.Serialize(filter));
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}
