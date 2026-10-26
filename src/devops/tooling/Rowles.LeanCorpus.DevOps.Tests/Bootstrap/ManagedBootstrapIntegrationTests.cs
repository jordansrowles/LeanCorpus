using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Rowles.LeanCorpus.DevOps.Repository;

namespace Rowles.LeanCorpus.DevOps.Tests.Bootstrap;

public sealed class ManagedBootstrapIntegrationTests : IClassFixture<ManagedBootstrapTestFixture>
{
    private readonly ManagedBootstrapTestFixture _fixture;

    public ManagedBootstrapIntegrationTests(ManagedBootstrapTestFixture fixture) => _fixture = fixture;

    [Fact]
    public void ScriptRunStringContainsFailureContextWithoutExposingSecretsOrUnboundedOutput()
    {
        var fakeBin = Path.Combine(Path.GetTempPath(), "bootstrap diagnostic fake bin " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(fakeBin);
        File.WriteAllText(Path.Combine(fakeBin, "dotnet"), "fake executable");
        try
        {
            var run = new ManagedBootstrapTestFixture.ScriptRun(
                1,
                new string('o', 32 * 1024),
                "Exception: simulated PowerShell failure\nAuthorization: Bearer diagnostic-secret-value\n")
            {
                Command = "pwsh -File /tmp/devops.ps1 bootstrap",
                WorkingDirectory = "/tmp/checkout with spaces",
                FakeBinPath = fakeBin,
                CommandResolution = "git => <not found>\ndotnet => fake apphost",
                EnvironmentSummary = "PATH=/fixture fake bin"
            };

            var diagnostic = run.ToString();

            Assert.Contains("Command: pwsh -File /tmp/devops.ps1 bootstrap", diagnostic, StringComparison.Ordinal);
            Assert.Contains("Exit code: 1", diagnostic, StringComparison.Ordinal);
            Assert.Contains("OS/RID:", diagnostic, StringComparison.Ordinal);
            Assert.Contains("Working directory: /tmp/checkout with spaces", diagnostic, StringComparison.Ordinal);
            Assert.Contains("Fake-bin inventory:", diagnostic, StringComparison.Ordinal);
            Assert.Contains("dotnet; attributes=", diagnostic, StringComparison.Ordinal);
            Assert.Contains("Get-Command git/dotnet resolution:", diagnostic, StringComparison.Ordinal);
            Assert.Contains("git => <not found>", diagnostic, StringComparison.Ordinal);
            Assert.Contains("PATH=/fixture fake bin", diagnostic, StringComparison.Ordinal);
            Assert.Contains("simulated PowerShell failure", diagnostic, StringComparison.Ordinal);
            Assert.Contains("<redacted>", diagnostic, StringComparison.Ordinal);
            Assert.Contains("output truncated", diagnostic, StringComparison.Ordinal);
            Assert.DoesNotContain("diagnostic-secret-value", diagnostic, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(fakeBin, recursive: true);
        }
    }

    [Fact]
    public void CombinedOutputAssertionAcceptsPowerShellContinuationLines()
    {
        var run = new ManagedBootstrapTestFixture.ScriptRun(
            1,
            string.Empty,
            "\u001b[31mException: '/tmp/invalid checkout' is not a LeanCorpus\n\u001b[0m    | \u001b[31mcheckout. Expected repository markers.\u001b[0m");

        AssertProcessCombinedOutputContains(run, "not a LeanCorpus checkout", StringComparison.Ordinal);
    }

    [Fact]
    public async Task BootstrapStagesProbesAndPromotesAValidSingleFileHost()
    {
        using var checkout = await TemporaryCheckout.CreateAsync(_fixture);
        var leases = Path.Combine(checkout.Root, "artifacts", "devops", "leases");
        Directory.CreateDirectory(leases);
        var staleMarker = JsonSerializer.Serialize(new
        {
            processId = int.MaxValue,
            processStartedUtc = DateTimeOffset.Parse("2000-01-01T00:00:00Z")
        });
        File.WriteAllText(Path.Combine(leases, "promotion.lock"), staleMarker);
        File.WriteAllText(Path.Combine(leases, "reader-stale.json"), staleMarker);

        var restoreCapture = Path.Combine(checkout.Root, "restore-calls.txt");
        var buildCapture = Path.Combine(checkout.Root, "build-calls.txt");
        var publishCapture = Path.Combine(checkout.Root, "publish-calls.txt");
        var run = await _fixture.RunScriptAsync(checkout, ["bootstrap"], new Dictionary<string, string?>
        {
            ["BOOTSTRAP_RESTORE_CAPTURE_PATH"] = restoreCapture,
            ["BOOTSTRAP_BUILD_CAPTURE_PATH"] = buildCapture,
            ["BOOTSTRAP_PUBLISH_CAPTURE_PATH"] = publishCapture
        });

        AssertProcessExitCode(0, run);
        AssertProcessStandardOutputContains(run, "Restoring the managed DevOps test project graph in locked mode", StringComparison.Ordinal);
        AssertProcessStandardOutputContains(run, "Staged managed host --help probe", StringComparison.Ordinal);
        AssertProcessStandardOutputContains(run, "Staged managed host --version probe", StringComparison.Ordinal);
        AssertProcessStandardOutputContains(run, "Staged managed build smoke", StringComparison.Ordinal);
        AssertProcessStandardOutputContains(run, "Promoted managed host --help probe", StringComparison.Ordinal);
        AssertProcessStandardOutputContains(run, "managed DevOps bootstrap completed", StringComparison.Ordinal);

        var cancellationToken = TestContext.Current.CancellationToken;
        var restoreCalls = await File.ReadAllLinesAsync(restoreCapture, cancellationToken);
        Assert.Equal(2, restoreCalls.Length);
        Assert.All(restoreCalls, command =>
        {
            Assert.Contains("--locked-mode", command, StringComparison.Ordinal);
            Assert.Contains("--disable-build-servers", command, StringComparison.Ordinal);
            Assert.Contains("-m:1", command, StringComparison.Ordinal);
        });
        Assert.Contains(restoreCalls, command => command.Contains("Rowles.LeanCorpus.DevOps.Tests.csproj", StringComparison.Ordinal));
        Assert.Contains(restoreCalls, command => command.Contains("Rowles.LeanCorpus.DevOps.csproj", StringComparison.Ordinal));
        Assert.Contains("Rowles.LeanCorpus.DevOps.Tests.csproj", await File.ReadAllTextAsync(buildCapture, cancellationToken), StringComparison.Ordinal);
        Assert.Contains("-p:IncludeNativeLibrariesForSelfExtract=true", await File.ReadAllTextAsync(publishCapture, cancellationToken), StringComparison.Ordinal);

        Assert.True(File.Exists(checkout.ExecutablePath));
        Assert.True(File.Exists(checkout.ManifestPath));
        Assert.False(File.Exists(checkout.ExecutablePath + ".deps.json"));
        Assert.Null(ManagedHostFingerprint.Validate(checkout.Root, checkout.ExecutablePath));
        using (var manifestDocument = JsonDocument.Parse(await File.ReadAllBytesAsync(checkout.ManifestPath, cancellationToken)))
        {
            var sourceFiles = manifestDocument.RootElement.GetProperty("sourceFiles").EnumerateArray().ToArray();
            var expectedPaths = GetSourceFiles(checkout.Root);
            Assert.Equal(expectedPaths, sourceFiles.Select(file => file.GetProperty("path").GetString()));
            Assert.Contains("src/devops/tooling/Rowles.LeanCorpus.DevOps/Packaging/Assets/Consumer.csproj.template", expectedPaths, StringComparer.Ordinal);
            Assert.Contains("src/devops/tooling/Rowles.LeanCorpus.DevOps/Packaging/Assets/Program.cs.template", expectedPaths, StringComparer.Ordinal);
            Assert.Contains("src/devops/tooling/Rowles.LeanCorpus.DevOps/Packaging/Assets/ConsumerSources/core.cs.txt", expectedPaths, StringComparer.Ordinal);
            Assert.All(sourceFiles, file =>
            {
                var path = file.GetProperty("path").GetString();
                var hash = file.GetProperty("sha256").GetString();
                Assert.DoesNotContain("\\", path, StringComparison.Ordinal);
                Assert.Matches("^[0-9a-f]{64}$", hash);
            });
            var canonical = string.Concat(sourceFiles.Select(file => $"{file.GetProperty("path").GetString()}\0{file.GetProperty("sha256").GetString()}\n"));
            var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
            Assert.Equal(manifestDocument.RootElement.GetProperty("sourceFingerprint").GetString(), fingerprint);
        }
        Assert.Empty(Directory.EnumerateFiles(leases, "reader-*.json"));
        Assert.False(File.Exists(Path.Combine(leases, "promotion.lock")));
    }

    [Fact]
    public async Task BootstrapAcceptsAScriptPathThroughADirectoryLink()
    {
        using var checkout = await TemporaryCheckout.CreateAsync(_fixture);
        var linkedRoot = Path.Combine(Path.GetTempPath(), "LeanCorpus bootstrap checkout alias " + Guid.NewGuid().ToString("N"));
        CreateDirectoryLink(linkedRoot, checkout.Root);
        try
        {
            var linkedScript = Path.Combine(linkedRoot, "devops.ps1");
            var run = await _fixture.RunScriptAsync(checkout, ["bootstrap"], scriptPathOverride: linkedScript);

            AssertProcessExitCode(0, run);
            Assert.True(File.Exists(checkout.ExecutablePath));
            Assert.Null(ManagedHostFingerprint.Validate(checkout.Root, checkout.ExecutablePath));
        }
        finally
        {
            if (Directory.Exists(linkedRoot))
            {
                Directory.Delete(linkedRoot);
            }
        }
    }

    [Fact]
    public async Task BootstrapUsesGitHubWorkspaceForGitSafetyWhenTheScriptPathIsPhysical()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        using var checkout = await TemporaryCheckout.CreateAsync(_fixture);
        var linkedRoot = Path.Combine(Path.GetTempPath(), "LeanCorpus bootstrap safe checkout alias " + Guid.NewGuid().ToString("N"));
        CreateDirectoryLink(linkedRoot, checkout.Root);
        checkout.InstallGitWrapperForSafeDirectory();
        try
        {
            var run = await _fixture.RunScriptAsync(checkout, ["bootstrap"], new Dictionary<string, string?>
            {
                ["BOOTSTRAP_GIT_SAFE_DIRECTORY"] = linkedRoot,
                ["BOOTSTRAP_GIT_CANONICAL_SAFE_DIRECTORY"] = checkout.Root,
                ["GITHUB_WORKSPACE"] = linkedRoot
            }, pathOverride: checkout.FakeBin, scriptPathOverride: checkout.ScriptPath);

            AssertProcessExitCode(0, run);
            Assert.True(File.Exists(checkout.ExecutablePath));
            Assert.Null(ManagedHostFingerprint.Validate(checkout.Root, checkout.ExecutablePath));
        }
        finally
        {
            if (Directory.Exists(linkedRoot))
            {
                Directory.Delete(linkedRoot);
            }
        }
    }

    [Theory]
    [InlineData("tool-restore")]
    [InlineData("restore")]
    [InlineData("build")]
    [InlineData("test")]
    [InlineData("publish")]
    [InlineData("staged-help")]
    [InlineData("promoted-help")]
    public async Task FailedBootstrapStagesPreserveThePreviousHostAndManifest(string failureStage)
    {
        using var checkout = await TemporaryCheckout.CreateAsync(_fixture);
        var previous = checkout.WritePreviousInstallation();

        var run = await _fixture.RunScriptAsync(checkout, ["bootstrap"], new Dictionary<string, string?>
        {
            ["BOOTSTRAP_FAKE_FAIL_STAGE"] = failureStage
        });

        AssertProcessFailed(run);
        var cancellationToken = TestContext.Current.CancellationToken;
        Assert.Equal("previous apphost sentinel", await File.ReadAllTextAsync(previous.HostPath, cancellationToken));
        Assert.Equal("previous manifest sentinel", await File.ReadAllTextAsync(previous.ManifestPath, cancellationToken));
    }

    [Fact]
    public async Task UnexpectedPublishSidecarAndPromotionFailurePreserveThePriorInstallation()
    {
        using (var checkout = await TemporaryCheckout.CreateAsync(_fixture))
        {
            var previous = checkout.WritePreviousInstallation();
            var sidecarRun = await _fixture.RunScriptAsync(checkout, ["bootstrap"], new Dictionary<string, string?>
            {
                ["BOOTSTRAP_FAKE_SIDECAR"] = "1"
            });

            AssertProcessFailed(sidecarRun);
            AssertProcessCombinedOutputContains(sidecarRun, "unexpected sidecars", StringComparison.OrdinalIgnoreCase);
            var cancellationToken = TestContext.Current.CancellationToken;
            Assert.Equal("previous apphost sentinel", await File.ReadAllTextAsync(previous.HostPath, cancellationToken));
            Assert.Equal("previous manifest sentinel", await File.ReadAllTextAsync(previous.ManifestPath, cancellationToken));
        }

        using (var checkout = await TemporaryCheckout.CreateAsync(_fixture))
        {
            Directory.CreateDirectory(checkout.ExecutablePath);
            Directory.CreateDirectory(Path.GetDirectoryName(checkout.ManifestPath)!);
            await File.WriteAllTextAsync(checkout.ManifestPath, "previous manifest sentinel", TestContext.Current.CancellationToken);
            var run = await _fixture.RunScriptAsync(checkout, ["bootstrap"]);

            AssertProcessFailed(run);
            AssertProcessCombinedOutputContains(run, "devops", StringComparison.OrdinalIgnoreCase);
            Assert.True(Directory.Exists(checkout.ExecutablePath));
            Assert.Equal("previous manifest sentinel", await File.ReadAllTextAsync(checkout.ManifestPath, TestContext.Current.CancellationToken));
        }
    }

    [Fact]
    public async Task BootstrapAcceptsAWorktreeGitFile()
    {
        using var checkout = await TemporaryCheckout.CreateWorktreeAsync(_fixture);
        Assert.True(File.Exists(Path.Combine(checkout.Root, ".git")));

        var run = await _fixture.RunScriptAsync(checkout, ["bootstrap"]);

        AssertProcessExitCode(0, run);
        AssertProcessStandardOutputContains(run, "managed DevOps bootstrap completed", StringComparison.Ordinal);
        Assert.Null(ManagedHostFingerprint.Validate(checkout.Root, checkout.ExecutablePath));
    }

    [Fact]
    public async Task PromotionWaitsForALiveReaderMarker()
    {
        using var checkout = await TemporaryCheckout.CreateAsync(_fixture);
        var leaseDirectory = Path.Combine(checkout.Root, "artifacts", "devops", "leases");
        Directory.CreateDirectory(leaseDirectory);
        using var process = Process.GetCurrentProcess();
        var readerPath = Path.Combine(leaseDirectory, "reader-integration-test.json");
        await File.WriteAllTextAsync(readerPath, JsonSerializer.Serialize(new
        {
            processId = process.Id,
            processStartedUtc = process.StartTime.ToUniversalTime()
        }), TestContext.Current.CancellationToken);

        await using var running = _fixture.StartScript(checkout, ["bootstrap"]);
        var promotionPath = Path.Combine(leaseDirectory, "promotion.lock");
        try
        {
            await WaitForFileAsync(promotionPath, TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);
        }
        catch (TimeoutException exception)
        {
            var failedRun = await running.StopAndCollectAsync();
            Assert.True(false, $"{exception.Message}{Environment.NewLine}{failedRun}");
        }

        await Task.Delay(250, TestContext.Current.CancellationToken);
        Assert.False(running.HasExited);

        File.Delete(readerPath);
        var result = await running.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        AssertProcessExitCode(0, result);
        Assert.False(File.Exists(promotionPath));
        Assert.Null(ManagedHostFingerprint.Validate(checkout.Root, checkout.ExecutablePath));
    }

    [Fact]
    public async Task DispatchShowsHelpForAValidHostAndPreservesArgumentTokensAndExitCode()
    {
        using var checkout = await TemporaryCheckout.CreateAsync(_fixture, includeDotNet: false, initialiseGit: false);
        checkout.InstallStubHost();
        WriteManifest(checkout.Root, checkout.ExecutablePath, checkout.ManifestPath);
        Assert.Null(ManagedHostFingerprint.Validate(checkout.Root, checkout.ExecutablePath));

        var help = await _fixture.RunScriptAsync(checkout, []);
        AssertProcessExitCode(0, help);
        AssertProcessStandardOutputContains(help, "LeanCorpus managed DevOps fixture", StringComparison.Ordinal);

        var capturePath = Path.Combine(checkout.Root, "forwarded-arguments.txt");
        var arguments = new[]
        {
            "benchmark", "remote", "start", "--host", "jordan@bench", "--path", "/srv/LeanCorpus checkout",
            "--branch", "issue-97", "--", "--suite", "query", "--filter", "*Term Query*"
        };
        var dispatched = await _fixture.RunScriptAsync(checkout, arguments, new Dictionary<string, string?>
        {
            ["DEVOPS_CAPTURE_PATH"] = capturePath
        });
        AssertProcessExitCode(0, dispatched);
        Assert.Equal(arguments, await File.ReadAllLinesAsync(capturePath, TestContext.Current.CancellationToken));

        var nonZero = await _fixture.RunScriptAsync(checkout, ["docs", "build"], new Dictionary<string, string?>
        {
            ["DEVOPS_FAKE_DISPATCH_EXIT_CODE"] = "37"
        });
        AssertProcessExitCode(37, nonZero);
    }

    [Theory]
    [InlineData("missing-apphost")]
    [InlineData("missing-manifest")]
    [InlineData("modified-source")]
    [InlineData("changed-config")]
    [InlineData("tampered-executable")]
    [InlineData("modified-template")]
    [InlineData("modified-resource")]
    [InlineData("added-resource")]
    [InlineData("deleted-resource")]
    [InlineData("renamed-resource")]
    public async Task DispatchRefusesMissingOrStaleHostInputsAndRequiresExplicitBootstrap(string mutation)
    {
        using var checkout = await TemporaryCheckout.CreateAsync(_fixture, includeDotNet: false, initialiseGit: false);
        checkout.InstallStubHost();
        WriteManifest(checkout.Root, checkout.ExecutablePath, checkout.ManifestPath);
        if (mutation is "modified-template" or "modified-resource" or "added-resource" or "deleted-resource" or "renamed-resource")
        {
            var baseline = await _fixture.RunScriptAsync(checkout, ["--help"]);
            AssertProcessExitCode(0, baseline);
        }

        switch (mutation)
        {
            case "missing-apphost":
                File.Delete(checkout.ExecutablePath);
                break;
            case "missing-manifest":
                File.Delete(checkout.ManifestPath);
                break;
            case "modified-source":
                await File.AppendAllTextAsync(Path.Combine(checkout.Root, "src", "devops", "tooling", "Rowles.LeanCorpus.DevOps", "Program.cs"), "// changed", TestContext.Current.CancellationToken);
                break;
            case "changed-config":
                await File.AppendAllTextAsync(Path.Combine(checkout.Root, ".config", "devops", "test-suites.json"), " ", TestContext.Current.CancellationToken);
                break;
            case "tampered-executable":
                await File.AppendAllTextAsync(checkout.ExecutablePath, "tampered", TestContext.Current.CancellationToken);
                break;
            case "modified-template":
                await File.AppendAllTextAsync(GetConsumerAssetPath(checkout.Root, "Consumer.csproj.template"), " changed", TestContext.Current.CancellationToken);
                break;
            case "modified-resource":
                await File.AppendAllTextAsync(GetConsumerAssetPath(checkout.Root, "ConsumerSources/core.cs.txt"), " changed", TestContext.Current.CancellationToken);
                break;
            case "added-resource":
                await File.WriteAllTextAsync(GetConsumerAssetPath(checkout.Root, "ConsumerSources/added.cs.txt"), "added", TestContext.Current.CancellationToken);
                break;
            case "deleted-resource":
                File.Delete(GetConsumerAssetPath(checkout.Root, "ConsumerSources/core.cs.txt"));
                break;
            case "renamed-resource":
                File.Move(
                    GetConsumerAssetPath(checkout.Root, "ConsumerSources/core.cs.txt"),
                    GetConsumerAssetPath(checkout.Root, "ConsumerSources/renamed.cs.txt"));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(mutation), mutation, "Unknown host mutation.");
        }

        var capturePath = Path.Combine(checkout.Root, "must-not-run.txt");
        var run = await _fixture.RunScriptAsync(checkout, ["build"], new Dictionary<string, string?>
        {
            ["DEVOPS_CAPTURE_PATH"] = capturePath
        });

        AssertProcessExitCode(3, run);
        AssertProcessCombinedOutputContains(run, "bootstrap", StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(capturePath));
    }

    [Fact]
    public async Task DispatchIgnoresGeneratedOutputDirectoriesWithinTheManagedHostTree()
    {
        using var checkout = await TemporaryCheckout.CreateAsync(_fixture, includeDotNet: false, initialiseGit: false);
        checkout.InstallStubHost();
        var hostSource = Path.Combine(checkout.Root, "src", "devops", "tooling", "Rowles.LeanCorpus.DevOps");
        foreach (var directoryName in new[] { "artifacts", "bin", "obj" })
        {
            var generatedDirectory = Path.Combine(hostSource, directoryName, "Nested");
            Directory.CreateDirectory(generatedDirectory);
            await File.WriteAllTextAsync(Path.Combine(generatedDirectory, "Generated.cs"), "generated output", TestContext.Current.CancellationToken);
        }
        WriteManifest(checkout.Root, checkout.ExecutablePath, checkout.ManifestPath);

        var run = await _fixture.RunScriptAsync(checkout, ["--help"]);

        AssertProcessExitCode(0, run);
        AssertProcessStandardOutputContains(run, "LeanCorpus managed DevOps fixture", StringComparison.Ordinal);
    }

    [Fact]
    public async Task DispatchRejectsAReparsePointInsideTheManagedHostTree()
    {
        using var checkout = await TemporaryCheckout.CreateAsync(_fixture, includeDotNet: false, initialiseGit: false);
        checkout.InstallStubHost();
        WriteManifest(checkout.Root, checkout.ExecutablePath, checkout.ManifestPath);
        var linkedSourceDirectory = Path.Combine(checkout.Root, "linked source directory");
        Directory.CreateDirectory(linkedSourceDirectory);
        await File.WriteAllTextAsync(Path.Combine(linkedSourceDirectory, "Linked.cs"), "linked source", TestContext.Current.CancellationToken);
        CreateDirectoryLink(
            Path.Combine(checkout.Root, "src", "devops", "tooling", "Rowles.LeanCorpus.DevOps", "LinkedSources"),
            linkedSourceDirectory);

        var capturePath = Path.Combine(checkout.Root, "must-not-run.txt");
        var run = await _fixture.RunScriptAsync(checkout, ["build"], new Dictionary<string, string?>
        {
            ["DEVOPS_CAPTURE_PATH"] = capturePath
        });

        AssertProcessExitCode(3, run);
        AssertProcessCombinedOutputContains(run, "link or reparse point", StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(capturePath));
    }

    [Fact]
    public async Task DispatchRejectsAReparsePointAtTheManagedHostSourceRoot()
    {
        using var checkout = await TemporaryCheckout.CreateAsync(_fixture, includeDotNet: false, initialiseGit: false);
        checkout.InstallStubHost();
        WriteManifest(checkout.Root, checkout.ExecutablePath, checkout.ManifestPath);
        var hostSource = Path.Combine(checkout.Root, "src", "devops", "tooling", "Rowles.LeanCorpus.DevOps");
        var movedSource = Path.Combine(Path.GetTempPath(), "linked managed host source " + Guid.NewGuid().ToString("N"));
        Directory.Move(hostSource, movedSource);
        CreateDirectoryLink(hostSource, movedSource);
        try
        {
            var capturePath = Path.Combine(checkout.Root, "must-not-run.txt");
            var run = await _fixture.RunScriptAsync(checkout, ["build"], new Dictionary<string, string?>
            {
                ["DEVOPS_CAPTURE_PATH"] = capturePath
            });

            AssertProcessExitCode(3, run);
            AssertProcessCombinedOutputContains(run, "link or reparse point", StringComparison.OrdinalIgnoreCase);
            Assert.False(File.Exists(capturePath));
        }
        finally
        {
            Directory.Delete(hostSource);
            Directory.Move(movedSource, hostSource);
        }
    }

    [Fact]
    public async Task BootstrapReportsMissingGitDotNetSdkAndInvalidCheckoutBeforeMutation()
    {
        using (var checkout = await TemporaryCheckout.CreateAsync(_fixture, initialiseGit: false))
        {
            var path = checkout.FakeBin;
            var run = await _fixture.RunScriptAsync(checkout, ["bootstrap"], pathOverride: path);
            AssertProcessFailed(run);
            AssertProcessCombinedOutputContains(run, "Git was not found", StringComparison.Ordinal);
        }

        using (var checkout = await TemporaryCheckout.CreateAsync(_fixture, includeDotNet: false))
        {
            checkout.InstallGitWrapper();
            var run = await _fixture.RunScriptAsync(checkout, ["bootstrap"], pathOverride: checkout.FakeBin);
            AssertProcessFailed(run);
            AssertProcessCombinedOutputContains(run, ".NET was not found", StringComparison.Ordinal);
        }

        using (var checkout = await TemporaryCheckout.CreateAsync(_fixture))
        {
            var restoreCapture = Path.Combine(checkout.Root, "restore-calls.txt");
            var run = await _fixture.RunScriptAsync(checkout, ["bootstrap"], new Dictionary<string, string?>
            {
                ["BOOTSTRAP_FAKE_FAIL_STAGE"] = "missing-sdk",
                ["BOOTSTRAP_RESTORE_CAPTURE_PATH"] = restoreCapture
            });
            AssertProcessFailed(run);
            AssertProcessCombinedOutputContains(run, "SDK required by global.json was not found", StringComparison.Ordinal);
            Assert.False(File.Exists(restoreCapture));
        }

        using (var checkout = await TemporaryCheckout.CreateAsync(_fixture))
        {
            File.Delete(Path.Combine(checkout.Root, "src", "Rowles.LeanCorpus.slnx"));
            var run = await _fixture.RunScriptAsync(checkout, ["bootstrap"]);
            AssertProcessFailed(run);
            AssertProcessCombinedOutputContains(run, "not a LeanCorpus checkout", StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task BootstrapVerbRejectsExtraTokensWithoutRunningAnyBuildStep()
    {
        using var checkout = await TemporaryCheckout.CreateAsync(_fixture);
        var restoreCapture = Path.Combine(checkout.Root, "restore-calls.txt");

        var run = await _fixture.RunScriptAsync(checkout, ["bootstrap", "--help"], new Dictionary<string, string?>
        {
            ["BOOTSTRAP_RESTORE_CAPTURE_PATH"] = restoreCapture
        });

        AssertProcessFailed(run);
        AssertProcessCombinedOutputContains(run, "does not accept additional arguments", StringComparison.Ordinal);
        Assert.False(File.Exists(restoreCapture));
    }

    private static async Task WaitForFileAsync(string path, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var deadline = Stopwatch.StartNew();
        while (deadline.Elapsed < timeout)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(path))
            {
                return;
            }

            await Task.Delay(50, cancellationToken);
        }

        throw new TimeoutException($"Timed out waiting for '{path}'.");
    }

    private static void AssertProcessExitCode(int expectedExitCode, ManagedBootstrapTestFixture.ScriptRun run)
    {
        if (run.ExitCode == expectedExitCode)
        {
            return;
        }

        Assert.True(false, $"Expected PowerShell child exit code {expectedExitCode}, actual {run.ExitCode}.{Environment.NewLine}{run}");
    }

    private static void AssertProcessFailed(ManagedBootstrapTestFixture.ScriptRun run)
    {
        if (run.ExitCode != 0)
        {
            return;
        }

        Assert.True(false, $"Expected the PowerShell child to fail, but it exited successfully.{Environment.NewLine}{run}");
    }

    private static void AssertProcessStandardOutputContains(
        ManagedBootstrapTestFixture.ScriptRun run,
        string expected,
        StringComparison comparison)
    {
        if (run.StandardOutput.Contains(expected, comparison))
        {
            return;
        }

        Assert.True(false, $"Expected child stdout to contain '{expected}'.{Environment.NewLine}{run}");
    }

    private static void AssertProcessCombinedOutputContains(
        ManagedBootstrapTestFixture.ScriptRun run,
        string expected,
        StringComparison comparison)
    {
        var normalisedOutput = NormaliseProcessOutput(run.CombinedOutput);
        var normalisedExpected = NormaliseProcessOutput(expected);
        if (normalisedOutput.Contains(normalisedExpected, comparison))
        {
            return;
        }

        Assert.True(false, $"Expected child stdout/stderr to contain '{expected}'.{Environment.NewLine}{run}");
    }

    private static string NormaliseProcessOutput(string output)
    {
        var withoutAnsi = Regex.Replace(output, @"\x1B\[[0-?]*[ -/]*[@-~]", string.Empty, RegexOptions.CultureInvariant);
        var withoutPowerShellContinuationBars = Regex.Replace(withoutAnsi, @"(?m)^[ \t]*\|[ \t]?", string.Empty, RegexOptions.CultureInvariant);
        return Regex.Replace(withoutPowerShellContinuationBars, @"\s+", " ", RegexOptions.CultureInvariant);
    }

    private static void WriteManifest(string repositoryRoot, string executablePath, string manifestPath)
    {
        var sourceFiles = GetSourceFiles(repositoryRoot).Select(relativePath => new
        {
            path = relativePath,
            sha256 = HashFile(Path.Combine(repositoryRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)))
        }).ToArray();
        var canonical = string.Concat(sourceFiles.Select(file => $"{file.path}\0{file.sha256}\n"));
        var sourceFingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
        Directory.CreateDirectory(Path.GetDirectoryName(manifestPath)!);
        File.WriteAllBytes(manifestPath, JsonSerializer.SerializeToUtf8Bytes(new
        {
            schemaVersion = 1,
            executableSha256 = HashFile(executablePath),
            sourceFingerprint,
            sourceFiles
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }

    private static string[] GetSourceFiles(string root)
    {
        var paths = new HashSet<string>(StringComparer.Ordinal);
        foreach (var relative in new[]
        {
            "global.json", "Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props",
            "src/devops/Directory.Build.props", "src/devops/Directory.Packages.props"
        })
        {
            var path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(path))
            {
                paths.Add(relative);
            }
        }

        foreach (var relativeDirectory in new[] { "src/devops/tooling/Rowles.LeanCorpus.DevOps", ".config/devops" })
        {
            var directory = Path.Combine(root, relativeDirectory.Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(directory))
            {
                continue;
            }

            foreach (var path in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
            {
                var relativePath = Path.GetRelativePath(directory, path);
                if (relativePath.Split(Path.DirectorySeparatorChar).Any(IsTransientOutputDirectory))
                {
                    continue;
                }

                var isStandardSource = new[] { ".cs", ".csproj", ".json", ".props", ".targets", ".slnx" }
                    .Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
                var isManagedHostResource = relativeDirectory == "src/devops/tooling/Rowles.LeanCorpus.DevOps" &&
                    (Path.GetExtension(path).Equals(".template", StringComparison.OrdinalIgnoreCase) ||
                     Path.GetFileName(path).EndsWith(".cs.txt", StringComparison.OrdinalIgnoreCase));
                if (isStandardSource || isManagedHostResource)
                {
                    paths.Add(Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/'));
                }
            }
        }

        return paths.Order(StringComparer.Ordinal).ToArray();
    }

    private static bool IsTransientOutputDirectory(string name)
        => name.Equals("artifacts", StringComparison.OrdinalIgnoreCase) ||
           name.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
           name.Equals("obj", StringComparison.OrdinalIgnoreCase);

    private static string GetConsumerAssetPath(string root, string relativePath)
        => Path.Combine(root, "src", "devops", "tooling", "Rowles.LeanCorpus.DevOps", "Packaging", "Assets", relativePath.Replace('/', Path.DirectorySeparatorChar));

    private static void CreateDirectoryLink(string linkPath, string targetPath)
    {
        if (!OperatingSystem.IsWindows())
        {
            Directory.CreateSymbolicLink(linkPath, targetPath);
            return;
        }

        var startInfo = new ProcessStartInfo(Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in new[] { "/c", "mklink", "/J", linkPath, targetPath })
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start cmd.exe to create a test junction.");
        var standardOutput = process.StandardOutput.ReadToEnd();
        var standardError = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"Creating a test junction failed with exit code {process.ExitCode}.{Environment.NewLine}{standardOutput}{Environment.NewLine}{standardError}");
        }
    }

    private static string HashFile(string path)
        => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();

    internal sealed record PreviousInstallation(string HostPath, string ManifestPath);

    internal sealed class TemporaryCheckout : IDisposable
    {
        private readonly ManagedBootstrapTestFixture _fixture;
        private readonly string _cleanupRoot;

        private TemporaryCheckout(ManagedBootstrapTestFixture fixture, string root, string fakeBin, string fakeHome, string cleanupRoot)
        {
            _fixture = fixture;
            Root = root;
            FakeBin = fakeBin;
            FakeHome = fakeHome;
            _cleanupRoot = cleanupRoot;
        }

        public string Root { get; }

        public string FakeBin { get; }

        public string FakeHome { get; }

        public string ScriptPath => Path.Combine(Root, "devops.ps1");

        public string ExecutablePath => Path.Combine(Root, OperatingSystem.IsWindows() ? "devops.exe" : "devops");

        public string ManifestPath => Path.Combine(Root, "artifacts", "devops", "managed-host.json");

        public static async Task<TemporaryCheckout> CreateAsync(
            ManagedBootstrapTestFixture fixture,
            bool includeDotNet = true,
            bool initialiseGit = true,
            bool includeSolution = true)
        {
            var root = Path.Combine(Path.GetTempPath(), "LeanCorpus bootstrap checkout " + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var checkout = new TemporaryCheckout(
                fixture,
                root,
                Path.Combine(root, "fake bin"),
                Path.Combine(root, "fake home"),
                root);
            checkout.Populate(includeDotNet, includeSolution);
            if (initialiseGit)
            {
                await fixture.RunGitAsync(root, ["init", "--quiet"]);
            }

            return checkout;
        }

        public static async Task<TemporaryCheckout> CreateWorktreeAsync(ManagedBootstrapTestFixture fixture)
        {
            var cleanupRoot = Path.Combine(Path.GetTempPath(), "LeanCorpus bootstrap worktree " + Guid.NewGuid().ToString("N"));
            var adminRoot = Path.Combine(cleanupRoot, "repository");
            var worktreeRoot = Path.Combine(cleanupRoot, "linked checkout");
            Directory.CreateDirectory(adminRoot);
            await fixture.RunGitAsync(adminRoot, ["init", "--quiet"]);
            await fixture.RunGitAsync(adminRoot, ["-c", "user.name=Bootstrap Test", "-c", "user.email=bootstrap@example.invalid", "commit", "--allow-empty", "-m", "fixture"]);
            await fixture.RunGitAsync(adminRoot, ["worktree", "add", "--detach", "--quiet", worktreeRoot, "HEAD"]);

            var checkout = new TemporaryCheckout(
                fixture,
                worktreeRoot,
                Path.Combine(worktreeRoot, "fake bin"),
                Path.Combine(worktreeRoot, "fake home"),
                cleanupRoot);
            checkout.Populate(includeDotNet: true, includeSolution: true);
            return checkout;
        }

        private void Populate(bool includeDotNet, bool includeSolution)
        {
            Directory.CreateDirectory(FakeBin);
            Directory.CreateDirectory(FakeHome);
            File.Copy(_fixture.RootScriptPath, ScriptPath);
            foreach (var (relative, content) in new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["global.json"] = "{\"sdk\":{\"version\":\"11.0.100-preview.7.26381.103\",\"allowPrerelease\":true}}",
                [".config/dotnet-tools.json"] = "{\"version\":1,\"isRoot\":true,\"tools\":{}}",
                ["Directory.Build.props"] = "<Project />",
                ["Directory.Build.targets"] = "<Project />",
                ["Directory.Packages.props"] = "<Project />",
                ["src/devops/Directory.Build.props"] = "<Project />",
                ["src/devops/Directory.Packages.props"] = "<Project />",
                ["src/devops/tooling/Rowles.LeanCorpus.DevOps.Tests/Rowles.LeanCorpus.DevOps.Tests.csproj"] = "<Project />",
                ["src/devops/tooling/Rowles.LeanCorpus.DevOps/Rowles.LeanCorpus.DevOps.csproj"] = "<Project />",
                ["src/devops/tooling/Rowles.LeanCorpus.DevOps/Program.cs"] = "fixture source",
                [".config/devops/benchmark-datasets.json"] = "{}",
                [".config/devops/benchmark-groups.json"] = "{}",
                [".config/devops/benchmark-strategies.json"] = "{}",
                [".config/devops/benchmark-suites.json"] = "{}",
                [".config/devops/code-areas.json"] = "{}",
                [".config/devops/test-suites.json"] = "{\"version\":1}"
            })
            {
                if (relative == "src/Rowles.LeanCorpus.slnx" && !includeSolution)
                {
                    continue;
                }

                var path = Path.Combine(Root, relative.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            }

            var hostDirectory = Path.Combine(Root, "src", "devops", "tooling", "Rowles.LeanCorpus.DevOps");
            File.WriteAllText(Path.Combine(hostDirectory, "Rowles.LeanCorpus.DevOps.csproj"), """
                <Project>
                  <ItemGroup>
                    <EmbeddedResource Include="Packaging/Assets/Consumer.csproj.template" />
                    <EmbeddedResource Include="Packaging/Assets/Program.cs.template" />
                    <EmbeddedResource Include="Packaging/Assets/ConsumerSources/*.cs.txt" />
                  </ItemGroup>
                </Project>
                """, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            var assetsDirectory = Path.Combine(hostDirectory, "Packaging", "Assets");
            Directory.CreateDirectory(Path.Combine(assetsDirectory, "ConsumerSources"));
            File.WriteAllText(Path.Combine(assetsDirectory, "Consumer.csproj.template"), "consumer project template", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.WriteAllText(Path.Combine(assetsDirectory, "Program.cs.template"), "consumer program template", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.WriteAllText(Path.Combine(assetsDirectory, "ConsumerSources", "core.cs.txt"), "core consumer source", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            if (includeSolution)
            {
                var solution = Path.Combine(Root, "src", "Rowles.LeanCorpus.slnx");
                Directory.CreateDirectory(Path.GetDirectoryName(solution)!);
                File.WriteAllText(solution, "<Solution />", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            }

            if (includeDotNet)
            {
                var dotNetName = OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet";
                CopyExecutable(_fixture.StubPath, Path.Combine(FakeBin, dotNetName));
            }
        }

        public void InstallStubHost() => CopyExecutable(_fixture.StubPath, ExecutablePath);

        public void InstallGitWrapper()
        {
            var gitPath = ManagedBootstrapTestFixture.FindExecutable("git");
            var wrapperName = OperatingSystem.IsWindows() ? "git.cmd" : "git";
            var wrapperPath = Path.Combine(FakeBin, wrapperName);
            var content = OperatingSystem.IsWindows()
                ? $"@echo off{Environment.NewLine}call \"{gitPath}\" %*{Environment.NewLine}exit /b %ERRORLEVEL%{Environment.NewLine}"
                : $"#!/bin/sh{Environment.NewLine}exec '{gitPath.Replace("'", "'\\''", StringComparison.Ordinal)}' \"$@\"{Environment.NewLine}";
            File.WriteAllText(wrapperPath, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(wrapperPath,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                    UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                    UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
            }
        }

        public void InstallGitWrapperForSafeDirectory()
        {
            if (OperatingSystem.IsWindows())
            {
                throw new PlatformNotSupportedException("The safe-directory Git wrapper is only used by the Linux symlink regression test.");
            }

            var gitPath = ManagedBootstrapTestFixture.FindExecutable("git");
            var wrapperPath = Path.Combine(FakeBin, "git");
            var content = $"#!/bin/sh{Environment.NewLine}" +
                "if [ \"$1\" = \"-C\" ] && { [ \"$2\" != \"$BOOTSTRAP_GIT_SAFE_DIRECTORY\" ] || [ \"$3\" != \"-c\" ] || [ \"$4\" != \"safe.directory=$BOOTSTRAP_GIT_CANONICAL_SAFE_DIRECTORY\" ]; }; then\n" +
                "  echo 'fatal: managed bootstrap did not pair the logical workspace with its canonical safe directory' >&2\n" +
                "  exit 128\n" +
                "fi\n" +
                $"GIT_TEST_ASSUME_DIFFERENT_OWNER=1 exec '{gitPath.Replace("'", "'\\''", StringComparison.Ordinal)}' \"$@\"{Environment.NewLine}";
            File.WriteAllText(wrapperPath, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.SetUnixFileMode(wrapperPath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        }

        public PreviousInstallation WritePreviousInstallation()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ManifestPath)!);
            File.WriteAllText(ExecutablePath, "previous apphost sentinel");
            File.WriteAllText(ManifestPath, "previous manifest sentinel");
            return new PreviousInstallation(ExecutablePath, ManifestPath);
        }

        public void Dispose()
        {
            if (Directory.Exists(_cleanupRoot))
            {
                DeleteReparsePoints(_cleanupRoot);
                ClearReadOnlyAttributes(_cleanupRoot);
                Directory.Delete(_cleanupRoot, recursive: true);
            }
        }

        private static void DeleteReparsePoints(string path)
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(path))
            {
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    if ((attributes & FileAttributes.Directory) != 0)
                    {
                        Directory.Delete(entry);
                    }
                    else
                    {
                        File.Delete(entry);
                    }

                    continue;
                }

                if ((attributes & FileAttributes.Directory) != 0)
                {
                    DeleteReparsePoints(entry);
                }
            }
        }

        private static void ClearReadOnlyAttributes(string path)
        {
            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                return;
            }

            if ((attributes & FileAttributes.Directory) != 0)
            {
                foreach (var entry in Directory.EnumerateFileSystemEntries(path))
                {
                    ClearReadOnlyAttributes(entry);
                }
            }

            if ((attributes & FileAttributes.ReadOnly) != 0)
            {
                File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
            }
        }

        private static void CopyExecutable(string source, string destination)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(source, destination, overwrite: true);
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(destination,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                    UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                    UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
            }
        }
    }
}

public sealed class ManagedBootstrapTestFixture : IAsyncLifetime
{
    private readonly string _temporaryRoot = Path.Combine(Path.GetTempPath(), "LeanCorpus managed bootstrap fixture " + Guid.NewGuid().ToString("N"));

    public string RepositoryRoot { get; private set; } = string.Empty;

    public string RootScriptPath { get; private set; } = string.Empty;

    public string PowerShellPath { get; private set; } = string.Empty;

    public string DotNetRoot { get; private set; } = string.Empty;

    public string StubPath { get; private set; } = string.Empty;

    public async ValueTask InitializeAsync()
    {
        RepositoryRoot = RepositoryPaths.Resolve(null, Directory.GetCurrentDirectory(), AppContext.BaseDirectory).RepositoryRoot;
        RootScriptPath = Path.Combine(RepositoryRoot, "devops.ps1");
        PowerShellPath = FindExecutable("pwsh");
        var dotNetPath = FindExecutable("dotnet");
        var resolvedDotNetPath = new FileInfo(dotNetPath).ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? dotNetPath;
        DotNetRoot = Path.GetDirectoryName(resolvedDotNetPath)!;

        var projectDirectory = Path.Combine(_temporaryRoot, "project");
        var outputDirectory = Path.Combine(_temporaryRoot, "publish");
        Directory.CreateDirectory(projectDirectory);
        var sourcePath = Path.Combine(RepositoryRoot, "src", "devops", "tooling", "Rowles.LeanCorpus.DevOps.ProcessTestChild", "Program.cs");
        File.Copy(sourcePath, Path.Combine(projectDirectory, "Program.cs"));
        await File.WriteAllTextAsync(Path.Combine(projectDirectory, "BootstrapStub.csproj"), """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net10.0</TargetFramework>
                <ImplicitUsings>enable</ImplicitUsings>
                <Nullable>enable</Nullable>
                <UseAppHost>true</UseAppHost>
                <AssemblyName>devops-process-test-child</AssemblyName>
              </PropertyGroup>
            </Project>
            """);

        var startInfo = new ProcessStartInfo(dotNetPath)
        {
            WorkingDirectory = RepositoryRoot,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in new[]
        {
            "publish", Path.Combine(projectDirectory, "BootstrapStub.csproj"),
            "--configuration", "Release", "--framework", "net10.0", "--runtime", RuntimeInformation.RuntimeIdentifier,
            "--self-contained", "false", "--output", outputDirectory,
            "-p:PublishSingleFile=true", "-p:IncludeNativeLibrariesForSelfExtract=false", "-p:DebugSymbols=false", "-p:DebugType=None",
            "-p:UseSharedCompilation=false", "--disable-build-servers", "-m:1"
        })
        {
            startInfo.ArgumentList.Add(argument);
        }

        startInfo.Environment["DOTNET_BUNDLE_EXTRACT_BASE_DIR"] = Path.Combine(_temporaryRoot, "bundle-extract");
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start the temporary bootstrap stub publish.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        await process.WaitForExitAsync(timeout.Token);
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"The bootstrap test stub publish failed with {process.ExitCode}.{Environment.NewLine}{await stdout}{Environment.NewLine}{await stderr}");
        }

        StubPath = Path.Combine(outputDirectory, OperatingSystem.IsWindows() ? "devops-process-test-child.exe" : "devops-process-test-child");
        if (!File.Exists(StubPath))
        {
            throw new FileNotFoundException("The single-file bootstrap test stub was not produced.", StubPath);
        }
    }

    internal RunningScript StartScript(
        ManagedBootstrapIntegrationTests.TemporaryCheckout checkout,
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string?>? environment = null,
        string? pathOverride = null,
        string? scriptPathOverride = null)
    {
        Directory.CreateDirectory(checkout.FakeHome);
        var diagnosticsPath = Path.Combine(checkout.FakeHome, "bootstrap-process-diagnostics-" + Guid.NewGuid().ToString("N") + ".txt");
        var wrapperPath = Path.Combine(checkout.FakeHome, "bootstrap-process-wrapper-" + Guid.NewGuid().ToString("N") + ".ps1");
        File.WriteAllText(wrapperPath, CreateDiagnosticWrapper(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        var scriptPath = scriptPathOverride ?? checkout.ScriptPath;
        var scriptDirectory = Path.GetDirectoryName(scriptPath)!;
        var startInfo = new ProcessStartInfo(PowerShellPath)
        {
            WorkingDirectory = scriptDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-File", wrapperPath, scriptPath }.Concat(arguments))
        {
            startInfo.ArgumentList.Add(argument);
        }

        var originalPath = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        startInfo.Environment["PATH"] = pathOverride ?? string.Join(Path.PathSeparator, new[] { checkout.FakeBin, originalPath });
        startInfo.Environment["HOME"] = checkout.FakeHome;
        startInfo.Environment["USERPROFILE"] = checkout.FakeHome;
        startInfo.Environment["GITHUB_WORKSPACE"] = checkout.Root;
        startInfo.Environment["DOTNET_ROOT"] = DotNetRoot;
        startInfo.Environment["DOTNET_ROOT_X64"] = DotNetRoot;
        startInfo.Environment["LEANCORPUS_BOOTSTRAP_TEST_STUB"] = "1";
        startInfo.Environment["LEANCORPUS_BOOTSTRAP_STUB_TEMPLATE_PATH"] = StubPath;
        startInfo.Environment["LEANCORPUS_BOOTSTRAP_TEST_DIAGNOSTICS_PATH"] = diagnosticsPath;
        startInfo.Environment["BOOTSTRAP_FAKE_FAIL_STAGE"] = string.Empty;
        startInfo.Environment["BOOTSTRAP_FAKE_SIDECAR"] = "0";
        startInfo.Environment["DEVOPS_CAPTURE_PATH"] = string.Empty;
        startInfo.Environment["DEVOPS_FAKE_DISPATCH_EXIT_CODE"] = string.Empty;
        startInfo.Environment["BOOTSTRAP_RESTORE_CAPTURE_PATH"] = string.Empty;
        startInfo.Environment["BOOTSTRAP_BUILD_CAPTURE_PATH"] = string.Empty;
        startInfo.Environment["BOOTSTRAP_PUBLISH_CAPTURE_PATH"] = string.Empty;
        if (environment is not null)
        {
            foreach (var (name, value) in environment)
            {
                if (value is null)
                {
                    startInfo.Environment.Remove(name);
                }
                else
                {
                    startInfo.Environment[name] = value;
                }
            }
        }

        var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start the PowerShell entry point.");
        var command = string.Join(" ", new[] { PowerShellPath, "-NoProfile", "-NonInteractive", "-File", wrapperPath, scriptPath }
            .Concat(arguments)
            .Select(QuoteForDisplay));
        var environmentSummary = ManagedBootstrapProcessDiagnostics.CreateEnvironmentSummary(startInfo.Environment);
        return new RunningScript(process, command, scriptDirectory, checkout.FakeBin, diagnosticsPath, environmentSummary);
    }

    private static string CreateDiagnosticWrapper() => """
        $ErrorActionPreference = 'Stop'
        $scriptPath = $args[0]
        [string[]]$scriptArguments = if ($args.Count -gt 1) { $args[1..($args.Count - 1)] } else { @() }
        $resolution = foreach ($name in @('git', 'dotnet')) {
            $command = Get-Command -Name $name -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
            if ($null -eq $command) {
                "$name => <not found>"
            }
            else {
                "$name => name=$($command.Name); type=$($command.CommandType); source=$($command.Source); path=$($command.Path); definition=$($command.Definition)"
            }
        }
        [System.IO.File]::WriteAllText(
            $env:LEANCORPUS_BOOTSTRAP_TEST_DIAGNOSTICS_PATH,
            [string]::Join([System.Environment]::NewLine, [string[]]$resolution))
        if ($scriptArguments.Count -eq 0) {
            & $scriptPath
        }
        else {
            & $scriptPath @scriptArguments
        }
        exit $LASTEXITCODE
        """;

    private static string QuoteForDisplay(string value) => "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";

    internal async Task<ScriptRun> RunScriptAsync(
        ManagedBootstrapIntegrationTests.TemporaryCheckout checkout,
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string?>? environment = null,
        string? pathOverride = null,
        TimeSpan? timeout = null,
        string? scriptPathOverride = null)
    {
        await using var running = StartScript(checkout, arguments, environment, pathOverride, scriptPathOverride);
        return await running.WaitAsync(timeout ?? TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
    }

    public async Task RunGitAsync(string workingDirectory, IReadOnlyList<string> arguments)
    {
        var startInfo = new ProcessStartInfo(FindExecutable("git"))
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start Git for the bootstrap fixture.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await process.WaitForExitAsync(timeout.Token);
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"Git fixture command failed with {process.ExitCode}.{Environment.NewLine}{await stdout}{Environment.NewLine}{await stderr}");
        }
    }

    public static string FindExecutable(string name)
    {
        var extensions = OperatingSystem.IsWindows() ? new[] { ".exe", ".cmd", ".bat", string.Empty } : [string.Empty];
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var extension in extensions)
            {
                var candidate = Path.Combine(directory, name + extension);
                if (File.Exists(candidate))
                {
                    return Path.GetFullPath(candidate);
                }
            }
        }

        throw new FileNotFoundException($"Required executable '{name}' was not found on PATH.");
    }

    public ValueTask DisposeAsync()
    {
        if (Directory.Exists(_temporaryRoot))
        {
            Directory.Delete(_temporaryRoot, recursive: true);
        }

        return ValueTask.CompletedTask;
    }

    public sealed record ScriptRun(int ExitCode, string StandardOutput, string StandardError)
    {
        internal string Command { get; init; } = "<unknown PowerShell command>";

        internal string WorkingDirectory { get; init; } = "<unknown working directory>";

        internal string FakeBinPath { get; init; } = "<unknown fake-bin path>";

        internal string CommandResolution { get; init; } = "<Get-Command resolution unavailable>";

        internal string EnvironmentSummary { get; init; } = "<environment summary unavailable>";

        public string CombinedOutput => StandardOutput + Environment.NewLine + StandardError;

        public override string ToString() => ManagedBootstrapProcessDiagnostics.Format(
            ExitCode,
            Command,
            WorkingDirectory,
            FakeBinPath,
            CommandResolution,
            EnvironmentSummary,
            StandardOutput,
            StandardError);
    }

    public sealed class RunningScript : IAsyncDisposable
    {
        private readonly Process _process;
        private readonly Task<string> _stdout;
        private readonly Task<string> _stderr;
        private readonly string _command;
        private readonly string _workingDirectory;
        private readonly string _fakeBinPath;
        private readonly string _diagnosticsPath;
        private readonly string _environmentSummary;

        internal RunningScript(
            Process process,
            string command,
            string workingDirectory,
            string fakeBinPath,
            string diagnosticsPath,
            string environmentSummary)
        {
            _process = process;
            _stdout = process.StandardOutput.ReadToEndAsync();
            _stderr = process.StandardError.ReadToEndAsync();
            _command = command;
            _workingDirectory = workingDirectory;
            _fakeBinPath = fakeBinPath;
            _diagnosticsPath = diagnosticsPath;
            _environmentSummary = environmentSummary;
        }

        public bool HasExited => _process.HasExited;

        public async Task<ScriptRun> WaitAsync(TimeSpan timeout, CancellationToken cancellationToken)
        {
            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(timeout);
            try
            {
                await _process.WaitForExitAsync(timeoutSource.Token);
            }
            catch (OperationCanceledException)
            {
                var result = await StopAndCollectAsync();
                throw new TimeoutException($"PowerShell entry point did not finish within {timeout}.{Environment.NewLine}{result}");
            }

            return await CollectResultAsync();
        }

        public async Task<ScriptRun> StopAndCollectAsync()
        {
            if (!_process.HasExited)
            {
                try
                {
                    _process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException) when (_process.HasExited)
                {
                }
            }

            await _process.WaitForExitAsync(CancellationToken.None);
            return await CollectResultAsync();
        }

        private async Task<ScriptRun> CollectResultAsync()
        {
            string commandResolution;
            try
            {
                commandResolution = File.Exists(_diagnosticsPath)
                    ? await File.ReadAllTextAsync(_diagnosticsPath, CancellationToken.None)
                    : "<diagnostic wrapper did not write Get-Command resolution>";
            }
            catch (IOException exception)
            {
                commandResolution = $"<could not read Get-Command resolution: {exception.GetType().Name}>";
            }
            catch (UnauthorizedAccessException exception)
            {
                commandResolution = $"<could not read Get-Command resolution: {exception.GetType().Name}>";
            }

            return new ScriptRun(_process.ExitCode, await _stdout, await _stderr)
            {
                Command = _command,
                WorkingDirectory = _workingDirectory,
                FakeBinPath = _fakeBinPath,
                CommandResolution = commandResolution,
                EnvironmentSummary = _environmentSummary
            };
        }

        public ValueTask DisposeAsync()
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
            }

            _process.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
