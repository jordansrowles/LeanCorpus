using System.Diagnostics;
using System.Text.Json;
using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Codecs.Postings;
using Rowles.LeanCorpus.Codecs.StoredFields;
using Rowles.LeanCorpus.Codecs.Vectors;
using Rowles.LeanCorpus.Diagnostics;
using Rowles.LeanCorpus.Index.Segment;
using Rowles.LeanCorpus.Serialization;
using Rowles.LeanCorpus.Store;


namespace Rowles.LeanCorpus.Index.Format;

/// <summary>
/// Inspects LeanCorpus index directories and reports the detected on-disk format.
/// </summary>
public static class IndexFormatInspector
{
    private static readonly string[] RequiredExtensions = [".seg", ".dic", ".pos", ".fdt", ".fdx", ".nrm"];

    /// <summary>
    /// Inspects the latest readable commit in <paramref name="directory"/>.
    /// </summary>
    /// <param name="directory">The index directory to inspect.</param>
    /// <param name="options">Inspection options.</param>
    /// <returns>The detected index format inventory.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="directory"/> is <c>null</c>.</exception>
    public static IndexFormatInventory Inspect(MMapDirectory directory, IndexFormatInspectionOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(directory);

        var sw = Stopwatch.StartNew();
        using var activity = LeanCorpusActivitySource.Source.StartActivity(LeanCorpusActivitySource.FormatInspect);
        IndexFormatInventory? inventory = null;
        var succeeded = false;
        try
        {
            inventory = InspectCore(directory, options);
            succeeded = true;
            return inventory;
        }
        finally
        {
            sw.Stop();
            activity?.SetTag("operation.succeeded", succeeded);
            if (inventory is not null)
            {
                if (inventory.CommitGeneration is int commitGeneration)
                    activity?.SetTag("index.commit_generation", commitGeneration);
                activity?.SetTag("index.segment_count", inventory.Segments.Count);
                activity?.SetTag("index.orphan_file_count", inventory.OrphanFiles.Count);
                activity?.SetTag("index.issue_count", inventory.Issues.Count);
                activity?.SetTag("index.has_unsupported_future_format", inventory.HasUnsupportedFutureFormat);
            }

            LeanCorpusMaintenanceMetrics.RecordFormatInspect(sw.Elapsed, succeeded);
        }
    }

    internal static (IReadOnlyList<CodecFileInventory> Files, IReadOnlyList<IndexCheckIssue> Issues) InspectSegmentFiles(
        MMapDirectory directory,
        string segmentId,
        IndexFormatInspectionOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(directory);
        ArgumentException.ThrowIfNullOrWhiteSpace(segmentId);
        options ??= new IndexFormatInspectionOptions();

        var issues = new List<IndexCheckIssue>();
        var files = new List<CodecFileInventory>();
        var compoundFileName = segmentId + ".cfs";
        var isCompound = directory.FileExists(compoundFileName);
        if (isCompound &&
            TryInspectDirectFile(directory, compoundFileName, segmentId, null, options, issues, out var container))
        {
            files.Add(container);
        }

        ISegmentFileSource? source = null;
        try
        {
            source = isCompound
                ? new CompoundSegmentFileSource(directory, segmentId)
                : new LooseSegmentFileSource(directory, segmentId);
            foreach (var fileName in source.EnumerateFiles())
            {
                var isCompoundMember = isCompound && !directory.FileExists(fileName);
                if (TryInspectFile(
                    source,
                    fileName,
                    segmentId,
                    null,
                    options,
                    issues,
                    isCompoundMember ? CodecPhysicalLocationKind.CompoundMember : CodecPhysicalLocationKind.LooseFile,
                    isCompoundMember ? compoundFileName : fileName,
                    isCompoundMember ? compoundFileName : null,
                    out var file))
                {
                    files.Add(file);
                }
            }

            ValidateCodecFamilies(source, files, segmentId, options, issues);
        }
        catch (Exception ex) when (isCompound && ex is IOException or InvalidDataException)
        {
            issues.Add(CreateIssue(
                IndexCheckSeverity.Error,
                IndexCheckIssueCodes.InvalidCodecMagic,
                $"Cannot inspect compound segment '{compoundFileName}': {ex.Message}",
                compoundFileName,
                segmentId,
                false));
        }
        finally
        {
            source?.Dispose();
        }

        return (files, issues);
    }

    private static IndexFormatInventory InspectCore(MMapDirectory directory, IndexFormatInspectionOptions? options)
    {
        options ??= new IndexFormatInspectionOptions();

        var directoryPath = directory.DirectoryPath;
        var issues = new List<IndexCheckIssue>();
        var commitData = TryFindLatestReadableCommit(directoryPath, issues, out int? commitGeneration);
        if (commitData is null)
        {
            return new IndexFormatInventory
            {
                DirectoryPath = directoryPath,
                CommitGeneration = commitGeneration,
                ContentToken = null,
                SegmentIds = [],
                Segments = [],
                OrphanFiles = InspectOrphanFiles(directory, [], options, issues),
                Issues = issues,
                HasUnsupportedFutureFormat = issues.Any(static issue => issue.Code is IndexCheckIssueCodes.UnsupportedFutureCodecVersion or IndexCheckIssueCodes.UnsupportedCodecFrameVersion),
                HasUnknownFormat = issues.Any(static issue => issue.Code == IndexCheckIssueCodes.UnknownCodecFormat)
            };
        }

        var segmentIds = commitData.Segments;
        var referencedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var segments = new List<SegmentFormatInventory>(segmentIds.Count);
        foreach (var segmentId in segmentIds)
            segments.Add(InspectSegment(directory, segmentId, options, issues, referencedFiles));

        var orphanFiles = InspectOrphanFiles(directory, referencedFiles, options, issues);
        return new IndexFormatInventory
        {
            DirectoryPath = directoryPath,
            CommitGeneration = commitGeneration,
            ContentToken = commitData.ContentToken,
            SegmentIds = segmentIds,
            Segments = segments,
            OrphanFiles = orphanFiles,
            Issues = issues,
            HasUnsupportedFutureFormat = HasUnsupportedFutureFormat(segments, orphanFiles),
            HasUnknownFormat = HasUnknownFormat(segments, orphanFiles)
        };
    }

    private static CommitData? TryFindLatestReadableCommit(
        string directoryPath,
        List<IndexCheckIssue> issues,
        out int? commitGeneration)
    {
        commitGeneration = null;
        var commitFiles = IndexFileInspector.FindCommitFiles(directoryPath);
        if (commitFiles.Count == 0)
        {
            issues.Add(CreateIssue(
                IndexCheckSeverity.Error,
                IndexCheckIssueCodes.NoCommitFile,
                "No commit file (segments_N) found in directory.",
                null,
                null,
                false));
            return null;
        }

        foreach (var (generation, filePath) in commitFiles)
        {
            var commitIssues = new IndexCheckResult();
            var commitData = IndexFileInspector.TryReadCommit(filePath, generation, commitIssues);
            foreach (var issue in commitIssues.DetailedIssues)
                issues.Add(issue);

            if (commitData is null)
                continue;

            commitGeneration = generation;
            return commitData;
        }

        commitGeneration = commitFiles[0].Generation;
        return null;
    }

    private static SegmentFormatInventory InspectSegment(
        MMapDirectory directory,
        string segmentId,
        IndexFormatInspectionOptions options,
        List<IndexCheckIssue> issues,
        HashSet<string> referencedFiles)
    {
        var directoryPath = directory.DirectoryPath;
        var basePath = Path.Combine(directoryPath, segmentId);
        var missingFiles = new List<string>();

        SegmentInfo? segmentInfo = null;
        var warnings = new List<string>();
        var segPath = basePath + ".seg";
        if (FileOpenRetry.FileExists(segPath))
        {
            try
            {
                segmentInfo = SegmentInfo.ReadFrom(segPath);
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException)
            {
                issues.Add(CreateIssue(
                    IndexCheckSeverity.Error,
                    IndexCheckIssueCodes.SegmentMetadataUnreadable,
                    $"Segment '{segmentId}' cannot read .seg metadata: {ex.Message}",
                    Path.GetFileName(segPath),
                    segmentId,
                    false));
            }
        }

        var requiredExtensions = segmentInfo?.IsCompoundFile == true
            ? new[] { ".seg", ".cfs" }
            : RequiredExtensions;
        foreach (var extension in requiredExtensions)
        {
            var path = basePath + extension;
            referencedFiles.Add(path);
            if (!FileOpenRetry.FileExists(path))
                missingFiles.Add(Path.GetFileName(path));
        }

        var physicalSegmentFiles = FindSegmentFiles(directoryPath, segmentId);
        foreach (var filePath in physicalSegmentFiles)
            referencedFiles.Add(filePath);

        var files = new List<CodecFileInventory>(physicalSegmentFiles.Count);
        var isCompound = segmentInfo?.IsCompoundFile == true;
        var compoundFileName = segmentId + ".cfs";
        if (isCompound && directory.FileExists(compoundFileName) &&
            TryInspectDirectFile(directory, compoundFileName, segmentId, null, options, issues, out var compoundInventory))
        {
            files.Add(compoundInventory);
        }

        ISegmentFileSource? fileSource = null;
        try
        {
            fileSource = isCompound && directory.FileExists(compoundFileName)
                ? new CompoundSegmentFileSource(directory, segmentId)
                : new LooseSegmentFileSource(directory, segmentId);

            foreach (var fileName in fileSource.EnumerateFiles())
            {
                if (!options.IncludeOptionalSidecars && !IsRequiredFile(fileName, segmentId, isCompound))
                    continue;

                var filePath = Path.Combine(directoryPath, fileName);
                var fieldName = TryGetVectorFieldName(basePath, filePath, segmentInfo);
                var isCompoundMember = isCompound && !directory.FileExists(fileName);
                if (TryInspectFile(
                    fileSource,
                    fileName,
                    segmentId,
                    fieldName,
                    options,
                    issues,
                    isCompoundMember ? CodecPhysicalLocationKind.CompoundMember : CodecPhysicalLocationKind.LooseFile,
                    isCompoundMember ? compoundFileName : fileName,
                    isCompoundMember ? compoundFileName : null,
                    out var inventory))
                    files.Add(inventory);
            }

            ValidateCodecFamilies(fileSource, files, segmentId, options, issues);
        }
        catch (Exception ex) when (isCompound && ex is IOException or InvalidDataException)
        {
            issues.Add(CreateIssue(
                IndexCheckSeverity.Error,
                IndexCheckIssueCodes.InvalidCodecMagic,
                $"Cannot inspect compound segment '{compoundFileName}': {ex.Message}",
                compoundFileName,
                segmentId,
                false));
        }
        finally
        {
            fileSource?.Dispose();
        }

        return new SegmentFormatInventory
        {
            SegmentId = segmentId,
            DocCount = segmentInfo?.DocCount,
            LiveDocCount = segmentInfo?.LiveDocCount,
            CommitGeneration = segmentInfo?.CommitGeneration,
            DelGeneration = segmentInfo?.DelGeneration,
            Files = files,
            MissingFiles = missingFiles,
            Warnings = warnings
        };
    }

    private static List<string> FindSegmentFiles(string directoryPath, string segmentId)
    {
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in FileOpenRetry.GetFiles(directoryPath, segmentId + ".*"))
            files.Add(file);
        foreach (var file in FileOpenRetry.GetFiles(directoryPath, segmentId + "_gen_*.del"))
            files.Add(file);
        foreach (var file in FileOpenRetry.GetFiles(directoryPath, segmentId + "_v_*.*"))
            files.Add(file);

        var result = files.ToList();
        result.Sort(StringComparer.OrdinalIgnoreCase);
        return result;
    }

    private static IReadOnlyList<CodecFileInventory> InspectOrphanFiles(
        MMapDirectory directory,
        HashSet<string> referencedFiles,
        IndexFormatInspectionOptions options,
        List<IndexCheckIssue> issues)
    {
        var directoryPath = directory.DirectoryPath;
        if (!FileOpenRetry.DirectoryExists(directoryPath))
            return [];

        var orphans = new List<CodecFileInventory>();
        foreach (var filePath in FileOpenRetry.GetFiles(directoryPath))
        {
            if (referencedFiles.Contains(filePath))
                continue;

            var fileName = Path.GetFileName(filePath);
            if (fileName.StartsWith("segments_", StringComparison.OrdinalIgnoreCase) ||
                fileName.StartsWith("stats_", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(fileName, "write.lock", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (TryInspectDirectFile(directory, fileName, null, null, options, issues, out var inventory))
                orphans.Add(inventory);
        }

        return orphans;
    }

    private static bool TryInspectFile(
        ISegmentFileSource source,
        string fileName,
        string? segmentId,
        string? fieldName,
        IndexFormatInspectionOptions options,
        List<IndexCheckIssue> issues,
        CodecPhysicalLocationKind physicalLocation,
        string physicalFileName,
        string? compoundFileName,
        out CodecFileInventory inventory)
        => TryInspectFile(
            fileName,
            segmentId,
            fieldName,
            options,
            issues,
            () => source.GetFileLength(fileName),
            () => source.OpenInput(fileName),
            physicalLocation,
            physicalFileName,
            compoundFileName,
            out inventory);

    private static bool TryInspectDirectFile(
        MMapDirectory directory,
        string fileName,
        string? segmentId,
        string? fieldName,
        IndexFormatInspectionOptions options,
        List<IndexCheckIssue> issues,
        out CodecFileInventory inventory)
        => TryInspectFile(
            fileName,
            segmentId,
            fieldName,
            options,
            issues,
            () => FileOpenRetry.GetFileLength(Path.Combine(directory.DirectoryPath, fileName)),
            () => directory.OpenInput(fileName),
            fileName.EndsWith(".cfs", StringComparison.OrdinalIgnoreCase)
                ? CodecPhysicalLocationKind.CompoundContainer
                : CodecPhysicalLocationKind.LooseFile,
            fileName,
            null,
            out inventory);

    private static bool TryInspectFile(
        string fileName,
        string? segmentId,
        string? fieldName,
        IndexFormatInspectionOptions options,
        List<IndexCheckIssue> issues,
        Func<long> getLength,
        Func<IndexInput> openInput,
        CodecPhysicalLocationKind physicalLocation,
        string physicalFileName,
        string? compoundFileName,
        out CodecFileInventory inventory)
    {
        var catalog = options.Catalog ?? throw new ArgumentException("The codec catalogue cannot be null.", nameof(options));
        if (!catalog.TryMatchFile(fileName, out var descriptor) || descriptor is null)
        {
            return TryInspectUnknownCanonicalFile(
                fileName,
                segmentId,
                fieldName,
                options,
                issues,
                getLength,
                openInput,
                catalog,
                physicalLocation,
                physicalFileName,
                compoundFileName,
                out inventory);
        }

        var extension = GetCodecExtension(fileName);
        var length = options.IncludeFileSizes ? getLength() : (long?)null;
        if (extension.Equals(".cfs", StringComparison.OrdinalIgnoreCase))
        {
            return TryInspectCompoundContainer(
                fileName,
                segmentId,
                descriptor,
                length,
                openInput,
                issues,
                catalog,
                physicalLocation,
                physicalFileName,
                out inventory);
        }

        var family = catalog.GetFamily(descriptor.FamilyId);
        var currentVersion = descriptor.CurrentFormatVersion;
        if (!currentVersion.HasValue)
        {
            inventory = new CodecFileInventory
            {
                FileName = fileName,
                Extension = extension,
                CodecName = descriptor.DisplayName,
                FormatId = descriptor.FormatId,
                FamilyId = descriptor.FamilyId,
                FamilyName = family.DisplayName,
                FrameKind = CodecFileFrameKind.External,
                FrameVersion = null,
                FormatVersion = null,
                CurrentFormatVersion = null,
                MagicStatus = CodecMagicStatus.NotApplicable,
                ChecksumAlgorithm = null,
                ChecksumStatus = CodecChecksumStatus.NotApplicable,
                IsSupported = true,
                IsCurrent = true,
                Length = length,
                SegmentId = segmentId,
                FieldName = fieldName,
                PhysicalLocation = physicalLocation,
                PhysicalFileName = physicalFileName,
                CompoundFileName = compoundFileName,
                IsKnownFormat = true
            };
            return true;
        }

        var magicStatus = CodecMagicStatus.NotApplicable;
        var frameKind = CodecFileFrameKind.Unknown;
        int? frameVersion = null;
        int? version = null;
        CodecFileChecksumAlgorithm? checksumAlgorithm = null;
        var checksumStatus = CodecChecksumStatus.NotApplicable;
        CodecFileErrorCode? errorCode = null;
        var detectedFormatId = descriptor.FormatId;
        var detectedFamilyId = descriptor.FamilyId;
        var detectedFamilyName = family.DisplayName;
        var isKnownFormat = true;
        try
        {
            using var input = openInput();
            if (HasCanonicalFrameMagic(input))
            {
                magicStatus = CodecMagicStatus.Valid;
                frameKind = CodecFileFrameKind.Canonical;
                using var session = CodecFileReader.Open(input);
                detectedFormatId = session.Metadata.FormatId;
                frameVersion = session.Metadata.FrameVersion;
                version = session.Metadata.FormatVersion;
                checksumAlgorithm = session.Metadata.ChecksumAlgorithm;
                checksumStatus = CodecChecksumStatus.NotVerified;

                if (!catalog.TryGetFile(detectedFormatId, out var declaredDescriptor) || declaredDescriptor is null)
                {
                    isKnownFormat = false;
                    detectedFamilyId = null;
                    detectedFamilyName = null;
                    errorCode = CodecFileErrorCode.UnknownFormat;
                    issues.Add(CreateIssue(
                        IndexCheckSeverity.Error,
                        IndexCheckIssueCodes.UnknownCodecFormat,
                        $"Codec file '{fileName}' declares unregistered format '{detectedFormatId}'.",
                        fileName,
                        segmentId,
                        false));
                }
                else if (!detectedFormatId.Equals(descriptor.FormatId, StringComparison.Ordinal))
                {
                    errorCode = CodecFileErrorCode.FormatMismatch;
                    issues.Add(CreateIssue(
                        IndexCheckSeverity.Error,
                        IndexCheckIssueCodes.CodecFormatMismatch,
                        $"Codec file '{fileName}' declares format '{detectedFormatId}', but its logical file role requires '{descriptor.FormatId}'.",
                        fileName,
                        segmentId,
                        false));
                }
                else if (descriptor.ChecksumPolicy == CodecChecksumPolicy.XxHash64 &&
                         checksumAlgorithm != CodecFileChecksumAlgorithm.XxHash64)
                {
                    errorCode = CodecFileErrorCode.UnsupportedChecksumAlgorithm;
                    issues.Add(CreateIssue(
                        IndexCheckSeverity.Error,
                        IndexCheckIssueCodes.InvalidCodecFrame,
                        $"Codec file '{fileName}' uses checksum algorithm '{checksumAlgorithm}', but its catalogue descriptor requires xxHash64.",
                        fileName,
                        segmentId,
                        false));
                }

                if (options.IncludeChecksums)
                {
                    try
                    {
                        session.ValidateChecksum();
                        checksumStatus = CodecChecksumStatus.Valid;
                    }
                    catch (CodecFileException ex) when (ex.ErrorCode == CodecFileErrorCode.ChecksumMismatch)
                    {
                        checksumStatus = CodecChecksumStatus.Invalid;
                        errorCode = ex.ErrorCode;
                        AddCodecIssue(issues, ex, fileName, segmentId, descriptor.DisplayName, catalog);
                    }

                    if (errorCode is null &&
                        detectedFormatId.Equals(descriptor.FormatId, StringComparison.Ordinal) &&
                        descriptor.ValidationHandler is not null &&
                        descriptor.SupportedVersions.Any(candidate => candidate.Version == version && candidate.IsReadable))
                    {
                        using var bodyInput = session.OpenBodyInput();
                        ValidateDescriptorBody(descriptor, bodyInput, fileName, session.Metadata);
                    }
                }
            }
            else
            {
                OpenLegacyFrame(input, descriptor, out frameKind, out version);
                if (options.IncludeChecksums &&
                    descriptor.ValidationHandler is not null &&
                    descriptor.SupportedVersions.Any(candidate => candidate.Version == version && candidate.IsReadable))
                {
                    input.Seek(0);
                    using var bodySession = CodecFileReader.OpenSupported(input, descriptor);
                    using var bodyInput = bodySession.OpenBodyInput();
                    ValidateDescriptorBody(descriptor, bodyInput, fileName, metadata: null);
                }
            }
        }
        catch (CodecFileException ex)
        {
            errorCode = ex.ErrorCode;
            frameVersion ??= ex.FrameVersion;
            version ??= ex.FormatVersion;
            magicStatus = ex.ErrorCode == CodecFileErrorCode.InvalidMagic
                ? CodecMagicStatus.Invalid
                : magicStatus == CodecMagicStatus.NotApplicable
                    ? CodecMagicStatus.Unknown
                    : magicStatus;
            AddCodecIssue(issues, ex, fileName, segmentId, descriptor.DisplayName, catalog);
        }
        catch (Exception ex) when (ex is IOException or EndOfStreamException or InvalidDataException)
        {
            errorCode = CodecFileErrorCode.InvalidMagic;
            magicStatus = CodecMagicStatus.Unknown;
            issues.Add(CreateIssue(
                IndexCheckSeverity.Error,
                IndexCheckIssueCodes.InvalidCodecMagic,
                $"Cannot read {descriptor.DisplayName} header from '{fileName}': {ex.Message}",
                fileName,
                segmentId,
                false));
        }

        var declaredVersion = version.HasValue
            ? descriptor.SupportedVersions.FirstOrDefault(candidate => candidate.Version == version.Value)
            : null;
        var isSupported = isKnownFormat &&
                          errorCode is null &&
                          version.HasValue &&
                          declaredVersion?.IsReadable == true;
        if (errorCode is null && version.HasValue && declaredVersion?.IsReadable != true)
        {
            var isFuture = version.Value > currentVersion.Value;
            issues.Add(CreateIssue(
                IndexCheckSeverity.Error,
                isFuture ? IndexCheckIssueCodes.UnsupportedFutureCodecVersion : IndexCheckIssueCodes.UnsupportedCodecVersion,
                $"Unsupported {descriptor.DisplayName} format version {version}; this build supports up to version {currentVersion}.",
                fileName,
                segmentId,
                false));
            errorCode = CodecFileErrorCode.UnsupportedFormatVersion;
        }

        inventory = new CodecFileInventory
        {
            FileName = fileName,
            Extension = extension,
            CodecName = descriptor.DisplayName,
            FormatId = detectedFormatId,
            FamilyId = detectedFamilyId,
            FamilyName = detectedFamilyName,
            FrameKind = frameKind,
            FrameVersion = frameVersion,
            FormatVersion = version,
            CurrentFormatVersion = currentVersion,
            MagicStatus = magicStatus,
            ChecksumAlgorithm = checksumAlgorithm,
            ChecksumStatus = checksumStatus,
            IsSupported = isSupported,
            IsCurrent = isSupported && frameKind == CodecFileFrameKind.Canonical && version == currentVersion,
            Length = length,
            SegmentId = segmentId,
            FieldName = fieldName,
            PhysicalLocation = physicalLocation,
            PhysicalFileName = physicalFileName,
            CompoundFileName = compoundFileName,
            IsKnownFormat = isKnownFormat,
            ErrorCode = errorCode
        };
        return true;
    }

    private static bool HasCanonicalFrameMagic(IndexInput input)
    {
        if (input.Length - input.Position < sizeof(int))
            return false;

        var start = input.Position;
        var magic = unchecked((uint)input.ReadInt32());
        input.Seek(start);
        return magic == CodecFileWriter.Magic;
    }

    private static void ValidateDescriptorBody(
        CodecFileDescriptor descriptor,
        IndexInput bodyInput,
        string fileName,
        CodecFrameMetadata? metadata)
    {
        try
        {
            descriptor.ValidationHandler!.Validate(bodyInput);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException)
        {
            throw new CodecFileException(
                CodecFileErrorCode.SemanticValidationFailure,
                $"Codec body validation failed for '{fileName}': {ex.Message}",
                fileName,
                descriptor.FormatId,
                metadata?.FrameVersion,
                metadata?.FormatVersion,
                bodyInput.Position,
                ex);
        }
    }

    private static void ValidateCodecFamilies(
        ISegmentFileSource source,
        IReadOnlyList<CodecFileInventory> files,
        string segmentId,
        IndexFormatInspectionOptions options,
        List<IndexCheckIssue> issues)
    {
        if (!options.IncludeChecksums)
            return;

        foreach (var family in options.Catalog.Families)
        {
            if (family.ValidationCoordinator is null)
                continue;

            var familyFiles = files
                .Where(file => file.FamilyId == family.FamilyId &&
                               file.FormatId is not null &&
                               file.IsSupported &&
                               file.ErrorCode is null &&
                               file.PhysicalLocation != CodecPhysicalLocationKind.CompoundContainer)
                .ToArray();
            if (familyFiles.Length == 0)
                continue;

            var bodyInputs = new Dictionary<string, IndexInput>(StringComparer.Ordinal);
            try
            {
                foreach (var file in familyFiles)
                {
                    if (!bodyInputs.TryAdd(file.FormatId!, OpenCodecBody(source, file.FileName, options.Catalog.GetFile(file.FormatId!))))
                    {
                        throw new InvalidDataException(
                            $"Codec family '{family.FamilyId}' has multiple logical files for format '{file.FormatId}', which cannot be coordinated by format ID.");
                    }
                }

                family.ValidationCoordinator.Validate(bodyInputs);
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException)
            {
                issues.Add(CreateIssue(
                    IndexCheckSeverity.Error,
                    IndexCheckIssueCodes.CodecSemanticValidationFailure,
                    $"Codec family validation failed for '{family.DisplayName}' in segment '{segmentId}': {ex.Message}",
                    null,
                    segmentId,
                    false));
            }
            finally
            {
                foreach (var input in bodyInputs.Values)
                    input.Dispose();
            }
        }
    }

    private static IndexInput OpenCodecBody(
        ISegmentFileSource source,
        string fileName,
        CodecFileDescriptor descriptor)
    {
        using var input = source.OpenInput(fileName);
        using var session = CodecFileReader.OpenSupported(input, descriptor);
        return session.OpenBodyInput();
    }

    private static void OpenLegacyFrame(
        IndexInput input,
        CodecFileDescriptor descriptor,
        out CodecFileFrameKind frameKind,
        out int? formatVersion)
    {
        long start = input.Position;
        var headerlessVersions = descriptor.SupportedVersions
            .Where(static candidate => candidate.IsReadable &&
                (candidate.LegacyFraming & CodecLegacyFraming.Headerless) != 0)
            .ToArray();
        if (headerlessVersions.Length == 1)
        {
            frameKind = CodecFileFrameKind.LegacyHeaderless;
            formatVersion = headerlessVersions[0].Version;
            return;
        }

        if (descriptor.FormatId is "leancorpus.postings.data" or
            "leancorpus.stored-fields.data" or
            "leancorpus.stored-fields.index")
        {
            if (input.Length - start < sizeof(byte))
                throw new CodecFileException(
                    CodecFileErrorCode.TruncatedHeader,
                    $"Legacy codec format '{descriptor.FormatId}' has a truncated custom header.",
                    formatId: descriptor.FormatId,
                    byteOffset: start);

            int version = input.ReadByte();
            input.Seek(start);
            formatVersion = version;
            if (version == 1)
            {
                using var session = LegacyCodecFileReader.Open(input, descriptor);
                frameKind = session.Metadata.FrameKind == LegacyCodecFrameKind.Envelope
                    ? CodecFileFrameKind.LegacyEnvelope
                    : CodecFileFrameKind.LegacyTrailer;
                return;
            }

            var supported = descriptor.SupportedVersions.FirstOrDefault(candidate => candidate.Version == version);
            if (supported is null || !supported.IsReadable)
                throw new CodecFileException(
                    CodecFileErrorCode.UnsupportedFormatVersion,
                    $"Legacy codec format '{descriptor.FormatId}' version {version} is not readable.",
                    formatId: descriptor.FormatId,
                    formatVersion: version,
                    byteOffset: start);

            if ((supported.LegacyFraming & CodecLegacyFraming.CodecKitTrailer) != 0 &&
                HasLegacyTrailer(input, start))
            {
                frameKind = CodecFileFrameKind.LegacyTrailer;
                return;
            }

            if ((supported.LegacyFraming & CodecLegacyFraming.CustomHeader) != 0)
            {
                frameKind = CodecFileFrameKind.LegacyCustomHeader;
                return;
            }

            throw new CodecFileException(
                CodecFileErrorCode.UnsupportedFormatVersion,
                $"Codec format '{descriptor.FormatId}' version {version} does not support its detected legacy framing.",
                formatId: descriptor.FormatId,
                formatVersion: version,
                byteOffset: start);
        }

        using var legacy = LegacyCodecFileReader.Open(input, descriptor);
        formatVersion = legacy.Metadata.FormatVersion;
        frameKind = legacy.Metadata.FrameKind == LegacyCodecFrameKind.Envelope
            ? CodecFileFrameKind.LegacyEnvelope
            : CodecFileFrameKind.LegacyTrailer;
    }

    private static bool HasLegacyTrailer(IndexInput input, long start)
    {
        if (input.Length - start < sizeof(byte) + sizeof(long))
            return false;

        long originalPosition = input.Position;
        try
        {
            input.Seek(input.Length - sizeof(long));
            long bodyLength = input.ReadInt64();
            return bodyLength >= 0 && sizeof(byte) + bodyLength + sizeof(long) == input.Length - start;
        }
        finally
        {
            input.Seek(originalPosition);
        }
    }

    private static bool TryInspectUnknownCanonicalFile(
        string fileName,
        string? segmentId,
        string? fieldName,
        IndexFormatInspectionOptions options,
        List<IndexCheckIssue> issues,
        Func<long> getLength,
        Func<IndexInput> openInput,
        CodecCatalog catalog,
        CodecPhysicalLocationKind physicalLocation,
        string physicalFileName,
        string? compoundFileName,
        out CodecFileInventory inventory)
    {
        using var input = openInput();
        if (!HasCanonicalFrameMagic(input))
        {
            inventory = null!;
            return false;
        }

        string? formatId = null;
        int? frameVersion = null;
        int? formatVersion = null;
        CodecFileChecksumAlgorithm? checksumAlgorithm = null;
        var checksumStatus = CodecChecksumStatus.Unknown;
        CodecFileErrorCode? errorCode = null;
        try
        {
            using var session = CodecFileReader.Open(input);
            formatId = session.Metadata.FormatId;
            frameVersion = session.Metadata.FrameVersion;
            formatVersion = session.Metadata.FormatVersion;
            checksumAlgorithm = session.Metadata.ChecksumAlgorithm;
            checksumStatus = CodecChecksumStatus.NotVerified;
            if (options.IncludeChecksums)
            {
                session.ValidateChecksum();
                checksumStatus = CodecChecksumStatus.Valid;
            }
        }
        catch (CodecFileException ex)
        {
            errorCode = ex.ErrorCode;
            formatId = ex.FormatId;
            frameVersion = ex.FrameVersion;
            formatVersion = ex.FormatVersion;
            checksumStatus = ex.ErrorCode == CodecFileErrorCode.ChecksumMismatch
                ? CodecChecksumStatus.Invalid
                : CodecChecksumStatus.Unknown;
            AddCodecIssue(issues, ex, fileName, segmentId, "codec file", catalog);
        }

        catalog.TryGetFile(formatId ?? string.Empty, out var declaredDescriptor);
        CodecFamilyDescriptor? family = null;
        if (declaredDescriptor is not null)
            catalog.TryGetFamily(declaredDescriptor.FamilyId, out family);

        if (errorCode is null)
        {
            errorCode = declaredDescriptor is null
                ? CodecFileErrorCode.UnknownFormat
                : CodecFileErrorCode.FormatMismatch;
            issues.Add(CreateIssue(
                IndexCheckSeverity.Error,
                declaredDescriptor is null ? IndexCheckIssueCodes.UnknownCodecFormat : IndexCheckIssueCodes.CodecFormatMismatch,
                declaredDescriptor is null
                    ? $"Codec file '{fileName}' declares unregistered format '{formatId}'."
                    : $"Codec file '{fileName}' declares registered format '{formatId}', but no catalogue file matcher claims its logical name.",
                fileName,
                segmentId,
                false));
        }

        inventory = new CodecFileInventory
        {
            FileName = fileName,
            Extension = GetCodecExtension(fileName),
            CodecName = declaredDescriptor?.DisplayName ?? formatId ?? "Unknown codec format",
            FormatId = formatId,
            FamilyId = declaredDescriptor?.FamilyId,
            FamilyName = family?.DisplayName,
            FrameKind = CodecFileFrameKind.Canonical,
            FrameVersion = frameVersion,
            FormatVersion = formatVersion,
            CurrentFormatVersion = declaredDescriptor?.CurrentFormatVersion,
            MagicStatus = CodecMagicStatus.Valid,
            ChecksumAlgorithm = checksumAlgorithm,
            ChecksumStatus = checksumStatus,
            IsSupported = false,
            IsCurrent = false,
            Length = options.IncludeFileSizes ? getLength() : null,
            SegmentId = segmentId,
            FieldName = fieldName,
            PhysicalLocation = physicalLocation,
            PhysicalFileName = physicalFileName,
            CompoundFileName = compoundFileName,
            IsKnownFormat = formatId is null || declaredDescriptor is not null,
            ErrorCode = errorCode
        };
        return true;
    }

    private static bool TryInspectCompoundContainer(
        string fileName,
        string? segmentId,
        CodecFileDescriptor descriptor,
        long? length,
        Func<IndexInput> openInput,
        List<IndexCheckIssue> issues,
        CodecCatalog catalog,
        CodecPhysicalLocationKind physicalLocation,
        string physicalFileName,
        out CodecFileInventory inventory)
    {
        var magicStatus = CodecMagicStatus.Unknown;
        int? version = null;
        try
        {
            using var input = openInput();
            if (input.ReadInt32() != CompoundFileWriter.Magic)
                throw new InvalidDataException("The compound container magic is invalid.");

            magicStatus = CodecMagicStatus.Valid;
            version = input.ReadInt32();
        }
        catch (Exception ex) when (ex is IOException or EndOfStreamException or InvalidDataException)
        {
            issues.Add(CreateIssue(
                IndexCheckSeverity.Error,
                IndexCheckIssueCodes.InvalidCodecMagic,
                $"Cannot read compound container header from '{fileName}': {ex.Message}",
                fileName,
                segmentId,
                false));
        }

        var currentVersion = CompoundFileWriter.Version;
        var isSupported = magicStatus == CodecMagicStatus.Valid && version == currentVersion;
        if (magicStatus == CodecMagicStatus.Valid && !isSupported)
        {
            var code = version > currentVersion
                ? IndexCheckIssueCodes.UnsupportedFutureCodecVersion
                : IndexCheckIssueCodes.UnsupportedCodecVersion;
            issues.Add(CreateIssue(
                IndexCheckSeverity.Error,
                code,
                $"Unsupported compound container version {version?.ToString() ?? "outside the supported range"}; this build supports version {currentVersion}.",
                fileName,
                segmentId,
                false));
        }

        inventory = new CodecFileInventory
        {
            FileName = fileName,
            Extension = ".cfs",
            CodecName = descriptor.DisplayName,
            FormatId = descriptor.FormatId,
            FamilyId = descriptor.FamilyId,
            FamilyName = catalog.GetFamily(descriptor.FamilyId).DisplayName,
            FrameKind = CodecFileFrameKind.Container,
            FrameVersion = version,
            FormatVersion = version,
            CurrentFormatVersion = currentVersion,
            MagicStatus = magicStatus,
            ChecksumAlgorithm = null,
            ChecksumStatus = CodecChecksumStatus.NotApplicable,
            IsSupported = isSupported,
            IsCurrent = isSupported,
            Length = length,
            SegmentId = segmentId,
            FieldName = null,
            PhysicalLocation = physicalLocation,
            PhysicalFileName = physicalFileName,
            CompoundFileName = null,
            IsKnownFormat = true,
            ErrorCode = isSupported ? null : CodecFileErrorCode.UnsupportedFrameVersion
        };
        return true;
    }

    private static bool IsRequiredFile(string fileName, string segmentId, bool isCompoundFile)
    {
        foreach (var extension in RequiredExtensions)
        {
            if (string.Equals(fileName, segmentId + extension, StringComparison.Ordinal))
                return true;
        }

        if (isCompoundFile && string.Equals(fileName, segmentId + ".cfs", StringComparison.Ordinal))
            return true;

        return false;
    }

    private static string GetCodecExtension(string filePath)
    {
        var fileName = Path.GetFileName(filePath);
        if (fileName.EndsWith(".stats.json", StringComparison.OrdinalIgnoreCase))
            return ".stats";

        return Path.GetExtension(filePath);
    }

    private static string? TryGetVectorFieldName(string basePath, string filePath, SegmentInfo? segmentInfo)
    {
        if (segmentInfo is null)
            return null;

        foreach (var vectorField in segmentInfo.VectorFields)
        {
            if (string.Equals(filePath, VectorFilePaths.VectorFile(basePath, vectorField.FieldName), StringComparison.OrdinalIgnoreCase) ||
                string.Equals(filePath, VectorFilePaths.HnswFile(basePath, vectorField.FieldName), StringComparison.OrdinalIgnoreCase))
            {
                return vectorField.FieldName;
            }
        }

        return null;
    }

    private static bool HasUnsupportedFutureFormat(
        IReadOnlyList<SegmentFormatInventory> segments,
        IReadOnlyList<CodecFileInventory> orphanFiles)
    {
        foreach (var segment in segments)
        {
            foreach (var file in segment.Files)
            {
                if (IsUnsupportedFuture(file))
                    return true;
            }
        }

        foreach (var file in orphanFiles)
        {
            if (IsUnsupportedFuture(file))
                return true;
        }

        return false;
    }

    private static bool IsUnsupportedFuture(CodecFileInventory file)
        => file.ErrorCode == CodecFileErrorCode.UnsupportedFrameVersion ||
           file.ErrorCode == CodecFileErrorCode.UnsupportedFormatVersion &&
           file.FormatVersion > file.CurrentFormatVersion;

    private static bool HasUnknownFormat(
        IReadOnlyList<SegmentFormatInventory> segments,
        IReadOnlyList<CodecFileInventory> orphanFiles)
        => segments.SelectMany(static segment => segment.Files)
               .Concat(orphanFiles)
               .Any(static file => !file.IsKnownFormat || file.ErrorCode == CodecFileErrorCode.UnknownFormat);

    private static void AddCodecIssue(
        List<IndexCheckIssue> issues,
        CodecFileException exception,
        string fileName,
        string? segmentId,
        string displayName,
        CodecCatalog catalog)
    {
        var code = exception.ErrorCode switch
        {
            CodecFileErrorCode.InvalidMagic => IndexCheckIssueCodes.InvalidCodecMagic,
            CodecFileErrorCode.UnsupportedFrameVersion => IndexCheckIssueCodes.UnsupportedCodecFrameVersion,
            CodecFileErrorCode.UnknownFormat => IndexCheckIssueCodes.UnknownCodecFormat,
            CodecFileErrorCode.UnsupportedFormatVersion when exception.FormatVersion.HasValue => IndexCheckIssueCodes.UnsupportedCodecVersion,
            CodecFileErrorCode.ChecksumMismatch => IndexCheckIssueCodes.CodecChecksumMismatch,
            CodecFileErrorCode.FormatMismatch => IndexCheckIssueCodes.CodecFormatMismatch,
            CodecFileErrorCode.SemanticValidationFailure => IndexCheckIssueCodes.CodecSemanticValidationFailure,
            _ => IndexCheckIssueCodes.InvalidCodecFrame
        };
        if (exception.ErrorCode == CodecFileErrorCode.UnsupportedFormatVersion &&
            catalog.TryGetFile(exception.FormatId ?? string.Empty, out var descriptor) &&
            descriptor?.CurrentFormatVersion is int currentVersion &&
            exception.FormatVersion > currentVersion)
        {
            code = IndexCheckIssueCodes.UnsupportedFutureCodecVersion;
        }

        issues.Add(CreateIssue(
            IndexCheckSeverity.Error,
            code,
            $"Cannot inspect {displayName} '{fileName}': {exception.Message}",
            fileName,
            segmentId,
            false));
    }

    private static IndexCheckIssue CreateIssue(
        IndexCheckSeverity severity,
        string code,
        string message,
        string? fileName,
        string? segmentId,
        bool isRepairable)
        => new()
        {
            Severity = severity,
            Code = code,
            Message = message,
            FileName = fileName,
            SegmentId = segmentId,
            IsRepairable = isRepairable,
            SuggestedActions = IndexRepairRecommendations.ForIssue(code)
        };
}
