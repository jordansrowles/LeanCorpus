using System.Collections.ObjectModel;

namespace Rowles.LeanCorpus.Codecs.CodecKit;

/// <summary>
/// Describes a logical persistence family containing one or more coordinated file roles.
/// </summary>
public sealed class CodecFamilyDescriptor
{
    private readonly ReadOnlyCollection<CodecFileDescriptor> _files;

    /// <summary>
    /// Creates a codec family descriptor.
    /// </summary>
    /// <param name="familyId">The stable, namespaced family identifier.</param>
    /// <param name="displayName">The human-readable family name.</param>
    /// <param name="files">The physical or logical file roles owned by the family.</param>
    /// <param name="validationCoordinator">An optional cross-file validation coordinator.</param>
    /// <param name="migrationCoordinator">An optional coordinated migration implementation.</param>
    public CodecFamilyDescriptor(
        string familyId,
        string displayName,
        IEnumerable<CodecFileDescriptor> files,
        ICodecFamilyValidationCoordinator? validationCoordinator = null,
        ICodecFamilyMigrationCoordinator? migrationCoordinator = null)
    {
        ArgumentNullException.ThrowIfNull(files);
        FamilyId = familyId;
        DisplayName = displayName;
        _files = Array.AsReadOnly(files.ToArray());
        ValidationCoordinator = validationCoordinator;
        MigrationCoordinator = migrationCoordinator;
    }

    /// <summary>Gets the stable, namespaced family identifier.</summary>
    public string FamilyId { get; }

    /// <summary>Gets the human-readable family name.</summary>
    public string DisplayName { get; }

    /// <summary>Gets the immutable set of file roles owned by this family.</summary>
    public IReadOnlyList<CodecFileDescriptor> Files => _files;

    /// <summary>Gets the optional cross-file validation coordinator.</summary>
    public ICodecFamilyValidationCoordinator? ValidationCoordinator { get; }

    /// <summary>Gets the optional coordinated migration implementation.</summary>
    public ICodecFamilyMigrationCoordinator? MigrationCoordinator { get; }
}
