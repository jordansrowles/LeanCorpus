using System.Text;
using Rowles.DataForge;

namespace Rowles.DataForge.Tests.Core;

public sealed class SeedDerivationTests
{
    [Fact]
    public void Seed_derivation_matches_locked_vectors()
    {
        Assert.Equal(0x543b24f353f90d3bUL, DataForgeSeedDerivation.Derive(42, "dataforge-golden", 1, 0));
        Assert.Equal(0x17cacb269e7abbd3UL, DataForgeSeedDerivation.Derive(42, "dataforge-golden", 1, 0, "golden"));
        Assert.Equal(0x639cbf9d3f547d20UL, DataForgeSeedDerivation.Derive(42, "leancorpus-search", 1, 0, "content"));
        Assert.Equal(0x5b942f306fc133fdUL, DataForgeSeedDerivation.Derive(42, "leancorpus-search", 1, 9_999, "content"));
    }

    [Fact]
    public void Seed_derivation_matches_full_sha256_vector()
    {
        Assert.Equal(
            "543b24f353f90d3b2556193fe6a95a4db67b8edfc4980f5f1825c269357329ec",
            Convert.ToHexStringLower(DataForgeSeedDerivation.DeriveDigest(42, "dataforge-golden", 1, 0)));
    }

    [Fact]
    public void Component_labels_are_stable_and_independent()
    {
        var context = new DataForgeRecordContext(42, "dataforge-golden", 1, 17);
        var firstNameStream = context.Random("structured/name");
        _ = context.Random("content").NextUInt64();
        _ = context.Random("content").NextUInt64();
        var laterNameStream = context.Random("structured/name");

        Assert.Equal(firstNameStream.NextUInt64(), laterNameStream.NextUInt64());
        Assert.NotEqual(
            context.Random("structured/name").NextUInt64(),
            context.Random("structured/company").NextUInt64());
    }

    [Fact]
    public void Seed_derivation_rejects_invalid_lengths()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => DataForgeSeedDerivation.Derive(1, new string('a', 129), 1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => DataForgeSeedDerivation.Derive(1, "profile", 1, 0, new string('a', 65)));
    }

    [Theory]
    [InlineData(0xD800)]
    [InlineData(0xDC00)]
    public void Seed_derivation_rejects_unpaired_surrogates(int codeUnit)
    {
        var malformed = new string((char)codeUnit, 1);
        Assert.Equal(1, malformed.Length);
        Assert.Equal(codeUnit, (int)malformed[0]);
        Assert.Throws<EncoderFallbackException>(() => DataForgeSeedDerivation.Derive(1, "profile" + malformed, 1, 0));
        Assert.Throws<EncoderFallbackException>(() => DataForgeSeedDerivation.Derive(1, "profile", 1, 0, malformed));
    }

    [Fact]
    public void Seed_derivation_accepts_valid_surrogate_pairs()
    {
        var value = new string([(char)0xD83D, (char)0xDE00]);
        Assert.Equal(new byte[] { 0xF0, 0x9F, 0x98, 0x80 }, DataForgeSeedDerivation.GetStrictUtf8Bytes(value));
        Assert.Equal(
            DataForgeSeedDerivation.Derive(1, char.ConvertFromUtf32(0x1F600), 1, 0, "component"),
            DataForgeSeedDerivation.Derive(1, value, 1, 0, "component"));
        Assert.Equal(
            DataForgeSeedDerivation.Derive(1, "profile", 1, 0, char.ConvertFromUtf32(0x1F600)),
            DataForgeSeedDerivation.Derive(1, "profile", 1, 0, value));
    }
}
