namespace Rowles.LeanCorpus.DevOps.Testing;

internal sealed record TestExecutionOutcomeReportDescriptor(TestExecutionOutcome Outcome, string Label);

internal static class TestExecutionOutcomeReportCatalog
{
    private static readonly TestExecutionOutcomeReportDescriptor[] Descriptors =
    [
        new(TestExecutionOutcome.Passed, "Passed"),
        new(TestExecutionOutcome.AssertionFailure, "Failed"),
        new(TestExecutionOutcome.Crashed, "Crashed"),
        new(TestExecutionOutcome.Hung, "Hung"),
        new(TestExecutionOutcome.Cancelled, "Cancelled"),
        new(TestExecutionOutcome.DiscoveryFailure, "Incomplete"),
        new(TestExecutionOutcome.Incomplete, "Incomplete"),
        new(TestExecutionOutcome.InfrastructureFailure, "InfrastructureError")
    ];

    private static readonly string[] CountLabels = Descriptors
        .Select(static descriptor => descriptor.Label)
        .Distinct(StringComparer.Ordinal)
        .ToArray();

    public static IReadOnlyList<TestExecutionOutcomeReportDescriptor> All => Descriptors;
    public static IReadOnlyList<string> CountedLabels => CountLabels;

    public static string GetLabel(TestExecutionOutcome outcome)
    {
        foreach (var descriptor in Descriptors)
        {
            if (descriptor.Outcome == outcome)
            {
                return descriptor.Label;
            }
        }

        return "InfrastructureError";
    }
}
