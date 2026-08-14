using System.Text;
using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Codecs.CodecKit.Codecs;
using Rowles.LeanCorpus.Codecs.CodecKit.Formats;
using Rowles.LeanCorpus.Codecs.DocValues;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Tests.Core.Codecs.CodecKit;
[Category(TestCategory.Unit)]
[Area(TestArea.CodecKit)]
public sealed class DocValuesCanonicalFrameTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "LeanCorpus_DocValuesFrame", Guid.NewGuid().ToString("N"));

    public DocValuesCanonicalFrameTests() => Directory.CreateDirectory(_directory);

    [Theory]
    [InlineData(DocValuesKind.Numeric)]
    [InlineData(DocValuesKind.Sorted)]
    [InlineData(DocValuesKind.SortedSet)]
    [InlineData(DocValuesKind.SortedNumeric)]
    [InlineData(DocValuesKind.Binary)]
    [InlineData(DocValuesKind.Int64)]
    [InlineData(DocValuesKind.Int64SortedNumeric)]
    public void NormalWriter_EmitsCatalogueCurrentFrame_AndRoundTrips(DocValuesKind kind)
    {
        string path = Path.Combine(_directory, "segment" + Extension(kind));
        WriteSample(kind, path);

        var descriptor = Descriptor(kind);
        using (var input = new IndexInput(path))
        using (var session = CodecFileReader.Open(input, descriptor))
        {
            Assert.Equal(descriptor.FormatId, session.Metadata.FormatId);
            Assert.Equal(descriptor.CurrentFormatVersion, session.Metadata.FormatVersion);
            Assert.Equal(CodecFileWriter.CurrentFrameVersion, session.Metadata.FrameVersion);
            Assert.Equal(CodecFileChecksumAlgorithm.XxHash64, session.Metadata.ChecksumAlgorithm);
            session.ValidateChecksum();
        }

        AssertSampleRoundTrip(kind, path);
    }

    [Theory]
    [InlineData(DocValuesKind.Numeric)]
    [InlineData(DocValuesKind.Sorted)]
    [InlineData(DocValuesKind.SortedSet)]
    [InlineData(DocValuesKind.SortedNumeric)]
    [InlineData(DocValuesKind.Binary)]
    [InlineData(DocValuesKind.Int64)]
    [InlineData(DocValuesKind.Int64SortedNumeric)]
    public void ProductionReader_ReadsLegacyEnvelopeFixture(DocValuesKind kind)
    {
        string canonicalPath = Path.Combine(_directory, "canonical" + Extension(kind));
        string legacyPath = Path.Combine(_directory, "legacy" + Extension(kind));
        WriteSample(kind, canonicalPath);

        byte[] body;
        using (var input = new IndexInput(canonicalPath))
        using (var session = CodecFileReader.Open(input, Descriptor(kind)))
            body = session.ReadBody();

        using (var output = new IndexOutput(legacyPath))
            CodecFileHeader.Write(output, LegacyFormat(kind), body);

        AssertSampleRoundTrip(kind, legacyPath);
    }

    [Theory]
    [InlineData(DocValuesKind.Numeric)]
    [InlineData(DocValuesKind.Sorted)]
    [InlineData(DocValuesKind.SortedSet)]
    [InlineData(DocValuesKind.SortedNumeric)]
    [InlineData(DocValuesKind.Binary)]
    [InlineData(DocValuesKind.Int64)]
    [InlineData(DocValuesKind.Int64SortedNumeric)]
    public void ProductionReader_RejectsCanonicalChecksumMismatch(DocValuesKind kind)
    {
        string path = Path.Combine(_directory, "corrupt" + Extension(kind));
        WriteSample(kind, path);
        byte[] bytes = File.ReadAllBytes(path);
        bytes[^1] ^= 0x01;
        File.WriteAllBytes(path, bytes);

        var exception = Assert.Throws<CodecFileException>(() => AssertSampleRoundTrip(kind, path));

        Assert.Equal(CodecFileErrorCode.ChecksumMismatch, exception.ErrorCode);
    }

    private static void WriteSample(DocValuesKind kind, string path)
    {
        switch (kind)
        {
            case DocValuesKind.Numeric:
                NumericDocValuesWriter.Write(
                    path,
                    new Dictionary<string, double[]> { ["price"] = [1.5, 0, 3.25] },
                    3,
                    new Dictionary<string, IReadOnlySet<int>> { ["price"] = new HashSet<int> { 0, 2 } });
                break;
            case DocValuesKind.Sorted:
                SortedDocValuesWriter.Write(
                    path,
                    new Dictionary<string, string?[]> { ["category"] = ["bravo", null, "alpha"] },
                    3);
                break;
            case DocValuesKind.SortedSet:
                SortedSetDocValuesWriter.Write(
                    path,
                    new Dictionary<string, IReadOnlyList<string>?[]>
                    {
                        ["tags"] = [new[] { "bravo", "alpha" }, null, new[] { "alpha" }],
                    },
                    3);
                break;
            case DocValuesKind.SortedNumeric:
                SortedNumericDocValuesWriter.Write(
                    path,
                    new Dictionary<string, IReadOnlyList<double>?[]>
                    {
                        ["scores"] = [new double[] { 2, 1 }, null, new double[] { 3 }],
                    },
                    3);
                break;
            case DocValuesKind.Binary:
                BinaryDocValuesWriter.Write(
                    path,
                    new Dictionary<string, IReadOnlyList<byte[]>?[]>
                    {
                        ["payload"] = [new[] { Encoding.UTF8.GetBytes("first") }, null, new[] { Encoding.UTF8.GetBytes("third") }],
                    },
                    3);
                break;
            case DocValuesKind.Int64:
                Int64DocValuesWriter.Write(
                    path,
                    new Dictionary<string, long[]> { ["count"] = [10, 0, 30] },
                    3,
                    new Dictionary<string, IReadOnlySet<int>> { ["count"] = new HashSet<int> { 0, 2 } });
                break;
            case DocValuesKind.Int64SortedNumeric:
                Int64SortedNumericDocValuesWriter.Write(
                    path,
                    new Dictionary<string, IReadOnlyList<long>?[]>
                    {
                        ["counts"] = [new long[] { 2, 1 }, null, new long[] { 3 }],
                    },
                    3);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind));
        }
    }

    private static void AssertSampleRoundTrip(DocValuesKind kind, string path)
    {
        switch (kind)
        {
            case DocValuesKind.Numeric:
            {
                var result = NumericDocValuesReader.Read(path);
                Assert.Equal([1.5, 0, 3.25], result.Values["price"]);
                Assert.False(result.Presence["price"]!.Contains(1));
                break;
            }
            case DocValuesKind.Sorted:
            {
                var result = SortedDocValuesReader.Read(path);
                Assert.Equal(["bravo", "", "alpha"], result.Values["category"]);
                Assert.False(result.Presence["category"]!.Contains(1));
                break;
            }
            case DocValuesKind.SortedSet:
                Assert.Equal(["alpha", "bravo"], SortedSetDocValuesReader.Read(path)["tags"][0]);
                break;
            case DocValuesKind.SortedNumeric:
                Assert.Equal([1, 2], SortedNumericDocValuesReader.Read(path)["scores"][0]);
                break;
            case DocValuesKind.Binary:
                Assert.Equal("third", Encoding.UTF8.GetString(BinaryDocValuesReader.Read(path)["payload"][2][0]));
                break;
            case DocValuesKind.Int64:
            {
                var result = Int64DocValuesReader.Read(path);
                Assert.Equal([10, 0, 30], result.Values["count"]);
                Assert.False(result.Presence["count"]!.Contains(1));
                break;
            }
            case DocValuesKind.Int64SortedNumeric:
                Assert.Equal([1, 2], Int64SortedNumericDocValuesReader.Read(path)["counts"][0]);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind));
        }
    }

    private static CodecFileDescriptor Descriptor(DocValuesKind kind)
        => CodecCatalog.Default.GetFile(kind switch
        {
            DocValuesKind.Numeric => "leancorpus.doc-values.numeric",
            DocValuesKind.Sorted => "leancorpus.doc-values.sorted",
            DocValuesKind.SortedSet => "leancorpus.doc-values.sorted-set",
            DocValuesKind.SortedNumeric => "leancorpus.doc-values.sorted-numeric",
            DocValuesKind.Binary => "leancorpus.doc-values.binary",
            DocValuesKind.Int64 => "leancorpus.doc-values.int64",
            DocValuesKind.Int64SortedNumeric => "leancorpus.doc-values.int64-sorted-numeric",
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        });

    private static ICodec<byte[]> LegacyFormat(DocValuesKind kind)
        => kind switch
        {
            DocValuesKind.Numeric => CodecFormats.NumericDocValues,
            DocValuesKind.Sorted => CodecFormats.SortedDocValues,
            DocValuesKind.SortedSet => CodecFormats.SortedSetDocValues,
            DocValuesKind.SortedNumeric => CodecFormats.SortedNumericDocValues,
            DocValuesKind.Binary => CodecFormats.BinaryDocValues,
            DocValuesKind.Int64 => CodecFormats.Int64DocValues,
            DocValuesKind.Int64SortedNumeric => CodecFormats.Int64SortedNumericDocValues,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

    private static string Extension(DocValuesKind kind)
        => kind switch
        {
            DocValuesKind.Numeric => ".dvn",
            DocValuesKind.Sorted => ".dvs",
            DocValuesKind.SortedSet => ".dss",
            DocValuesKind.SortedNumeric => ".dsn",
            DocValuesKind.Binary => ".dvb",
            DocValuesKind.Int64 => ".dvnl",
            DocValuesKind.Int64SortedNumeric => ".dsnl",
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

    public void Dispose()
    {
        if (!Directory.Exists(_directory))
            return;
        GC.Collect();
        GC.WaitForPendingFinalizers();
        Directory.Delete(_directory, recursive: true);
    }

    public enum DocValuesKind
    {
        Numeric,
        Sorted,
        SortedSet,
        SortedNumeric,
        Binary,
        Int64,
        Int64SortedNumeric,
    }
}
