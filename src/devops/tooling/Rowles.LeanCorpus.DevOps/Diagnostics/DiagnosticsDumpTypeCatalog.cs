namespace Rowles.LeanCorpus.DevOps.Diagnostics;

internal enum DiagnosticsDumpType
{
    Mini,
    Heap,
    Full,
    Triage
}

internal static class DiagnosticsDumpTypeCatalog
{
    private static readonly string[] OptionNames = ["Mini", "Heap", "Full", "Triage"];
    public const string DefaultOption = "Mini";
    public static string ExpectedNames => $"{string.Join(", ", OptionNames, 0, OptionNames.Length - 1)} or {OptionNames[^1]}";

    public static bool TryParse(string value, out DiagnosticsDumpType dumpType)
    {
        dumpType = value.Trim().ToLowerInvariant() switch
        {
            "mini" => DiagnosticsDumpType.Mini,
            "heap" => DiagnosticsDumpType.Heap,
            "full" => DiagnosticsDumpType.Full,
            "triage" => DiagnosticsDumpType.Triage,
            _ => (DiagnosticsDumpType)(-1)
        };
        return Enum.IsDefined(dumpType);
    }

    public static string GetOption(DiagnosticsDumpType dumpType) => dumpType switch
    {
        DiagnosticsDumpType.Mini => "Mini",
        DiagnosticsDumpType.Heap => "Heap",
        DiagnosticsDumpType.Full => "Full",
        DiagnosticsDumpType.Triage => "Triage",
        _ => throw new ArgumentOutOfRangeException(nameof(dumpType), dumpType, "Unknown diagnostics dump type.")
    };
}
