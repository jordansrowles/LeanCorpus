using System.CommandLine;
using Rowles.LeanCorpus.Index;

namespace Rowles.LeanCorpus.Cli;

/// <summary>
/// Implements the LeanCorpus command-line tooling.
/// </summary>
public static partial class IndexCheckerCli
{
    /// <summary>
    /// Runs the command-line app.
    /// </summary>
    /// <param name="args">Command-line arguments.</param>
    /// <returns>Exit code: 0 for success, 1 for validation or compatibility failures, 2 for invalid arguments or CLI failures.</returns>
    public static int Run(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        return Run(args, Console.Out, Console.Error);
    }

    /// <summary>
    /// Runs the command-line app with redirected text writers.
    /// </summary>
    /// <param name="args">Command-line arguments.</param>
    /// <param name="output">Standard output writer.</param>
    /// <param name="error">Standard error writer.</param>
    /// <returns>Exit code: 0 for success, 1 for validation or compatibility failures, 2 for invalid arguments or CLI failures.</returns>
    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        var root = BuildRootCommand();
        var configuration = new InvocationConfiguration
        {
            Output = output,
            Error = error,
            EnableDefaultExceptionHandler = false
        };

        try
        {
            var parseResult = root.Parse(args);
            int exitCode = parseResult.Invoke(configuration);
            return parseResult.Errors.Count > 0 ? CliExitCodes.InvalidArguments : exitCode;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
        {
            error.WriteLine(ex.Message);
            return CliExitCodes.InvalidArguments;
        }
    }

    internal static CheckRequest CreateRequest(
        string indexPath,
        bool deep,
        bool json,
        bool postings,
        bool storedFields,
        bool docValues,
        bool vectors,
        bool hnsw,
        bool liveDocs,
        bool summaryOnly,
        bool failOnWarnings,
        string? outputPath)
    {
        if (string.IsNullOrWhiteSpace(indexPath))
            throw new ArgumentException("Missing index path.", nameof(indexPath));
        if (!Directory.Exists(indexPath))
            throw new ArgumentException($"Index path '{indexPath}' does not exist.", nameof(indexPath));

        var options = new IndexCheckOptions
        {
            Deep = deep,
            VerifyPostings = postings,
            VerifyStoredFields = storedFields,
            VerifyDocValues = docValues,
            VerifyVectors = vectors,
            VerifyHnsw = hnsw,
            VerifyLiveDocs = liveDocs
        };

        return new CheckRequest(
            Path.GetFullPath(indexPath),
            options,
            json,
            summaryOnly,
            failOnWarnings,
            string.IsNullOrWhiteSpace(outputPath) ? null : Path.GetFullPath(outputPath));
    }
}
