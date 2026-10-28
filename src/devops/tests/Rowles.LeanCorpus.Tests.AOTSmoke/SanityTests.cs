using Xunit;
using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Tests.AOTSmoke;

public class SanityTests
{
    [Fact]
    public void TrueIsTrue() => Assert.True(true);

    [Fact]
    public void CanonicalCodecFrameRoundTripsUnderAot()
    {
        string path = Path.Combine(Path.GetTempPath(), $"lc-aot-codec-{Guid.NewGuid():N}.nrm");
        try
        {
            var catalog = new CodecCatalogBuilder().AddBuiltIns().Build();
            var descriptor = catalog.GetFile("leancorpus.norms.data");
            CodecFileWriter.WriteAtomically(path, descriptor, durable: false, body =>
            {
                body.WriteInt32(42);
                body.WriteByte(7);
            });

            using var input = new IndexInput(path);
            using var frame = CodecFileReader.Open(input, catalog);
            Assert.Equal("leancorpus.norms.data", frame.Metadata.FormatId);
            Assert.Equal(descriptor.CurrentFormatVersion, frame.Metadata.FormatVersion);
            frame.ValidateChecksum();
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }
}
