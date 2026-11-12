using System.Numerics;
using System.Runtime;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Rowles.LeanCorpus.Benchmarks;

/// <summary>
/// The runtime a BenchmarkDotNet child actually ran with, observed inside that child: GC mode,
/// tiering, processor count, vector width and the hardware instruction sets the JIT may use.
/// Configured job settings say what was asked for; this says what applied.
/// </summary>
internal sealed class BenchmarkEffectiveRuntimeReport
{
    public string BenchmarkName { get; set; } = string.Empty;
    public string RuntimeFramework { get; set; } = string.Empty;
    public string ProcessArchitecture { get; set; } = string.Empty;
    public bool GcServer { get; set; }
    public bool GcConcurrent { get; set; }
    public string GcLatencyMode { get; set; } = string.Empty;
    public string TieredPgo { get; set; } = string.Empty;
    public string TieredCompilation { get; set; } = string.Empty;
    public int ProcessorCount { get; set; }
    public int VectorWidthBits { get; set; }
    public bool Vector128Accelerated { get; set; }
    public bool Vector256Accelerated { get; set; }
    public bool Vector512Accelerated { get; set; }
    public string[] InstructionSets { get; set; } = [];

    /// <summary>SHA-256 of the instruction sets and vector width, for exact comparison.</summary>
    public string IntrinsicsFingerprintSha256 { get; set; } = string.Empty;
}

internal static class BenchmarkChildRuntime
{
    private const string ArtifactDirectoryVariable = "LEANCORPUS_ARTIFACT_DIR";
    private const string DirectoryName = "child-runtime";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// Runs in every process that loads the benchmark assembly. Only a BenchmarkDotNet child, which
    /// receives <c>--benchmarkName</c>, records its runtime; the host and other tools do nothing.
    /// </summary>
#pragma warning disable CA2255 // A benchmark executable, not a library: the probe must run before any benchmark code.
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void RecordChildRuntime()
    {
        try
        {
            string? benchmarkName = ReadArgument(Environment.GetCommandLineArgs(), "--benchmarkName");
            string? artifactRoot = Environment.GetEnvironmentVariable(ArtifactDirectoryVariable);
            if (string.IsNullOrWhiteSpace(benchmarkName) || string.IsNullOrWhiteSpace(artifactRoot))
                return;

            var report = Capture(benchmarkName);
            string directory = Path.Combine(Path.GetFullPath(artifactRoot), DirectoryName);
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, string.Concat(Guid.NewGuid().ToString("N"), ".json"));
            File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(report, JsonOptions));
            File.Move(path + ".tmp", path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Missing evidence fails qualification closed; it must not fail the benchmark.
        }
    }

    internal static BenchmarkEffectiveRuntimeReport Capture(string benchmarkName)
    {
        string[] instructionSets = SupportedInstructionSets();
        int vectorWidthBits = Vector<byte>.Count * 8;
        return new BenchmarkEffectiveRuntimeReport
        {
            BenchmarkName = benchmarkName,
            RuntimeFramework = RuntimeInformation.FrameworkDescription,
            ProcessArchitecture = RuntimeInformation.ProcessArchitecture.ToString(),
            GcServer = GCSettings.IsServerGC,
            GcConcurrent = GCSettings.LatencyMode != GCLatencyMode.Batch,
            GcLatencyMode = GCSettings.LatencyMode.ToString(),
            TieredPgo = Environment.GetEnvironmentVariable("DOTNET_TieredPGO")
                ?? AppContext.GetData("System.Runtime.TieredPGO")?.ToString() ?? "runtime-default",
            TieredCompilation = Environment.GetEnvironmentVariable("DOTNET_TieredCompilation")
                ?? AppContext.GetData("System.Runtime.TieredCompilation")?.ToString() ?? "runtime-default",
            ProcessorCount = Environment.ProcessorCount,
            VectorWidthBits = vectorWidthBits,
            Vector128Accelerated = Vector128.IsHardwareAccelerated,
            Vector256Accelerated = Vector256.IsHardwareAccelerated,
            Vector512Accelerated = Vector512.IsHardwareAccelerated,
            InstructionSets = instructionSets,
            IntrinsicsFingerprintSha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
                string.Concat(string.Join(",", instructionSets), "|vector=", vectorWidthBits.ToString(System.Globalization.CultureInfo.InvariantCulture))))).ToLowerInvariant()
        };
    }

    /// <summary>Reads every child runtime record under an artifact directory, keyed by benchmark name.</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<BenchmarkEffectiveRuntimeReport>> ReadAll(string? artifactDirectory)
    {
        var result = new Dictionary<string, IReadOnlyList<BenchmarkEffectiveRuntimeReport>>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(artifactDirectory))
            return result;
        string directory = Path.Combine(Path.GetFullPath(artifactDirectory), DirectoryName);
        if (!Directory.Exists(directory))
            return result;
        foreach (var group in Directory.EnumerateFiles(directory, "*.json", SearchOption.TopDirectoryOnly)
                     .Order(StringComparer.Ordinal)
                     .Select(static path => JsonSerializer.Deserialize<BenchmarkEffectiveRuntimeReport>(File.ReadAllText(path), JsonOptions))
                     .OfType<BenchmarkEffectiveRuntimeReport>()
                     .GroupBy(static report => report.BenchmarkName, StringComparer.Ordinal))
            result[group.Key] = group.ToArray();
        return result;
    }

    /// <summary>
    /// The single runtime of a case's children, or <see langword="null"/> when none was recorded or
    /// launches disagree, so a pair can never silently mix runtimes.
    /// </summary>
    public static BenchmarkEffectiveRuntimeReport? ForCase(
        IReadOnlyDictionary<string, IReadOnlyList<BenchmarkEffectiveRuntimeReport>> records,
        string benchmarkName)
    {
        if (!records.TryGetValue(benchmarkName, out var reports) || reports.Count == 0)
            return null;
        var first = reports[0];
        return reports.All(report => JsonSerializer.Serialize(report, JsonOptions) == JsonSerializer.Serialize(first, JsonOptions))
            ? first
            : null;
    }

    private static string? ReadArgument(string[] arguments, string name)
    {
        for (int i = 0; i + 1 < arguments.Length; i++)
        {
            if (string.Equals(arguments[i], name, StringComparison.Ordinal))
                return arguments[i + 1];
        }
        return null;
    }

    private static string[] SupportedInstructionSets()
    {
        var sets = new List<string>();
        void Add(bool supported, string name)
        {
            if (supported)
                sets.Add(name);
        }

        Add(System.Runtime.Intrinsics.X86.Sse.IsSupported, "Sse");
        Add(System.Runtime.Intrinsics.X86.Sse2.IsSupported, "Sse2");
        Add(System.Runtime.Intrinsics.X86.Sse3.IsSupported, "Sse3");
        Add(System.Runtime.Intrinsics.X86.Ssse3.IsSupported, "Ssse3");
        Add(System.Runtime.Intrinsics.X86.Sse41.IsSupported, "Sse41");
        Add(System.Runtime.Intrinsics.X86.Sse42.IsSupported, "Sse42");
        Add(System.Runtime.Intrinsics.X86.Popcnt.IsSupported, "Popcnt");
        Add(System.Runtime.Intrinsics.X86.Lzcnt.IsSupported, "Lzcnt");
        Add(System.Runtime.Intrinsics.X86.Bmi1.IsSupported, "Bmi1");
        Add(System.Runtime.Intrinsics.X86.Bmi2.IsSupported, "Bmi2");
        Add(System.Runtime.Intrinsics.X86.Aes.IsSupported, "Aes");
        Add(System.Runtime.Intrinsics.X86.Pclmulqdq.IsSupported, "Pclmulqdq");
        Add(System.Runtime.Intrinsics.X86.Avx.IsSupported, "Avx");
        Add(System.Runtime.Intrinsics.X86.Avx2.IsSupported, "Avx2");
        Add(System.Runtime.Intrinsics.X86.Fma.IsSupported, "Fma");
        Add(System.Runtime.Intrinsics.X86.AvxVnni.IsSupported, "AvxVnni");
        Add(System.Runtime.Intrinsics.X86.Avx512F.IsSupported, "Avx512F");
        Add(System.Runtime.Intrinsics.X86.Avx512BW.IsSupported, "Avx512BW");
        Add(System.Runtime.Intrinsics.X86.Avx512CD.IsSupported, "Avx512CD");
        Add(System.Runtime.Intrinsics.X86.Avx512DQ.IsSupported, "Avx512DQ");
        Add(System.Runtime.Intrinsics.X86.Avx512Vbmi.IsSupported, "Avx512Vbmi");
        Add(System.Runtime.Intrinsics.Arm.AdvSimd.IsSupported, "AdvSimd");
        Add(System.Runtime.Intrinsics.Arm.Aes.IsSupported, "ArmAes");
        Add(System.Runtime.Intrinsics.Arm.Crc32.IsSupported, "Crc32");
        Add(System.Runtime.Intrinsics.Arm.Dp.IsSupported, "Dp");
        Add(System.Runtime.Intrinsics.Arm.Rdm.IsSupported, "Rdm");
        Add(System.Runtime.Intrinsics.Arm.Sha1.IsSupported, "Sha1");
        Add(System.Runtime.Intrinsics.Arm.Sha256.IsSupported, "Sha256");
        return [.. sets];
    }
}
