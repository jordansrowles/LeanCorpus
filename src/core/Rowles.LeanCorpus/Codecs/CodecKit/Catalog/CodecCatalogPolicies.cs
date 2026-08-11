using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Codecs.CodecKit;

/// <summary>Describes how a codec body is consumed.</summary>
public enum CodecAccessKind
{
    /// <summary>The complete body may be decoded into an in-memory model.</summary>
    Materialised,

    /// <summary>The body is processed sequentially without whole-body materialisation.</summary>
    Streaming,

    /// <summary>The reader retains a bounded input and addresses body regions directly.</summary>
    RandomAccess,

    /// <summary>The format is owned by an external serialiser or container implementation.</summary>
    External,
}

/// <summary>Describes the framing written for a current persistent format.</summary>
public enum CodecFramingPolicy
{
    /// <summary>The format uses the canonical self-identifying CodecKit file frame.</summary>
    Canonical,

    /// <summary>The format is framed by an external serialiser.</summary>
    External,

    /// <summary>The format is a container whose framing is validated separately.</summary>
    Container,
}

/// <summary>Identifies legacy framing that a supported body version can be read from.</summary>
[Flags]
public enum CodecLegacyFraming
{
    /// <summary>No legacy framing is supported for this version.</summary>
    None = 0,

    /// <summary>The version can be read from the original CodecKit length envelope.</summary>
    CodecKitEnvelope = 1 << 0,

    /// <summary>The version can be read from the ADR009 streaming trailer.</summary>
    CodecKitTrailer = 1 << 1,

    /// <summary>The version can be read from a format-specific legacy header.</summary>
    CustomHeader = 1 << 2,

    /// <summary>The version can be read from a legacy headerless representation.</summary>
    Headerless = 1 << 3,
}

/// <summary>Describes the checksum required for current writes.</summary>
public enum CodecChecksumPolicy
{
    /// <summary>The format does not use a catalogue-managed checksum.</summary>
    None,

    /// <summary>The canonical body is protected by xxHash64.</summary>
    XxHash64,
}

/// <summary>Describes how a supported format version can be migrated.</summary>
public enum CodecMigrationBehaviour
{
    /// <summary>No migration is required because the format remains externally framed.</summary>
    None,

    /// <summary>The body can be streamed unchanged into the current frame.</summary>
    Reframe,

    /// <summary>The body must be decoded and written through the current writer.</summary>
    Rewrite,

    /// <summary>All coordinated files in the family must be rewritten together.</summary>
    CoordinatedRewrite,

    /// <summary>Inspection is supported, but migration requires rebuilding the data.</summary>
    Unsupported,
}

/// <summary>Validates the semantic body of one logical codec file.</summary>
public interface ICodecFileValidationHandler
{
    /// <summary>Validates a bounded input containing only the codec body.</summary>
    void Validate(IndexInput bodyInput);
}

/// <summary>Migrates one logical codec body through a specialist implementation.</summary>
public interface ICodecFileMigrationHandler
{
    /// <summary>Migrates a bounded source body into the current body output.</summary>
    void Migrate(IndexInput sourceBody, IndexOutput targetBody);
}

/// <summary>Validates coordinated logical files owned by one codec family.</summary>
public interface ICodecFamilyValidationCoordinator
{
    /// <summary>Validates the supplied bounded body inputs by format ID.</summary>
    void Validate(IReadOnlyDictionary<string, IndexInput> bodyInputs);
}

/// <summary>Migrates coordinated logical files owned by one codec family.</summary>
public interface ICodecFamilyMigrationCoordinator
{
    /// <summary>Migrates bounded body inputs into current body outputs, keyed by format ID.</summary>
    void Migrate(
        IReadOnlyDictionary<string, IndexInput> sourceBodies,
        IReadOnlyDictionary<string, IndexOutput> targetBodies);
}
