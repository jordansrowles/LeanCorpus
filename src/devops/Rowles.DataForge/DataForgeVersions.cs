namespace Rowles.DataForge;

/// <summary>Version numbers for persisted DataForge contracts.</summary>
public static class DataForgeVersions
{
    public const int DataForgeVersion = 1;
    public const int SeedDerivationVersion = 1;
    public const int CanonicalFormatVersion = 1;
    public const int ManifestSchemaVersion = 1;
}

public enum DataForgeSourceKind
{
    Generated,
    Imported
}

public sealed record DataForgeProfileDescriptor(
    string ProfileId,
    int ProfileVersion,
    string Kind,
    ulong DefaultSeed,
    int DefaultCount,
    string Description);

public sealed class DataForgeGenerationOptions
{
    public DataForgeGenerationOptions(
        ulong seed,
        int recordCount,
        IReadOnlyDictionary<string, string>? parameters = null)
    {
        if (recordCount is < 1 or > 10_000_000)
            throw new ArgumentOutOfRangeException(nameof(recordCount), "Record count must be between 1 and 10,000,000.");

        Seed = seed;
        RecordCount = recordCount;
        Parameters = CopyParameters(parameters);
    }

    public ulong Seed { get; }

    public int RecordCount { get; }

    public IReadOnlyDictionary<string, string> Parameters { get; }

    private static IReadOnlyDictionary<string, string> CopyParameters(IReadOnlyDictionary<string, string>? parameters)
    {
        var sorted = new SortedDictionary<string, string>(StringComparer.Ordinal);
        if (parameters is null)
            return sorted;

        if (parameters.Count > 64)
            throw new ArgumentOutOfRangeException(nameof(parameters), "At most 64 parameters are allowed.");

        foreach (var pair in parameters)
        {
            ArgumentNullException.ThrowIfNull(pair.Key);
            ArgumentNullException.ThrowIfNull(pair.Value);

            var keyBytes = DataForgeSeedDerivation.GetStrictUtf8ByteCount(pair.Key);
            var valueBytes = DataForgeSeedDerivation.GetStrictUtf8ByteCount(pair.Value);
            if (keyBytes is < 1 or > 64)
                throw new ArgumentOutOfRangeException(nameof(parameters), "Parameter keys must contain 1 to 64 UTF-8 bytes.");
            if (valueBytes > 1_024)
                throw new ArgumentOutOfRangeException(nameof(parameters), "Parameter values must contain at most 1,024 UTF-8 bytes.");
            if (!sorted.TryAdd(pair.Key, pair.Value))
                throw new ArgumentException($"Duplicate parameter key '{pair.Key}'.", nameof(parameters));
        }

        return sorted;
    }
}

public sealed record DataForgeDependencyVersion(string Name, string Version);

public sealed record DataForgeParameter(string Name, string Value);

public sealed record DataForgeSummary(string Name, string Value);

public interface IDataForgeProfile
{
    DataForgeProfileDescriptor Descriptor { get; }
}

public interface IDataForgeGeneratedProfile<TRecord> : IDataForgeProfile
{
    IEnumerable<TRecord> Generate(DataForgeGenerationOptions options);

    IDataForgeCanonicalRecordWriter<TRecord> CanonicalRecordWriter { get; }

    IReadOnlyList<DataForgeDependencyVersion> Dependencies { get; }

    IReadOnlyList<DataForgeSummary> Summarise(DataForgeGenerationOptions options);
}

public interface IDataForgeCanonicalRecordWriter<in TRecord>
{
    void Write(CanonicalJsonWriter writer, TRecord record);
}
