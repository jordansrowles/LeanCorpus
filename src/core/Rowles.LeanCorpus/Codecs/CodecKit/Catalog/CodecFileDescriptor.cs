using System.Collections.ObjectModel;

namespace Rowles.LeanCorpus.Codecs.CodecKit;

/// <summary>
/// Describes one physical or logical persistent file role owned by a codec family.
/// </summary>
public sealed class CodecFileDescriptor
{
    private readonly ReadOnlyCollection<CodecVersionDescriptor> _supportedVersions;
    private readonly ReadOnlyCollection<CodecFileMatcher> _temporaryFileMatchers;

    /// <summary>
    /// Creates a persistent file descriptor.
    /// </summary>
    /// <param name="formatId">The stable, namespaced format identifier.</param>
    /// <param name="familyId">The identifier of the owning family.</param>
    /// <param name="displayName">The human-readable file-role name.</param>
    /// <param name="fileMatcher">The matcher used to recognise logical file names.</param>
    /// <param name="currentFormatVersion">The current writable body-format version, or <c>null</c> for versionless formats.</param>
    /// <param name="supportedVersions">Supported versions in strictly increasing order.</param>
    /// <param name="accessKind">How the body is consumed.</param>
    /// <param name="currentFraming">The framing used by current writes.</param>
    /// <param name="checksumPolicy">The checksum required for current writes.</param>
    /// <param name="migrationBehaviour">How legacy storage for this role reaches current storage.</param>
    /// <param name="temporaryFileMatchers">Matchers for temporary files owned by this role.</param>
    /// <param name="validationHandler">An optional specialist semantic validation handler.</param>
    /// <param name="migrationHandler">An optional specialist migration handler.</param>
    public CodecFileDescriptor(
        string formatId,
        string familyId,
        string displayName,
        CodecFileMatcher fileMatcher,
        int? currentFormatVersion,
        IEnumerable<CodecVersionDescriptor>? supportedVersions = null,
        CodecAccessKind accessKind = CodecAccessKind.External,
        CodecFramingPolicy currentFraming = CodecFramingPolicy.External,
        CodecChecksumPolicy checksumPolicy = CodecChecksumPolicy.None,
        CodecMigrationBehaviour migrationBehaviour = CodecMigrationBehaviour.None,
        IEnumerable<CodecFileMatcher>? temporaryFileMatchers = null,
        ICodecFileValidationHandler? validationHandler = null,
        ICodecFileMigrationHandler? migrationHandler = null)
    {
        FormatId = formatId;
        FamilyId = familyId;
        DisplayName = displayName;
        FileMatcher = fileMatcher;
        CurrentFormatVersion = currentFormatVersion;
        _supportedVersions = Array.AsReadOnly(supportedVersions?.ToArray() ?? []);
        AccessKind = accessKind;
        CurrentFraming = currentFraming;
        ChecksumPolicy = checksumPolicy;
        MigrationBehaviour = migrationBehaviour;
        _temporaryFileMatchers = Array.AsReadOnly(temporaryFileMatchers?.ToArray() ?? []);
        ValidationHandler = validationHandler;
        MigrationHandler = migrationHandler;
    }

    /// <summary>Gets the stable, namespaced format identifier.</summary>
    public string FormatId { get; }

    /// <summary>Gets the identifier of the owning family.</summary>
    public string FamilyId { get; }

    /// <summary>Gets the human-readable file-role name.</summary>
    public string DisplayName { get; }

    /// <summary>Gets the matcher used to recognise logical file names.</summary>
    public CodecFileMatcher FileMatcher { get; }

    /// <summary>Gets the current writable body-format version, or <c>null</c> for a versionless format.</summary>
    public int? CurrentFormatVersion { get; }

    /// <summary>Gets supported versions in strictly increasing order.</summary>
    public IReadOnlyList<CodecVersionDescriptor> SupportedVersions => _supportedVersions;

    /// <summary>Gets how the body is consumed.</summary>
    public CodecAccessKind AccessKind { get; }

    /// <summary>Gets the framing used by current writes.</summary>
    public CodecFramingPolicy CurrentFraming { get; }

    /// <summary>Gets the checksum required for current writes.</summary>
    public CodecChecksumPolicy ChecksumPolicy { get; }

    /// <summary>Gets how legacy storage for this role reaches current storage.</summary>
    public CodecMigrationBehaviour MigrationBehaviour { get; }

    /// <summary>Gets matchers for temporary files owned by this role.</summary>
    public IReadOnlyList<CodecFileMatcher> TemporaryFileMatchers => _temporaryFileMatchers;

    /// <summary>Gets the optional specialist semantic validation handler.</summary>
    public ICodecFileValidationHandler? ValidationHandler { get; }

    /// <summary>Gets the optional specialist migration handler.</summary>
    public ICodecFileMigrationHandler? MigrationHandler { get; }
}
