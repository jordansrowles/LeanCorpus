namespace Rowles.LeanCorpus.DevOps.Documentation;

internal sealed record DocumentationOwnedOutput(string RelativeRoot);

internal static class DocumentationOwnedOutputCatalog
{
    private static readonly DocumentationOwnedOutput[] Outputs =
    [
        new("content"),
        new("api"),
        new("site"),
        new("coverage"),
        new("generated")
    ];

    public static IReadOnlyList<DocumentationOwnedOutput> All => Outputs;

    public static IReadOnlyList<string> EnumerateFiles(string documentationRoot, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentationRoot);
        var root = Path.GetFullPath(documentationRoot);
        var files = new List<(int OutputIndex, string RelativePath, string FullPath)>();
        for (var outputIndex = 0; outputIndex < Outputs.Length; outputIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var output = Outputs[outputIndex];
            var outputRoot = Path.GetFullPath(Path.Combine(root, output.RelativeRoot));
            if (!Directory.Exists(outputRoot))
            {
                continue;
            }

            RejectLinkedPath(outputRoot);
            CollectFiles(outputIndex, outputRoot, outputRoot, files, cancellationToken);
        }

        return files
            .OrderBy(static item => item.OutputIndex)
            .ThenBy(static item => item.RelativePath, StringComparer.Ordinal)
            .Select(static item => item.FullPath)
            .ToArray();
    }

    private static void CollectFiles(
        int outputIndex,
        string outputRoot,
        string directory,
        List<(int OutputIndex, string RelativePath, string FullPath)> files,
        CancellationToken cancellationToken)
    {
        foreach (var entry in Directory.EnumerateFileSystemEntries(directory, "*", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            RejectLinkedPath(entry);
            var attributes = File.GetAttributes(entry);
            if ((attributes & FileAttributes.Directory) != 0)
            {
                CollectFiles(outputIndex, outputRoot, entry, files, cancellationToken);
                continue;
            }

            var relativePath = Path.GetRelativePath(outputRoot, entry).Replace('\\', '/');
            files.Add((outputIndex, relativePath, entry));
        }
    }

    private static void RejectLinkedPath(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException($"Documentation output contains a linked path and cannot be hashed safely: {path}");
        }
    }
}
