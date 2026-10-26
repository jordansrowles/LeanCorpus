using System.Text.RegularExpressions;
using Rowles.LeanCorpus.DevOps.Repository;

namespace Rowles.LeanCorpus.DevOps.Tests.Commands;

public sealed class ManagedCliWorkflowContractTests
{
    private static readonly HashSet<string> TestOptions = new(StringComparer.Ordinal)
    {
        "--ci", "--framework", "--hang-timeout", "--count", "--profile", "--diagnostics",
        "--area", "--category", "--filter", "--fail-fast", "--timeout", "--rid", "--runtime-identifier"
    };

    private static readonly HashSet<string> AotOptions = new(StringComparer.Ordinal)
    {
        "--ci", "--framework", "--rid", "--runtime-identifier"
    };

    [Fact]
    public void ActiveCiTestAndAotInvocationsUseRegisteredManagedOptions()
    {
        var checkout = RepositoryPaths.Resolve(null, Directory.GetCurrentDirectory(), AppContext.BaseDirectory);
        string[] relativeFiles =
        [
            ".github/workflows/build.yml",
            ".github/workflows/test-stress.yml",
            ".github/workflows/test-diagnostics.yml"
        ];
        var invocations = relativeFiles
            .SelectMany(relativePath => ExtractInvocations(checkout.RepositoryRoot, relativePath))
            .ToArray();

        Assert.NotEmpty(invocations);
        Assert.Contains(invocations, invocation => invocation.Command == "test");
        Assert.Contains(invocations, invocation => invocation.Command == "aot");

        foreach (var invocation in invocations)
        {
            var allowedOptions = invocation.Command == "test" ? TestOptions : AotOptions;
            string[] invalidOptions = ExtractOptions(invocation.Arguments)
                .Where(option => !allowedOptions.Contains(option))
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            Assert.True(invalidOptions.Length == 0,
                $"{invocation.Source} invokes managed '{invocation.Command}' with unsupported option(s): {string.Join(", ", invalidOptions)}.");
        }

        Assert.Contains(invocations, invocation => invocation.Source.EndsWith("build.yml", StringComparison.Ordinal) &&
            invocation.Command == "test" && ExtractOptions(invocation.Arguments).Contains("--hang-timeout", StringComparer.Ordinal));
        Assert.Contains(invocations, invocation => invocation.Source.EndsWith("build.yml", StringComparison.Ordinal) &&
            invocation.Command == "aot" && ExtractOptions(invocation.Arguments).Contains("--rid", StringComparer.Ordinal));
        Assert.Contains(invocations, invocation => invocation.Source.EndsWith("test-stress.yml", StringComparison.Ordinal) &&
            ExtractOptions(invocation.Arguments).Contains("--hang-timeout", StringComparer.Ordinal));
        Assert.Contains(invocations, invocation => invocation.Source.EndsWith("test-diagnostics.yml", StringComparison.Ordinal) &&
            ExtractOptions(invocation.Arguments).Contains("--filter", StringComparer.Ordinal));
    }

    [Fact]
    public void RootDispatcherReservesBootstrapAndForwardsOtherArgumentsExactly()
    {
        var checkout = RepositoryPaths.Resolve(null, Directory.GetCurrentDirectory(), AppContext.BaseDirectory);
        var script = File.ReadAllText(Path.Combine(checkout.RepositoryRoot, "devops.ps1"));

        Assert.Contains("bootstrap", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("@($args)", script, StringComparison.Ordinal);
        Assert.Contains("& $managedHost @forwardedArguments", script, StringComparison.Ordinal);
        Assert.Contains("exit $LASTEXITCODE", script, StringComparison.Ordinal);
        Assert.DoesNotContain("setup.ps1", script, StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<ManagedInvocation> ExtractInvocations(string repositoryRoot, string relativePath)
    {
        string path = Path.Combine(repositoryRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        string content = File.ReadAllText(path);

        foreach (Match match in Regex.Matches(content,
            @"(?m)^\s*(?:run:\s*)?(?:&\s*\$managedHost\s+|\./devops(?:\.exe)?\s+)(?<command>test|aot)\b(?<arguments>[^\r\n]*)",
            RegexOptions.CultureInvariant))
        {
            yield return new ManagedInvocation(relativePath, match.Groups["command"].Value, match.Value);
        }

        foreach (Match match in Regex.Matches(content,
            @"(?m)^\s*&\s*\$powerShell\.Source\s+-NoProfile\s+-File\s+\$dispatcher\s+(?<command>test|aot)\b[^\r\n]*",
            RegexOptions.CultureInvariant))
        {
            yield return new ManagedInvocation(relativePath, match.Groups["command"].Value, match.Value);
        }

        foreach (Match match in Regex.Matches(content,
            @"(?s)\$testArgs\s*=\s*@\((?<arguments>.*?)&\s*\$managedHost\s+@testArgs",
            RegexOptions.CultureInvariant))
        {
            string arguments = match.Groups["arguments"].Value;
            if (Regex.IsMatch(arguments, @"'test'|""test""", RegexOptions.CultureInvariant))
                yield return new ManagedInvocation(relativePath, "test", arguments);
        }
    }

    private static string[] ExtractOptions(string arguments)
        => Regex.Matches(arguments, @"(?<![A-Za-z0-9_])--(?<name>[A-Za-z][A-Za-z0-9-]*)", RegexOptions.CultureInvariant)
            .Select(match => "--" + match.Groups["name"].Value)
            .ToArray();

    private sealed record ManagedInvocation(string Source, string Command, string Arguments);
}
