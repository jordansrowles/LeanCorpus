namespace Rowles.LeanCorpus.Tests.Core.CodecKit;

[Category(TestCategory.Unit)]
[Area(TestArea.CodecKit)]
public sealed class VectorPresenceTests
{
    [Theory]
    [InlineData(VectorQuantisation.None)]
    [InlineData(VectorQuantisation.Int8)]
    [InlineData(VectorQuantisation.BBQ)]
    public void SparsePresenceDistinguishesExplicitZero(VectorQuantisation quantisation)
    {
        string path = Path.Combine(Path.GetTempPath(), $"vector-presence-{Guid.NewGuid():N}");
        var vectors = new Dictionary<int, ReadOnlyMemory<float>> { [0] = new float[2], [2] = new float[] { 1, 0 } };
        try
        {
            if (quantisation == VectorQuantisation.None)
            {
                VectorWriter.WriteField(path, 3, 2, vectors);
                using var reader = VectorReader.Open(path);
                Assert.True(reader.HasVector(0));
                Assert.False(reader.HasVector(1));
                Assert.True(reader.HasVector(2));
                Assert.Equal(new float[2], reader.ReadVector(0));
            }
            else
            {
                if (quantisation == VectorQuantisation.Int8)
                    QuantisedVectorWriter.WriteInt8(path, 3, 2, vectors);
                else
                    QuantisedVectorWriter.WriteBBQ(path, 3, 2, vectors, new float[2]);
                using var reader = QuantisedVectorReader.Open(path);
                Assert.True(reader.HasVector(0));
                Assert.False(reader.HasVector(1));
                Assert.True(reader.HasVector(2));
            }
        }
        finally { File.Delete(path); }
    }
    [Fact]
    public void VersionsAndCatalogueRetainReadableV1()
    {
        Assert.Equal(2, CodecConstants.VectorVersion);
        Assert.Equal(2, CodecConstants.QuantisedVectorVersion);
        Assert.Equal(1, CodecConstants.HnswVersion);
        foreach (var descriptor in new[] { VectorCodecFiles.Float32, VectorCodecFiles.Quantised })
        {
            var legacy = Assert.Single(descriptor.SupportedVersions, version => version.Version == 1);
            Assert.True(legacy.IsReadable);
            Assert.False(legacy.IsWritable);
            Assert.Equal(CodecMigrationBehaviour.Rewrite, legacy.MigrationBehaviour);
            Assert.True(Assert.Single(descriptor.SupportedVersions, version => version.Version == 2).IsWritable);
        }
    }

    [Theory]
    [InlineData(VectorQuantisation.None)]
    [InlineData(VectorQuantisation.Int8)]
    [InlineData(VectorQuantisation.BBQ)]
    public void CorruptPresenceAndBodyLengthsAreRejected(VectorQuantisation quantisation)
    {
        string path = Path.Combine(Path.GetTempPath(), $"vector-corruption-{Guid.NewGuid():N}");
        var descriptor = quantisation == VectorQuantisation.None ? VectorCodecFiles.Float32 : VectorCodecFiles.Quantised;
        try
        {
            var vectors = new Dictionary<int, ReadOnlyMemory<float>> { [0] = new float[2], [2] = new float[] { 1, 0 } };
            if (quantisation == VectorQuantisation.None) VectorWriter.WriteField(path, 3, 2, vectors);
            else if (quantisation == VectorQuantisation.Int8) QuantisedVectorWriter.WriteInt8(path, 3, 2, vectors);
            else QuantisedVectorWriter.WriteBBQ(path, 3, 2, vectors, new float[2]);
            byte[] body;
            using (var input = new IndexInput(path))
            using (var frame = CodecFileReader.OpenSupported(input, descriptor))
                body = frame.ReadBody();
            byte[] badPadding = (byte[])body.Clone();
            badPadding[9] |= 0x80;
            foreach (var corrupt in new[] { badPadding, body[..9], body[..^1], body.Concat(new byte[1]).ToArray() })
            {
                CodecFileWriter.WriteAtomically(path, descriptor, false, output => output.WriteBytes(corrupt, 0, corrupt.Length));
                Assert.ThrowsAny<InvalidDataException>(() =>
                {
                    if (quantisation == VectorQuantisation.None) { using var reader = VectorReader.Open(path); }
                    else { using var reader = QuantisedVectorReader.Open(path); }
                });
            }
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void WriterRejectsInvalidIdsDuplicatesAndDimensions()
    {
        string path = Path.Combine(Path.GetTempPath(), $"vector-validation-{Guid.NewGuid():N}");
        foreach (var vectors in new[] {
            new Dictionary<int, ReadOnlyMemory<float>> { [-1] = new float[2] },
            new Dictionary<int, ReadOnlyMemory<float>> { [3] = new float[2] },
            new Dictionary<int, ReadOnlyMemory<float>> { [0] = new float[3] } })
        {
            Assert.Throws<InvalidDataException>(() => VectorWriter.WriteField(path, 3, 2, vectors));
            Assert.Throws<InvalidDataException>(() => QuantisedVectorWriter.WriteInt8(path, 3, 2, vectors));
            Assert.Throws<InvalidDataException>(() => QuantisedVectorWriter.WriteBBQ(path, 3, 2, vectors, new float[2]));
        }
        Assert.Throws<InvalidDataException>(() => VectorPresence.Create(3, new[] { 0, 0 }));
        Assert.Throws<OverflowException>(() => VectorPresence.ByteCount(int.MaxValue));
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void BitmapUsesDocumentOrderAcrossByteBoundaries()
    {
        Assert.Equal(new byte[] { 1, 2 }, VectorPresence.Create(10, new[] { 9, 0 }));
        Assert.Empty(VectorPresence.Create(0, Array.Empty<int>()));
    }

    [Fact]
    public void StreamingWritersRejectDuplicatePresentIdsBeforeWriting()
    {
        string path = Path.Combine(Path.GetTempPath(), $"vector-source-validation-{Guid.NewGuid():N}");
        var source = new InMemoryVectorSource(new Dictionary<int, ReadOnlyMemory<float>> { [0] = new float[2] }, 2);
        Assert.Throws<InvalidDataException>(() => VectorWriter.WriteField(path, 1, 2, source, new[] { 0, 0 }));
        Assert.Throws<InvalidDataException>(() => QuantisedVectorWriter.WriteInt8(path, 1, 2, source, new[] { 0, 0 }));
        Assert.Throws<InvalidDataException>(() => QuantisedVectorWriter.WriteBBQ(path, 1, 2, source, new[] { 0, 0 }));
        Assert.False(File.Exists(path));
    }

}
