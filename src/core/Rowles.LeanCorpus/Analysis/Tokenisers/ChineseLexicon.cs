using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Analysis.Tokenisers;

/// <summary>
/// Provides the default Chinese lexicon for the <see cref="ChineseLexiconTokeniser"/>.
/// Searches parent directories for <c>lexicons/chinese-dict.txt</c>.
/// If the file is not present, the factory falls back to a minimal built-in list.
/// </summary>
public static class ChineseLexicon
{
    /// <summary>
    /// Gets the default Chinese word list, loaded from the optional lexicon file.
    /// Falls back to a minimal built-in word list if the file is absent.
    /// </summary>
    public static IReadOnlyList<string> Default => _default.Value;

    private static readonly Lazy<IReadOnlyList<string>> _default = new(LoadDefault);

    private static IReadOnlyList<string> LoadDefault()
    {
        string? path = FindDefaultPath();
        if (path is not null)
        {
            var words = new List<string>();
            foreach (var line in FileOpenRetry.ReadLines(path, System.Text.Encoding.UTF8))
            {
                var trimmed = line.Trim();
                if (trimmed.Length > 0 && !trimmed.StartsWith('#'))
                    words.Add(trimmed);
            }

            if (words.Count > 0)
                return words;
        }

        // Minimal fallback for applications that do not install the optional lexicon.
        return new[]
        {
            "的", "一", "是", "在", "不", "了", "有", "和", "人", "这",
            "中", "大", "为", "上", "个", "国", "我", "以", "要", "他",
            "我们", "中国", "他们", "自己", "可以", "没有", "什么", "如果",
            "因为", "所以", "但是", "已经", "就是", "这个", "那个", "还是",
            "中华", "人民", "共和国", "华人", "世界"
        };
    }

    private static string? FindDefaultPath()
    {
        string? current = AppContext.BaseDirectory;
        while (current is not null)
        {
            string candidate = Path.Combine(current, "lexicons", "chinese-dict.txt");
            if (File.Exists(candidate))
                return candidate;
            current = Path.GetDirectoryName(current);
        }

        return null;
    }
}
