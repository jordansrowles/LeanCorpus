namespace Rowles.LeanCorpus.DevOps.Documentation;

internal enum DocumentationAction
{
    Prepare,
    Metadata,
    Build,
    Test,
    Serve
}

internal static class DocumentationActionCatalog
{
    private static readonly string[] ActionNames = ["prepare", "metadata", "build", "test", "serve"];
    public static IReadOnlyList<string> Names => ActionNames;
    public static string ExpectedNames => $"{string.Join(", ", ActionNames, 0, ActionNames.Length - 1)} or {ActionNames[^1]}";

    public static bool TryParse(string value, out DocumentationAction action)
    {
        action = value.Trim().ToLowerInvariant() switch
        {
            "prepare" => DocumentationAction.Prepare,
            "metadata" => DocumentationAction.Metadata,
            "build" => DocumentationAction.Build,
            "test" => DocumentationAction.Test,
            "serve" => DocumentationAction.Serve,
            _ => (DocumentationAction)(-1)
        };
        return Enum.IsDefined(action);
    }

    public static string GetName(DocumentationAction action) => action switch
    {
        DocumentationAction.Prepare => "prepare",
        DocumentationAction.Metadata => "metadata",
        DocumentationAction.Build => "build",
        DocumentationAction.Test => "test",
        DocumentationAction.Serve => "serve",
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, "Unknown documentation action.")
    };
}
