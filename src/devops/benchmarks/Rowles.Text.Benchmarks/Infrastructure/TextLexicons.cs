using System.Security.Cryptography;

namespace Rowles.LeanCorpus.Benchmarks;

/// <summary>Finds repository lexicons and records their identity in benchmark evidence.</summary>
internal static class TextLexicons
{
    /// <summary>Returns <c>lexicons/&lt;relative&gt;</c> under the repository root, or fails setup when it is absent.</summary>
    public static string Require(string relativePath)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            string candidate = Path.Combine(directory.FullName, "lexicons", relativePath);
            if (File.Exists(candidate))
                return candidate;
            if (File.Exists(Path.Combine(directory.FullName, ".config", "devops", "test-suites.json")))
                throw new InvalidOperationException($"Lexicon 'lexicons/{relativePath}' is missing from the repository at '{directory.FullName}'.");
        }

        throw new InvalidOperationException($"Lexicon 'lexicons/{relativePath}' was not found above '{AppContext.BaseDirectory}'.");
    }

    /// <summary>Lower-case hexadecimal SHA-256 of a file.</summary>
    public static string Sha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    /// <summary>Requires a lexicon and logs its path and hash as benchmark evidence.</summary>
    public static string RequireAndRecord(string name, string relativePath)
    {
        string path = Require(relativePath);
        Console.WriteLine($"Benchmark lexicon {name}: path=lexicons/{relativePath}, sha256={Sha256(path)}");
        return path;
    }
}
