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
        new("src/devops/tooling/Rowles.LeanCorpus.DevOps.Tests/Bootstrap/ManagedBootstrapIntegrationTests.cs", "*", Category.B,
            "Builds, links and promotes fake checkouts and worktrees; inside this checkout they inherit its Directory.Build.props and Git state."),
        new("src/devops/tooling/Rowles.LeanCorpus.DevOps.Tests/Repository/RepositoryPathsTests.cs", "*", Category.B,
            "Tests checkout discovery from directories that must not be inside any checkout."),
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
        var violations = new List<TemporaryStorageViolation>();
        foreach (string scannedRoot in ScannedRoots)
        {
            string root = RepositoryPaths.FromRoot(scannedRoot.Split('/'));
            foreach (string file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
            {
                string relative = Path.GetRelativePath(RepositoryPaths.Root, file).Replace('\\', '/');
                if (relative.Split('/').Any(static segment => segment is "bin" or "obj"))
                    continue;
                violations.AddRange(TemporaryStorageScanner.Scan(relative, File.ReadAllText(file)));
            }
        }

        return violations.OrderBy(static violation => violation.Path, StringComparer.Ordinal).ThenBy(static violation => violation.Line).ToArray();
    }

    /// <summary>A member of <c>*</c> covers every violation in the file; use it only when the whole file is the exception.</summary>
    private static bool Matches(RegisteredException entry, TemporaryStorageViolation violation)
        => string.Equals(entry.Path, violation.Path, StringComparison.Ordinal)
           && (entry.Member == "*" || string.Equals(entry.Member, violation.Member, StringComparison.Ordinal));
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
