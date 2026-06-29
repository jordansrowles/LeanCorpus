using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Codecs.CodecKit.Codecs;
using Rowles.LeanCorpus.Codecs.CodecKit.Formats;

namespace Rowles.LeanCorpus.Tests.Unit.Codecs.CodecKit;

[Trait("Category", "CodecKit")]
public sealed class CodecMigrationTests
{
    // ═══════════════════════════════════════════════════
    //  CodecVersionStep
    // ═══════════════════════════════════════════════════

    [Fact(DisplayName = "CodecVersionStep stores version, label, and reader")]
    public void CodecVersionStep_StoresAllFields()
    {
        var reader = Codec.BytesOwnedRemaining();
        var step = new CodecVersionStep(3, "pos-v3", reader);

        Assert.Equal(3, step.Version);
        Assert.Equal("pos-v3", step.Label);
        Assert.Same(reader, step.Reader);
    }

    [Fact(DisplayName = "CodecVersionStep supports equality by value")]
    public void CodecVersionStep_ValueEquality()
    {
        var r1 = Codec.BytesOwnedRemaining();
        var r2 = Codec.BytesOwnedRemaining();

        var a = new CodecVersionStep(1, "v1", r1);
        var b = new CodecVersionStep(1, "v1", r1);
        var c = new CodecVersionStep(2, "v2", r2);

        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    // ═══════════════════════════════════════════════════
    //  CodecFormat
    // ═══════════════════════════════════════════════════

    [Fact(DisplayName = "CodecFormat stores codec id and steps")]
    public void CodecFormat_StoresAllFields()
    {
        var steps = new List<CodecVersionStep>
        {
            new(1, "pos-v1", Codec.BytesOwnedRemaining()),
        };

        var fmt = new CodecFormat("pos", steps);

        Assert.Equal("pos", fmt.CodecId);
        Assert.Single(fmt.Steps);
        Assert.Equal(1, fmt.Steps[0].Version);
    }

    [Fact(DisplayName = "CodecFormat rejects empty steps list")]
    public void CodecFormat_EmptySteps_Throws()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            new CodecFormat("pos", Array.Empty<CodecVersionStep>()));

        Assert.Equal("steps", ex.ParamName);
    }

    [Fact(DisplayName = "CodecFormat rejects null codecId")]
    public void CodecFormat_NullCodecId_Throws()
    {
        var steps = new List<CodecVersionStep>
        {
            new(1, "v1", Codec.BytesOwnedRemaining()),
        };

        Assert.Throws<ArgumentNullException>(() =>
            new CodecFormat(null!, steps));
    }

    [Fact(DisplayName = "CodecFormat rejects null steps")]
    public void CodecFormat_NullSteps_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new CodecFormat("pos", null!));
    }

    [Fact(DisplayName = "CodecFormat rejects duplicate versions")]
    public void CodecFormat_DuplicateVersions_Throws()
    {
        var steps = new List<CodecVersionStep>
        {
            new(1, "v1", Codec.BytesOwnedRemaining()),
            new(1, "v1-dup", Codec.BytesOwnedRemaining()),
        };

        var ex = Assert.Throws<ArgumentException>(() =>
            new CodecFormat("pos", steps));

        Assert.Equal("steps", ex.ParamName);
        Assert.Contains("Duplicate version 1", ex.Message);
        Assert.Contains("pos", ex.Message);
    }

    [Fact(DisplayName = "CodecFormat allows ascending unique versions")]
    public void CodecFormat_UniqueVersions_Accepted()
    {
        var steps = new List<CodecVersionStep>
        {
            new(1, "v1", Codec.BytesOwnedRemaining()),
            new(2, "v2", Codec.BytesOwnedRemaining()),
            new(3, "v3", Codec.BytesOwnedRemaining()),
        };

        var fmt = new CodecFormat("pos", steps);

        Assert.Equal(3, fmt.Steps.Count);
        Assert.Equal(1, fmt.Steps[0].Version);
        Assert.Equal(3, fmt.Steps[2].Version);
    }

    // ═══════════════════════════════════════════════════
    //  CodecMigrationRegistry
    // ═══════════════════════════════════════════════════

    [Fact(DisplayName = "Default registry is a singleton")]
    public void Registry_Default_IsSameInstance()
    {
        Assert.Same(CodecMigrationRegistry.Default, CodecMigrationRegistry.Default);
    }

    [Fact(DisplayName = "Register stores format and Get retrieves it")]
    public void Registry_Register_And_Get()
    {
        var steps = new List<CodecVersionStep>
        {
            new(1, "v1", Codec.BytesOwnedRemaining()),
        };
        var fmt = new CodecFormat("test-codec", steps);

        CodecMigrationRegistry.Default.Register(fmt);
        var retrieved = CodecMigrationRegistry.Default.Get("test-codec");

        Assert.NotNull(retrieved);
        Assert.Same(fmt, retrieved);
    }

    [Fact(DisplayName = "Get returns null for unregistered codec id")]
    public void Registry_Get_Unregistered_ReturnsNull()
    {
        var result = CodecMigrationRegistry.Default.Get("nonexistent-codec-id");
        Assert.Null(result);
    }

    [Fact(DisplayName = "Register overwrites existing format with same codec id")]
    public void Registry_Register_Overwrites()
    {
        var steps1 = new List<CodecVersionStep>
        {
            new(1, "old", Codec.BytesOwnedRemaining()),
        };
        var fmt1 = new CodecFormat("test-overwrite", steps1);

        var steps2 = new List<CodecVersionStep>
        {
            new(1, "new-v1", Codec.BytesOwnedRemaining()),
            new(2, "new-v2", Codec.BytesOwnedRemaining()),
        };
        var fmt2 = new CodecFormat("test-overwrite", steps2);

        CodecMigrationRegistry.Default.Register(fmt1);
        CodecMigrationRegistry.Default.Register(fmt2);

        var retrieved = CodecMigrationRegistry.Default.Get("test-overwrite");
        Assert.NotNull(retrieved);
        Assert.Equal(2, retrieved.Steps.Count);
        Assert.Equal("new-v2", retrieved.Steps[1].Label);
    }

    [Fact(DisplayName = "Register returns the registry for chaining")]
    public void Registry_Register_ReturnsRegistryForChaining()
    {
        var fmt = new CodecFormat("chain-test",
            new List<CodecVersionStep>
            {
                new(1, "v1", Codec.BytesOwnedRemaining()),
            });

        var result = CodecMigrationRegistry.Default.Register(fmt);

        Assert.Same(CodecMigrationRegistry.Default, result);
    }

    [Fact(DisplayName = "Register rejects null format")]
    public void Registry_Register_Null_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            CodecMigrationRegistry.Default.Register(null!));
    }

    [Fact(DisplayName = "CodecMigrationRegistry instances are independent")]
    public void Registry_InstancesAreIndependent()
    {
        var r1 = new CodecMigrationRegistry();
        var r2 = new CodecMigrationRegistry();

        var fmt1 = new CodecFormat("a", new List<CodecVersionStep>
        {
            new(1, "v1", Codec.BytesOwnedRemaining()),
        });
        var fmt2 = new CodecFormat("b", new List<CodecVersionStep>
        {
            new(2, "v2", Codec.BytesOwnedRemaining()),
        });

        r1.Register(fmt1);
        r2.Register(fmt2);

        Assert.NotNull(r1.Get("a"));
        Assert.Null(r1.Get("b"));
        Assert.Null(r2.Get("a"));
        Assert.NotNull(r2.Get("b"));
    }
}
