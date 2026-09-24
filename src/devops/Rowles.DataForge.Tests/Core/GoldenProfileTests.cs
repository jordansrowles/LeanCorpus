using System.Globalization;
using System.Text;
using Rowles.DataForge;

namespace Rowles.DataForge.Tests.Core;

public sealed class GoldenProfileTests
{
    private const string ExpectedRecords =
        "{\"id\":0,\"u64Hex\":\"6074ee7d2bef9d5a\",\"bounded\":911,\"flag\":true,\"singleBitsHex\":\"3eb6eb42\",\"text\":\"record-0\"}\n" +
        "{\"id\":1,\"u64Hex\":\"6351c0d2390f2b3a\",\"bounded\":608,\"flag\":false,\"singleBitsHex\":\"3f5915f2\",\"text\":\"record-1\"}\n" +
        "{\"id\":2,\"u64Hex\":\"0c32a1471fc38625\",\"bounded\":634,\"flag\":false,\"singleBitsHex\":\"3f53cf84\",\"text\":\"record-2\"}\n";

    [Fact]
    public void Golden_profile_matches_exact_326_byte_stream_and_hash()
    {
        using var stream = new MemoryStream();
        var summary = DataForgeMaterialiser.GenerateToStream(new GoldenProfile(), new DataForgeGenerationOptions(42, 3), stream);
        var expectedBytes = Encoding.UTF8.GetBytes(ExpectedRecords);

        Assert.Equal(326, expectedBytes.Length);
        Assert.Equal(expectedBytes, stream.ToArray());
        Assert.Equal(326, summary.LogicalByteCount);
        Assert.Equal("b76cace15f1d5fbffa8bbd993ee37a66e084d5c685dbdab3986c2189fd51047c", summary.ContentSha256);
    }

    [Fact]
    public void Materialised_golden_dataset_verifies_and_reproduces()
    {
        var directory = Path.Combine(Path.GetTempPath(), "dataforge-golden-" + Guid.NewGuid().ToString("N"));
        try
        {
            var profile = new GoldenProfile();
            var result = DataForgeMaterialiser.Materialise(profile, new DataForgeGenerationOptions(42, 3), directory);
            var verified = DataForgeVerifier.VerifyMaterialised(directory);
            var reproduced = DataForgeVerifier.Reproduce(directory, profile);

            Assert.Equal(result.Identity.GetCanonicalBytes(), verified.Identity.GetCanonicalBytes());
            Assert.True(reproduced.IsReproduced);
            Assert.Equal(result.Manifest.ContentSha256, reproduced.Manifest.ContentSha256);
            Assert.Equal("dataforge-golden", result.Identity.ProfileId);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Materialisation_refuses_overwrite_unless_force_is_set()
    {
        var directory = Path.Combine(Path.GetTempPath(), "dataforge-force-" + Guid.NewGuid().ToString("N"));
        try
        {
            var profile = new GoldenProfile();
            var options = new DataForgeGenerationOptions(42, 3);
            _ = DataForgeMaterialiser.Materialise(profile, options, directory);
            Assert.Throws<IOException>(() => DataForgeMaterialiser.Materialise(profile, options, directory));
            _ = DataForgeMaterialiser.Materialise(profile, options, directory, force: true);
            Assert.False(DataForgeVerifier.VerifyMaterialised(directory).IsReproduced);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    internal sealed record GoldenRecord(int Id, string U64Hex, int Bounded, bool Flag, string SingleBitsHex, string Text);

    internal sealed class GoldenProfile : IDataForgeGeneratedProfile<GoldenRecord>
    {
        public DataForgeProfileDescriptor Descriptor { get; } = new(
            "dataforge-golden", 1, "Generated", 42, 3, "DataForge canonical output qualification profile.");

        public IReadOnlyList<DataForgeDependencyVersion> Dependencies { get; } = [];

        public IDataForgeCanonicalRecordWriter<GoldenRecord> CanonicalRecordWriter { get; } = new GoldenRecordWriter();

        public IEnumerable<GoldenRecord> Generate(DataForgeGenerationOptions options)
        {
            for (var ordinal = 0; ordinal < options.RecordCount; ordinal++)
            {
                var random = new DataForgeRecordContext(options.Seed, Descriptor.ProfileId, Descriptor.ProfileVersion, (ulong)ordinal).Random("golden");
                var u64 = random.NextUInt64().ToString("x16", CultureInfo.InvariantCulture);
                var bounded = random.NextInt32(1_000);
                var flag = random.NextBoolean();
                var single = BitConverter.SingleToInt32Bits(random.NextSingle01()).ToString("x8", CultureInfo.InvariantCulture);
                yield return new GoldenRecord(ordinal, u64, bounded, flag, single, $"record-{ordinal.ToString(CultureInfo.InvariantCulture)}");
            }
        }

        public IReadOnlyList<DataForgeSummary> Summarise(DataForgeGenerationOptions options) => [];

        private sealed class GoldenRecordWriter : IDataForgeCanonicalRecordWriter<GoldenRecord>
        {
            public void Write(CanonicalJsonWriter writer, GoldenRecord record)
            {
                writer.WriteStartObject();
                writer.WritePropertyName("id");
                writer.WriteInt32Value(record.Id);
                writer.WritePropertyName("u64Hex");
                writer.WriteStringValue(record.U64Hex);
                writer.WritePropertyName("bounded");
                writer.WriteInt32Value(record.Bounded);
                writer.WritePropertyName("flag");
                writer.WriteBooleanValue(record.Flag);
                writer.WritePropertyName("singleBitsHex");
                writer.WriteStringValue(record.SingleBitsHex);
                writer.WritePropertyName("text");
                writer.WriteStringValue(record.Text);
                writer.WriteEndObject();
            }
        }
    }
}
