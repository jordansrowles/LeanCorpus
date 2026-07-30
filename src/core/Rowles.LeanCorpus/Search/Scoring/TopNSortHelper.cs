namespace Rowles.LeanCorpus.Search.Scoring;

/// <summary>
/// Heap-based partial sort that returns the top-N elements of a parallel
/// (key, doc) array without materialising a full sort over the input.
/// Complexity: O(n log topN) instead of O(n log n).
/// </summary>
internal static class TopNSortHelper
{
    /// <summary>Selects the top-N <see cref="ScoreDoc"/>s ranked by <paramref name="keys"/>.</summary>
    /// <param name="docs">Parallel array of documents, one per key.</param>
    /// <param name="keys">Sort keys, one per document.</param>
    /// <param name="topN">Maximum number of documents to return.</param>
    /// <param name="descending">If <c>true</c>, the largest keys win; otherwise the smallest keys win.</param>
    /// <returns>Up to <paramref name="topN"/> documents in sorted order.</returns>
    public static ScoreDoc[] SelectTopN(ScoreDoc[] docs, double[] keys, int topN, bool descending)
    {
        int n = docs.Length;
        if (topN >= n)
        {
            return Order(keys, docs, descending);
        }

        // Build a heap whose root is the "worst" of the topN seen so far,
        // so an incoming element only displaces it when strictly better.
        // Ascending top-N keeps the smallest keys, so the heap is a max-heap
        // (root is the largest, which gets evicted by smaller incoming keys).
        // Descending top-N keeps the largest keys, so the heap is a min-heap.
        var heapKeys = new double[topN];
        var heapDocs = new ScoreDoc[topN];
        Array.Copy(keys, heapKeys, topN);
        Array.Copy(docs, heapDocs, topN);
        BuildHeap(heapKeys, heapDocs, descending);

        for (int i = topN; i < n; i++)
        {
            double k = keys[i];
            // Beats root => replace root and sift down.
            if (IsBetter(k, docs[i], heapKeys[0], heapDocs[0], descending))
            {
                heapKeys[0] = k;
                heapDocs[0] = docs[i];
                SiftDown(heapKeys, heapDocs, 0, topN, descending);
            }
        }

        return Order(heapKeys, heapDocs, descending);
    }

    /// <summary>64-bit integer-keyed variant of <see cref="SelectTopN(ScoreDoc[], double[], int, bool)"/>.</summary>
    public static ScoreDoc[] SelectTopN(ScoreDoc[] docs, long[] keys, int topN, bool descending)
    {
        int n = docs.Length;
        if (topN >= n)
        {
            return Order(keys, docs, descending);
        }

        var heapKeys = new long[topN];
        var heapDocs = new ScoreDoc[topN];
        Array.Copy(keys, heapKeys, topN);
        Array.Copy(docs, heapDocs, topN);
        BuildHeap(heapKeys, heapDocs, descending);

        for (int i = topN; i < n; i++)
        {
            long k = keys[i];
            if (IsBetter(k, docs[i], heapKeys[0], heapDocs[0], descending))
            {
                heapKeys[0] = k;
                heapDocs[0] = docs[i];
                SiftDown(heapKeys, heapDocs, 0, topN, descending);
            }
        }

        return Order(heapKeys, heapDocs, descending);
    }

    /// <summary>String-keyed variant of <see cref="SelectTopN(ScoreDoc[], double[], int, bool)"/>.</summary>
    public static ScoreDoc[] SelectTopN(ScoreDoc[] docs, string[] keys, int topN, bool descending)
    {
        int n = docs.Length;
        if (topN >= n)
        {
            return Order(keys, docs, descending);
        }

        var heapKeys = new string[topN];
        var heapDocs = new ScoreDoc[topN];
        Array.Copy(keys, heapKeys, topN);
        Array.Copy(docs, heapDocs, topN);
        BuildHeap(heapKeys, heapDocs, descending);

        for (int i = topN; i < n; i++)
        {
            var k = keys[i];
            if (IsBetter(k, docs[i], heapKeys[0], heapDocs[0], descending))
            {
                heapKeys[0] = k;
                heapDocs[0] = docs[i];
                SiftDown(heapKeys, heapDocs, 0, topN, descending);
            }
        }

        return Order(heapKeys, heapDocs, descending);
    }

    private static void BuildHeap(double[] keys, ScoreDoc[] docs, bool descending)
    {
        for (int i = keys.Length / 2 - 1; i >= 0; i--)
            SiftDown(keys, docs, i, keys.Length, descending);
    }

    private static void BuildHeap(long[] keys, ScoreDoc[] docs, bool descending)
    {
        for (int i = keys.Length / 2 - 1; i >= 0; i--)
            SiftDown(keys, docs, i, keys.Length, descending);
    }

    private static void BuildHeap(string[] keys, ScoreDoc[] docs, bool descending)
    {
        for (int i = keys.Length / 2 - 1; i >= 0; i--)
            SiftDown(keys, docs, i, keys.Length, descending);
    }

    private static void SiftDown(double[] keys, ScoreDoc[] docs, int i, int size, bool descending)
    {
        while (true)
        {
            int worst = i;
            int left = 2 * i + 1;
            int right = 2 * i + 2;
            if (left < size && IsWorse(keys[left], docs[left], keys[worst], docs[worst], descending)) worst = left;
            if (right < size && IsWorse(keys[right], docs[right], keys[worst], docs[worst], descending)) worst = right;
            if (worst == i) return;
            (keys[i], keys[worst]) = (keys[worst], keys[i]);
            (docs[i], docs[worst]) = (docs[worst], docs[i]);
            i = worst;
        }
    }

    private static void SiftDown(long[] keys, ScoreDoc[] docs, int i, int size, bool descending)
    {
        while (true)
        {
            int worst = i;
            int left = 2 * i + 1;
            int right = 2 * i + 2;
            if (left < size && IsWorse(keys[left], docs[left], keys[worst], docs[worst], descending)) worst = left;
            if (right < size && IsWorse(keys[right], docs[right], keys[worst], docs[worst], descending)) worst = right;
            if (worst == i) return;
            (keys[i], keys[worst]) = (keys[worst], keys[i]);
            (docs[i], docs[worst]) = (docs[worst], docs[i]);
            i = worst;
        }
    }

    private static void SiftDown(string[] keys, ScoreDoc[] docs, int i, int size, bool descending)
    {
        while (true)
        {
            int worst = i;
            int left = 2 * i + 1;
            int right = 2 * i + 2;
            if (left < size && IsWorse(keys[left], docs[left], keys[worst], docs[worst], descending)) worst = left;
            if (right < size && IsWorse(keys[right], docs[right], keys[worst], docs[worst], descending)) worst = right;
            if (worst == i) return;
            (keys[i], keys[worst]) = (keys[worst], keys[i]);
            (docs[i], docs[worst]) = (docs[worst], docs[i]);
            i = worst;
        }
    }

    // "Worse" = the candidate that should be evicted first.
    // Ascending top-N keeps small keys, so the worst element is the largest (max-heap root).
    // Descending top-N keeps large keys, so the worst element is the smallest (min-heap root).
    private static bool IsBetter(double a, ScoreDoc aDoc, double b, ScoreDoc bDoc, bool descending) => Compare(a, aDoc, b, bDoc, descending) < 0;
    private static bool IsBetter(long a, ScoreDoc aDoc, long b, ScoreDoc bDoc, bool descending) => Compare(a, aDoc, b, bDoc, descending) < 0;
    private static bool IsBetter(string a, ScoreDoc aDoc, string b, ScoreDoc bDoc, bool descending) => Compare(a, aDoc, b, bDoc, descending) < 0;
    private static bool IsWorse(double a, ScoreDoc aDoc, double b, ScoreDoc bDoc, bool descending) => Compare(a, aDoc, b, bDoc, descending) > 0;
    private static bool IsWorse(long a, ScoreDoc aDoc, long b, ScoreDoc bDoc, bool descending) => Compare(a, aDoc, b, bDoc, descending) > 0;
    private static bool IsWorse(string a, ScoreDoc aDoc, string b, ScoreDoc bDoc, bool descending) => Compare(a, aDoc, b, bDoc, descending) > 0;
    private static int Compare(double a, ScoreDoc aDoc, double b, ScoreDoc bDoc, bool descending) { int value = a.CompareTo(b); if (descending) value = -value; return value != 0 ? value : aDoc.DocId.CompareTo(bDoc.DocId); }
    private static int Compare(long a, ScoreDoc aDoc, long b, ScoreDoc bDoc, bool descending) { int value = a.CompareTo(b); if (descending) value = -value; return value != 0 ? value : aDoc.DocId.CompareTo(bDoc.DocId); }
    private static int Compare(string a, ScoreDoc aDoc, string b, ScoreDoc bDoc, bool descending) { int value = string.CompareOrdinal(a, b); if (descending) value = -value; return value != 0 ? value : aDoc.DocId.CompareTo(bDoc.DocId); }

    private static ScoreDoc[] Order(double[] keys, ScoreDoc[] docs, bool descending)
    { var entries = new DoubleEntry[docs.Length]; for (int i = 0; i < docs.Length; i++) entries[i] = new DoubleEntry(keys[i], docs[i]); Array.Sort(entries, (a, b) => Compare(a.Key, a.Document, b.Key, b.Document, descending)); for (int i = 0; i < docs.Length; i++) docs[i] = entries[i].Document; return docs; }
    private static ScoreDoc[] Order(long[] keys, ScoreDoc[] docs, bool descending)
    { var entries = new Int64Entry[docs.Length]; for (int i = 0; i < docs.Length; i++) entries[i] = new Int64Entry(keys[i], docs[i]); Array.Sort(entries, (a, b) => Compare(a.Key, a.Document, b.Key, b.Document, descending)); for (int i = 0; i < docs.Length; i++) docs[i] = entries[i].Document; return docs; }
    private static ScoreDoc[] Order(string[] keys, ScoreDoc[] docs, bool descending)
    { var entries = new StringEntry[docs.Length]; for (int i = 0; i < docs.Length; i++) entries[i] = new StringEntry(keys[i], docs[i]); Array.Sort(entries, (a, b) => Compare(a.Key, a.Document, b.Key, b.Document, descending)); for (int i = 0; i < docs.Length; i++) docs[i] = entries[i].Document; return docs; }

    private readonly record struct DoubleEntry(double Key, ScoreDoc Document);
    private readonly record struct Int64Entry(long Key, ScoreDoc Document);
    private readonly record struct StringEntry(string Key, ScoreDoc Document);
}
