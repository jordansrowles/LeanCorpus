namespace Rowles.LeanCorpus.Analysis.Tokenisers;

/// <summary>
/// Path hierarchy tokeniser. Emits compound tokens from root to leaf
/// (or leaf to root in suffix mode).
/// </summary>
public sealed class PathTreeTokeniser : ISpanTokeniser
{
    public const string PathType = "path";
    public bool Lowercase { get; init; } = true;
    public bool EmitDepthPayloads { get; init; }
    public bool SuffixMode { get; init; }
    public bool SkipRoot { get; init; }

    public void Tokenise(ReadOnlySpan<char> input, ISpanTokenSink sink)
    {
        if (input.IsEmpty) return;
        int rootEnd = ConsumeRoot(input, out _);
        int offset = rootEnd;

        Span<int> ss = stackalloc int[64];
        Span<int> se = stackalloc int[64];
        int n = 0;
        while (offset < input.Length)
        {
            if (IsSep(input[offset])) { offset++; continue; }
            ss[n] = offset;
            while (offset < input.Length && !IsSep(input[offset])) offset++;
            se[n] = offset;
            n++;
        }
        if (n == 0) return;

        if (SuffixMode) EmitSuffix(input, ss, se, n, sink);
        else EmitForward(input, ss, se, n, sink);
    }

    void EmitForward(ReadOnlySpan<char> input, Span<int> ss, Span<int> se, int n, ISpanTokenSink sink)
    {
        int depth = -1;
        for (int i = 0; i < n; i++)
        {
            depth++;
            int e = se[i];
            Emit(input[..e], 0, e, depth, sink);
        }
    }

    void EmitSuffix(ReadOnlySpan<char> input, Span<int> ss, Span<int> se, int n, ISpanTokenSink sink)
    {
        int last = n - 1, depth = -1;
        for (int i = last; i >= 0; i--)
        {
            depth++;
            int s = i == 0 ? 0 : ss[i];
            int e = se[last];
            Emit(input[s..e], s, e, depth, sink);
        }
    }

    void Emit(ReadOnlySpan<char> text, int start, int end, int depth, ISpanTokenSink sink)
    {
        var payload = EmitDepthPayloads ? System.BitConverter.GetBytes(depth) : null;
        if (!Lowercase || !HasUpper(text)) { sink.Add(text, start, end, PathType, 1, payload); return; }

        char[]? rented = null;
        var lowered = text.Length <= 256 ? stackalloc char[text.Length]
            : (rented = System.Buffers.ArrayPool<char>.Shared.Rent(text.Length)).AsSpan(0, text.Length);
        for (int i = 0; i < text.Length; i++) { char c = text[i]; lowered[i] = c is >= 'A' and <= 'Z' ? (char)(c + 32) : c; }
        sink.Add(lowered[..text.Length], start, end, PathType, 1, payload);
        if (rented is not null) System.Buffers.ArrayPool<char>.Shared.Return(rented);
    }

    static bool HasUpper(ReadOnlySpan<char> t) { for (int i = 0; i < t.Length; i++) if (t[i] is >= 'A' and <= 'Z') return true; return false; }

    static int ConsumeRoot(ReadOnlySpan<char> input, out ReadOnlySpan<char> root)
    {
        // UNC: \\server\share
        if (input.Length >= 2 && IsSep(input[0]) && IsSep(input[1]))
        {
            int i = 2; while (i < input.Length && !IsSep(input[i])) i++; if (i < input.Length) i++;
            while (i < input.Length && !IsSep(input[i])) i++;
            root = input[..i]; return i;
        }
        // Scheme URI
        int col = input.IndexOf(':');
        if (col > 0 && col + 2 < input.Length && IsSep(input[col + 1]) && IsSep(input[col + 2]))
        {
            int i = col + 3;
            if (i < input.Length && IsSep(input[i])) { root = input[..i]; return i; }
            while (i < input.Length && !IsSep(input[i])) i++;
            root = input[..i]; return i;
        }
        // Drive letter
        if (input.Length >= 2 && IsLetter(input[0]) && input[1] == ':')
        {
            int i = 2; if (i < input.Length && IsSep(input[i])) i++;
            root = input[..i]; return i;
        }
        // Unix absolute
        if (IsSep(input[0])) { root = input[..1]; return 1; }
        // Relative
        root = default; return 0;
    }

    static bool IsLetter(char c) => c is >= 'A' and <= 'Z' or >= 'a' and <= 'z';
    static bool IsSep(char c) => c == '/' || c == '\\';
}
