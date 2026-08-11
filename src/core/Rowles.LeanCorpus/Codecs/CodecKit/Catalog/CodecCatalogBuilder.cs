namespace Rowles.LeanCorpus.Codecs.CodecKit;

/// <summary>
/// Builds an immutable <see cref="CodecCatalog"/> and validates its declarations as one unit.
/// </summary>
public sealed class CodecCatalogBuilder
{
    private readonly List<CodecFamilyDescriptor> _families = [];

    /// <summary>Adds a codec family declaration.</summary>
    /// <param name="family">The family to add.</param>
    /// <returns>This builder for chaining.</returns>
    public CodecCatalogBuilder Add(CodecFamilyDescriptor family)
    {
        ArgumentNullException.ThrowIfNull(family);
        _families.Add(family);
        return this;
    }

    /// <summary>Adds all statically declared LeanCorpus built-in persistent formats.</summary>
    /// <returns>This builder for chaining.</returns>
    public CodecCatalogBuilder AddBuiltIns()
    {
        foreach (var family in CodecCatalogBuiltIns.Families)
            _families.Add(family);
        return this;
    }

    /// <summary>Validates the accumulated declarations and creates an immutable catalogue snapshot.</summary>
    public CodecCatalog Build()
    {
        var familyIds = new HashSet<string>(StringComparer.Ordinal);
        var formatIds = new HashSet<string>(StringComparer.Ordinal);
        var physicalClaims = new Dictionary<string, string>(StringComparer.Ordinal);
        var temporaryClaims = new Dictionary<string, string>(StringComparer.Ordinal);
        var physicalMatchers = new List<(CodecFileMatcher Matcher, string FormatId)>();
        var temporaryMatchers = new List<(CodecFileMatcher Matcher, string FormatId)>();
        var files = new List<CodecFileDescriptor>();

        foreach (var family in _families)
        {
            ValidateIdentifier(family.FamilyId, nameof(CodecFamilyDescriptor.FamilyId));
            ValidateDisplayName(family.DisplayName, nameof(CodecFamilyDescriptor.DisplayName));

            if (!familyIds.Add(family.FamilyId))
                throw new InvalidOperationException($"Duplicate codec family ID '{family.FamilyId}'.");
            if (family.Files.Count == 0)
                throw new InvalidOperationException($"Codec family '{family.FamilyId}' must declare at least one file role.");

            foreach (var file in family.Files)
            {
                if (file is null)
                    throw new InvalidOperationException($"Codec family '{family.FamilyId}' contains a null file role.");

                ValidateFile(family, file);
                if (!formatIds.Add(file.FormatId))
                    throw new InvalidOperationException($"Duplicate codec format ID '{file.FormatId}'.");

                file.FileMatcher.Validate(nameof(CodecFileDescriptor.FileMatcher));
                if (physicalClaims.TryGetValue(file.FileMatcher.PhysicalClaim, out var claimedBy))
                {
                    throw new InvalidOperationException(
                        $"Codec formats '{claimedBy}' and '{file.FormatId}' claim the same physical file role.");
                }

                foreach (var (matcher, formatId) in physicalMatchers)
                {
                    if (file.FileMatcher.Overlaps(matcher))
                    {
                        throw new InvalidOperationException(
                            $"Codec formats '{formatId}' and '{file.FormatId}' make overlapping physical file claims.");
                    }
                }

                physicalClaims.Add(file.FileMatcher.PhysicalClaim, file.FormatId);
                physicalMatchers.Add((file.FileMatcher, file.FormatId));
                ValidateStoragePolicy(file);
                foreach (var temporaryMatcher in file.TemporaryFileMatchers)
                {
                    if (temporaryMatcher is null)
                        throw new InvalidOperationException($"Codec format '{file.FormatId}' contains a null temporary-file matcher.");
                    temporaryMatcher.Validate(nameof(CodecFileDescriptor.TemporaryFileMatchers));
                    if (temporaryClaims.TryGetValue(temporaryMatcher.PhysicalClaim, out var temporaryClaimedBy))
                    {
                        throw new InvalidOperationException(
                            $"Codec formats '{temporaryClaimedBy}' and '{file.FormatId}' claim the same temporary-file pattern.");
                    }
                    foreach (var (matcher, formatId) in temporaryMatchers)
                    {
                        if (temporaryMatcher.Overlaps(matcher))
                        {
                            throw new InvalidOperationException(
                                $"Codec formats '{formatId}' and '{file.FormatId}' make overlapping temporary-file claims.");
                        }
                    }

                    temporaryClaims.Add(temporaryMatcher.PhysicalClaim, file.FormatId);
                    temporaryMatchers.Add((temporaryMatcher, file.FormatId));
                }
                files.Add(file);
            }
        }

        return new CodecCatalog(_families.ToArray(), files.ToArray());
    }

    private static void ValidateFile(CodecFamilyDescriptor family, CodecFileDescriptor file)
    {
        ValidateIdentifier(file.FormatId, nameof(CodecFileDescriptor.FormatId));
        ValidateIdentifier(file.FamilyId, nameof(CodecFileDescriptor.FamilyId));
        ValidateDisplayName(file.DisplayName, nameof(CodecFileDescriptor.DisplayName));
        ArgumentNullException.ThrowIfNull(file.FileMatcher);

        if (!file.FamilyId.Equals(family.FamilyId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Codec format '{file.FormatId}' declares family '{file.FamilyId}' but belongs to '{family.FamilyId}'.");
        }

        ValidateVersions(file);
    }

    private static void ValidateVersions(CodecFileDescriptor file)
    {
        if (file.SupportedVersions.Count == 0)
        {
            if (file.CurrentFormatVersion.HasValue)
            {
                throw new InvalidOperationException(
                    $"Codec format '{file.FormatId}' declares current version {file.CurrentFormatVersion} without supported versions.");
            }

            return;
        }

        if (!file.CurrentFormatVersion.HasValue)
            throw new InvalidOperationException($"Codec format '{file.FormatId}' has supported versions but no current version.");

        CodecVersionDescriptor? current = null;
        CodecVersionDescriptor? newestWritable = null;
        var previousVersion = 0;
        foreach (var version in file.SupportedVersions)
        {
            if (version is null)
                throw new InvalidOperationException($"Codec format '{file.FormatId}' contains a null version declaration.");
            if (version.Version <= 0)
                throw new InvalidOperationException($"Codec format '{file.FormatId}' has invalid version {version.Version}.");
            if (version.Version <= previousVersion)
                throw new InvalidOperationException($"Codec format '{file.FormatId}' versions must be strictly increasing.");
            if (string.IsNullOrWhiteSpace(version.DiagnosticLabel))
                throw new InvalidOperationException($"Codec format '{file.FormatId}' version {version.Version} has no diagnostic label.");
            if (!version.IsReadable && !version.IsWritable)
                throw new InvalidOperationException($"Codec format '{file.FormatId}' version {version.Version} is neither readable nor writable.");
            if ((version.LegacyFraming & ~AllLegacyFraming) != 0)
                throw new InvalidOperationException($"Codec format '{file.FormatId}' version {version.Version} has an invalid legacy framing policy.");
            if (!Enum.IsDefined(version.MigrationBehaviour))
                throw new InvalidOperationException($"Codec format '{file.FormatId}' version {version.Version} has an invalid migration behaviour.");

            if (version.Version == file.CurrentFormatVersion.Value)
                current = version;
            if (version.IsWritable)
                newestWritable = version;
            previousVersion = version.Version;
        }

        if (current is null)
        {
            throw new InvalidOperationException(
                $"Codec format '{file.FormatId}' current version {file.CurrentFormatVersion} is not registered.");
        }
        if (!current.IsReadable || !current.IsWritable)
            throw new InvalidOperationException($"Codec format '{file.FormatId}' current version must be readable and writable.");
        if (newestWritable is null || newestWritable.Version != current.Version)
            throw new InvalidOperationException($"Codec format '{file.FormatId}' current version must be the newest writable version.");
    }

    private static void ValidateStoragePolicy(CodecFileDescriptor file)
    {
        if (!Enum.IsDefined(file.AccessKind))
            throw new InvalidOperationException($"Codec format '{file.FormatId}' has an invalid access kind.");
        if (!Enum.IsDefined(file.CurrentFraming))
            throw new InvalidOperationException($"Codec format '{file.FormatId}' has an invalid current framing policy.");
        if (!Enum.IsDefined(file.ChecksumPolicy))
            throw new InvalidOperationException($"Codec format '{file.FormatId}' has an invalid checksum policy.");
        if (!Enum.IsDefined(file.MigrationBehaviour))
            throw new InvalidOperationException($"Codec format '{file.FormatId}' has an invalid migration behaviour.");

        if (file.CurrentFormatVersion.HasValue)
        {
            if (file.CurrentFraming != CodecFramingPolicy.Canonical)
                throw new InvalidOperationException($"Versioned codec format '{file.FormatId}' must use canonical current framing.");
            if (file.ChecksumPolicy == CodecChecksumPolicy.None)
                throw new InvalidOperationException($"Canonical codec format '{file.FormatId}' must declare a checksum policy.");
            if (file.TemporaryFileMatchers.Count == 0)
                throw new InvalidOperationException($"Canonical codec format '{file.FormatId}' must declare temporary-file patterns.");
            if (file.MigrationBehaviour == CodecMigrationBehaviour.None)
                throw new InvalidOperationException($"Canonical codec format '{file.FormatId}' must declare migration behaviour.");

            var currentVersion = file.SupportedVersions.Single(
                version => version.Version == file.CurrentFormatVersion.Value);
            if (currentVersion.MigrationBehaviour != file.MigrationBehaviour)
                throw new InvalidOperationException($"Codec format '{file.FormatId}' current-version migration behaviour is inconsistent.");
        }
        else
        {
            if (file.CurrentFraming == CodecFramingPolicy.Canonical)
                throw new InvalidOperationException($"Canonical codec format '{file.FormatId}' must declare supported versions.");
            if (file.ChecksumPolicy != CodecChecksumPolicy.None)
                throw new InvalidOperationException($"Externally framed codec format '{file.FormatId}' cannot declare a canonical checksum.");
        }
    }

    private static void ValidateDisplayName(string displayName, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
            throw new ArgumentException("Display names cannot be empty.", parameterName);
    }

    private static void ValidateIdentifier(string identifier, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(identifier) || identifier.Length > 64)
            throw new ArgumentException("Codec identifiers must contain at most 64 ASCII bytes.", parameterName);

        var segments = identifier.Split('.');
        if (segments.Length < 2)
            throw new ArgumentException($"Codec identifier '{identifier}' must be namespaced.", parameterName);

        foreach (var segment in segments)
        {
            if (segment.Length == 0 || segment[0] is < 'a' or > 'z')
                throw new ArgumentException($"Invalid codec identifier '{identifier}'.", parameterName);

            foreach (var character in segment)
            {
                if (character is not (>= 'a' and <= 'z') and not (>= '0' and <= '9') and not '-')
                    throw new ArgumentException($"Invalid codec identifier '{identifier}'.", parameterName);
            }
        }
    }

    private const CodecLegacyFraming AllLegacyFraming =
        CodecLegacyFraming.CodecKitEnvelope |
        CodecLegacyFraming.CodecKitTrailer |
        CodecLegacyFraming.CustomHeader |
        CodecLegacyFraming.Headerless;
}
