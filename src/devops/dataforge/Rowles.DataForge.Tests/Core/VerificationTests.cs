using Rowles.DataForge;

namespace Rowles.DataForge.Tests.Core;

public sealed class VerificationTests
{
    [Theory]
    [InlineData("tampered")]
    [InlineData("truncated")]
    [InlineData("extra-record")]
    public void Materialised_file_mutations_are_rejected(string mutation)
    {
        var directory = Path.Combine(Path.GetTempPath(), "dataforge-verify-" + Guid.NewGuid().ToString("N"));
        try
        {
            _ = DataForgeMaterialiser.Materialise(
                new GoldenProfileTests.GoldenProfile(),
                new DataForgeGenerationOptions(42, 3),
                directory);
            var path = Path.Combine(directory, "records.ndjson");
            var bytes = File.ReadAllBytes(path);
            switch (mutation)
            {
                case "tampered":
                    bytes[10] ^= 1;
                    File.WriteAllBytes(path, bytes);
                    break;
                case "truncated":
                    File.WriteAllBytes(path, bytes[..^1]);
                    break;
                case "extra-record":
                    using (var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.None))
                        stream.Write(bytes.AsSpan(0, Array.IndexOf(bytes, (byte)'\n') + 1));
                    break;
            }

            Assert.Throws<InvalidDataException>(() => DataForgeVerifier.VerifyMaterialised(directory));
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Reproduction_rejects_a_different_profile_identity()
    {
        var directory = Path.Combine(Path.GetTempPath(), "dataforge-profile-" + Guid.NewGuid().ToString("N"));
        try
        {
            _ = DataForgeMaterialiser.Materialise(
                new GoldenProfileTests.GoldenProfile(),
                new DataForgeGenerationOptions(42, 3),
                directory);
            Assert.Throws<InvalidDataException>(() => DataForgeVerifier.Reproduce(directory, new AlternateProfile()));
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class AlternateProfile : IDataForgeGeneratedProfile<GoldenProfileTests.GoldenRecord>
    {
        public DataForgeProfileDescriptor Descriptor { get; } = new("other", 1, "Generated", 42, 1, "Alternate profile.");

        public IReadOnlyList<DataForgeDependencyVersion> Dependencies { get; } = [];

        public IDataForgeCanonicalRecordWriter<GoldenProfileTests.GoldenRecord> CanonicalRecordWriter => throw new NotSupportedException();

        public IEnumerable<GoldenProfileTests.GoldenRecord> Generate(DataForgeGenerationOptions options) => [];

        public IReadOnlyList<DataForgeSummary> Summarise(DataForgeGenerationOptions options) => [];
    }
}
