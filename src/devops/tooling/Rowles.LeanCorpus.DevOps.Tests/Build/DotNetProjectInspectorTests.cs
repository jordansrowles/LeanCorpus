using Rowles.LeanCorpus.DevOps.Build;
using Rowles.LeanCorpus.DevOps.Infrastructure;
using Rowles.LeanCorpus.DevOps.Repository;
using Rowles.LeanCorpus.DevOps.Terminal;

namespace Rowles.LeanCorpus.DevOps.Tests.Build;

public sealed class DotNetProjectInspectorTests
{
    [Fact]
    public async Task ResolvesSelectedSdkAndInspectsSlnxAndProjectMetadataAgainstDotNetCli()
    {
        var checkout = RepositoryPaths.Resolve(explicitRepositoryPath: null, Directory.GetCurrentDirectory(), AppContext.BaseDirectory);
        var processRunner = new ProcessRunner();
        var runDirectory = Path.Combine(Path.GetTempPath(), "DevOps MSBuild probe " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(runDirectory);
        try
        {
            var sdk = await new DotNetSdkResolver(processRunner).ResolveAsync(checkout, runDirectory, TestContext.Current.CancellationToken);
            Assert.Equal(OutcomeKind.Success, sdk.Outcome);
            Assert.NotNull(sdk.SelectedVersion);
            Assert.NotNull(sdk.SdkDirectory);
            Assert.True(File.Exists(Path.Combine(sdk.SdkDirectory, "MSBuild.dll")));

            var inspector = new DotNetProjectInspector(processRunner);
            var solution = await inspector.InspectSolutionAsync(checkout, runDirectory, TestContext.Current.CancellationToken);
            Assert.Equal(OutcomeKind.Success, solution.Outcome);
            Assert.NotEmpty(solution.Projects);
            Assert.True(solution.MatchesDotNetCli, solution.Failure);
            Assert.Contains(solution.Projects, project =>
                Path.GetFullPath(project.ProjectPath) == RepositoryProjectCatalog.GetProjectPath(checkout.RepositoryRoot, RepositoryProjectRole.DevOpsHost));

            var project = await inspector.InspectProjectAsync(
                checkout,
                "src/devops/tooling/Rowles.LeanCorpus.DevOps/Rowles.LeanCorpus.DevOps.csproj",
                "Release",
                "net10.0",
                sdk.SdkDirectory,
                runDirectory,
                TestContext.Current.CancellationToken);

            Assert.Equal(OutcomeKind.Success, project.Outcome);
            Assert.Equal("Release", project.Configuration);
            Assert.Equal("net10.0", project.TargetFramework);
            Assert.False(string.IsNullOrWhiteSpace(project.TargetPath));
            Assert.True(project.ApiMatchesCli || !project.UsedMsBuildApi, project.Failure);
        }
        finally
        {
            if (Directory.Exists(runDirectory))
            {
                Directory.Delete(runDirectory, recursive: true);
            }
        }
    }
}
