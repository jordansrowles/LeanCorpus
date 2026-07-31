using System.Runtime.CompilerServices;

namespace Rowles.LeanCorpus.Analysis.Tokenisers.Japanese;

internal static class JapaneseViterbi
{
    private const int MaximumUnknownWordLength = 1024;

    [ThreadStatic]
    private static Scratch? _threadScratch;

    internal static void Tokenise(
        ReadOnlySpan<char> input,
        JapaneseDictionary dictionary,
        ISpanTokenSink sink)
    {
        var scratch = _threadScratch ??= new Scratch();
        scratch.Reset(input.Length + 1);
        int bos = scratch.AddNode(0, 0, 0, 0, 0, -1, false);
        scratch.Heads[0] = bos;

        var characterDefinition = dictionary.CharacterDefinition;
        for (int position = 0; position < input.Length; position++)
        {
            if (scratch.Heads[position] < 0)
                continue;

            bool anyKnown = AddKnownWords(input, position, dictionary, scratch);

            char first = input[position];
            bool supplementary = char.IsHighSurrogate(first)
                && position + 1 < input.Length
                && char.IsLowSurrogate(input[position + 1]);
            byte characterClass = supplementary
                ? CharacterDefinition.Default
                : characterDefinition.GetClass(first);
            bool invoke = !supplementary && characterDefinition.IsInvoke(first);

            if (!anyKnown || invoke)
            {
                int unknownLength = GetUnknownLength(
                    input,
                    position,
                    characterClass,
                    supplementary,
                    characterDefinition);
                int end = position + unknownLength;
                bool punctuation = IsPunctuation(input[position..end]);

                dictionary.GetUnknownRange(characterClass, out int start, out int count);
                for (int i = 0; i < count; i++)
                {
                    dictionary.GetUnknownEntry(start + i, out int contextId, out int wordCost);
                    AddCandidate(
                        scratch,
                        dictionary,
                        position,
                        end,
                        contextId,
                        wordCost,
                        punctuation);
                }
            }
        }

        int best = FindBestEndNode(scratch, dictionary, input.Length);
        if (best < 0)
            throw new InvalidDataException("Japanese dictionary produced no path through the input.");

        int countBack = 0;
        for (int node = best; scratch.Nodes[node].End > 0; node = scratch.Nodes[node].Previous)
        {
            scratch.EnsureBacktraceCapacity(countBack + 1);
            scratch.Backtrace[countBack++] = node;
        }

        for (int i = countBack - 1; i >= 0; i--)
        {
            ref readonly var node = ref scratch.Nodes[scratch.Backtrace[i]];
            if (!node.Punctuation)
                sink.Add(input[node.Start..node.End], node.Start, node.End, JapaneseTokeniser.JapaneseType);
        }
    }

    private static bool AddKnownWords(
        ReadOnlySpan<char> input,
        int start,
        JapaneseDictionary dictionary,
        Scratch scratch)
    {
        var cursor = dictionary.CreateKnownWordCursor();
        bool any = false;
        Span<byte> utf8 = stackalloc byte[4];

        for (int position = start; position < input.Length;)
        {
            int codePoint = CjkUnicode.DecodeCodePoint(input, position, out int charsConsumed);
            int byteCount = EncodeUtf8(codePoint, utf8);
            for (int i = 0; i < byteCount; i++)
            {
                if (!cursor.Move(utf8[i]))
                    return any;
            }

            position += charsConsumed;
            if (!cursor.TryGetOutput(out long rawSourceId))
                continue;
            if ((ulong)rawSourceId > int.MaxValue)
                throw new InvalidDataException("Japanese FST source id is too large.");

            dictionary.GetKnownRange((int)rawSourceId, out int entryStart, out int entryCount);
            bool punctuation = IsPunctuation(input[start..position]);
            for (int i = 0; i < entryCount; i++)
            {
                dictionary.GetKnownEntry(entryStart + i, out int contextId, out int wordCost);
                AddCandidate(
                    scratch,
                    dictionary,
                    start,
                    position,
                    contextId,
                    wordCost,
                    punctuation);
            }

            any = true;
        }

        return any;
    }

    private static void AddCandidate(
        Scratch scratch,
        JapaneseDictionary dictionary,
        int start,
        int end,
        int contextId,
        int wordCost,
        bool punctuation)
    {
        int bestPrevious = -1;
        long bestCost = long.MaxValue;
        for (int node = scratch.Heads[start]; node >= 0; node = scratch.Nodes[node].NextAtPosition)
        {
            ref readonly var previous = ref scratch.Nodes[node];
            long cost = (long)previous.Cost
                + dictionary.GetConnectionCost(previous.ContextId, contextId)
                + wordCost;
            if (cost < bestCost)
            {
                bestCost = cost;
                bestPrevious = node;
            }
        }

        if (bestPrevious < 0)
            return;

        int boundedCost = bestCost > int.MaxValue
            ? int.MaxValue
            : bestCost < int.MinValue
                ? int.MinValue
                : (int)bestCost;
        int index = scratch.AddNode(
            boundedCost,
            contextId,
            start,
            end,
            bestPrevious,
            scratch.Heads[end],
            punctuation);
        scratch.Heads[end] = index;
    }

    private static int FindBestEndNode(
        Scratch scratch,
        JapaneseDictionary dictionary,
        int end)
    {
        int best = -1;
        long bestCost = long.MaxValue;
        for (int node = scratch.Heads[end]; node >= 0; node = scratch.Nodes[node].NextAtPosition)
        {
            ref readonly var current = ref scratch.Nodes[node];
            long cost = (long)current.Cost + dictionary.GetConnectionCost(current.ContextId, 0);
            if (cost < bestCost)
            {
                bestCost = cost;
                best = node;
            }
        }
        return best;
    }

    private static int GetUnknownLength(
        ReadOnlySpan<char> input,
        int start,
        byte characterClass,
        bool supplementary,
        CharacterDefinition characterDefinition)
    {
        int firstLength = supplementary ? 2 : 1;
        if (supplementary || !characterDefinition.IsGroup(input[start]))
            return firstLength;

        bool punctuation = IsPunctuation(input.Slice(start, firstLength));
        int length = firstLength;
        while (start + length < input.Length && length < MaximumUnknownWordLength)
        {
            char next = input[start + length];
            if (char.IsSurrogate(next)
                || characterDefinition.GetClass(next) != characterClass
                || IsPunctuation(input.Slice(start + length, 1)) != punctuation)
            {
                break;
            }

            length++;
        }
        return length;
    }

    private static bool IsPunctuation(ReadOnlySpan<char> value)
    {
        foreach (char current in value)
        {
            if (char.IsWhiteSpace(current)
                || char.IsSeparator(current)
                || char.IsPunctuation(current))
            {
                return true;
            }
        }
        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int EncodeUtf8(int codePoint, Span<byte> destination)
    {
        if (codePoint <= 0x7F)
        {
            destination[0] = (byte)codePoint;
            return 1;
        }
        if (codePoint <= 0x7FF)
        {
            destination[0] = (byte)(0xC0 | (codePoint >> 6));
            destination[1] = (byte)(0x80 | (codePoint & 0x3F));
            return 2;
        }
        if (codePoint <= 0xFFFF)
        {
            destination[0] = (byte)(0xE0 | (codePoint >> 12));
            destination[1] = (byte)(0x80 | ((codePoint >> 6) & 0x3F));
            destination[2] = (byte)(0x80 | (codePoint & 0x3F));
            return 3;
        }

        destination[0] = (byte)(0xF0 | (codePoint >> 18));
        destination[1] = (byte)(0x80 | ((codePoint >> 12) & 0x3F));
        destination[2] = (byte)(0x80 | ((codePoint >> 6) & 0x3F));
        destination[3] = (byte)(0x80 | (codePoint & 0x3F));
        return 4;
    }

    private sealed class Scratch
    {
        internal int[] Heads = [];
        internal Node[] Nodes = [];
        internal int[] Backtrace = [];
        private int _nodeCount;

        internal void Reset(int positionCount)
        {
            if (Heads.Length < positionCount)
                Heads = new int[Grow(Heads.Length, positionCount)];
            Array.Fill(Heads, -1, 0, positionCount);
            _nodeCount = 0;
        }

        internal int AddNode(
            int cost,
            int contextId,
            int start,
            int end,
            int previous,
            int nextAtPosition,
            bool punctuation)
        {
            if (Nodes.Length == _nodeCount)
                Array.Resize(ref Nodes, Grow(Nodes.Length, _nodeCount + 1));

            int index = _nodeCount++;
            Nodes[index] = new Node(
                cost,
                contextId,
                start,
                end,
                previous,
                nextAtPosition,
                punctuation);
            return index;
        }

        internal void EnsureBacktraceCapacity(int required)
        {
            if (Backtrace.Length < required)
                Array.Resize(ref Backtrace, Grow(Backtrace.Length, required));
        }

        private static int Grow(int current, int required)
        {
            int size = Math.Max(current, 32);
            while (size < required)
                size = checked(size * 2);
            return size;
        }
    }

    private readonly record struct Node(
        int Cost,
        int ContextId,
        int Start,
        int End,
        int Previous,
        int NextAtPosition,
        bool Punctuation);
}
