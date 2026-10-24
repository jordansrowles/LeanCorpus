namespace Rowles.LeanCorpus.DevOps.Configuration;

internal enum TestRunnerCapability
{
    Trx,
    HangDump,
    CrashDump
}

internal sealed record TestRunnerCapabilityDescriptor(TestRunnerCapability Capability, string Name);

internal static class TestRunnerCapabilityCatalog
{
    private static readonly TestRunnerCapabilityDescriptor[] Descriptors =
    [
        new(TestRunnerCapability.Trx, "Trx"),
        new(TestRunnerCapability.HangDump, "HangDump"),
        new(TestRunnerCapability.CrashDump, "CrashDump")
    ];

    public static IReadOnlyList<TestRunnerCapabilityDescriptor> All => Descriptors;

    public static bool TryParse(string name, out TestRunnerCapability capability)
    {
        foreach (var descriptor in Descriptors)
        {
            if (string.Equals(descriptor.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                capability = descriptor.Capability;
                return true;
            }
        }

        capability = default;
        return false;
    }

    public static string GetName(TestRunnerCapability capability)
    {
        foreach (var descriptor in Descriptors)
        {
            if (descriptor.Capability == capability)
            {
                return descriptor.Name;
            }
        }

        throw new ArgumentOutOfRangeException(nameof(capability), capability, "Unknown test runner capability.");
    }

    public static bool Has(IReadOnlyList<string> capabilities, TestRunnerCapability capability)
    {
        ArgumentNullException.ThrowIfNull(capabilities);
        return capabilities.Any(value => TryParse(value, out var parsed) && parsed == capability);
    }
}
