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
                    HistoricalBbqVectorFixtures.CopyLegacyDictionary(path, 3, 2);
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
        Assert.Equal(3, CodecConstants.QuantisedVectorVersion);
        Assert.Equal(1, CodecConstants.HnswVersion);

        var legacyFloat32 = Assert.Single(VectorCodecFiles.Float32.SupportedVersions, version => version.Version == 1);
        Assert.True(legacyFloat32.IsReadable);
        Assert.False(legacyFloat32.IsWritable);
        Assert.Equal(CodecMigrationBehaviour.Rewrite, legacyFloat32.MigrationBehaviour);
        Assert.True(Assert.Single(VectorCodecFiles.Float32.SupportedVersions, version => version.Version == 2).IsWritable);

        foreach (var versionNumber in new[] { 1, 2 })
        {
            var legacyQuantised = Assert.Single(VectorCodecFiles.Quantised.SupportedVersions,
                version => version.Version == versionNumber);
            Assert.True(legacyQuantised.IsReadable);
            Assert.False(legacyQuantised.IsWritable);
            Assert.Equal(CodecMigrationBehaviour.Rewrite, legacyQuantised.MigrationBehaviour);
        }

        Assert.True(Assert.Single(VectorCodecFiles.Quantised.SupportedVersions, version => version.Version == 3).IsWritable);
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
            else HistoricalBbqVectorFixtures.CopyLegacyDictionary(path, 3, 2);
            byte[] body;
            using (var input = new IndexInput(path))
            using (var frame = CodecFileReader.OpenSupported(input, descriptor))
                body = frame.ReadBody();
            byte[] badPadding = (byte[])body.Clone();
            badPadding[9] |= 0x80;
            byte[] mismatchedStorageKind = (byte[])body.Clone();
            mismatchedStorageKind[10] = (byte)(quantisation == VectorQuantisation.Int8
                ? QuantisedVectorStorageKind.LegacyBbqSign
                : QuantisedVectorStorageKind.LegacyInt8);
            byte[] unknownStorageKind = (byte[])body.Clone();
            unknownStorageKind[10] = byte.MaxValue;
            var corruptions = new List<byte[]> { badPadding, body[..9], body[..^1], body.Concat(new byte[1]).ToArray() };
            if (quantisation != VectorQuantisation.None)
            {
                corruptions.Add(mismatchedStorageKind);
                corruptions.Add(unknownStorageKind);
            }
            foreach (var corrupt in corruptions)
            {
                // Deliberately malformed test bytes use the raw framing primitive, not a semantic writer.
                CodecFileWriter.WriteAtomically(path, descriptor.FormatId, descriptor.CurrentFormatVersion!.Value,
                    false, output => output.WriteBytes(corrupt, 0, corrupt.Length));
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
            Assert.Throws<InvalidOperationException>(() => QuantisedVectorWriter.WriteBBQ(path, 3, 2, vectors, new float[2]));
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
        Assert.Throws<InvalidOperationException>(() => QuantisedVectorWriter.WriteBBQ(path, 1, 2, source, new[] { 0, 0 }));
        Assert.False(File.Exists(path));
    }

}
