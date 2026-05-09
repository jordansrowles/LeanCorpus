namespace Rowles.LeanLucene.Search.Scoring;

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
            Array.Sort(keys, docs, 0, n);
            if (descending) Array.Reverse(docs, 0, n);
            return docs;
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
            if (descending ? k > heapKeys[0] : k < heapKeys[0])
            {
                heapKeys[0] = k;
                heapDocs[0] = docs[i];
                SiftDown(heapKeys, heapDocs, 0, topN, descending);
            }
        }

        Array.Sort(heapKeys, heapDocs, 0, topN);
        if (descending) Array.Reverse(heapDocs, 0, topN);
        return heapDocs;
    }

    /// <summary>String-keyed variant of <see cref="SelectTopN(ScoreDoc[], double[], int, bool)"/>.</summary>
    public static ScoreDoc[] SelectTopN(ScoreDoc[] docs, string[] keys, int topN, bool descending)
    {
        int n = docs.Length;
        if (topN >= n)
        {
            Array.Sort(keys, docs, 0, n, StringComparer.Ordinal);
            if (descending) Array.Reverse(docs, 0, n);
            return docs;
        }

        var heapKeys = new string[topN];
        var heapDocs = new ScoreDoc[topN];
        Array.Copy(keys, heapKeys, topN);
        Array.Copy(docs, heapDocs, topN);
        BuildHeap(heapKeys, heapDocs, descending);

        for (int i = topN; i < n; i++)
        {
            var k = keys[i];
            int cmp = string.CompareOrdinal(k, heapKeys[0]);
            if (descending ? cmp > 0 : cmp < 0)
            {
                heapKeys[0] = k;
                heapDocs[0] = docs[i];
                SiftDown(heapKeys, heapDocs, 0, topN, descending);
            }
        }

        Array.Sort(heapKeys, heapDocs, 0, topN, StringComparer.Ordinal);
        if (descending) Array.Reverse(heapDocs, 0, topN);
        return heapDocs;
    }

    private static void BuildHeap(double[] keys, ScoreDoc[] docs, bool descending)
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
            if (left < size && IsWorse(keys[left], keys[worst], descending)) worst = left;
            if (right < size && IsWorse(keys[right], keys[worst], descending)) worst = right;
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
            if (left < size && IsWorse(keys[left], keys[worst], descending)) worst = left;
            if (right < size && IsWorse(keys[right], keys[worst], descending)) worst = right;
            if (worst == i) return;
            (keys[i], keys[worst]) = (keys[worst], keys[i]);
            (docs[i], docs[worst]) = (docs[worst], docs[i]);
            i = worst;
        }
    }

    // "Worse" = the candidate that should be evicted first.
    // Ascending top-N keeps small keys, so the worst element is the largest (max-heap root).
    // Descending top-N keeps large keys, so the worst element is the smallest (min-heap root).
    private static bool IsWorse(double a, double b, bool descending)
        => descending ? a < b : a > b;

    private static bool IsWorse(string a, string b, bool descending)
    {
        int cmp = string.CompareOrdinal(a, b);
        return descending ? cmp < 0 : cmp > 0;
    }
}
