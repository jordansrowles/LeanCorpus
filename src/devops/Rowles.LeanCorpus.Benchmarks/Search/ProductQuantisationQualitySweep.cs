using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Rowles.LeanCorpus.Codecs.Vectors;
using Rowles.LeanCorpus.Search.Queries;

namespace Rowles.LeanCorpus.Benchmarks;

/// <summary>
/// Runs product-quantisation quality experiments without HNSW or BenchmarkDotNet.
/// This is a tuning tool, not a latency benchmark.
/// </summary>
internal static class ProductQuantisationQualitySweep
{
    private const int QueryCount = 16;
    private const int TopK = 10;
    private const int MaximumCandidateWindow = 400;
    private const int BaseTrainingSampleSize = 2_048;
    private const int LargeTrainingSampleSize = 8_192;
    private static readonly int[] Dimensions = [64, 128];
    private static readonly int[] SubspaceWidths = [8, 4, 2, 1];
    private static readonly int[] CandidateWindows = [10, 40, 100, 400];

    internal static int Run(string runDirectory, int documentCount, bool confirmationOnly = false)
    {
        if (documentCount < QuantisedVectorWriter.DefaultProductCentroidCount)
        {
            Console.Error.WriteLine(
                $"PQ quality requires at least {QuantisedVectorWriter.DefaultProductCentroidCount} documents.");
            return 1;
        }

        var results = new List<ProductQuantisationQualityResult>();
        foreach (int dimension in Dimensions)
        {
            Console.WriteLine();
            Console.WriteLine($"PQ quality sweep: {documentCount:N0} vectors, {dimension} dimensions");
            var corpus = CreateCorpus(documentCount, dimension);
            var exactTopDocs = CreateExactTopDocs(corpus.OriginalVectors, dimension);
            int baseSampleSize = Math.Min(BaseTrainingSampleSize, documentCount);

            var dimensionResults = new List<ProductQuantisationQualityResult>();
            ReadOnlySpan<int> widths = confirmationOnly
                ? [4, QuantisedVectorWriter.DefaultProductSubspaceDimensions]
                : SubspaceWidths;
            foreach (int subspaceWidth in widths)
            {
                ProductQuantisationQualityResult result = Evaluate(
                    runDirectory,
                    corpus,
                    exactTopDocs,
                    dimension,
                    subspaceWidth,
                    baseSampleSize);
                dimensionResults.Add(result);
                results.Add(result);
                PrintResult(result);
            }

            ProductQuantisationQualityResult best = dimensionResults
                .OrderByDescending(result => result.RecallAt10)
                .ThenByDescending(result => result.SubspaceDimensions)
                .First();
            int largeSampleSize = Math.Min(LargeTrainingSampleSize, documentCount);
            if (!confirmationOnly && largeSampleSize > baseSampleSize)
            {
                ProductQuantisationQualityResult result = Evaluate(
                    runDirectory,
                    corpus,
                    exactTopDocs,
                    dimension,
                    best.SubspaceDimensions,
                    largeSampleSize);
                results.Add(result);
                PrintResult(result);
            }
        }

        var artefact = new ProductQuantisationQualityArtefact(
            documentCount,
            QueryCount,
            TopK,
            CandidateWindows,
            results);
        string jsonPath = Path.Combine(runDirectory, $"pq-quality-sweep-{documentCount}.json");
        string markdownPath = Path.Combine(runDirectory, $"pq-quality-sweep-{documentCount}.md");
        File.WriteAllText(
            jsonPath,
            JsonSerializer.Serialize(artefact, new JsonSerializerOptions { WriteIndented = true }));
        File.WriteAllText(markdownPath, CreateMarkdown(artefact));

        Console.WriteLine();
        Console.WriteLine($"PQ quality artefacts: {jsonPath}");
        return 0;
    }

    private static ProductQuantisationQualityResult Evaluate(
        string runDirectory,
        ProductQuantisationCorpus corpus,
        int[][] exactTopDocs,
        int dimension,
        int subspaceDimensions,
        int trainingSampleSize)
    {
        string filePath = Path.Combine(
            runDirectory,
            $"pq-quality-{corpus.OriginalVectors.Length}-{dimension}-w{subspaceDimensions}-s{trainingSampleSize}.vq");
        var stopwatch = Stopwatch.StartNew();
        QuantisedVectorWriter.WriteProductQuantisation(
            filePath,
            corpus.OriginalVectors.Length,
            dimension,
            corpus.NormalisedVectors,
            subspaceDimensions,
            trainingSampleSize,
            QuantisedVectorWriter.DefaultProductTrainingIterations);
        stopwatch.Stop();

        var reconstructed = new float[corpus.OriginalVectors.Length][];
        using (var reader = QuantisedVectorReader.Open(filePath))
        {
            for (int docId = 0; docId < reconstructed.Length; docId++)
            {
                reconstructed[docId] = reader.ReadVector(docId)
                    ?? throw new InvalidDataException($"PQ vector {docId} is missing.");
            }
        }

        var recallByWindow = CandidateWindows.ToDictionary(window => window, _ => 0d);
        for (int queryIndex = 0; queryIndex < QueryCount; queryIndex++)
        {
            float[] query = CreateQuery(dimension, queryIndex);
            int[] approximate = FindTopDocs(reconstructed, query, MaximumCandidateWindow);
            foreach (int window in CandidateWindows)
            {
                recallByWindow[window] += RecallAt10(
                    exactTopDocs[queryIndex],
                    approximate.AsSpan(0, Math.Min(window, approximate.Length)));
            }
        }

        foreach (int window in CandidateWindows)
            recallByWindow[window] /= QueryCount;

        long rawFloatBytes = checked((long)corpus.OriginalVectors.Length * dimension * sizeof(float));
        long codeBytes = checked(
            (long)corpus.OriginalVectors.Length *
            ((dimension + subspaceDimensions - 1) / subspaceDimensions));
        long codebookBytes = checked(
            (long)QuantisedVectorWriter.DefaultProductCentroidCount * dimension * sizeof(float));
        long vectorPayloadBytes = codeBytes + codebookBytes;
        long fileBytes = new FileInfo(filePath).Length;

        return new ProductQuantisationQualityResult(
            dimension,
            subspaceDimensions,
            (dimension + subspaceDimensions - 1) / subspaceDimensions,
            trainingSampleSize,
            QuantisedVectorWriter.DefaultProductTrainingIterations,
            recallByWindow[10],
            recallByWindow[40],
            recallByWindow[100],
            recallByWindow[400],
            rawFloatBytes,
            vectorPayloadBytes,
            1d - (double)vectorPayloadBytes / rawFloatBytes,
            fileBytes,
            1d - (double)fileBytes / rawFloatBytes,
            stopwatch.Elapsed.TotalMilliseconds);
    }

    private static ProductQuantisationCorpus CreateCorpus(int documentCount, int dimension)
    {
        var random = new Random(7);
        var original = new float[documentCount][];
        var normalised = new Dictionary<int, ReadOnlyMemory<float>>(documentCount);
        for (int docId = 0; docId < documentCount; docId++)
        {
            var vector = new float[dimension];
            for (int value = 0; value < dimension; value++)
                vector[value] = (float)(random.NextDouble() * 2d - 1d);
            original[docId] = vector;

            var copy = vector.ToArray();
            NormaliseInPlace(copy);
            normalised[docId] = copy;
        }
        return new ProductQuantisationCorpus(original, normalised);
    }

    private static int[][] CreateExactTopDocs(float[][] vectors, int dimension)
    {
        var result = new int[QueryCount][];
        for (int queryIndex = 0; queryIndex < QueryCount; queryIndex++)
            result[queryIndex] = FindTopDocs(vectors, CreateQuery(dimension, queryIndex), TopK);
        return result;
    }

    private static float[] CreateQuery(int dimension, int queryIndex)
    {
        // Advance the same deterministic stream used by the VQ gate to this query.
        var random = new Random(19);
        var query = new float[dimension];
        for (int current = 0; current <= queryIndex; current++)
        {
            for (int value = 0; value < dimension; value++)
                query[value] = (float)(random.NextDouble() * 2d - 1d);
        }
        return query;
    }

    private static int[] FindTopDocs(float[][] vectors, ReadOnlySpan<float> query, int count)
    {
        var heap = new PriorityQueue<ScoredDocument, float>(count);
        for (int docId = 0; docId < vectors.Length; docId++)
        {
            float score = VectorQuery.CosineSimilarity(vectors[docId], query);
            if (heap.Count < count)
            {
                heap.Enqueue(new ScoredDocument(docId, score), score);
            }
            else if (heap.TryPeek(out _, out float minimumScore) && score > minimumScore)
            {
                heap.Dequeue();
                heap.Enqueue(new ScoredDocument(docId, score), score);
            }
        }

        return heap.UnorderedItems
            .Select(item => item.Element)
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.DocId)
            .Select(item => item.DocId)
            .ToArray();
    }

    private static double RecallAt10(ReadOnlySpan<int> exact, ReadOnlySpan<int> candidates)
    {
        int matches = 0;
        foreach (int exactDocId in exact)
        {
            if (candidates.Contains(exactDocId))
                matches++;
        }
        return (double)matches / exact.Length;
    }

    private static void NormaliseInPlace(Span<float> vector)
    {
        float squaredNorm = 0f;
        foreach (float value in vector)
            squaredNorm += value * value;
        float inverseNorm = 1f / MathF.Sqrt(squaredNorm);
        for (int i = 0; i < vector.Length; i++)
            vector[i] *= inverseNorm;
    }

    private static void PrintResult(ProductQuantisationQualityResult result)
    {
        Console.WriteLine(
            string.Create(
                CultureInfo.InvariantCulture,
                $"  width={result.SubspaceDimensions}, sample={result.TrainingSampleSize:N0}: " +
                $"R@10={result.RecallAt10:F3}, R@40={result.CandidateRecallAt40:F3}, " +
                $"R@100={result.CandidateRecallAt100:F3}, R@400={result.CandidateRecallAt400:F3}, " +
                $"payload reduction={result.VectorPayloadReduction:P1}, train={result.TrainingMilliseconds / 1000d:F1}s"));
    }

    private static string CreateMarkdown(ProductQuantisationQualityArtefact artefact)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Product quantisation quality sweep");
        builder.AppendLine();
        builder.AppendLine($"Documents: {artefact.DocumentCount:N0}; queries: {artefact.QueryCount}.");
        builder.AppendLine();
        builder.AppendLine("| Dim | Width | Sample | R@10 | R@40 | R@100 | R@400 | Payload reduction | File reduction | Train ms |");
        builder.AppendLine("|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|");
        foreach (ProductQuantisationQualityResult result in artefact.Results)
        {
            builder.AppendLine(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"| {result.Dimension} | {result.SubspaceDimensions} | {result.TrainingSampleSize} | " +
                    $"{result.RecallAt10:F3} | {result.CandidateRecallAt40:F3} | " +
                    $"{result.CandidateRecallAt100:F3} | {result.CandidateRecallAt400:F3} | " +
                    $"{result.VectorPayloadReduction:P1} | {result.QuantisedFileReduction:P1} | " +
                    $"{result.TrainingMilliseconds:F0} |"));
        }
        return builder.ToString();
    }

    private readonly record struct ScoredDocument(int DocId, float Score);

    private sealed record ProductQuantisationCorpus(
        float[][] OriginalVectors,
        IReadOnlyDictionary<int, ReadOnlyMemory<float>> NormalisedVectors);
}

internal sealed record ProductQuantisationQualityArtefact(
    int DocumentCount,
    int QueryCount,
    int TopK,
    int[] CandidateWindows,
    IReadOnlyList<ProductQuantisationQualityResult> Results);

internal sealed record ProductQuantisationQualityResult(
    int Dimension,
    int SubspaceDimensions,
    int SubquantiserCount,
    int TrainingSampleSize,
    int TrainingIterations,
    double RecallAt10,
    double CandidateRecallAt40,
    double CandidateRecallAt100,
    double CandidateRecallAt400,
    long RawFloatBytes,
    long VectorPayloadBytes,
    double VectorPayloadReduction,
    long QuantisedFileBytes,
    double QuantisedFileReduction,
    double TrainingMilliseconds);
