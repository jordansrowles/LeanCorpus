using Rowles.LeanCorpus.DevOps.Infrastructure;
using Rowles.LeanCorpus.DevOps.Repository;
using Rowles.LeanCorpus.DevOps.Terminal;
using Rowles.LeanCorpus.DevOps.Testing;

namespace Rowles.LeanCorpus.DevOps.Tests.Testing;

public sealed class TestPreparationServiceTests
{
    [Fact]
    public async Task CiResolvesAndRunsPreparedMtpApphostWithoutRestoreOrBuild()
    {
        using var checkout = TemporaryCheckout.Create();
        var target = MtpTarget(checkout.ProjectPath);
        var targetPath = Path.Combine(checkout.Root, "artifacts", "bin", "Fake.Tests", "release", "net10.0", "Fake.Tests.dll");
        var executablePath = Path.Combine(Path.GetDirectoryName(targetPath)!, OperatingSystem.IsWindows() ? "Fake.Tests.exe" : "Fake.Tests");
        Directory.CreateDirectory(Path.GetDirectoryName(executablePath)!);
        await File.WriteAllTextAsync(executablePath, "prepared", TestContext.Current.CancellationToken);
        var process = new RecordingProcessRunner(targetPath);

        var result = await new TestPreparationService(process).PrepareAsync(new TestPreparationRequest
        {
            Checkout = checkout.Identity,
            Targets = [target],
            Ci = true
        }, Path.Combine(checkout.Root, "artifacts", "test", "runs", "prepare"), TestContext.Current.CancellationToken);

        Assert.Equal(OutcomeKind.Success, result.Outcome);
        Assert.Equal(executablePath, Assert.Single(result.Targets).ExecutablePath);
        Assert.Equal("resolve MTP target path", Assert.Single(result.Timings).Operation);
        Assert.Single(process.Requests);
        Assert.Contains("-getProperty:TargetPath", process.Requests[0].Arguments);
    }

    [Fact]
    public async Task ReusesPreparedMtpExecutableForTargetsWithTheSameBuildIdentity()
    {
        using var checkout = TemporaryCheckout.Create();
        var target = MtpTarget(checkout.ProjectPath);
        var filteredTarget = target with { Key = "core/net10.0-search", Filter = "Area=Search" };
        var targetPath = Path.Combine(checkout.Root, "artifacts", "bin", "Fake.Tests", "release", "net10.0", "Fake.Tests.dll");
        var executablePath = Path.Combine(Path.GetDirectoryName(targetPath)!, OperatingSystem.IsWindows() ? "Fake.Tests.exe" : "Fake.Tests");
        Directory.CreateDirectory(Path.GetDirectoryName(executablePath)!);
        await File.WriteAllTextAsync(executablePath, "prepared", TestContext.Current.CancellationToken);
        var process = new RecordingProcessRunner(targetPath);

        var result = await new TestPreparationService(process).PrepareAsync(new TestPreparationRequest
        {
            Checkout = checkout.Identity,
            Targets = [target, filteredTarget],
            Ci = true
        }, Path.Combine(checkout.Root, "artifacts", "test", "runs", "prepare"), TestContext.Current.CancellationToken);

        Assert.Equal(OutcomeKind.Success, result.Outcome);
        Assert.Equal(2, result.Targets.Count);
        Assert.All(result.Targets, prepared => Assert.Equal(executablePath, prepared.ExecutablePath));
        Assert.Single(process.Requests);
    }

    private static TestTarget MtpTarget(string project)
        => new("core/net10.0", "Core", "core", "Mtp", project, "net10.0", "Release", string.Empty,
            string.Empty, [], true, ["Trx"], []);

    private sealed class RecordingProcessRunner(string targetPath) : IProcessRunner
    {
        public List<ProcessRequest> Requests { get; } = [];

        public async Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            Directory.CreateDirectory(Path.GetDirectoryName(request.StandardOutputLogPath)!);
            Directory.CreateDirectory(Path.GetDirectoryName(request.StandardErrorLogPath)!);
            await File.WriteAllTextAsync(request.StandardOutputLogPath,
                request.Arguments.Contains("-getProperty:TargetPath", StringComparer.Ordinal) ? targetPath : string.Empty,
                cancellationToken);
            await File.WriteAllTextAsync(request.StandardErrorLogPath, string.Empty, cancellationToken);
            return new ProcessResult(OutcomeKind.Success, 0, [], [], 0, 0, TimeSpan.Zero,
                request.StandardOutputLogPath, request.StandardErrorLogPath, null);
        }
    }

    private sealed class TemporaryCheckout : IDisposable
    {
        private TemporaryCheckout(string root)
        {
            Root = root;
            ProjectPath = Path.Combine("src", "devops", "Fake.Tests", "Fake.Tests.csproj");
            Identity = new CheckoutIdentity(root, Path.Combine(root, ".git"), CheckoutGitMarkerKind.Directory);
        }

        public string Root { get; }
        public string ProjectPath { get; }
        public CheckoutIdentity Identity { get; }

        public static TemporaryCheckout Create()
        {
            var root = Path.Combine(Path.GetTempPath(), "DevOps test preparation "+ Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(root, ".git"));
            var project = Path.Combine(root, "src", "devops", "Fake.Tests", "Fake.Tests.csproj");
            Directory.CreateDirectory(Path.GetDirectoryName(project)!);
            File.WriteAllText(project, "<Project />");
            return new TemporaryCheckout(root);
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
