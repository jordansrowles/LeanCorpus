using System.Buffers.Binary;
using Rowles.LeanCorpus.Codecs.Vectors;
using Rowles.LeanCorpus.Search.Simd;
using Rowles.LeanCorpus.Tests.Shared.Fixtures;

namespace Rowles.LeanCorpus.Tests.Core.Codecs;

[Category(TestCategory.Unit)]
[Area(TestArea.CodecKit)]
public sealed class CanonicalInt8VectorTests : IClassFixture<TestDirectoryFixture>
{
    private readonly TestDirectoryFixture _fixture;

    public CanonicalInt8VectorTests(TestDirectoryFixture fixture) => _fixture = fixture;

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(63)]
    [InlineData(64)]
    [InlineData(65)]
    [InlineData(127)]
    [InlineData(128)]
    [InlineData(129)]
    [InlineData(384)]
    [InlineData(768)]
    [InlineData(1536)]
    public void WriteAndReadRecords_AreStableAcrossDimensionsAndPreservePresence(int dimension)
    {
        const int docCount = 6;
        int[] vectorDocIds = [0, 2, 4, 5];
        var vectors = new Dictionary<int, ReadOnlyMemory<float>>
        {
            [0] = CreateNormalisedVector(dimension),
            [2] = new float[dimension],
            [4] = Enumerable.Repeat(0.5f, dimension).ToArray(),
            [5] = CreateRepeatedComponentVector(dimension)
        };
        var source = new InMemoryVectorSource(vectors, dimension);
        string firstPath = TempFile($"canonical_{dimension}_first.raw");
        string secondPath = TempFile($"canonical_{dimension}_second.raw");
        byte[] presence = VectorPresence.Create(docCount, vectorDocIds);

        CanonicalInt8VectorWriter.WriteRecords(firstPath, docCount, dimension, source, vectorDocIds);
        CanonicalInt8VectorWriter.WriteRecords(secondPath, docCount, dimension, source, vectorDocIds);

        Assert.Equal(File.ReadAllBytes(firstPath), File.ReadAllBytes(secondPath));

        using var reader = CanonicalInt8VectorReader.OpenRecords(firstPath, docCount, dimension, presence);
        Assert.Equal(docCount, reader.Count);
        Assert.Equal(dimension, reader.Dimension);
        Assert.True(reader.HasVector(2));
        Assert.False(reader.HasVector(1));

        Span<float> decoded = stackalloc float[dimension];
        Span<byte> rawRecord = stackalloc byte[CanonicalInt8VectorMetadata.Size + dimension];
        for (int docId = 0; docId < docCount; docId++)
        {
            reader.CopyVectorTo(docId, decoded);
            reader.CopyCanonicalRecordTo(docId, rawRecord);

            ReadOnlySpan<byte> codes = reader.GetCanonicalCodes(docId);
            Assert.Equal(dimension, codes.Length);
            Assert.Equal(codes.ToArray(), rawRecord[CanonicalInt8VectorMetadata.Size..].ToArray());

            CanonicalInt8VectorMetadata metadata = reader.GetCanonicalMetadata(docId);
            Assert.Equal(codes.ToArray().Sum(static code => (int)code), metadata.SumOfCodes);

            double decodedSquaredNorm = 0d;
            for (int index = 0; index < dimension; index++)
            {
                float expected = (float)((double)metadata.Offset + (double)metadata.Scale * codes[index]);
                Assert.Equal(expected, decoded[index]);
                decodedSquaredNorm += (double)expected * expected;
            }
            Assert.Equal((float)decodedSquaredNorm, metadata.DecodedSquaredNorm);

            if (!reader.HasVector(docId))
            {
                Assert.All(decoded.ToArray(), static value => Assert.Equal(0f, value));
                Assert.All(rawRecord.ToArray(), static value => Assert.Equal((byte)0, value));
            }
        }
    }

    [Fact]
    public void Encode_UsesDeterministicNearestRoundingAndZeroScaleForConstants()
    {
        Span<byte> codes = stackalloc byte[3];
        CanonicalInt8VectorMetadata metadata = CanonicalInt8VectorWriter.Encode([-1f, 0f, 1f], codes);

        Assert.Equal((byte)0, codes[0]);
        Assert.Equal((byte)128, codes[1]);
        Assert.Equal((byte)255, codes[2]);
        Assert.Equal(-1f, metadata.Offset);
        Assert.Equal(2f / 255f, metadata.Scale);
        Assert.Equal(383, metadata.SumOfCodes);

        Span<byte> constantCodes = stackalloc byte[9];
        CanonicalInt8VectorMetadata constantMetadata = CanonicalInt8VectorWriter.Encode(
            [0.25f, 0.25f, 0.25f, 0.25f, 0.25f, 0.25f, 0.25f, 0.25f, 0.25f], constantCodes);

        Assert.Equal(0.25f, constantMetadata.Offset);
        Assert.Equal(0f, constantMetadata.Scale);
        Assert.Equal(0, constantMetadata.SumOfCodes);
        Assert.All(constantCodes.ToArray(), static code => Assert.Equal((byte)0, code));
    }

    [Fact]
    public void Reader_CopiesRawRecordsBitForBitAcrossDocumentRemapping()
    {
        const int dimension = 129;
        int[] sourceDocIds = [1, 3, 4];
        int[] remap = [4, 1, 3];
        var vectors = sourceDocIds.ToDictionary(
            static docId => docId,
            docId => (ReadOnlyMemory<float>)CreateNormalisedVector(dimension).Select(value => value + docId / 16f).ToArray());
        string sourcePath = TempFile("canonical_remap_source.raw");
        string remappedPath = TempFile("canonical_remap_destination.raw");
        CanonicalInt8VectorWriter.WriteRecords(sourcePath, 5, dimension,
            new InMemoryVectorSource(vectors, dimension), sourceDocIds);

        using (var source = CanonicalInt8VectorReader.OpenRecords(sourcePath, 5, dimension, VectorPresence.Create(5, sourceDocIds)))
        {
            int recordSize = checked(CanonicalInt8VectorMetadata.Size + dimension);
            byte[] remappedRecords = new byte[checked(remap.Length * recordSize)];
            Span<byte> record = stackalloc byte[recordSize];
            for (int newDocId = 0; newDocId < remap.Length; newDocId++)
            {
                source.CopyCanonicalRecordTo(remap[newDocId], record);
                record.CopyTo(remappedRecords.AsSpan(newDocId * recordSize, recordSize));
            }
            File.WriteAllBytes(remappedPath, remappedRecords);
        }

        using var remapped = CanonicalInt8VectorReader.OpenRecords(remappedPath, remap.Length, dimension, VectorPresence.Create(remap.Length, [0, 1, 2]));
        using var original = CanonicalInt8VectorReader.OpenRecords(sourcePath, 5, dimension, VectorPresence.Create(5, sourceDocIds));
        int remappedRecordSize = checked(CanonicalInt8VectorMetadata.Size + dimension);
        Span<byte> actual = stackalloc byte[remappedRecordSize];
        Span<byte> expected = stackalloc byte[remappedRecordSize];
        for (int newDocId = 0; newDocId < remap.Length; newDocId++)
        {
            remapped.CopyCanonicalRecordTo(newDocId, actual);
            original.CopyCanonicalRecordTo(remap[newDocId], expected);
            Assert.Equal(expected.ToArray(), actual.ToArray());
        }
    }

    [Theory]
    [InlineData(1e-30f, -2e-30f, 3e-30f)]
    [InlineData(float.MaxValue * 0.25f, -float.MaxValue * 0.5f, float.MaxValue * 0.125f)]
    public void Encode_AfterStableNormalisationKeepsTinyAndExtremeFiniteInputsFinite(float first, float second, float third)
    {
        Span<float> vector = stackalloc float[] { first, second, third };
        Assert.True(SimdVectorOps.NormaliseInPlace(vector));

        Span<byte> codes = stackalloc byte[vector.Length];
        CanonicalInt8VectorMetadata metadata = CanonicalInt8VectorWriter.Encode(vector, codes);

        Assert.True(float.IsFinite(metadata.Offset));
        Assert.True(float.IsFinite(metadata.Scale));
        Assert.True(float.IsFinite(metadata.DecodedSquaredNorm));
        Assert.True(metadata.DecodedSquaredNorm > 0f);
        Assert.All(codes.ToArray(), static code => Assert.InRange(code, (byte)0, byte.MaxValue));
    }

    [Fact]
    public void Encode_RejectsNonFiniteValuesWithoutPublishingPartialRecords()
    {
        string path = TempFile("canonical_non_finite.raw");
        var vectors = new Dictionary<int, ReadOnlyMemory<float>>
        {
            [0] = new float[] { 1f, float.NaN, 0f }
        };

        Assert.Throws<ArgumentException>(() => CanonicalInt8VectorWriter.WriteRecords(
            path,
            docCount: 1,
            dimension: 3,
            new InMemoryVectorSource(vectors, 3),
            [0]));

        Assert.False(File.Exists(path));
        Assert.Empty(Directory.EnumerateFiles(_fixture.Path, Path.GetFileName(path) + ".*"));
    }

    [Fact]
    public void Reader_RejectsNonFiniteMetadataAndTruncatedRows()
    {
        const int dimension = 8;
        var vectors = new Dictionary<int, ReadOnlyMemory<float>> { [0] = CreateNormalisedVector(dimension) };
        var source = new InMemoryVectorSource(vectors, dimension);
        string path = TempFile("canonical_corrupt.raw");
        byte[] presence = VectorPresence.Create(1, [0]);
        CanonicalInt8VectorWriter.WriteRecords(path, 1, dimension, source, [0]);

        byte[] bytes = File.ReadAllBytes(path);
        BinaryPrimitives.WriteInt32LittleEndian(bytes, BitConverter.SingleToInt32Bits(float.NaN));
        File.WriteAllBytes(path, bytes);
        Assert.Throws<InvalidDataException>(() => CanonicalInt8VectorReader.OpenRecords(path, 1, dimension, presence));

        File.WriteAllBytes(path, new byte[bytes.Length - 1]);
        Assert.Throws<InvalidDataException>(() => CanonicalInt8VectorReader.OpenRecords(path, 1, dimension, presence));
    }

    [Fact]
    public void Reader_CopyMethodsDoNotAllocatePerCandidate()
    {
        const int dimension = 384;
        var vectors = new Dictionary<int, ReadOnlyMemory<float>> { [0] = CreateNormalisedVector(dimension) };
        string path = TempFile("canonical_allocations.raw");
        CanonicalInt8VectorWriter.WriteRecords(path, 1, dimension, new InMemoryVectorSource(vectors, dimension), [0]);

        using var reader = CanonicalInt8VectorReader.OpenRecords(path, 1, dimension, VectorPresence.Create(1, [0]));
        Span<float> decoded = stackalloc float[dimension];
        Span<byte> record = stackalloc byte[CanonicalInt8VectorMetadata.Size + dimension];
        for (int index = 0; index < 4; index++)
        {
            reader.CopyVectorTo(0, decoded);
            reader.CopyCanonicalRecordTo(0, record);
            _ = reader.GetCanonicalMetadata(0);
            _ = reader.GetCanonicalCodes(0).Length;
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int index = 0; index < 256; index++)
        {
            reader.CopyVectorTo(0, decoded);
            reader.CopyCanonicalRecordTo(0, record);
            _ = reader.GetCanonicalMetadata(0);
            _ = reader.GetCanonicalCodes(0).Length;
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0, allocated);
    }

    private string TempFile(string name) => Path.Combine(_fixture.Path, $"{Guid.NewGuid():N}_{name}");

    private static float[] CreateNormalisedVector(int dimension)
    {
        var vector = new float[dimension];
        for (int index = 0; index < dimension; index++)
            vector[index] = ((index * 17 + 3) % 31 - 15) / 16f;
        if (!SimdVectorOps.NormaliseInPlace(vector))
            vector[0] = 1f;
        return vector;
    }

    private static float[] CreateRepeatedComponentVector(int dimension)
    {
        var vector = new float[dimension];
        for (int index = 0; index < dimension; index++)
            vector[index] = index % 3 == 0 ? 0.25f : -0.75f;
        return vector;
    }
}
