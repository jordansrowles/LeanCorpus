using System.Runtime.CompilerServices;
using BenchmarkDotNet.Attributes;
using Rowles.LeanCorpus.Codecs.StoredFields;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Benchmarks;

/// <summary>Measures flush-time stored-field encoding for wide flat documents.</summary>
[MemoryDiagnoser]
public class StoredFieldsWriteBenchmarks
{
    private const int DocumentCount = 96;
    private const int FieldCount = 96;
    private string _indexPath = string.Empty;
    private string _fdtPath = string.Empty;
    private string _fdxPath = string.Empty;
    private readonly List<int> _documentStarts = new(DocumentCount);
    private readonly List<int> _fieldIds = new(DocumentCount * FieldCount);
    private readonly List<StoredFieldValue> _values = new(DocumentCount * FieldCount);
    private readonly List<string> _fieldNames = new(FieldCount);

    [GlobalSetup]
    public void Setup()
    {
        _indexPath = Path.Combine(BenchmarkHelpers.TempRoot, $"stored-fields-write-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_indexPath);
        _fdtPath = Path.Combine(_indexPath, "segment.fdt");
        _fdxPath = Path.Combine(_indexPath, "segment.fdx");

        for (int fieldId = 0; fieldId < FieldCount; fieldId++)
            _fieldNames.Add($"field-{fieldId:D3}");

        for (int docId = 0; docId < DocumentCount; docId++)
        {
            _documentStarts.Add(_fieldIds.Count);
            for (int entry = 0; entry < FieldCount; entry++)
            {
                // A coprime stride interleaves field IDs while keeping one value per field.
                int fieldId = entry * 37 % FieldCount;
                _fieldIds.Add(fieldId);
                _values.Add(StoredFieldValue.FromString($"value-{docId:D3}-{fieldId:D3}"));
            }
        }
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_indexPath))
            Directory.Delete(_indexPath, recursive: true);
    }

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public void FlushWideFlatDocuments()
    {
        if (File.Exists(_fdtPath)) File.Delete(_fdtPath);
        if (File.Exists(_fdxPath)) File.Delete(_fdxPath);

        StoredFieldsWriter.Write(
            _fdtPath,
            _fdxPath,
            _documentStarts,
            _fieldIds,
            _values,
            _fieldNames,
            blockSize: DocumentCount,
            compression: FieldCompressionPolicy.Deflate);
    }
}
