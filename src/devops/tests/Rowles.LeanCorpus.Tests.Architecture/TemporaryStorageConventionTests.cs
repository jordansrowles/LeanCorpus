using Rowles.LeanCorpus.Tests.Architecture.Infrastructure;
using Rowles.LeanCorpus.Tests.Architecture.TemporaryStorage;

namespace Rowles.LeanCorpus.Tests.Architecture;

/// <summary>
/// ADR046: repository-owned tests, benchmarks and tooling must not select operating-system
/// temporary storage directly. Use <c>RepositoryTemporaryStorage</c>, the shared fixtures or
/// <c>ArtifactPaths.TemporaryRoot</c>, or register a categorised exception below.
/// </summary>
public sealed class TemporaryStorageConventionTests
{
    /// <summary>Exception categories from ADR046.</summary>
    internal enum Category
    {
        /// <summary>A: cross-checkout coordination.</summary>
        A,
        /// <summary>B: a test that must create a checkout, worktree or symlink outside the repository.</summary>
        B,
        /// <summary>C: a test whose subject is operating-system temporary storage itself.</summary>
        C,
        /// <summary>D: the helper's own root resolution.</summary>
        D
    }

    internal sealed record RegisteredException(string Path, string Member, Category Category, string Reason);

    internal static readonly RegisteredException[] Register =
    [
        new("src/devops/tooling/Rowles.LeanCorpus.DevOps/Benchmarks/BenchmarkCommandService.cs", "BenchmarkCommandService.BenchmarkHostLeaseRoot", Category.A,
            "Host-wide benchmark lease shared by every checkout on the machine; a per-checkout path would let two checkouts benchmark at once."),
        new("src/devops/tooling/Rowles.LeanCorpus.DevOps.Tests/Bootstrap/ManagedBootstrapIntegrationTests.cs", "ManagedBootstrapIntegrationTests.ScriptRunStringContainsFailureContextWithoutExposingSecretsOrUnboundedOutput", Category.B,
            "A fake bin directory placed first on PATH for the bootstrap script; inside the checkout it would be found by the checkout's own tooling."),
        new("src/devops/tooling/Rowles.LeanCorpus.DevOps.Tests/Bootstrap/ManagedBootstrapIntegrationTests.cs", "ManagedBootstrapIntegrationTests.BootstrapAcceptsAScriptPathThroughADirectoryLink", Category.B,
            "A directory link to a fake checkout; the link must not resolve inside this checkout."),
        new("src/devops/tooling/Rowles.LeanCorpus.DevOps.Tests/Bootstrap/ManagedBootstrapIntegrationTests.cs", "ManagedBootstrapIntegrationTests.BootstrapUsesGitHubWorkspaceForGitSafetyWhenTheScriptPathIsPhysical", Category.B,
            "A directory link to a fake checkout used to test Git safe-directory handling outside this checkout."),
        new("src/devops/tooling/Rowles.LeanCorpus.DevOps.Tests/Bootstrap/ManagedBootstrapIntegrationTests.cs", "ManagedBootstrapIntegrationTests.DispatchRejectsAReparsePointAtTheManagedHostSourceRoot", Category.B,
            "Moves the managed host source out of the fake checkout to create a reparse point; the target must be outside any checkout."),
        new("src/devops/tooling/Rowles.LeanCorpus.DevOps.Tests/Bootstrap/ManagedBootstrapIntegrationTests.cs", "ManagedBootstrapIntegrationTests.TemporaryCheckout.CreateAsync", Category.B,
            "Builds a fake checkout; inside this checkout it inherits its Directory.Build.props and the build fails with CS5001."),
        new("src/devops/tooling/Rowles.LeanCorpus.DevOps.Tests/Bootstrap/ManagedBootstrapIntegrationTests.cs", "ManagedBootstrapIntegrationTests.TemporaryCheckout.CreateWorktreeAsync", Category.B,
            "Creates a Git worktree of the fake checkout, which must be outside any checkout."),
        new("src/devops/tooling/Rowles.LeanCorpus.DevOps.Tests/Bootstrap/ManagedBootstrapIntegrationTests.cs", "ManagedBootstrapTestFixture._temporaryRoot", Category.B,
            "Root for the fixture's fake checkouts and installations, which must be outside this checkout."),
        new("src/devops/tooling/Rowles.LeanCorpus.DevOps.Tests/Repository/RepositoryPathsTests.cs", "RepositoryPathsTests.TemporaryCheckout.Create", Category.B,
            "A fake checkout for checkout discovery, which must not be inside this checkout."),
        new("src/devops/tooling/Rowles.LeanCorpus.DevOps.Tests/Repository/RepositoryPathsTests.cs", "RepositoryPathsTests.TemporaryCheckout.CreateSourceArchive", Category.B,
            "A source archive layout for checkout discovery, which must not be inside this checkout."),
        new("src/devops/tooling/Rowles.LeanCorpus.DevOps.Tests/Repository/RepositoryPathsTests.cs", "RepositoryPathsTests.TemporaryDirectory.Create", Category.B,
            "A directory outside any checkout, used to prove discovery falls back to the executable's checkout."),
    ];

    private static readonly string[] ScannedRoots = ["src/devops", "src/server/devops"];

    [Fact]
    public void Repository_code_must_not_select_operating_system_temporary_storage()
    {
        var violations = ScanRepository();
        var unregistered = violations
            .Where(violation => !Register.Any(entry => Matches(entry, violation)))
            .Select(static violation => violation.ToString());
        var stale = Register
            .Where(entry => !violations.Any(violation => Matches(entry, violation)))
            .Select(static entry => $"stale exception: {entry.Path} ({entry.Member})");

        RuleAssert.Empty(
            "Use RepositoryTemporaryStorage, the shared test fixtures or ArtifactPaths.TemporaryRoot instead of operating-system temporary storage (ADR046):",
            unregistered.Concat(stale));
    }

    [Fact]
    public void Every_registered_exception_has_a_reason()
    {
        RuleAssert.Empty("Temporary storage exceptions need a reason:",
            Register.Where(static entry => string.IsNullOrWhiteSpace(entry.Reason)).Select(static entry => $"{entry.Path} ({entry.Member})"));
    }

    internal static IReadOnlyList<TemporaryStorageViolation> ScanRepository()
    {
        var files = ScannedRoots
            .SelectMany(static root => Directory.EnumerateFiles(RepositoryPaths.FromRoot(root.Split('/')), "*.cs", SearchOption.AllDirectories))
            .Select(static file => Path.GetRelativePath(RepositoryPaths.Root, file).Replace('\\', '/'))
            .Where(static relative => !relative.Split('/').Any(static segment => segment is "bin" or "obj"))
            .ToArray();
        var projects = ScannedRoots
            .SelectMany(static root => Directory.EnumerateFiles(RepositoryPaths.FromRoot(root.Split('/')), "*.csproj", SearchOption.AllDirectories))
            .Where(static project => !project.Replace('\\', '/').Split('/').Any(static segment => segment is "bin" or "obj"))
            .ToDictionary(static project => Path.GetRelativePath(RepositoryPaths.Root, Path.GetDirectoryName(project)!).Replace('\\', '/'), StringComparer.Ordinal);

        var violations = new List<TemporaryStorageViolation>();
        foreach (var group in files.GroupBy(file => OwningProject(file, projects.Keys), StringComparer.Ordinal))
        {
            var sources = group.Select(static file => new ScannedSource(file, File.ReadAllText(RepositoryPaths.FromRoot(file.Split('/'))))).ToArray();
            var (usings, statics) = group.Key.Length > 0 && projects.TryGetValue(group.Key, out var projectFile)
                ? TemporaryStorageScanner.ReadProjectUsings(projectFile)
                : (TemporaryStorageScanner.ImplicitUsings, Array.Empty<string>());
            violations.AddRange(TemporaryStorageScanner.ScanProject(sources, usings, statics));
        }

        return violations.OrderBy(static violation => violation.Path, StringComparer.Ordinal).ThenBy(static violation => violation.Line).ToArray();
    }

    private static string OwningProject(string file, IEnumerable<string> projectDirectories)
        => projectDirectories
            .Where(directory => file.StartsWith(directory + "/", StringComparison.Ordinal))
            .OrderByDescending(static directory => directory.Length)
            .FirstOrDefault() ?? string.Empty;

    private static bool Matches(RegisteredException entry, TemporaryStorageViolation violation)
        => string.Equals(entry.Path, violation.Path, StringComparison.Ordinal)
           && string.Equals(entry.Member, violation.Member, StringComparison.Ordinal);
}

public sealed class TemporaryStorageScannerTests
{
    private static IReadOnlyList<TemporaryStorageViolation> Scan(string source) => TemporaryStorageScanner.Scan("Sample.cs", source);

    [Fact]
    public void Detects_qualified_and_simple_calls()
    {
        var violations = Scan("""
            using System.IO;
            class Sample
            {
                void Simple() => Path.GetTempPath();
                void Qualified() => System.IO.Path.GetTempFileName();
                void Global() => global::System.IO.Directory.CreateTempSubdirectory();
            }
            """);

        Assert.Equal(["Sample.Simple", "Sample.Qualified", "Sample.Global"], violations.Select(static violation => violation.Member));
        Assert.Equal([4, 5, 6], violations.Select(static violation => violation.Line));
    }

    [Fact]
    public void Detects_aliases_static_imports_and_environment_reads()
    {
        var violations = Scan("""
            using IOPath = System.IO.Path;
            using static System.IO.Path;
            class Sample
            {
                string Field = IOPath.GetTempPath();
                string Static() => GetTempPath();
                string Variable() => System.Environment.GetEnvironmentVariable("TMPDIR");
                string Other() => System.Environment.GetEnvironmentVariable("HOME");
            }
            """);

        Assert.Equal(["Sample.Field", "Sample.Static", "Sample.Variable"], violations.Select(static violation => violation.Member));
    }

    [Fact]
    public void Ignores_comments_literals_nameof_and_unrelated_methods()
    {
        var violations = Scan("""
            using System.IO;
            class Sample
            {
                // Path.GetTempPath() in a comment
                string Literal = "Path.GetTempPath()";
                string Name = nameof(Path.GetTempPath);
                string Unrelated(Helper helper) => helper.GetTempPath();
                class Helper { public string GetTempPath() => "x"; }
                string Own() => new Helper().GetTempPath();
            }
            """);

        Assert.Empty(violations);
    }

    [Fact]
    public void Static_import_of_another_type_does_not_count()
    {
        var violations = Scan("""
            using static System.Math;
            class Sample { string Local() { string GetTempPath() => "x"; return GetTempPath(); } }
            """);

        Assert.Empty(violations);
    }

    [Fact]
    public void Detects_namespace_aliases_and_global_static_usings_in_other_files()
    {
        var violations = TemporaryStorageScanner.ScanProject(
        [
            new ScannedSource("Usings.cs", "global using static System.IO.Path;"),
            new ScannedSource("Alias.cs", """
                using Files = System.IO;
                class Alias { string Run() => Files.Path.GetTempPath(); }
                """),
            new ScannedSource("Static.cs", "class Static { string Run() => GetTempFileName(); }")
        ], TemporaryStorageScanner.ImplicitUsings);

        Assert.Equal(["Alias.Run", "Static.Run"], violations.Select(static violation => violation.Member));
    }

    [Fact]
    public void Detects_calls_through_implicit_usings_and_constant_environment_names()
    {
        var violations = Scan("""
            class Sample
            {
                const string Name = "TMP";
                string Implicit() => Path.GetTempPath();
                string Constant() => System.Environment.GetEnvironmentVariable(Name);
            }
            """);

        Assert.Equal(["Sample.Implicit", "Sample.Constant"], violations.Select(static violation => violation.Member));
    }

    [Fact]
    public void A_local_method_named_like_the_API_is_not_reported_under_a_static_using()
    {
        var violations = Scan("""
            using static System.IO.Path;
            class Sample
            {
                string GetTempPath() => "local";
                string Run() => GetTempPath();
            }
            """);

        Assert.Empty(violations);
    }

    [Fact]
    public void Reports_the_containing_member_for_local_functions_and_constructors()
    {
        var violations = Scan("""
            class Sample
            {
                Sample() { System.IO.Path.GetTempPath(); }
                void Outer() { string Inner() => System.IO.Path.GetTempPath(); Inner(); }
            }
            """);

        Assert.Equal(["Sample..ctor", "Sample.Outer"], violations.Select(static violation => violation.Member));
    }
}
