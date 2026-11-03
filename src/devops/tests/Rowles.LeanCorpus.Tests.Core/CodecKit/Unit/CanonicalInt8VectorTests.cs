using Rowles.LeanCorpus.Codecs.Vectors;
using Rowles.LeanCorpus.Tests.Shared.Fixtures;

namespace Rowles.LeanCorpus.Tests.Core.Codecs;

[Category(TestCategory.Unit)]
[Area(TestArea.CodecKit)]
public sealed class CanonicalInt8VectorTests : IClassFixture<TestDirectoryFixture>
{
    private readonly TestDirectoryFixture _fixture;

    public CanonicalInt8VectorTests(TestDirectoryFixture fixture) => _fixture = fixture;

    [Fact]
    public void HistoricalCanonicalReader_CopiesRawRowsWithoutAllocatingPerCandidate()
    {
        const int docCount = 5;
        const int dimension = 129;
        string path = Path.Combine(_fixture.Path, $"{Guid.NewGuid():N}_historical_canonical.vq");
        HistoricalBbqVectorFixtures.CopyOsq(
            path, docCount, dimension, "WriteAndRead_V3BodyHasLockedArraysAndPreservesCanonicalRows");

        using var reader = QuantisedVectorReader.Open(path);
        Assert.Equal(QuantisedVectorStorageKind.BbqOsqCanonicalInt8, reader.StorageKind);
        Span<float> decoded = stackalloc float[dimension];
        Span<byte> record = stackalloc byte[CanonicalInt8VectorMetadata.Size + dimension];
        for (int docId = 0; docId < docCount; docId++)
        {
            CanonicalInt8VectorMetadata metadata = reader.GetCanonicalMetadata(docId);
            ReadOnlySpan<byte> codes = reader.GetCanonicalCodes(docId);
            reader.CopyCanonicalRecordTo(docId, record);
            reader.ReadVector(docId, decoded);

            Assert.Equal(codes.ToArray(), record[CanonicalInt8VectorMetadata.Size..].ToArray());
            Assert.Equal(codes.ToArray().Sum(static code => (int)code), metadata.SumOfCodes);
            for (int index = 0; index < dimension; index++)
                Assert.Equal((float)((double)metadata.Offset + (double)metadata.Scale * codes[index]), decoded[index]);
        }

        for (int iteration = 0; iteration < 4; iteration++)
        {
            reader.ReadVector(0, decoded);
            reader.CopyCanonicalRecordTo(0, record);
            _ = reader.GetCanonicalMetadata(0);
            _ = reader.GetCanonicalCodes(0).Length;
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int iteration = 0; iteration < 256; iteration++)
        {
            reader.ReadVector(0, decoded);
            reader.CopyCanonicalRecordTo(0, record);
            _ = reader.GetCanonicalMetadata(0);
            _ = reader.GetCanonicalCodes(0).Length;
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0, allocated);
    }
}
