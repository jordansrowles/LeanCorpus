using System.Runtime.CompilerServices;

namespace Rowles.LeanCorpus.Codecs.Postings;

/// <summary>
/// Internal control used only by the benchmark assembly to compare the production
/// read-session path with the former per-primitive read path.
/// </summary>
internal static class PostingsReadBenchmarkControl
{
    private static int s_mode;

    private static int s_recording;
    private static long s_operationDrainEnterCount;
    private static long s_decodedBlockCount;

    internal static bool UsePerPrimitiveReads =>
        Volatile.Read(ref s_mode) == (int)PostingsReadBenchmarkMode.PerPrimitive;

    internal static void SetMode(PostingsReadBenchmarkMode mode) =>
        Volatile.Write(ref s_mode, (int)mode);

    internal static void StartRecording()
    {
        Interlocked.Exchange(ref s_operationDrainEnterCount, 0);
        Interlocked.Exchange(ref s_decodedBlockCount, 0);
        Volatile.Write(ref s_recording, 1);
    }

    internal static PostingsReadBenchmarkMetrics StopRecording()
    {
        Volatile.Write(ref s_recording, 0);
        return new PostingsReadBenchmarkMetrics(
            Interlocked.Read(ref s_operationDrainEnterCount),
            Interlocked.Read(ref s_decodedBlockCount));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void RecordOperationDrainEnters(long count)
    {
        if (Volatile.Read(ref s_recording) != 0)
            Interlocked.Add(ref s_operationDrainEnterCount, count);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void RecordDecodedBlock()
    {
        if (Volatile.Read(ref s_recording) != 0)
            Interlocked.Increment(ref s_decodedBlockCount);
    }
}

internal enum PostingsReadBenchmarkMode
{
    ReadSession,
    PerPrimitive,
}

internal readonly record struct PostingsReadBenchmarkMetrics(
    long OperationDrainEnterCount,
    long DecodedBlockCount);
