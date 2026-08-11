using Rowles.LeanCorpus.Codecs;
using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Codecs.Hnsw;
using Rowles.LeanCorpus.Codecs.StoredFields;
using Rowles.LeanCorpus.Codecs.Vectors;
using Rowles.LeanCorpus.Index.Format;
using Rowles.LeanCorpus.Index.Migration;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Index;

/// <summary>
/// Validates the structural integrity of a LeanCorpus index stored in a <see cref="MMapDirectory"/>.
/// </summary>
public static class IndexValidator
{
    private static readonly string[] RequiredExtensions = [".seg", ".dic", ".pos", ".fdt", ".fdx", ".nrm"];

    /// <summary>
    /// Validates the latest commit in <paramref name="directory"/>.
    /// </summary>
    /// <param name="directory">The directory containing the index.</param>
    /// <returns>The validation result.</returns>
    public static IndexCheckResult Validate(MMapDirectory directory)
        => Check(directory);

    /// <summary>
    /// Checks the latest commit in <paramref name="directory"/>.
    /// </summary>
    /// <param name="directory">The directory containing the index.</param>
    /// <param name="options">Validation options. Defaults to shallow validation.</param>
    /// <returns>The validation result.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="directory"/> is <c>null</c>.</exception>
    public static IndexCheckResult Check(MMapDirectory directory, IndexCheckOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(directory);

        options ??= new IndexCheckOptions();
        var catalog = options.Catalog ?? throw new ArgumentException("The codec catalogue cannot be null.", nameof(options));
        var result = new IndexCheckResult();
        var dirPath = directory.DirectoryPath;
        var formatInventory = IndexFormatInspector.Inspect(directory, new IndexFormatInspectionOptions
        {
            IncludeOptionalSidecars = options.IncludeOptionalSidecars,
            IncludeFileSizes = false,
            IncludeChecksums = options.Deep,
            Catalog = catalog
        });
        AddFrameIssues(formatInventory, result);
        result.FilesChecked = formatInventory.Segments.Sum(static segment => segment.Files.Count);
        CheckMigrationMarker(dirPath, result);
        CheckStaleTemporaryFiles(dirPath, catalog, result);
        var commits = IndexFileInspector.FindCommitFiles(dirPath);
        if (commits.Count == 0)
        {
            result.AddIssue(
                IndexCheckSeverity.Error,
                IndexCheckIssueCodes.NoCommitFile,
                "No commit file (segments_N) found in directory.",
                null,
                null,
                false);
            return result;
        }

        var (generation, commitPath) = commits[0];
        result.CommitGeneration = generation;
        var commitData = IndexFileInspector.TryReadCommit(commitPath, generation, result);
        if (commitData is null)
            return result;

        foreach (var segmentId in commitData.Segments)
            CheckSegment(dirPath, segmentId, options, result);

        return result;
    }

    private static void CheckMigrationMarker(string dirPath, IndexCheckResult result)
    {
        var markerPath = Path.Combine(dirPath, IndexMigrationRecovery.MarkerFileName);
        if (!FileOpenRetry.FileExists(markerPath))
            return;

        try
        {
            var marker = IndexMigrationRecovery.GetState(dirPath);
            if (marker.State is not IndexMigrationState.None and not IndexMigrationState.Published)
            {
                result.AddIssue(
                    IndexCheckSeverity.Error,
                    IndexCheckIssueCodes.MigrationInProgress,
                    $"Migration marker is in state {marker.State}. Roll back or abandon migration before opening the index.",
                    IndexMigrationRecovery.MarkerFileName,
                    null,
                    true);
            }
        }
        catch (InvalidDataException ex)
        {
            result.AddIssue(
                IndexCheckSeverity.Error,
                IndexCheckIssueCodes.PartialMigrationMarkerState,
                $"Migration marker cannot be read: {ex.Message}",
                IndexMigrationRecovery.MarkerFileName,
                null,
                true);
        }
    }

    private static void CheckStaleTemporaryFiles(string dirPath, CodecCatalog catalog, IndexCheckResult result)
    {
        if (!FileOpenRetry.DirectoryExists(dirPath))
            return;

        foreach (var path in FileOpenRetry.EnumerateFiles(dirPath, "*.tmp"))
        {
            var fileName = Path.GetFileName(path);
            if (!IsRecognisedTemporaryFile(fileName, catalog))
                continue;

            result.AddIssue(
                IndexCheckSeverity.Warning,
                IndexCheckIssueCodes.StaleTemporaryFile,
                $"Recognised temporary file '{fileName}' was left by an interrupted write.",
                fileName,
                null,
                true);
        }
    }

    private static bool IsRecognisedTemporaryFile(string fileName, CodecCatalog catalog)
        => catalog.TryMatchTemporaryFile(fileName, out _);

    private static void CheckSegment(string dirPath, string segmentId, IndexCheckOptions options, IndexCheckResult result)
    {
        result.SegmentsChecked++;
        var basePath = Path.Combine(dirPath, segmentId);

        bool hasSegmentMetadata = IndexFileInspector.CheckRequiredFile(basePath + ".seg", segmentId, result);

        var segPath = basePath + ".seg";
        if (!hasSegmentMetadata)
            return;

        SegmentInfo info;
        try
        {
            info = SegmentInfo.ReadFrom(segPath);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or System.Text.Json.JsonException)
        {
            result.AddIssue(
                IndexCheckSeverity.Error,
                IndexCheckIssueCodes.SegmentMetadataUnreadable,
                $"Segment '{segmentId}' cannot read .seg metadata: {ex.Message}",
                Path.GetFileName(segPath),
                segmentId,
                false);
            return;
        }

        if (!string.IsNullOrEmpty(info.SegmentId) && !string.Equals(info.SegmentId, segmentId, StringComparison.Ordinal))
        {
            result.AddIssue(
                IndexCheckSeverity.Error,
                IndexCheckIssueCodes.SegmentIdMismatch,
                $"Segment metadata ID '{info.SegmentId}' does not match referenced segment ID '{segmentId}'.",
                Path.GetFileName(segPath),
                segmentId,
                false);
        }

        if (info.DocCount < 0)
        {
            result.AddIssue(
                IndexCheckSeverity.Error,
                IndexCheckIssueCodes.InvalidDocCount,
                $"Segment '{segmentId}' has invalid DocCount={info.DocCount}.",
                Path.GetFileName(segPath),
                segmentId,
                false);
        }

        if (info.LiveDocCount < 0 || info.LiveDocCount > info.DocCount)
        {
            result.AddIssue(
                IndexCheckSeverity.Error,
                IndexCheckIssueCodes.InvalidLiveDocCount,
                $"Segment '{segmentId}' has LiveDocCount={info.LiveDocCount}, outside [0,{info.DocCount}].",
                Path.GetFileName(segPath),
                segmentId,
                false);
        }

        if (info.IsCompoundFile)
        {
            result.DocumentsChecked += Math.Max(info.DocCount, 0);
            ValidateCompoundFile(dirPath, segmentId, result);
            CheckDeletionGeneration(basePath, segmentId, info, options, result);
            RunCompoundDeepChecks(dirPath, info, options, result);
            return;
        }

        for (int i = 1; i < RequiredExtensions.Length; i++)
            IndexFileInspector.CheckRequiredFile(basePath + RequiredExtensions[i], segmentId, result);

        result.DocumentsChecked += Math.Max(info.DocCount, 0);
        CheckStoredFields(basePath, segmentId, info, result);
        CheckDeletionGeneration(basePath, segmentId, info, options, result);
        CheckVectors(basePath, segmentId, info, options, result);
        RunDeepChecks(directoryPath: dirPath, basePath, info, options, result);
    }

    private static void AddFrameIssues(IndexFormatInventory inventory, IndexCheckResult result)
    {
        foreach (var issue in inventory.Issues)
        {
            if (issue.Code is IndexCheckIssueCodes.InvalidCodecMagic or
                IndexCheckIssueCodes.UnsupportedCodecVersion or
                IndexCheckIssueCodes.UnsupportedFutureCodecVersion or
                IndexCheckIssueCodes.UnsupportedCodecFrameVersion or
                IndexCheckIssueCodes.UnknownCodecFormat or
                IndexCheckIssueCodes.CodecChecksumMismatch or
                IndexCheckIssueCodes.CodecFormatMismatch or
                IndexCheckIssueCodes.InvalidCodecFrame or
                IndexCheckIssueCodes.CodecSemanticValidationFailure)
            {
                result.AddIssue(issue);
            }
        }
    }

    private static void ValidateCompoundFile(string directoryPath, string segmentId, IndexCheckResult result)
    {
        string compoundPath = Path.Combine(directoryPath, segmentId + ".cfs");
        if (!IndexFileInspector.CheckRequiredFile(compoundPath, segmentId, result))
            return;

        try
        {
            using var compoundDirectory = new MMapDirectory(directoryPath);
            using var compound = CompoundFileReader.Open(compoundDirectory, segmentId + ".cfs");
            foreach (var extension in new[] { ".dic", ".pos", ".fdt", ".fdx", ".nrm" })
            {
                if (!compound.HasFile(segmentId + extension))
                {
                    result.AddIssue(
                        IndexCheckSeverity.Error,
                        IndexCheckIssueCodes.RequiredFileMissing,
                        $"Compound segment '{segmentId}' is missing member '{segmentId + extension}'.",
                        segmentId + ".cfs",
                        segmentId,
                        false);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException)
        {
            result.AddIssue(
                IndexCheckSeverity.Error,
                IndexCheckIssueCodes.InvalidCodecMagic,
                $"Compound segment '{segmentId}' is unreadable: {ex.Message}",
                segmentId + ".cfs",
                segmentId,
                false);
        }
    }

    private static void CheckStoredFields(string basePath, string segmentId, SegmentInfo info, IndexCheckResult result)
    {
        CheckStoredFieldsCompression(basePath + ".fdt", segmentId, result);
        CheckStoredFieldsIndex(basePath + ".fdx", segmentId, info, result);
    }

    private static void CheckStoredFieldsCompression(string fdtPath, string segmentId, IndexCheckResult result)
    {
        if (!FileOpenRetry.FileExists(fdtPath))
            return;

        result.FilesChecked++;
        var fileName = Path.GetFileName(fdtPath);
        try
        {
            using var input = new IndexInput(fdtPath);
            using var frame = StoredFieldsCodecFiles.OpenData(input);
            input.ReadInt32();
            byte policyByte = input.ReadByte();
            if (!CompressionCodecRegistry.TryGet(policyByte, out _))
            {
                result.AddIssue(
                    IndexCheckSeverity.Error,
                    IndexCheckIssueCodes.UnregisteredCompressionPolicy,
                    $"Stored fields use unregistered compression policy byte {policyByte}.",
                    fileName,
                    segmentId,
                    false);
            }
        }
        catch (Exception ex) when (ex is IOException or EndOfStreamException or InvalidDataException)
        {
            result.AddIssue(
                IndexCheckSeverity.Error,
                IndexCheckIssueCodes.InvalidStoredFieldHeader,
                $"Cannot read stored fields data header from '{fileName}': {ex.Message}",
                fileName,
                segmentId,
                false);
        }
    }

    private static void CheckStoredFieldsIndex(string fdxPath, string segmentId, SegmentInfo info, IndexCheckResult result)
    {
        if (!FileOpenRetry.FileExists(fdxPath))
            return;

        result.FilesChecked++;
        var fileName = Path.GetFileName(fdxPath);
        try
        {
            using var input = new IndexInput(fdxPath);
            using var frame = StoredFieldsCodecFiles.OpenIndex(input);
            int blockSize = input.ReadInt32();
            int docCount = input.ReadInt32();
            int blockCount = input.ReadInt32();
            if (blockSize <= 0 || blockCount < 0 || docCount < 0 || docCount != info.DocCount)
            {
                result.AddIssue(
                    IndexCheckSeverity.Error,
                    IndexCheckIssueCodes.StoredFieldDocCountMismatch,
                    $"Stored fields index doc count {docCount} does not match segment DocCount {info.DocCount}.",
                    fileName,
                    segmentId,
                    false);
            }

            long previous = -1;
            for (int i = 0; i < blockCount; i++)
            {
                long current = input.ReadInt64();
                if (current < previous || current < 0)
                {
                    result.AddIssue(
                        IndexCheckSeverity.Error,
                        IndexCheckIssueCodes.InvalidStoredFieldOffsets,
                        $"Stored fields index contains invalid block offset {current} at block {i}.",
                        fileName,
                        segmentId,
                        false);
                    return;
                }

                previous = current;
            }
        }
        catch (Exception ex) when (ex is IOException or EndOfStreamException or InvalidDataException)
        {
            result.AddIssue(
                IndexCheckSeverity.Error,
                IndexCheckIssueCodes.InvalidStoredFieldHeader,
                $"Cannot read stored fields index header from '{fileName}': {ex.Message}",
                fileName,
                segmentId,
                false);
        }
    }

    private static void CheckDeletionGeneration(
        string basePath,
        string segmentId,
        SegmentInfo info,
        IndexCheckOptions options,
        IndexCheckResult result)
    {
        var delPath = info.DelGeneration is int generation
            ? Path.Combine(Path.GetDirectoryName(basePath)!, $"{segmentId}_gen_{generation}.del")
            : basePath + ".del";

        if (!FileOpenRetry.FileExists(delPath))
        {
            if (info.LiveDocCount < info.DocCount)
            {
                result.AddIssue(
                    IndexCheckSeverity.Error,
                    IndexCheckIssueCodes.DeletionFileMissing,
                    $"Segment '{segmentId}' has deleted documents but deletion file '{Path.GetFileName(delPath)}' is missing.",
                    Path.GetFileName(delPath),
                    segmentId,
                    true);
            }
            return;
        }

        result.FilesChecked++;
        if (options.Deep || options.VerifyLiveDocs)
            ValidateLiveDocs(delPath, segmentId, info, result);
    }

    private static void CheckVectors(string basePath, string segmentId, SegmentInfo info, IndexCheckOptions options, IndexCheckResult result)
    {
        foreach (var vectorField in info.VectorFields)
        {
            var vectorPath = VectorFilePaths.VectorFile(basePath, vectorField.FieldName);
            if (!FileOpenRetry.FileExists(vectorPath))
            {
                result.AddIssue(
                    IndexCheckSeverity.Error,
                    IndexCheckIssueCodes.VectorFileMissing,
                    $"Vector field '{vectorField.FieldName}' declares missing file '{Path.GetFileName(vectorPath)}'.",
                    Path.GetFileName(vectorPath),
                    segmentId,
                    true);
                continue;
            }

            CheckVectorHeader(vectorPath, segmentId, info, vectorField, result);

            if (vectorField.HasHnsw)
            {
                var hnswPath = VectorFilePaths.HnswFile(basePath, vectorField.FieldName);
                if (!FileOpenRetry.FileExists(hnswPath))
                {
                    result.AddIssue(
                        IndexCheckSeverity.Error,
                        IndexCheckIssueCodes.HnswFileMissing,
                        $"Vector field '{vectorField.FieldName}' declares missing HNSW file '{Path.GetFileName(hnswPath)}'.",
                        Path.GetFileName(hnswPath),
                        segmentId,
                        true);
                    continue;
                }

                CheckHnswHeader(hnswPath, segmentId, vectorField, result);
            }
        }
    }

    private static void CheckVectorHeader(
        string vectorPath,
        string segmentId,
        SegmentInfo info,
        VectorFieldInfo vectorField,
        IndexCheckResult result)
    {
        result.FilesChecked++;
        var fileName = Path.GetFileName(vectorPath);
        try
        {
            using var input = new IndexInput(vectorPath);
            using var frame = CodecFileReader.OpenSupported(input, VectorCodecFiles.Float32);
            int vectorCount = input.ReadInt32();
            int dimension = input.ReadInt32();
            if (vectorCount != info.DocCount)
            {
                result.AddIssue(
                    IndexCheckSeverity.Error,
                    IndexCheckIssueCodes.VectorCountMismatch,
                    $"Vector file has {vectorCount} rows but segment DocCount is {info.DocCount}.",
                    fileName,
                    segmentId,
                    false);
            }

            if (dimension != vectorField.Dimension)
            {
                result.AddIssue(
                    IndexCheckSeverity.Error,
                    IndexCheckIssueCodes.VectorDimensionMismatch,
                    $"Vector file dimension {dimension} does not match declared dimension {vectorField.Dimension}.",
                    fileName,
                    segmentId,
                    false);
            }
        }
        catch (Exception ex) when (ex is IOException or EndOfStreamException)
        {
            result.AddIssue(
                IndexCheckSeverity.Error,
                IndexCheckIssueCodes.InvalidVectorHeader,
                $"Cannot read vector header from '{fileName}': {ex.Message}",
                fileName,
                segmentId,
                false);
        }
    }

    private static void CheckHnswHeader(string hnswPath, string segmentId, VectorFieldInfo vectorField, IndexCheckResult result)
    {
        result.FilesChecked++;
        var fileName = Path.GetFileName(hnswPath);
        try
        {
            using var input = new IndexInput(hnswPath);
            using var frame = CodecFileReader.OpenSupported(input, VectorCodecFiles.Hnsw);
            int dimension = input.ReadInt32();
            bool normalised = input.ReadByte() != 0;
            if (dimension != vectorField.Dimension)
            {
                result.AddIssue(
                    IndexCheckSeverity.Error,
                    IndexCheckIssueCodes.HnswDimensionMismatch,
                    $"HNSW file dimension {dimension} does not match declared dimension {vectorField.Dimension}.",
                    fileName,
                    segmentId,
                    false);
            }

            if (normalised != vectorField.Normalised)
            {
                result.AddIssue(
                    IndexCheckSeverity.Error,
                    IndexCheckIssueCodes.HnswNormalisationMismatch,
                    $"HNSW normalisation flag {normalised} does not match declared value {vectorField.Normalised}.",
                    fileName,
                    segmentId,
                    false);
            }
        }
        catch (Exception ex) when (ex is IOException or EndOfStreamException)
        {
            result.AddIssue(
                IndexCheckSeverity.Error,
                IndexCheckIssueCodes.InvalidHnswHeader,
                $"Cannot read HNSW header from '{fileName}': {ex.Message}",
                fileName,
                segmentId,
                false);
        }
    }

    private static void RunDeepChecks(
        string directoryPath,
        string basePath,
        SegmentInfo info,
        IndexCheckOptions options,
        IndexCheckResult result)
    {
        if (options.Deep || options.VerifyDocValues)
            ValidateDocValuesDeep(basePath, info, result);
        if (options.Deep || options.VerifyStoredFields)
            ValidateStoredFieldsDeep(basePath, info, result);
        if (options.Deep || options.VerifyPostings)
            ValidatePostingsDeep(directoryPath, info, result);
        if (options.Deep || options.VerifyVectors)
            ValidateVectorsDeep(basePath, info, result);
        if (options.Deep || options.VerifyHnsw)
            ValidateHnswDeep(basePath, info, result);
        if (options.Deep)
            ValidateTermVectorsDeep(directoryPath, info, result);
    }

    private static void RunCompoundDeepChecks(
        string directoryPath,
        SegmentInfo info,
        IndexCheckOptions options,
        IndexCheckResult result)
    {
        if (options.Deep || options.VerifyDocValues)
            ValidateCompoundDocValuesDeep(directoryPath, info, result);
        if (options.Deep || options.VerifyStoredFields)
            ValidateCompoundStoredFieldsDeep(directoryPath, info, result);
        if (options.Deep || options.VerifyPostings)
            ValidatePostingsDeep(directoryPath, info, result);
        if (options.Deep || options.VerifyVectors)
            ValidateCompoundVectorsDeep(directoryPath, info, result);
        if (options.Deep || options.VerifyHnsw)
            ValidateCompoundHnswDeep(directoryPath, info, result);
        if (options.Deep)
            ValidateTermVectorsDeep(directoryPath, info, result);
    }

    private static void ValidateCompoundDocValuesDeep(
        string directoryPath,
        SegmentInfo info,
        IndexCheckResult result)
    {
        try
        {
            using var directory = new MMapDirectory(directoryPath);
            using var reader = new SegmentReader(directory, info);
            foreach (var fieldName in info.FieldNames)
            {
                ValidateDocValuesLength(reader.GetNumericDocValues(fieldName), info);
                ValidateDocValuesLength(reader.GetSortedDocValues(fieldName), info);
                ValidateDocValuesLength(reader.GetSortedSetDocValues(fieldName), info);
                ValidateDocValuesLength(reader.GetSortedNumericDocValues(fieldName), info);
                ValidateDocValuesLength(reader.GetBinaryDocValues(fieldName), info);
                ValidateDocValuesLength(reader.GetInt64DocValues(fieldName), info);
                ValidateDocValuesLength(reader.GetSortedInt64DocValues(fieldName), info);
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or EndOfStreamException or InvalidOperationException)
        {
            result.AddIssue(
                IndexCheckSeverity.Error,
                IndexCheckIssueCodes.DocValuesReadFailure,
                $"Cannot read compound DocValues for segment '{info.SegmentId}': {ex.Message}",
                info.SegmentId + ".cfs",
                info.SegmentId,
                false);
        }
    }

    private static void ValidateDocValuesLength(Array? values, SegmentInfo info)
    {
        if (values is not null && values.Length != info.DocCount)
            throw new InvalidDataException($"DocValues field length {values.Length} does not match segment DocCount {info.DocCount}.");
    }

    private static void ValidateCompoundStoredFieldsDeep(
        string directoryPath,
        SegmentInfo info,
        IndexCheckResult result)
    {
        try
        {
            using var directory = new MMapDirectory(directoryPath);
            using var reader = new SegmentReader(directory, info);
            for (int docId = 0; docId < info.DocCount; docId++)
                reader.GetStoredFieldValues(docId);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or EndOfStreamException or InvalidOperationException)
        {
            result.AddIssue(
                IndexCheckSeverity.Error,
                IndexCheckIssueCodes.StoredFieldsReadFailure,
                $"Cannot read compound stored fields for segment '{info.SegmentId}': {ex.Message}",
                info.SegmentId + ".cfs",
                info.SegmentId,
                false);
        }
    }

    private static void ValidateCompoundVectorsDeep(
        string directoryPath,
        SegmentInfo info,
        IndexCheckResult result)
    {
        try
        {
            using var directory = new MMapDirectory(directoryPath);
            using var reader = new SegmentReader(directory, info);
            foreach (var vectorField in info.VectorFields)
            {
                for (int docId = 0; docId < info.DocCount; docId++)
                {
                    var vector = reader.GetVector(vectorField.FieldName, docId);
                    if (vector is not null && vector.Length != vectorField.Dimension)
                        throw new InvalidDataException($"Vector dimension {vector.Length} does not match declared dimension {vectorField.Dimension}.");
                }
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or EndOfStreamException or InvalidOperationException)
        {
            result.AddIssue(
                IndexCheckSeverity.Error,
                IndexCheckIssueCodes.VectorReadFailure,
                $"Cannot read compound vectors for segment '{info.SegmentId}': {ex.Message}",
                info.SegmentId + ".cfs",
                info.SegmentId,
                false);
        }
    }

    private static void ValidateCompoundHnswDeep(
        string directoryPath,
        SegmentInfo info,
        IndexCheckResult result)
    {
        try
        {
            using var directory = new MMapDirectory(directoryPath);
            using var reader = new SegmentReader(directory, info);
            foreach (var vectorField in info.VectorFields)
            {
                if (vectorField.HasHnsw && reader.GetHnswGraph(vectorField.FieldName) is null)
                    throw new InvalidDataException($"HNSW graph for field '{vectorField.FieldName}' is missing.");
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or EndOfStreamException or InvalidOperationException)
        {
            result.AddIssue(
                IndexCheckSeverity.Error,
                IndexCheckIssueCodes.HnswReadFailure,
                $"Cannot read compound HNSW data for segment '{info.SegmentId}': {ex.Message}",
                info.SegmentId + ".cfs",
                info.SegmentId,
                false);
        }
    }

    private static void ValidateTermVectorsDeep(
        string directoryPath,
        SegmentInfo info,
        IndexCheckResult result)
    {
        try
        {
            using var directory = new MMapDirectory(directoryPath);
            using var reader = new SegmentReader(directory, info);
            for (int docId = 0; docId < info.DocCount; docId++)
                reader.GetTermVectors(docId);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or EndOfStreamException or InvalidOperationException)
        {
            result.AddIssue(
                IndexCheckSeverity.Error,
                IndexCheckIssueCodes.TermVectorsReadFailure,
                $"Cannot read term vectors for segment '{info.SegmentId}': {ex.Message}",
                info.SegmentId + ".tvd",
                info.SegmentId,
                false);
        }
    }

    private static void ValidateDocValuesDeep(string basePath, SegmentInfo info, IndexCheckResult result)
    {
        TryReadDocValues(basePath + ".dvn", info, result, static path => NumericDocValuesReader.Read(path).Values.Values.Select(static values => values.Length));
        TryReadDocValues(basePath + ".dvs", info, result, static path => SortedDocValuesReader.Read(path).Values.Values.Select(static values => values.Length));
        TryReadDocValues(basePath + ".dss", info, result, static path => SortedSetDocValuesReader.Read(path).Values.Select(static values => values.Length));
        TryReadDocValues(basePath + ".dsn", info, result, static path => SortedNumericDocValuesReader.Read(path).Values.Select(static values => values.Length));
        TryReadDocValues(basePath + ".dvb", info, result, static path => BinaryDocValuesReader.Read(path).Values.Select(static values => values.Length));
        TryReadDocValues(basePath + ".dvnl", info, result, static path => Int64DocValuesReader.Read(path).Values.Values.Select(static values => values.Length));
        TryReadDocValues(basePath + ".dsnl", info, result, static path => Int64SortedNumericDocValuesReader.Read(path).Values.Select(static values => values.Length));
    }

    private static void TryReadDocValues(
        string path,
        SegmentInfo info,
        IndexCheckResult result,
        Func<string, IEnumerable<int>> readLengths)
    {
        if (!FileOpenRetry.FileExists(path))
            return;

        string fileName = Path.GetFileName(path);
        try
        {
            foreach (int length in readLengths(path))
            {
                if (length != info.DocCount)
                {
                    result.AddIssue(
                        IndexCheckSeverity.Error,
                        IndexCheckIssueCodes.DocValuesDocCountMismatch,
                        $"DocValues file '{fileName}' has field length {length}, expected {info.DocCount}.",
                        fileName,
                        info.SegmentId,
                        false);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or EndOfStreamException)
        {
            result.AddIssue(
                IndexCheckSeverity.Error,
                IndexCheckIssueCodes.DocValuesReadFailure,
                $"Cannot read DocValues file '{fileName}': {ex.Message}",
                fileName,
                info.SegmentId,
                false);
        }
    }

    private static void ValidateStoredFieldsDeep(string basePath, SegmentInfo info, IndexCheckResult result)
    {
        string fileName = Path.GetFileName(basePath + ".fdt");
        try
        {
            using var reader = StoredFieldsReader.Open(basePath + ".fdt", basePath + ".fdx");
            for (int docId = 0; docId < info.DocCount; docId++)
                reader.ReadDocument(docId);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or EndOfStreamException or InvalidOperationException)
        {
            result.AddIssue(
                IndexCheckSeverity.Error,
                IndexCheckIssueCodes.StoredFieldsReadFailure,
                $"Cannot read stored fields for segment '{info.SegmentId}': {ex.Message}",
                fileName,
                info.SegmentId,
                false);
        }
    }

    private static void ValidatePostingsDeep(string directoryPath, SegmentInfo info, IndexCheckResult result)
    {
        try
        {
            using var directory = new MMapDirectory(directoryPath);
            using var reader = new SegmentReader(directory, info);
            foreach (var fieldName in info.FieldNames)
            {
                var terms = reader.GetAllTermsForField(fieldName + '\0');
                foreach (var (_, offset) in terms)
                {
                    using var postings = reader.GetPostingsEnumAtOffset(offset);
                    int previous = -1;
                    while (postings.MoveNext())
                    {
                        int docId = postings.DocId;
                        if (docId <= previous || docId < 0 || docId >= info.DocCount)
                            throw new InvalidDataException($"Invalid postings doc ID {docId} in segment '{info.SegmentId}'.");
                        previous = docId;
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or EndOfStreamException)
        {
            result.AddIssue(
                IndexCheckSeverity.Error,
                IndexCheckIssueCodes.PostingsReadFailure,
                $"Cannot validate postings for segment '{info.SegmentId}': {ex.Message}",
                info.SegmentId + ".pos",
                info.SegmentId,
                false);
        }
    }

    private static void ValidateLiveDocs(string delPath, string segmentId, SegmentInfo info, IndexCheckResult result)
    {
        var fileName = Path.GetFileName(delPath);
        try
        {
            var liveDocs = LiveDocs.Deserialise(delPath, info.DocCount);
            if (liveDocs.MaxDoc != info.DocCount || liveDocs.LiveCount != info.LiveDocCount)
            {
                result.AddIssue(
                    IndexCheckSeverity.Error,
                    IndexCheckIssueCodes.DeletionLiveCountMismatch,
                    $"Deletion file live count {liveDocs.LiveCount} does not match segment LiveDocCount {info.LiveDocCount}.",
                    fileName,
                    segmentId,
                    false);
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or EndOfStreamException)
        {
            result.AddIssue(
                IndexCheckSeverity.Error,
                IndexCheckIssueCodes.DeletionFileUnreadable,
                $"Cannot read deletion file '{fileName}': {ex.Message}",
                fileName,
                segmentId,
                false);
        }
    }

    private static void ValidateVectorsDeep(string basePath, SegmentInfo info, IndexCheckResult result)
    {
        foreach (var vectorField in info.VectorFields)
        {
            var vectorPath = VectorFilePaths.VectorFile(basePath, vectorField.FieldName);
            string fileName = Path.GetFileName(vectorPath);
            try
            {
                using var reader = VectorReader.Open(vectorPath);
                if (reader.VectorCount > 0)
                {
                    reader.ReadVector(0);
                    reader.ReadVector(reader.VectorCount - 1);
                }
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or EndOfStreamException)
            {
                result.AddIssue(
                    IndexCheckSeverity.Error,
                    IndexCheckIssueCodes.VectorReadFailure,
                    $"Cannot read vector file '{fileName}': {ex.Message}",
                    fileName,
                    info.SegmentId,
                    false);
            }
        }
    }

    private static void ValidateHnswDeep(string basePath, SegmentInfo info, IndexCheckResult result)
    {
        foreach (var vectorField in info.VectorFields)
        {
            if (!vectorField.HasHnsw)
                continue;

            var vectorPath = VectorFilePaths.VectorFile(basePath, vectorField.FieldName);
            var hnswPath = VectorFilePaths.HnswFile(basePath, vectorField.FieldName);
            string fileName = Path.GetFileName(hnswPath);
            try
            {
                using var vectorReader = VectorReader.Open(vectorPath);
                var source = new VectorReaderSource(vectorReader);
                HnswReader.Read(hnswPath, source, vectorField.Normalised);
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or EndOfStreamException)
            {
                result.AddIssue(
                    IndexCheckSeverity.Error,
                    IndexCheckIssueCodes.HnswReadFailure,
                    $"Cannot read HNSW file '{fileName}': {ex.Message}",
                    fileName,
                    info.SegmentId,
                    false);
            }
        }
    }
}
