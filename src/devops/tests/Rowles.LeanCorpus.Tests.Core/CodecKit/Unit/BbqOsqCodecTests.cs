using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Codecs.Vectors;
using Rowles.LeanCorpus.Search.Simd;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Tests.Shared.Fixtures;

namespace Rowles.LeanCorpus.Tests.Core.Codecs;

[Category(TestCategory.Unit)]
[Area(TestArea.CodecKit)]
public sealed class BbqOsqCodecTests : IClassFixture<TestDirectoryFixture>
{
    private readonly TestDirectoryFixture _fixture;

    public BbqOsqCodecTests(TestDirectoryFixture fixture) => _fixture = fixture;

    [Fact]
    public void WriteAndRead_V3BodyHasLockedArraysAndPreservesCanonicalRows()
    {
        const int docCount = 5;
        const int dimension = 129;
        int[] vectorDocIds = [0, 2, 3];
        var vectors = new Dictionary<int, ReadOnlyMemory<float>>
        {
            [0] = NormalisedVector(dimension, 71),
            [2] = new float[dimension],
            [3] = NormalisedVector(dimension, 93)
        };
        var source = new InMemoryVectorSource(vectors, dimension);
        string path = TempFile("bbq_osq_v3.vq");
        string expectedCanonicalPath = TempFile("bbq_osq_expected_canonical.raw");
        byte[] presence = VectorPresence.Create(docCount, vectorDocIds);
        CanonicalInt8VectorWriter.WriteRecords(expectedCanonicalPath, docCount, dimension, source, vectorDocIds);

        QuantisedVectorWriter.WriteBBQOsqCanonical(path, docCount, dimension, source, vectorDocIds);

        int packedBytes = (dimension + 7) / 8;
        long expectedBodyLength = 8L + 1 + VectorPresence.ByteCount(docCount) + 2
            + (long)dimension * sizeof(float) + sizeof(float)
            + (long)docCount * 4 * sizeof(float)
            + (long)docCount * packedBytes
            + (long)docCount * CanonicalInt8VectorMetadata.Size
            + (long)docCount * dimension;
        using (var input = new IndexInput(path))
        using (var frame = CodecFileReader.Open(input, CodecCatalog.Default.GetFile("leancorpus.vectors.quantised")))
        {
            Assert.Equal(3, frame.Metadata.FormatVersion);
            Assert.Equal(expectedBodyLength, frame.Metadata.BodyLength);
            frame.ValidateChecksum();
        }

        using var reader = QuantisedVectorReader.Open(path);
        using var expectedCanonical = CanonicalInt8VectorReader.OpenRecords(
            expectedCanonicalPath, docCount, dimension, presence);
        Assert.Equal(VectorQuantisation.BBQ, reader.Quantisation);
        Assert.Equal(QuantisedVectorStorageKind.BbqOsqCanonicalInt8, reader.StorageKind);
        Assert.Equal(CanonicalVectorEncoding.PerVectorUnsigned8, reader.CanonicalEncoding);
        Assert.Equal(docCount, reader.DocCount);
        Assert.Equal(dimension, reader.Dimension);
        Assert.True(reader.HasVector(2));
        Assert.False(reader.HasVector(1));

        float centroidNorm = Dot(reader.Centroid, reader.Centroid);
        Assert.Equal(centroidNorm, reader.CentroidSquaredNorm);
        Assert.InRange(MathF.Abs(centroidNorm - 1f), 0f, 1e-4f);

        Span<byte> actualRecord = stackalloc byte[CanonicalInt8VectorMetadata.Size + dimension];
        Span<byte> expectedRecord = stackalloc byte[CanonicalInt8VectorMetadata.Size + dimension];
        Span<float> decoded = stackalloc float[dimension];
        for (int docId = 0; docId < docCount; docId++)
        {
            reader.CopyCanonicalRecordTo(docId, actualRecord);
            expectedCanonical.CopyCanonicalRecordTo(docId, expectedRecord);
            Assert.Equal(expectedRecord.ToArray(), actualRecord.ToArray());

            reader.ReadVector(docId, decoded);
            Assert.Equal(expectedCanonical.ReadVector(docId), decoded.ToArray());

            BbqOsqCorrections corrections = reader.GetOsqCorrections(docId);
            int packedCodeSum = 0;
            ReadOnlySpan<byte> packed = reader.GetRawBBQVector(docId);
            for (int index = 0; index < dimension; index++)
                packedCodeSum += (packed[index >> 3] >> (7 - (index & 7))) & 1;
            Assert.Equal(packedCodeSum, corrections.CodeSum);

            CanonicalInt8VectorMetadata canonicalMetadata = reader.GetCanonicalMetadata(docId);
            Assert.Equal(canonicalMetadata.SumOfCodes,
                reader.GetCanonicalCodes(docId).ToArray().Sum(static code => (int)code));
            if (!reader.HasVector(docId))
            {
                Assert.Equal(default, corrections);
                Assert.All(actualRecord.ToArray(), static value => Assert.Equal((byte)0, value));
            }
        }

        float[] queryVector = NormalisedVector(dimension, 107);
        PreparedBbqQuery query = reader.PrepareBbqQuery(queryVector);
        for (int docId = 0; docId < docCount; docId++)
        {
            if (!reader.HasVector(docId))
                continue;
            Assert.InRange(reader.ScoreBbqCandidate(query, docId), -1f, 1f);
        }

        Assert.Empty(Directory.EnumerateFiles(_fixture.Path,
            Path.GetFileName(path) + ".*canonical.tmp"));
        Assert.Empty(Directory.EnumerateFiles(_fixture.Path,
            Path.GetFileName(path) + ".*osq.tmp"));
    }

    [Fact]
    public void Reader_RejectsUnknownCanonicalEncodingAndTruncatedV3Body()
    {
        const int docCount = 1;
        const int dimension = 128;
        int[] ids = [0];
        var vectors = new Dictionary<int, ReadOnlyMemory<float>> { [0] = NormalisedVector(dimension, 101) };
        string path = TempFile("bbq_osq_corrupt.vq");
        QuantisedVectorWriter.WriteBBQOsqCanonical(path, docCount, dimension,
            new InMemoryVectorSource(vectors, dimension), ids);

        long bodyStart;
        using (var input = new IndexInput(path))
        using (var frame = CodecFileReader.Open(input, CodecCatalog.Default.GetFile("leancorpus.vectors.quantised")))
            bodyStart = frame.Metadata.BodyStart;

        byte[] bytes = File.ReadAllBytes(path);
        byte[] validBytes = (byte[])bytes.Clone();
        int encodingOffset = checked((int)bodyStart + 8 + 1 + VectorPresence.ByteCount(docCount) + 1);
        bytes[encodingOffset] = byte.MaxValue;
        File.WriteAllBytes(path, bytes);
        Assert.Throws<InvalidDataException>(() => QuantisedVectorReader.Open(path));

        File.WriteAllBytes(path, validBytes.AsSpan(0, validBytes.Length - 1).ToArray());
        Assert.ThrowsAny<Exception>(() => QuantisedVectorReader.Open(path));
    }

    [Fact]
    public void Writer_RejectsDimensionsBelowTheV3MinimumBeforeCreatingFiles()
    {
        const int dimension = 127;
        var vectors = new Dictionary<int, ReadOnlyMemory<float>> { [0] = NormalisedVector(dimension, 53) };
        string path = TempFile("bbq_osq_unsupported_dimension.vq");

        Assert.Throws<ArgumentOutOfRangeException>(() => QuantisedVectorWriter.WriteBBQOsqCanonical(
            path, 1, dimension, new InMemoryVectorSource(vectors, dimension), [0]));

        Assert.False(File.Exists(path));
        Assert.Empty(Directory.EnumerateFiles(_fixture.Path, Path.GetFileName(path) + ".*"));
    }

    private string TempFile(string name) => Path.Combine(_fixture.Path, $"{Guid.NewGuid():N}_{name}");

    private static float[] NormalisedVector(int dimension, int seed)
    {
        var random = new Random(seed);
        var vector = new float[dimension];
        for (int index = 0; index < dimension; index++)
            vector[index] = (float)(random.NextDouble() * 2d - 1d);
        Assert.True(SimdVectorOps.NormaliseInPlace(vector));
        return vector;
    }

    private static float Dot(ReadOnlySpan<float> left, ReadOnlySpan<float> right)
    {
        float dot = 0f;
        for (int index = 0; index < left.Length; index++)
            dot += left[index] * right[index];
        return dot;
    }
}
