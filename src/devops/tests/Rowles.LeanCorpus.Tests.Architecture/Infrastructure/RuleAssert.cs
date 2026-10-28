using Xunit.Sdk;

namespace Rowles.LeanCorpus.Tests.Architecture.Infrastructure;

internal static class RuleAssert
{
    internal static void Empty(string rule, IEnumerable<string> failingTypes)
    {
        string message = FormatFailures(rule, failingTypes);
        if (message.Length > 0)
            throw new XunitException(message);
    }

    internal static string FormatFailures(string rule, IEnumerable<string> failingTypes)
    {
        var failures = failingTypes
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        return failures.Length == 0
            ? string.Empty
            : $"{rule}{Environment.NewLine}{string.Join(Environment.NewLine, failures.Select(static name => $"  {name}"))}";
    }
}
