namespace Rowles.LeanCorpus.Tests.Core.Codecs;

/// <summary>Deterministic vector data and an independent oracle for Float32 vector graph tests.</summary>
internal static class VectorGraphTestData
{
    internal sealed class ArrayVectorSource(float[][] vectors) : IVectorSource
    {
        public int Dimension { get; } = vectors.First(static v => v is not null).Length;
        public int Count => vectors.Length;
        public ReadOnlySpan<float> GetVector(int docId) => vectors[docId];
        public bool HasVector(int docId) => (uint)docId < (uint)vectors.Length && vectors[docId] is not null;
    }

    /// <summary>Gaussian clusters around random unit centres.</summary>
    internal static float[][] Clustered(int count, int dimension, int clusters, float spread, int seed)
    {
        var random = new Random(seed); // NOSONAR S2245: deterministic test data
        var centres = new float[clusters][];
        for (int c = 0; c < clusters; c++)
            centres[c] = Normalise(Gaussian(random, dimension, 1f));

        var vectors = new float[count][];
        for (int i = 0; i < count; i++)
        {
            float[] centre = centres[random.Next(clusters)];
            float[] noise = Gaussian(random, dimension, spread);
            var vector = new float[dimension];
            for (int d = 0; d < dimension; d++)
                vector[d] = centre[d] + noise[d];
            vectors[i] = vector;
        }
        return vectors;
    }

    internal static float[][] Uniform(int count, int dimension, int seed)
    {
        var random = new Random(seed); // NOSONAR S2245: deterministic test data
        var vectors = new float[count][];
        for (int i = 0; i < count; i++)
        {
            vectors[i] = new float[dimension];
            for (int d = 0; d < dimension; d++)
                vectors[i][d] = (float)(random.NextDouble() * 2 - 1);
        }
        return vectors;
    }

    internal static float[] Gaussian(Random random, int dimension, float scale)
    {
        var vector = new float[dimension];
        for (int d = 0; d < dimension; d++)
        {
            double u1 = 1.0 - random.NextDouble();
            double u2 = random.NextDouble();
            vector[d] = (float)(Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2) * scale);
        }
        return vector;
    }

    internal static float[] Normalise(float[] vector)
    {
        double norm = Math.Sqrt(vector.Sum(static v => (double)v * v));
        return norm == 0 ? (float[])vector.Clone() : vector.Select(v => (float)(v / norm)).ToArray();
    }

    internal static float[][] NormaliseAll(float[][] vectors)
        => vectors.Select(static v => v is null ? null! : Normalise(v)).ToArray();

    /// <summary>Double-precision cosine, independent of the production scorer.</summary>
    internal static double Cosine(float[] a, float[] b)
    {
        double dot = 0, na = 0, nb = 0;
        for (int i = 0; i < a.Length; i++)
        {
            dot += (double)a[i] * b[i];
            na += (double)a[i] * a[i];
            nb += (double)b[i] * b[i];
        }
        return na == 0 || nb == 0 ? 0 : dot / (Math.Sqrt(na) * Math.Sqrt(nb));
    }

    /// <summary>
    /// Tie-aware recall: a returned document counts when its exact cosine is within
    /// <paramref name="tolerance"/> of the Kth best eligible score. Duplicates earn no credit.
    /// </summary>
    internal static double TieAwareRecall(
        float[][] vectors, float[] query, IReadOnlyList<int> returned, int k, Func<int, bool>? eligible = null,
        double tolerance = 1e-6)
    {
        var scores = new List<double>();
        for (int docId = 0; docId < vectors.Length; docId++)
        {
            if (vectors[docId] is null || (eligible is not null && !eligible(docId)))
                continue;
            scores.Add(Cosine(query, vectors[docId]));
        }
        int expected = Math.Min(k, scores.Count);
        if (expected == 0)
            return 1.0;
        scores.Sort();
        scores.Reverse();
        double kth = scores[expected - 1];
        int credited = returned.Take(k).Distinct().Count(docId => Cosine(query, vectors[docId]) >= kth - tolerance);
        return Math.Min(credited, expected) / (double)expected;
    }

    internal static VectorGraph Build(float[][] vectors, HnswBuildConfig config, long seed, bool normalised)
    {
        var docIds = Enumerable.Range(0, vectors.Length).Where(i => vectors[i] is not null).ToArray();
        return VectorGraphBuilder.Build(new ArrayVectorSource(vectors), docIds, config, seed, normalised);
    }

    /// <summary>Returns every node reachable from the entry point at level zero.</summary>
    internal static int ReachableFromEntry(VectorGraph graph)
    {
        if (graph.NodeCount == 0)
            return 0;
        var seen = new bool[graph.NodeCount];
        var stack = new Stack<int>();
        seen[graph.EntryOrdinal] = true;
        stack.Push(graph.EntryOrdinal);
        int reached = 1;
        while (stack.Count > 0)
        {
            foreach (int neighbour in graph.Neighbours(stack.Pop(), 0).ToArray())
            {
                if (!seen[neighbour])
                {
                    seen[neighbour] = true;
                    reached++;
                    stack.Push(neighbour);
                }
            }
        }
        return reached;
    }
}
