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
    /// <param name="currentFormatVersion">The current body-format version, or <c>null</c> for versionless formats. A retired role retains its latest known version.</param>
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

    /// <summary>Gets the current body-format version, or <c>null</c> for a versionless format.</summary>
    public int? CurrentFormatVersion { get; }

    /// <summary>Gets the current writable target, or <c>null</c> for a retired or versionless role.</summary>
    public int? WritableFormatVersion => _supportedVersions.Any(
        version => version.Version == CurrentFormatVersion && version.IsReadable && version.IsWritable)
        ? CurrentFormatVersion
        : null;

    /// <summary>Gets an optional representation policy that can only restrict version capabilities.</summary>
    public ICodecLifecyclePolicy? LifecyclePolicy { get; init; }

    /// <summary>Evaluates an operation against this file role's version and representation capabilities.</summary>
    public CodecLifecycleDecision Evaluate(
        CodecLifecycleOperation operation,
        CodecRepresentationIdentity? source = null,
        CodecRepresentationIdentity? target = null)
    {
        if (!Enum.IsDefined(operation)
            || (operation is CodecLifecycleOperation.Read or CodecLifecycleOperation.RewriteSource && target.HasValue)
            || (operation == CodecLifecycleOperation.Create && source.HasValue))
            return Deny("InvalidOperation", operation, "Unexpected operation or identity arguments.");

        CodecVersionDescriptor? sourceVersion = null;
        if (operation != CodecLifecycleOperation.Create)
        {
            sourceVersion = ResolveIdentity(source);
            if (sourceVersion is null)
                return Deny("UnknownCodecIdentity", operation, "A registered source identity is required.");
            if (!sourceVersion.IsReadable)
                return Deny("CodecNotReadable", operation, $"Source version {sourceVersion.Version} is not readable.");
        }

        if (operation is CodecLifecycleOperation.Create or CodecLifecycleOperation.Migrate)
        {
            var targetVersion = ResolveIdentity(target);
            if (targetVersion is null)
                return Deny("UnknownCodecIdentity", operation, "A registered target identity is required.");
            if (!targetVersion.IsReadable || !targetVersion.IsWritable || targetVersion.Version != WritableFormatVersion)
                return Deny("CodecReadOnly", operation, $"Version {targetVersion.Version} is not the current writable target.");
        }

        if (operation == CodecLifecycleOperation.RewriteSource
            && (!WritableFormatVersion.HasValue || sourceVersion!.MigrationBehaviour == CodecMigrationBehaviour.Unsupported))
            return Deny("CodecReadOnly", operation, $"Source version {sourceVersion!.Version} is retired from rewriting.");

        if (operation == CodecLifecycleOperation.Migrate
            && (sourceVersion!.MigrationBehaviour == CodecMigrationBehaviour.Unsupported
                || (source!.Value.Version != target!.Value.Version
                    && sourceVersion.MigrationBehaviour == CodecMigrationBehaviour.None)))
            return Deny("MigrationNotSupported", operation, $"Source version {sourceVersion!.Version} has no permitted migration route.");

        return LifecyclePolicy?.Evaluate(operation, source, target) ?? CodecLifecycleDecision.Allowed;
    }

    private CodecVersionDescriptor? ResolveIdentity(CodecRepresentationIdentity? identity)
    {
        if (identity is not { } value || value.FormatId != FormatId
            || (LifecyclePolicy is null && value.RepresentationId is not null))
            return null;
        return _supportedVersions.FirstOrDefault(version => version.Version == value.Version);
    }

    private CodecLifecycleDecision Deny(string reason, CodecLifecycleOperation operation, string explanation)
        => CodecLifecycleDecision.Denied(reason, $"{FormatId}: {operation}: {explanation}");

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
