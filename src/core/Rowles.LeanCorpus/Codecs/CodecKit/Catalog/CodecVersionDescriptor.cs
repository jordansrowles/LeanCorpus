namespace Rowles.LeanCorpus.Codecs.CodecKit;

/// <summary>
/// Describes one supported body-format version of a persistent codec file.
/// </summary>
public sealed class CodecVersionDescriptor
{
    /// <summary>
    /// Creates a supported version declaration.
    /// </summary>
    /// <param name="version">The positive body-format version number.</param>
    /// <param name="diagnosticLabel">A stable label used in diagnostics.</param>
    /// <param name="isReadable">Whether this build can read the version.</param>
    /// <param name="isWritable">Whether this build can write the version.</param>
    /// <param name="legacyFraming">Legacy framing from which this version can be read.</param>
    /// <param name="migrationBehaviour">How this version can be migrated to current storage.</param>
    public CodecVersionDescriptor(
        int version,
        string diagnosticLabel,
        bool isReadable = true,
        bool isWritable = false,
        CodecLegacyFraming legacyFraming = CodecLegacyFraming.None,
        CodecMigrationBehaviour migrationBehaviour = CodecMigrationBehaviour.None)
    {
        Version = version;
        DiagnosticLabel = diagnosticLabel;
        IsReadable = isReadable;
        IsWritable = isWritable;
        LegacyFraming = legacyFraming;
        MigrationBehaviour = migrationBehaviour;
    }

    /// <summary>Gets the body-format version number.</summary>
    public int Version { get; }

    /// <summary>Gets the stable diagnostic label for this version.</summary>
    public string DiagnosticLabel { get; }

    /// <summary>Gets a value indicating whether this build can read the version.</summary>
    public bool IsReadable { get; }

    /// <summary>Gets a value indicating whether this build can write the version.</summary>
    public bool IsWritable { get; }

    /// <summary>Gets legacy framing from which this version can be read.</summary>
    public CodecLegacyFraming LegacyFraming { get; }

    /// <summary>Gets the migration behaviour for this version.</summary>
    public CodecMigrationBehaviour MigrationBehaviour { get; }
}
