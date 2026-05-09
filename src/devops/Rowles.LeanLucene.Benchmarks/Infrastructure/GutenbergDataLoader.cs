namespace Rowles.LeanLucene.Benchmarks;

/// <summary>
/// A single indexed paragraph from a Gutenberg ebook.
/// </summary>
internal sealed record BookParagraph(string Id, string Title, string Body);

/// <summary>
/// Loads Project Gutenberg plain-text ebooks from the bench data directory,
/// strips the standard Gutenberg header and footer, and splits the content
/// into indexable paragraphs.
/// </summary>
internal static class GutenbergDataLoader
{
    private static readonly Dictionary<string, string> BookTitles = new(StringComparer.OrdinalIgnoreCase)
    {
        ["11"]   = "Alice's Adventures in Wonderland",
        ["84"]   = "Frankenstein",
        ["98"]   = "A Tale of Two Cities",
        ["158"]  = "Emma",
        ["174"]  = "The Picture of Dorian Gray",
        ["345"]  = "Dracula",
        ["1260"] = "Jane Eyre",
        ["1342"] = "Pride and Prejudice",
        ["1661"] = "The Adventures of Sherlock Holmes",
        ["2701"] = "Moby Dick"
    };

    private const int MinParagraphLength = 50;

    /// <summary>
    /// Loads all ebooks from the standard bench data directory.
    /// </summary>
    /// <returns>All paragraphs across all books, in file order.</returns>
    /// <exception cref="DirectoryNotFoundException">
    /// Thrown when the Gutenberg ebook directory cannot be located.
    /// </exception>
    public static BookParagraph[] Load()
    {
        var dataDir = FindDataDirectory();
        return Load(dataDir);
    }

    /// <summary>
    /// Loads all ebooks from the specified directory.
    /// </summary>
    public static BookParagraph[] Load(string dataDirectory)
    {
        var files = Directory.GetFiles(dataDirectory, "*.txt", SearchOption.TopDirectoryOnly);
        if (files.Length == 0)
            throw new FileNotFoundException($"No .txt files found in '{dataDirectory}'.");

        var paragraphs = new List<BookParagraph>(capacity: 20_000);

        foreach (var file in files.OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            var bookId = Path.GetFileNameWithoutExtension(file);
            var title = BookTitles.TryGetValue(bookId, out var t) ? t : bookId;
            var content = StripGutenbergWrappers(File.ReadAllText(file));

            var paras = SplitParagraphs(content);
            for (int i = 0; i < paras.Count; i++)
                paragraphs.Add(new BookParagraph($"{bookId}-{i}", title, paras[i]));
        }

        return [.. paragraphs];
    }

    /// <summary>
    /// Returns the raw text of each book as a single string.
    /// Used by analysis benchmarks where paragraph splitting is not needed.
    /// </summary>
    public static (string Title, string Text)[] LoadBookTexts()
    {
        var dataDir = FindDataDirectory();
        var files = Directory.GetFiles(dataDir, "*.txt", SearchOption.TopDirectoryOnly);

        return files
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .Select(file =>
            {
                var bookId = Path.GetFileNameWithoutExtension(file);
                var title = BookTitles.TryGetValue(bookId, out var t) ? t : bookId;
                var text = StripGutenbergWrappers(File.ReadAllText(file));
                return (title, text);
            })
            .ToArray();
    }

    private static string FindDataDirectory()
    {
        var envPath = Environment.GetEnvironmentVariable("GUTENBERG_DATA_PATH");
        if (!string.IsNullOrWhiteSpace(envPath) && Directory.Exists(envPath))
            return envPath;

        var root = FindRepositoryRoot();
        var candidate = Path.Combine(root, "bench", "data", "gutenberg-ebooks");
        if (Directory.Exists(candidate))
            return candidate;

        throw new DirectoryNotFoundException(
            $"Gutenberg ebook directory not found. Expected: '{candidate}'. " +
            "Set GUTENBERG_DATA_PATH environment variable to override.");
    }

    internal static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Rowles.LeanLucene.slnx")))
                return current.FullName;
            current = current.Parent;
        }

        return Directory.GetCurrentDirectory();
    }

    private static string StripGutenbergWrappers(string text)
    {
        const string startMarker = "*** START OF THE PROJECT GUTENBERG";
        const string endMarker   = "*** END OF THE PROJECT GUTENBERG";

        var startIdx = text.IndexOf(startMarker, StringComparison.OrdinalIgnoreCase);
        if (startIdx >= 0)
        {
            var lineEnd = text.IndexOf('\n', startIdx);
            if (lineEnd >= 0)
                text = text[(lineEnd + 1)..];
        }

        var endIdx = text.IndexOf(endMarker, StringComparison.OrdinalIgnoreCase);
        if (endIdx >= 0)
            text = text[..endIdx];

        return text;
    }

    private static List<string> SplitParagraphs(string text)
    {
        var separators = new[] { "\r\n\r\n", "\n\n" };
        var parts = text.Split(separators, StringSplitOptions.RemoveEmptyEntries);

        var result = new List<string>(parts.Length);
        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            if (trimmed.Length >= MinParagraphLength)
                result.Add(trimmed);
        }

        return result;
    }
}
