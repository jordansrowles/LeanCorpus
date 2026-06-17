using System.Buffers;
using Rowles.LeanCorpus.Codecs.CodecKit.Codecs;
using Rowles.LeanCorpus.Codecs.CodecKit.Exceptions;

namespace Rowles.LeanCorpus.Tests.Unit.Codecs.CodecKit.Internal;

abstract class Animal { }

class Dog : Animal
{
    public string Name { get; init; } = "";
}

class Cat : Animal
{
    public int Lives { get; init; }
}

[Trait("Category", "CodecKit")]
public sealed class VersionedCodecTests
{
    private static readonly ICodec<string> StringCodec =
        Codec.LengthPrefixed(Codec.VarInt32, Codec.Utf8StringRemaining(), TrailingDataPolicy.Allow);

    private static readonly ICodec<Dog> DogCodec = Codec.Record<Dog>()
        .Field("name", d => d.Name, StringCodec)
        .Build(f => new Dog { Name = (string)f["name"]! });

    private static readonly ICodec<Cat> CatCodec = Codec.Record<Cat>()
        .Field("lives", c => c.Lives, Codec.Int32LE)
        .Build(f => new Cat { Lives = (int)f["lives"]! });

    private static readonly ICodec<Animal> Versioned = Codec.Versioned<Animal, int>(
        Codec.Int32LE,
        Codec.VersionCase<Animal, Dog>(1, "Dog", DogCodec),
        Codec.VersionCase<Animal, Cat>(2, "Cat", CatCodec));

    [Fact(DisplayName = "VersionedCodec: Constructor throws ArgumentNullException when versionCodec is null")]
    public void Constructor_NullVersionCodec_Throws()
    {
        var ex = Assert.Throws<ArgumentNullException>(() =>
            new VersionedCodec<Animal, int>(null!));
        Assert.Contains("versionCodec", ex.Message);
    }

    [Fact(DisplayName = "VersionedCodec: Constructor throws ArgumentException when cases array is null")]
    public void Constructor_NullCases_Throws()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            new VersionedCodec<Animal, int>(Codec.Int32LE, null!));
        Assert.Contains("cases", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(DisplayName = "VersionedCodec: Constructor throws ArgumentException when cases array is empty")]
    public void Constructor_EmptyCases_Throws()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            new VersionedCodec<Animal, int>(Codec.Int32LE, []));
        Assert.Contains("cases", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(DisplayName = "VersionedCodec: Constructor throws ArgumentException on duplicate version values")]
    public void Constructor_DuplicateVersion_Throws()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            Codec.Versioned<Animal, int>(
                Codec.Int32LE,
                Codec.VersionCase<Animal, Dog>(1, "Dog", DogCodec),
                Codec.VersionCase<Animal, Cat>(1, "Cat", CatCodec)));
        Assert.Contains("Duplicate", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(DisplayName = "VersionedCodec: Constructor throws ArgumentException on duplicate case types")]
    public void Constructor_DuplicateCaseType_Throws()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            Codec.Versioned<Animal, int>(
                Codec.Int32LE,
                Codec.VersionCase<Animal, Dog>(1, "Dog1", DogCodec),
                Codec.VersionCase<Animal, Dog>(2, "Dog2", DogCodec)));
        Assert.Contains("Duplicate", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(DisplayName = "VersionedCodec: Round-trip Dog — decode returns Dog with correct Name")]
    public void RoundTrip_Dog()
    {
        var dog = new Dog { Name = "Rex" };
        byte[] encoded = Codec.EncodeToArray(Versioned, dog);
        Animal decoded = Codec.Decode(Versioned, encoded);

        var result = Assert.IsType<Dog>(decoded);
        Assert.Equal("Rex", result.Name);
    }

    [Fact(DisplayName = "VersionedCodec: Round-trip Cat — decode returns Cat with correct Lives")]
    public void RoundTrip_Cat()
    {
        var cat = new Cat { Lives = 9 };
        byte[] encoded = Codec.EncodeToArray(Versioned, cat);
        Animal decoded = Codec.Decode(Versioned, encoded);

        var result = Assert.IsType<Cat>(decoded);
        Assert.Equal(9, result.Lives);
    }

    [Fact(DisplayName = "VersionedCodec: Round-trip Dog with empty string name")]
    public void RoundTrip_Dog_EmptyName()
    {
        var dog = new Dog { Name = "" };
        byte[] encoded = Codec.EncodeToArray(Versioned, dog);
        Animal decoded = Codec.Decode(Versioned, encoded);

        var result = Assert.IsType<Dog>(decoded);
        Assert.Equal("", result.Name);
    }

    [Fact(DisplayName = "VersionedCodec: Round-trip Cat with zero lives")]
    public void RoundTrip_Cat_ZeroLives()
    {
        var cat = new Cat { Lives = 0 };
        byte[] encoded = Codec.EncodeToArray(Versioned, cat);
        Animal decoded = Codec.Decode(Versioned, encoded);

        var result = Assert.IsType<Cat>(decoded);
        Assert.Equal(0, result.Lives);
    }

    [Fact(DisplayName = "VersionedCodec: Round-trip Dog with Unicode name")]
    public void RoundTrip_Dog_UnicodeName()
    {
        var dog = new Dog { Name = "Rønäldo 🐕" };
        byte[] encoded = Codec.EncodeToArray(Versioned, dog);
        Animal decoded = Codec.Decode(Versioned, encoded);

        var result = Assert.IsType<Dog>(decoded);
        Assert.Equal("Rønäldo 🐕", result.Name);
    }

    [Fact(DisplayName = "VersionedCodec: Round-trip Cat with MaxValue lives")]
    public void RoundTrip_Cat_MaxLives()
    {
        var cat = new Cat { Lives = int.MaxValue };
        byte[] encoded = Codec.EncodeToArray(Versioned, cat);
        Animal decoded = Codec.Decode(Versioned, encoded);

        var result = Assert.IsType<Cat>(decoded);
        Assert.Equal(int.MaxValue, result.Lives);
    }

    [Fact(DisplayName = "VersionedCodec: Multiple interleaved Dog/Cat round-trips preserve values")]
    public void RoundTrip_Interleaved()
    {
        var animals = new Animal[]
        {
            new Dog { Name = "Buddy" },
            new Cat { Lives = 7 },
            new Dog { Name = "Max" },
            new Cat { Lives = 3 },
            new Dog { Name = "Bella" },
        };

        foreach (var animal in animals)
        {
            byte[] encoded = Codec.EncodeToArray(Versioned, animal);
            Animal decoded = Codec.Decode(Versioned, encoded);

            if (animal is Dog dog)
            {
                var result = Assert.IsType<Dog>(decoded);
                Assert.Equal(dog.Name, result.Name);
            }
            else if (animal is Cat cat)
            {
                var result = Assert.IsType<Cat>(decoded);
                Assert.Equal(cat.Lives, result.Lives);
            }
        }
    }

    [Fact(DisplayName = "VersionedCodec: Encode null value throws ArgumentNullException")]
    public void Encode_Null_Throws()
    {
        var ex = Assert.Throws<ArgumentNullException>(() =>
            Codec.EncodeToArray(Versioned, null!));
        Assert.Contains("value", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(DisplayName = "VersionedCodec: Encode unregistered type throws CodecValidationException")]
    public void Encode_UnregisteredType_Throws()
    {
        var unregistered = new UnregisteredAnimal();

        var ex = Assert.Throws<CodecValidationException>(() =>
            Codec.EncodeToArray(Versioned, unregistered));
        Assert.Equal(CodecErrorCode.InvalidValue, ex.ErrorCode);
    }

    [Fact(DisplayName = "VersionedCodec: Decode with unknown version throws UnknownVersionException")]
    public void Decode_UnknownVersion_Throws()
    {
        byte[] data = Codec.EncodeToArray(Codec.Int32LE, 99);

        var ex = Assert.Throws<UnknownVersionException>(() =>
            Codec.Decode(Versioned, data));
        Assert.Equal(99, ex.VersionValue);
    }

    [Fact(DisplayName = "VersionedCodec: Decode with negative unknown version throws UnknownVersionException")]
    public void Decode_NegativeUnknownVersion_Throws()
    {
        byte[] data = Codec.EncodeToArray(Codec.Int32LE, -1);

        var ex = Assert.Throws<UnknownVersionException>(() =>
            Codec.Decode(Versioned, data));
        Assert.Equal(-1, ex.VersionValue);
    }

    [Fact(DisplayName = "VersionedCodec: Decode empty data throws")]
    public void Decode_EmptyData_Throws()
    {
        Assert.Throws<InsufficientDataException>(() =>
            Codec.Decode(Versioned, []));
    }

    [Fact(DisplayName = "VersionedCodec: Decode version only but no body throws")]
    public void Decode_VersionOnly_NoBody_Throws()
    {
        byte[] data = Codec.EncodeToArray(Codec.Int32LE, 1);

        Assert.Throws<InsufficientDataException>(() =>
            Codec.Decode(Versioned, data));
    }

    [Fact(DisplayName = "VersionedCodec: Works with byte version codec (UInt8)")]
    public void ByteVersionCodec_RoundTrip()
    {
        var versioned = Codec.Versioned<Animal, byte>(
            Codec.UInt8,
            Codec.VersionCase<Animal, Dog>((byte)1, "Dog", DogCodec),
            Codec.VersionCase<Animal, Cat>((byte)2, "Cat", CatCodec));

        var dog = new Dog { Name = "Spot" };
        byte[] encoded = Codec.EncodeToArray(versioned, dog);
        Animal decoded = Codec.Decode(versioned, encoded);

        var result = Assert.IsType<Dog>(decoded);
        Assert.Equal("Spot", result.Name);
    }

    [Fact(DisplayName = "VersionedCodec: Decode with VarUInt32 version codec works correctly")]
    public void VarUIntVersionCodec_RoundTrip()
    {
        var versioned = Codec.Versioned<Animal, uint>(
            Codec.VarUInt32,
            Codec.VersionCase<Animal, Dog>(1u, "Dog", DogCodec),
            Codec.VersionCase<Animal, Cat>(2u, "Cat", CatCodec));

        var dog = new Dog { Name = "Lassie" };
        byte[] encoded = Codec.EncodeToArray(versioned, dog);
        Animal decoded = Codec.Decode(versioned, encoded);

        var result = Assert.IsType<Dog>(decoded);
        Assert.Equal("Lassie", result.Name);
    }
}

file sealed class UnregisteredAnimal : Animal { }
