using Rowles.LeanCorpus.Codecs;
using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Codecs.CodecKit.Codecs;
using Rowles.LeanCorpus.Codecs.CodecKit.Formats;
using Rowles.LeanCorpus.Codecs.Hnsw;
using Rowles.LeanCorpus.Codecs.StoredFields;
using Rowles.LeanCorpus.Codecs.Vectors;
using Rowles.LeanCorpus.Index.Migration;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Index;

/// <summary>
/// Validates the structural integrity of a LeanCorpus index stored in a <see cref="MMapDirectory"/>.
/// </summary>
public static class IndexValidator
{
    private static readonly string[] RequiredExtensions = [".seg", ".dic", ".pos", ".fdt", ".fdx", ".nrm"];

    private static readonly (string Extension, byte Version, ICodec<byte[]> Format, string FileType)[] HeaderChecks =
    [
        (".dic", CodecConstants.TermDictionaryVersion, CodecFormats.TermDictionary, "term dictionary (.dic)"),
        (".pos", CodecConstants.PostingsVersion, CodecFormats.Postings, "postings (.pos)"),
        (".nrm", CodecConstants.NormsVersion, CodecFormats.Norms, "norms (.nrm)"),
        (".dvn", CodecConstants.NumericDocValuesVersion, CodecFormats.NumericDocValues, "numeric doc values (.dvn)"),
        (".dvs", CodecConstants.SortedDocValuesVersion, CodecFormats.SortedDocValues, "sorted doc values (.dvs)"),
        (".dss", CodecConstants.SortedSetDocValuesVersion, CodecFormats.SortedSetDocValues, "sorted-set doc values (.dss)"),
        (".dsn", CodecConstants.SortedNumericDocValuesVersion, CodecFormats.SortedNumericDocValues, "sorted-numeric doc values (.dsn)"),
        (".dvb", CodecConstants.BinaryDocValuesVersion, CodecFormats.BinaryDocValues, "binary doc values (.dvb)"),
        (".bkd", CodecConstants.BKDVersion, CodecFormats.Bkd, "BKD tree (.bkd)"),
        (".fln", CodecConstants.FieldLengthVersion, CodecFormats.FieldLengths, "field lengths (.fln)"),
        (".tvd", CodecConstants.TermVectorsVersion, CodecFormats.TermVectors, "term vectors data (.tvd)"),
        (".tvx", CodecConstants.TermVectorsVersion, CodecFormats.TermVectors, "term vectors index (.tvx)")
    ];

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
        var result = new IndexCheckResult();
        var dirPath = directory.DirectoryPath;
        CheckMigrationMarker(dirPath, result);
        CheckStaleTemporaryFiles(dirPath, result);
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
        if (!File.Exists(markerPath))
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

    private static void CheckStaleTemporaryFiles(string dirPath, IndexCheckResult result)
    {
        if (!Directory.Exists(dirPath))
            return;

        foreach (var path in Directory.EnumerateFiles(dirPath, "*.tmp"))
        {
            var fileName = Path.GetFileName(path);
            if (!IsRecognisedTemporaryFile(fileName))
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

    private static bool IsRecognisedTemporaryFile(string fileName)
        => Codecs.CodecKit.Formats.CodecFormats.IsRecognisedTemporaryFile(fileName);

    private static void CheckSegment(string dirPath, string segmentId, IndexCheckOptions options, IndexCheckResult result)
    {
        result.SegmentsChecked++;
        var basePath = Path.Combine(dirPath, segmentId);

        foreach (var extension in RequiredExtensions)
            IndexFileInspector.CheckRequiredFile(basePath + extension, segmentId, result);

        var segPath = basePath + ".seg";
        if (!File.Exists(segPath))
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

        result.DocumentsChecked += Math.Max(info.DocCount, 0);
        CheckHeaders(basePath, segmentId, options, result);
        CheckStoredFields(basePath, segmentId, info, result);
        CheckDeletionGeneration(basePath, segmentId, info, options, result);
        CheckVectors(basePath, segmentId, info, options, result);
        RunDeepChecks(directoryPath: dirPath, basePath, info, options, result);
    }

    private static void CheckHeaders(string basePath, string segmentId, IndexCheckOptions options, IndexCheckResult result)
    {
        foreach (var (extension, version, format, fileType) in HeaderChecks)
        {
            if (!options.IncludeOptionalSidecars && !IsRequiredHeader(extension))
                continue;

            IndexFileInspector.CheckCodecHeader(basePath + extension, version, format, fileType, segmentId, result);
        }

        if (options.IncludeOptionalSidecars)
        {
            IndexFileInspector.CheckOptionalFile(basePath + ".num", segmentId, result);
            IndexFileInspector.CheckOptionalFile(basePath + ".pbs", segmentId, result);
        }
    }

    private static bool IsRequiredHeader(string extension)
        => extension is ".dic" or ".pos" or ".nrm";

    private static void CheckStoredFields(string basePath, string segmentId, SegmentInfo info, IndexCheckResult result)
    {
        CheckStoredFieldsCompression(basePath + ".fdt", segmentId, result);
        CheckStoredFieldsIndex(basePath + ".fdx", segmentId, info, result);
    }

    private static void CheckStoredFieldsCompression(string fdtPath, string segmentId, IndexCheckResult result)
    {
        if (!File.Exists(fdtPath))
            return;

        result.FilesChecked++;
        var fileName = Path.GetFileName(fdtPath);
        try
        {
            using var stream = FileOpenRetry.OpenReadDelete(fdtPath);
            using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: false);
            byte version;
            try
            {
                version = StoredFieldsFileHeader.ReadVersion(reader);
            }
            catch (Exception ex) when (ex is InvalidDataException or EndOfStreamException)
            {
                result.AddIssue(
                    IndexCheckSeverity.Error,
                    IndexCheckIssueCodes.InvalidStoredFieldHeader,
                    $"Invalid stored fields data file: {ex.Message}",
                    fileName,
                    segmentId,
                    false);
                return;
            }
            if (version > CodecConstants.StoredFieldsVersion)
            {
                try
                {
                    reader.ReadInt32(); // blockSize
                    reader.ReadByte();  // compression
                }
                catch (EndOfStreamException)
                {
                    result.AddIssue(
                        IndexCheckSeverity.Error,
                        IndexCheckIssueCodes.InvalidStoredFieldHeader,
                        $"Invalid stored fields data file: header is truncated.",
                        fileName,
                        segmentId,
                        false);
                    return;
                }

                result.AddIssue(
                    IndexCheckSeverity.Error,
                    IndexCheckIssueCodes.UnsupportedStoredFieldVersion,
                    $"Unsupported stored fields format version {version}; this build supports up to version {CodecConstants.StoredFieldsVersion}.",
                    fileName,
                    segmentId,
                    false);
                return;
            }
            reader.ReadInt32();
            byte policyByte = reader.ReadByte();
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
        catch (Exception ex) when (ex is IOException or EndOfStreamException)
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
        if (!File.Exists(fdxPath))
            return;

        result.FilesChecked++;
        var fileName = Path.GetFileName(fdxPath);
        try
        {
            using var stream = FileOpenRetry.OpenReadDelete(fdxPath);
            using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: false);

            byte version;
            try
            {
                version = StoredFieldsFileHeader.ReadVersion(reader);
            }
            catch (Exception ex) when (ex is InvalidDataException or EndOfStreamException)
            {
                result.AddIssue(
                    IndexCheckSeverity.Error,
                    IndexCheckIssueCodes.InvalidStoredFieldHeader,
                    $"Invalid stored fields index file: {ex.Message}",
                    fileName,
                    segmentId,
                    false);
                return;
            }
            if (version > CodecConstants.StoredFieldsVersion)
            {
                try
                {
                    reader.ReadInt32(); // blockSize
                    reader.ReadInt32(); // docCount
                    reader.ReadInt32(); // blockCount
                }
                catch (EndOfStreamException)
                {
                    result.AddIssue(
                        IndexCheckSeverity.Error,
                        IndexCheckIssueCodes.InvalidStoredFieldHeader,
                        $"Invalid stored fields index file: header is truncated.",
                        fileName,
                        segmentId,
                        false);
                    return;
                }

                result.AddIssue(
                    IndexCheckSeverity.Error,
                    IndexCheckIssueCodes.UnsupportedStoredFieldVersion,
                    $"Unsupported stored fields index format version {version}; this build supports up to version {CodecConstants.StoredFieldsVersion}.",
                    fileName,
                    segmentId,
                    false);
                return;
            }

            int blockSize = reader.ReadInt32();
            int docCount = reader.ReadInt32();
            int blockCount = reader.ReadInt32();
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
                long current = reader.ReadInt64();
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
        catch (Exception ex) when (ex is IOException or EndOfStreamException)
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

        if (!File.Exists(delPath))
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
            if (!File.Exists(vectorPath))
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
                if (!File.Exists(hnswPath))
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
            using var stream = FileOpenRetry.OpenReadDelete(vectorPath);
            using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: false);
            byte version;
            try
            {
                version = CodecFileHeader.ReadVersion(reader, CodecFormats.Vectors);
            }
            catch (Exception ex) when (ex is InvalidDataException or EndOfStreamException)
            {
                result.AddIssue(
                    IndexCheckSeverity.Error,
                    IndexCheckIssueCodes.InvalidVectorHeader,
                    $"Invalid vector header for field '{vectorField.FieldName}': {ex.Message}",
                    fileName,
                    segmentId,
                    false);
                return;
            }

            if (version > CodecConstants.VectorVersion)
            {
                result.AddIssue(
                    IndexCheckSeverity.Error,
                    IndexCheckIssueCodes.InvalidVectorHeader,
                    $"Invalid vector header for field '{vectorField.FieldName}'.",
                    fileName,
                    segmentId,
                    false);
                return;
            }

            int vectorCount = reader.ReadInt32();
            int dimension = reader.ReadInt32();
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
            using var stream = FileOpenRetry.OpenReadDelete(hnswPath);
            using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: false);
            byte version;
            try
            {
                version = CodecFileHeader.ReadVersion(reader, CodecFormats.Hnsw);
            }
            catch (Exception ex) when (ex is InvalidDataException or EndOfStreamException)
            {
                result.AddIssue(
                    IndexCheckSeverity.Error,
                    IndexCheckIssueCodes.InvalidHnswHeader,
                    $"Invalid HNSW header for field '{vectorField.FieldName}': {ex.Message}",
                    fileName,
                    segmentId,
                    false);
                return;
            }

            if (version > CodecConstants.HnswVersion)
            {
                result.AddIssue(
                    IndexCheckSeverity.Error,
                    IndexCheckIssueCodes.InvalidHnswHeader,
                    $"Invalid HNSW header for field '{vectorField.FieldName}'.",
                    fileName,
                    segmentId,
                    false);
                return;
            }

            int dimension = reader.ReadInt32();
            bool normalised = reader.ReadByte() != 0;
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
    }

    private static void ValidateDocValuesDeep(string basePath, SegmentInfo info, IndexCheckResult result)
    {
        TryReadDocValues(basePath + ".dvn", info, result, static path => NumericDocValuesReader.Read(path).Values.Values.Select(static values => values.Length));
        TryReadDocValues(basePath + ".dvs", info, result, static path => SortedDocValuesReader.Read(path).Values.Values.Select(static values => values.Length));
        TryReadDocValues(basePath + ".dss", info, result, static path => SortedSetDocValuesReader.Read(path).Values.Select(static values => values.Length));
        TryReadDocValues(basePath + ".dsn", info, result, static path => SortedNumericDocValuesReader.Read(path).Values.Select(static values => values.Length));
        TryReadDocValues(basePath + ".dvb", info, result, static path => BinaryDocValuesReader.Read(path).Values.Select(static values => values.Length));
    }

    private static void TryReadDocValues(
        string path,
        SegmentInfo info,
        IndexCheckResult result,
        Func<string, IEnumerable<int>> readLengths)
    {
        if (!File.Exists(path))
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
