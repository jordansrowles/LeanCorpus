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
    public void Seed_derivation_rejects_invalid_lengths_and_unpaired_surrogates()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => DataForgeSeedDerivation.Derive(1, new string('a', 129), 1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => DataForgeSeedDerivation.Derive(1, "profile", 1, 0, new string('a', 65)));
        Assert.Throws<EncoderFallbackException>(() => DataForgeSeedDerivation.Derive(1, "profile\ud800", 1, 0));
    }
}
