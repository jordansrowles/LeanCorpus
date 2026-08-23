using System.Text;
using System.Runtime.CompilerServices;
using BenchmarkDotNet.Attributes;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Benchmarks;

/// <summary>Measures the issue #61 Windows storage hot paths in isolation.</summary>
[MemoryDiagnoser]
[HtmlExporter]
[JsonExporterAttribute.Full]
[MarkdownExporterAttribute.GitHub]
public class WindowsStoragePathBenchmarks
{
    private const int ValueCount = 100_000;
    private const int SliceOpenCount = 64;
    private const int SequentialWriteBytes = 16 * 1024 * 1024;

    private string _path = string.Empty;
    private string _varIntPath = string.Empty;
    private string _compoundPath = string.Empty;
    private string _unallocatedOutputPath = string.Empty;
    private string _preallocatedOutputPath = string.Empty;
    private IndexInput? _varIntInput;
    private MMapDirectory? _directory;
    private byte[] _writeBuffer = [];
    private long _memberOffset;
    private int _memberLength;

    [GlobalSetup]
    public void GlobalSetup()
    {
        _path = Path.Combine(BenchmarkHelpers.TempRoot, $"windows-storage-path-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_path);
        _varIntPath = Path.Combine(_path, "values.bin");
        _compoundPath = Path.Combine(_path, "shared.cfs");
        _unallocatedOutputPath = Path.Combine(_path, "sequential.bin");
        _preallocatedOutputPath = Path.Combine(_path, "sequential-preallocated.bin");

        using (var output = new IndexOutput(_varIntPath))
        {
            for (int i = 0; i < ValueCount; i++)
                output.WriteVarInt(i & 0x3fff);
        }

        _memberLength = 4096;
        WriteCompoundFile(_compoundPath, "member.bin", new byte[_memberLength], out _memberOffset);
        _writeBuffer = new byte[64 * 1024];
        Array.Fill<byte>(_writeBuffer, 0x5a);
        _varIntInput = new IndexInput(_varIntPath);
        _directory = new MMapDirectory(_path);
    }

    [GlobalCleanup]
    public void GlobalCleanup()
    {
        _varIntInput?.Dispose();
        _directory?.Dispose();
        RecentFeatureBenchmarkIndex.Delete(_path);
    }

    [Benchmark(Baseline = true)]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public long PrimitiveVarInt_PerReadDrain()
    {
        long position = 0;
        long sum = 0;
        for (int i = 0; i < ValueCount; i++)
            sum += _varIntInput!.ReadVarIntFast(ref position);
        return sum;
    }

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public long PrimitiveVarInt_ScopedDecoderLease()
    {
        long position = 0;
        long sum = 0;
        using var reader = _varIntInput!.BeginReadSession();
        for (int i = 0; i < ValueCount; i++)
            sum += reader.ReadVarIntFast(ref position);
        return sum;
    }

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int CompoundSlices_SharedMapping()
    {
        int sum = 0;
        using var compound = CompoundFileReader.Open(_directory!, Path.GetFileName(_compoundPath));
        for (int i = 0; i < SliceOpenCount; i++)
        {
            using var input = compound.OpenInput(_directory!, "member.bin");
            sum += input.ReadByte();
        }
        return sum;
    }

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int CompoundSlices_IndependentMappings()
    {
        int sum = 0;
        for (int i = 0; i < SliceOpenCount; i++)
        {
            using var input = _directory!.OpenInputSlice(
                Path.GetFileName(_compoundPath), _memberOffset, _memberLength);
            sum += input.ReadByte();
        }
        return sum;
    }

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public long SequentialWrite_NoPreallocation() => WriteSequential(_unallocatedOutputPath, preallocationSize: 0);

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public long SequentialWrite_Preallocated()
        => WriteSequential(_preallocatedOutputPath, preallocationSize: SequentialWriteBytes);

    private long WriteSequential(string path, long preallocationSize)
    {
        using var output = new IndexOutput(path, preallocationSize: preallocationSize);
        for (int written = 0; written < SequentialWriteBytes; written += _writeBuffer.Length)
            output.WriteBytes(_writeBuffer);
        return output.Position;
    }

    private static void WriteCompoundFile(string path, string memberName, byte[] bytes, out long memberOffset)
    {
        int nameByteCount = Encoding.UTF8.GetByteCount(memberName);
        memberOffset = 3L * sizeof(int) + Get7BitEncodedLength(nameByteCount) + nameByteCount + 2L * sizeof(long);
        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: false);
        writer.Write(CompoundFileWriter.Magic);
        writer.Write(CompoundFileWriter.Version);
        writer.Write(1);
        writer.Write(memberName);
        writer.Write(memberOffset);
        writer.Write((long)bytes.Length);
        writer.Write(bytes);
    }

    private static int Get7BitEncodedLength(int value)
    {
        int length = 1;
        while ((value >>= 7) != 0)
            length++;
        return length;
    }
}
