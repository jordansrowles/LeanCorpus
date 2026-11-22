using System.Collections.ObjectModel;
using System.Globalization;
using System.Security.Cryptography;
using Rowles.LeanCorpus.Codecs.Vectors;
using Rowles.LeanCorpus.Index.Format;
using Rowles.LeanCorpus.Index.Segment;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Index.Inspection;

/// <summary>
/// Provides stable read-only inspection views over a committed LeanCorpus index snapshot.
/// The inspector owns its directory and reader lifetimes and does not expose codec readers.
/// </summary>
public sealed class IndexInspector : IDisposable
{
    private readonly MMapDirectory _directory;
    private readonly IndexFormatInventory _format;
    private readonly SegmentInfo[] _infos;
    private readonly SegmentReader[] _readers;
    private readonly int[] _documentBases;
    private bool _disposed;

    /// <summary>Opens a committed index for read-only inspection.</summary>
    /// <param name="directoryPath">The index directory path.</param>
    /// <exception cref="ArgumentException">The directory path is empty or does not exist.</exception>
    /// <exception cref="IOException">The index directory cannot be opened.</exception>
    public IndexInspector(string directoryPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);
        string fullPath = MMapDirectory.ResolveExistingDirectory(directoryPath);

        _directory = new MMapDirectory(fullPath);
        try
        {
            _format = IndexFormatInspector.Inspect(_directory);
            _infos = _format.SegmentIds
                .Select(id => SegmentInfo.ReadFrom(Path.Combine(fullPath, id + ".seg")))
                .ToArray();
            _readers = new SegmentReader[_infos.Length];
            _documentBases = new int[_infos.Length];
            int documentBase = 0;
            for (int index = 0; index < _infos.Length; index++)
            {
                _documentBases[index] = documentBase;
                _readers[index] = new SegmentReader(_directory, _infos[index]);
                documentBase = checked(documentBase + _infos[index].DocCount);
            }
        }
        catch
        {
            if (_readers is not null)
                foreach (SegmentReader? reader in _readers)
                    reader?.Dispose();
            _directory.Dispose();
            throw;
        }
    }

    /// <summary>Gets the overview of the committed snapshot and its physical files.</summary>
    public IndexInspectionOverview Overview
    {
        get
        {
            ThrowIfDisposed();
            var files = _directory.GetPhysicalFileStatistics();
            bool HasDocValues(bool ordinalOnly)
                => _readers.Any(reader => reader.Info.FieldNames.Any(fieldName => ordinalOnly
                    ? reader.HasSortedDocValues(fieldName) || reader.HasSortedSetDocValues(fieldName)
                    : reader.HasNumericDocValues(fieldName) || reader.HasInt64DocValues(fieldName)
                        || reader.HasSortedDocValues(fieldName) || reader.HasSortedSetDocValues(fieldName)
                        || reader.HasSortedNumericDocValues(fieldName) || reader.HasSortedInt64DocValues(fieldName)
                        || reader.HasBinaryDocValues(fieldName)));

            return new IndexInspectionOverview(
                _directory.DirectoryPath,
                _format.CommitGeneration,
                _format.ContentToken,
                _infos.Sum(static info => info.DocCount),
                _infos.Sum(static info => info.LiveDocCount),
                _infos.Length,
                files.Count,
                files.Bytes,
                _infos.Any(static info => info.VectorFields.Count > 0),
                _infos.Any(static info => info.VectorFields.Any(static vectorInfo => vectorInfo.HasHnsw)),
                HasDocValues(ordinalOnly: true),
                _infos.Any(static info => info.SpatialFields.Count > 0),
                _infos.Count(static info => info.IsCompoundFile),
                _format.OrphanFiles.Count,
                _format.HasUnsupportedFutureFormat,
                _format.Segments.SelectMany(static segment => segment.Files).Any(static file => file.IsSupported && !file.IsCurrent),
                _format.HasUnknownFormat)
            {
                HasVectorGraph = _infos.Any(static info => info.VectorFields.Any(static vectorInfo => vectorInfo.HasGraph)),
            };
        }
    }

    /// <summary>Gets the committed segments in commit order.</summary>
    public IReadOnlyList<IndexInspectionSegment> GetSegments()
    {
        ThrowIfDisposed();
        return Array.AsReadOnly(_format.Segments.Select(segment => new IndexInspectionSegment(
            segment.SegmentId,
            segment.DocCount,
            segment.LiveDocCount,
            segment.CommitGeneration,
            segment.DelGeneration,
                segment.Files.Sum(static file => file.Length ?? 0L),
            _infos.FirstOrDefault(info => string.Equals(info.SegmentId, segment.SegmentId, StringComparison.Ordinal))?.IsCompoundFile,
            Array.AsReadOnly(_infos.FirstOrDefault(info => string.Equals(info.SegmentId, segment.SegmentId, StringComparison.Ordinal))?.FieldNames.ToArray() ?? []),
            Array.AsReadOnly(_infos.FirstOrDefault(info => string.Equals(info.SegmentId, segment.SegmentId, StringComparison.Ordinal))?.VectorFields.ToArray() ?? []),
            Array.AsReadOnly(_infos.FirstOrDefault(info => string.Equals(info.SegmentId, segment.SegmentId, StringComparison.Ordinal))?.SpatialFields.ToArray() ?? []),
            Array.AsReadOnly(segment.Files.ToArray()),
            Array.AsReadOnly(segment.MissingFiles.ToArray()),
            Array.AsReadOnly(segment.Warnings.ToArray()))).ToArray());
    }

    /// <summary>Gets fields and the capabilities discoverable from the committed files.</summary>
    public IReadOnlyList<IndexInspectionField> GetFields()
    {
        ThrowIfDisposed();
        string[] names = _infos.SelectMany(static info => info.FieldNames)
            .Concat(_infos.SelectMany(static info => info.VectorFields).Select(static field => field.FieldName))
            .Concat(_infos.SelectMany(static info => info.SpatialFields).Select(static field => field.FieldName))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        var results = new List<IndexInspectionField>(names.Length);
        foreach (string name in names)
        {
            int segmentCount = 0;
            int documentsWithValue = 0;
            var uniqueTerms = new HashSet<string>(StringComparer.Ordinal);
            long? totalTermFrequency = 0;
            bool stored = false;
            bool hasDocValues = false;
            bool hasOrdinalDocValues = false;
            bool hasTermVectors = false;
            int? dimension = null;
            bool? normalised = null;
            bool? hasHnsw = null;
            int? hnswNodeCount = null;
            bool? hasVectorGraph = null;
            int? vectorGraphNodeCount = null;
            VectorQuantisation? quantisation = null;
            SpatialFieldKind? spatialKind = null;
            bool indexed = false;

            for (int segmentIndex = 0; segmentIndex < _readers.Length; segmentIndex++)
            {
                SegmentReader reader = _readers[segmentIndex];
                SegmentInfo info = _infos[segmentIndex];
                bool contains = info.FieldNames.Contains(name, StringComparer.Ordinal)
                    || info.VectorFields.Any(field => string.Equals(field.FieldName, name, StringComparison.Ordinal))
                    || info.SpatialFields.Any(field => string.Equals(field.FieldName, name, StringComparison.Ordinal));
                if (!contains)
                    continue;
                segmentCount++;
                indexed |= info.FieldNames.Contains(name, StringComparer.Ordinal);
                hasDocValues |= reader.HasNumericDocValues(name) || reader.HasInt64DocValues(name)
                    || reader.HasSortedDocValues(name) || reader.HasSortedSetDocValues(name)
                    || reader.HasSortedNumericDocValues(name) || reader.HasSortedInt64DocValues(name)
                    || reader.HasBinaryDocValues(name);
                hasOrdinalDocValues |= reader.HasSortedDocValues(name) || reader.HasSortedSetDocValues(name);

                VectorFieldInfo? vector = info.VectorFields.FirstOrDefault(field => string.Equals(field.FieldName, name, StringComparison.Ordinal));
                if (vector is not null)
                {
                    dimension ??= vector.Dimension;
                    normalised ??= vector.Normalised;
                    quantisation ??= vector.Quantisation;
                    hasHnsw = (hasHnsw ?? false) || vector.HasHnsw;
                    int nodes = vector.HasHnsw ? reader.GetHnswGraph(name)?.NodeCount ?? 0 : 0;
                    hnswNodeCount = (hnswNodeCount ?? 0) + nodes;
                    hasVectorGraph = (hasVectorGraph ?? false) || vector.HasGraph;
                    int graphNodes = 0;
                    if (vector.HasGraph)
                        reader.TryValidateVectorGraph(name, out graphNodes, out _);
                    vectorGraphNodeCount = (vectorGraphNodeCount ?? 0) + graphNodes;
                }

                SpatialFieldInfo? spatial = info.SpatialFields.FirstOrDefault(field => string.Equals(field.FieldName, name, StringComparison.Ordinal));
                if (spatial is not null)
                    spatialKind ??= spatial.Kind;

                for (int localId = 0; localId < reader.MaxDoc; localId++)
                {
                    if (!reader.IsLive(localId))
                        continue;
                    if (reader.HasFieldValue(name, localId))
                        documentsWithValue++;
                    if (reader.GetStoredFields(localId).ContainsKey(name)
                        || reader.GetStoredBinaryFields(localId).ContainsKey(name))
                        stored = true;
                }

                if (reader.HasTermVectors && !hasTermVectors)
                {
                    for (int localId = 0; localId < reader.MaxDoc; localId++)
                    {
                        if (reader.IsLive(localId) && reader.GetTermVectors(localId)?.ContainsKey(name) == true)
                        {
                            hasTermVectors = true;
                            break;
                        }
                    }
                }

                if (!info.FieldNames.Contains(name, StringComparer.Ordinal))
                    continue;
                List<(string Term, long Offset)> terms = reader.GetAllTermsForField(string.Concat(name, "\0"));
                foreach ((string qualifiedTerm, long _) in terms)
                {
                    string term = qualifiedTerm[(name.Length + 1)..];
                    uniqueTerms.Add(term);
                    foreach (int localId in reader.GetDocIds(name, term))
                        if (reader.IsLive(localId))
                            totalTermFrequency += reader.GetTermFrequency(name, term, localId);
                }
            }

            results.Add(new IndexInspectionField(name, indexed, stored, hasDocValues, hasTermVectors,
                hasOrdinalDocValues, segmentCount, documentsWithValue, uniqueTerms.Count, totalTermFrequency,
                dimension, normalised, hasHnsw, hnswNodeCount, quantisation, spatialKind)
            {
                HasVectorGraph = hasVectorGraph,
                VectorGraphNodeCount = vectorGraphNodeCount,
            });
        }

        return Array.AsReadOnly(results.ToArray());
    }

    /// <summary>Gets sorted indexed terms for a field, optionally narrowed by a prefix.</summary>
    /// <param name="field">The field name.</param>
    /// <param name="prefix">An optional unqualified term prefix.</param>
    /// <param name="take">The maximum number of terms to return.</param>
    public IReadOnlyList<IndexInspectionTerm> GetTerms(string field, string? prefix = null, int take = 100)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(field);
        if (take is < 1 or > 100_000)
            throw new ArgumentOutOfRangeException(nameof(take), "Term result limit must be between 1 and 100,000.");
        ThrowIfDisposed();

        var terms = new SortedDictionary<string, (int DocumentFrequency, long TotalTermFrequency)>(StringComparer.Ordinal);
        foreach (SegmentReader reader in _readers)
        {
            foreach ((string qualified, long _) in reader.GetAllTermsForField(string.Concat(field, "\0")))
            {
                string term = qualified[(field.Length + 1)..];
                if (prefix is not null && !term.StartsWith(prefix, StringComparison.Ordinal))
                    continue;
                var stats = terms.GetValueOrDefault(term);
                foreach (int localId in reader.GetDocIds(field, term))
                {
                    if (!reader.IsLive(localId))
                        continue;
                    stats.DocumentFrequency++;
                    stats.TotalTermFrequency += reader.GetTermFrequency(field, term, localId);
                }
                terms[term] = stats;
            }
        }

        return Array.AsReadOnly(terms.Take(take)
            .Select(static pair => new IndexInspectionTerm(pair.Key, pair.Value.DocumentFrequency, pair.Value.TotalTermFrequency))
            .ToArray());
    }

    /// <summary>Gets live postings for one field/term pair.</summary>
    /// <param name="field">The field name.</param>
    /// <param name="term">The unqualified term text.</param>
    /// <param name="take">The maximum number of postings to return.</param>
    public IReadOnlyList<IndexInspectionPosting> GetPostings(string field, string term, int take = 1_000)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(field);
        ArgumentNullException.ThrowIfNull(term);
        if (take is < 1 or > 1_000_000)
            throw new ArgumentOutOfRangeException(nameof(take), "Posting result limit must be between 1 and 1,000,000.");
        ThrowIfDisposed();

        var results = new List<IndexInspectionPosting>(Math.Min(take, 256));
        for (int segmentIndex = 0; segmentIndex < _readers.Length && results.Count < take; segmentIndex++)
        {
            SegmentReader reader = _readers[segmentIndex];
            foreach (int localId in reader.GetDocIds(field, term))
            {
                if (!reader.IsLive(localId))
                    continue;
                int[] positions = reader.GetPositions(field, term, localId) ?? [];
                results.Add(new IndexInspectionPosting(_documentBases[segmentIndex] + localId,
                    _infos[segmentIndex].SegmentId, localId, reader.GetTermFrequency(field, term, localId),
                    Array.AsReadOnly(positions)));
                if (results.Count == take)
                    break;
            }
        }

        return Array.AsReadOnly(results.ToArray());
    }

    /// <summary>Gets one document by its global identifier.</summary>
    /// <param name="documentId">The global document identifier.</param>
    public IndexInspectionDocument GetDocument(int documentId)
    {
        ThrowIfDisposed();
        for (int segmentIndex = 0; segmentIndex < _readers.Length; segmentIndex++)
        {
            SegmentReader reader = _readers[segmentIndex];
            int localId = documentId - _documentBases[segmentIndex];
            if ((uint)localId >= (uint)reader.MaxDoc)
                continue;

            var stored = reader.GetStoredFields(localId).ToDictionary(
                static pair => pair.Key,
                static pair => (IReadOnlyList<string>)Array.AsReadOnly(pair.Value.ToArray()),
                StringComparer.Ordinal);
            var binary = reader.GetStoredBinaryFields(localId).ToDictionary(
                static pair => pair.Key,
                static pair => (IReadOnlyList<int>)Array.AsReadOnly(pair.Value.Select(static value => value.Length).ToArray()),
                StringComparer.Ordinal);
            var binaryHashes = reader.GetStoredBinaryFields(localId).ToDictionary(
                static pair => pair.Key,
                static pair => (IReadOnlyList<string>)Array.AsReadOnly(pair.Value
                    .Select(static value => Convert.ToHexStringLower(SHA256.HashData(value))).ToArray()),
                StringComparer.Ordinal);
            var vectors = new Dictionary<string, IReadOnlyList<float>>(StringComparer.Ordinal);
            foreach (string field in reader.VectorFieldNames)
            {
                float[]? value = reader.GetVector(field, localId);
                if (value is not null)
                    vectors[field] = Array.AsReadOnly(value);
            }

            var docValues = new List<IndexInspectionDocValue>();
            foreach (string field in reader.Info.FieldNames.Order(StringComparer.Ordinal))
            {
                if (reader.TryGetNumericValue(field, localId, out double numeric))
                    docValues.Add(DocValue(field, "numeric", numeric.ToString("R", CultureInfo.InvariantCulture)));
                if (reader.TryGetInt64Value(field, localId, out long int64))
                    docValues.Add(DocValue(field, "int64", int64.ToString(CultureInfo.InvariantCulture)));
                if (reader.TryGetSortedDocValue(field, localId, out string sorted))
                    docValues.Add(DocValue(field, "sorted", sorted));
                if (reader.TryGetSortedSetDocValues(field, localId, out IReadOnlyList<string> sortedSet))
                    docValues.Add(new IndexInspectionDocValue(field, "sorted-set", Array.AsReadOnly(sortedSet.ToArray())));
                if (reader.TryGetSortedNumericDocValues(field, localId, out IReadOnlyList<double> sortedNumeric))
                    docValues.Add(new IndexInspectionDocValue(field, "sorted-numeric", Array.AsReadOnly(sortedNumeric
                        .Select(static value => value.ToString("R", CultureInfo.InvariantCulture)).ToArray())));
                if (reader.TryGetSortedInt64DocValues(field, localId, out IReadOnlyList<long> sortedInt64))
                    docValues.Add(new IndexInspectionDocValue(field, "sorted-int64", Array.AsReadOnly(sortedInt64
                        .Select(static value => value.ToString(CultureInfo.InvariantCulture)).ToArray())));
                if (reader.TryGetBinaryDocValues(field, localId, out IReadOnlyList<byte[]> binaryValues))
                    docValues.Add(new IndexInspectionDocValue(field, "binary-sha256", Array.AsReadOnly(binaryValues
                        .Select(static value => Convert.ToHexStringLower(SHA256.HashData(value))).ToArray())));
            }

            return new IndexInspectionDocument(documentId, _infos[segmentIndex].SegmentId, localId,
                reader.IsLive(localId), new ReadOnlyDictionary<string, IReadOnlyList<string>>(stored),
                new ReadOnlyDictionary<string, IReadOnlyList<int>>(binary),
                new ReadOnlyDictionary<string, IReadOnlyList<float>>(vectors),
                new ReadOnlyDictionary<string, IReadOnlyList<string>>(binaryHashes),
                Array.AsReadOnly(docValues.ToArray()));
        }

        throw new ArgumentOutOfRangeException(nameof(documentId), documentId, "Document identifier is outside the committed index.");
    }

    /// <summary>Gets the inspected format inventory, including orphan files and codec details.</summary>
    public IndexFormatInventory GetFormatInventory()
    {
        ThrowIfDisposed();
        return _format;
    }

    /// <summary>Gets the index size report produced by the Core diagnostics calculator.</summary>
    public Diagnostics.IndexSizeReport GetSizeReport()
    {
        ThrowIfDisposed();
        return Diagnostics.IndexSizeCalculator.Calculate(_directory.DirectoryPath);
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private static IndexInspectionDocValue DocValue(string field, string kind, string value)
        => new(field, kind, Array.AsReadOnly([value]));

    /// <summary>Releases the inspector's segment readers and directory snapshot.</summary>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        foreach (SegmentReader reader in _readers)
            reader.Dispose();
        _directory.Dispose();
    }
}
