using System.CommandLine;
using Rowles.LeanCorpus.Cli.TestLab;

namespace Rowles.LeanCorpus.Cli;

internal static partial class WorkbenchCommands
{
    private static int RunFingerprint(string path, bool json, TextWriter output)
    {
        IndexFingerprintResult result = IndexFingerprinter.Calculate(path);
        if (json)
            WriteJson(output, result);
        else
        {
            output.WriteLine($"Index: {Path.GetFullPath(path)}");
            output.WriteLine($"Algorithm: {result.Algorithm}");
            output.WriteLine($"Logical: {result.LogicalFingerprint}");
            output.WriteLine($"Physical: {result.PhysicalFingerprint}");
            output.WriteLine($"Generation: {result.CommitGeneration?.ToString() ?? "unknown"}; documents: {result.LiveDocumentCount}/{result.DocumentCount}; segments: {result.SegmentCount}");
            output.WriteLine($"Recipe: {result.RecipeIdentity ?? "not present"}");
        }
        return CliExitCodes.Success;
    }

    private static int RunDiff(string before, string after, bool logicalOnly, bool json, TextWriter output)
    {
        IndexDiffResult result = IndexFingerprinter.Diff(before, after, logicalOnly);
        if (json)
            WriteJson(output, result);
        else
        {
            output.WriteLine($"Logical equivalence: {result.LogicalEquivalent}");
            if (!logicalOnly)
                output.WriteLine($"Physical equivalence: {result.PhysicalEquivalent}");
            output.WriteLine($"Documents: {result.Before.LiveDocumentCount}/{result.Before.DocumentCount} -> {result.After.LiveDocumentCount}/{result.After.DocumentCount}");
            output.WriteLine($"Segments: {result.Before.SegmentCount} -> {result.After.SegmentCount}");
            output.WriteLine($"Logical fingerprint: {result.Before.LogicalFingerprint} -> {result.After.LogicalFingerprint}");
            if (!logicalOnly)
                output.WriteLine($"Physical fingerprint: {result.Before.PhysicalFingerprint} -> {result.After.PhysicalFingerprint}");
        }
        return result.LogicalEquivalent && (logicalOnly || result.PhysicalEquivalent)
            ? CliExitCodes.Success
            : CliExitCodes.ValidationErrors;
    }
}
