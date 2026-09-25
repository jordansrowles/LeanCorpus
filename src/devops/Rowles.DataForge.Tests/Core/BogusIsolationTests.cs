using Rowles.DataForge;

namespace Rowles.DataForge.Tests.Core;

public sealed class BogusIsolationTests
{
    [Fact]
    public void Faker_values_repeat_from_local_seed_and_fixed_time_reference()
    {
        var first = DataForgeFakerFactory.Create("en_GB", 987654321);
        var firstValues = (first.FirstName(), first.LastName(), first.CompanyName(), first.LoremWords(8));
        var second = DataForgeFakerFactory.Create("en_GB", 987654321);
        var secondValues = (second.FirstName(), second.LastName(), second.CompanyName(), second.LoremWords(8));

        Assert.Equal(firstValues, secondValues);
        Assert.Equal(new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc), first.DateTimeReference);
    }

    [Fact]
    public void System_random_adapter_overrides_all_used_streams()
    {
        var first = new DataForgeSystemRandom(42);
        var second = new DataForgeSystemRandom(42);

        Assert.Equal(first.Next(), second.Next());
        Assert.Equal(first.Next(10_000), second.Next(10_000));
        Assert.Equal(first.Next(-50, 75), second.Next(-50, 75));
        Assert.Equal(first.NextInt64(long.MaxValue), second.NextInt64(long.MaxValue));
        Assert.Equal(first.NextDouble(), second.NextDouble());
        Assert.Equal(first.NextSingle(), second.NextSingle());

        var firstBytes = new byte[19];
        var secondBytes = new byte[19];
        first.NextBytes(firstBytes);
        second.NextBytes(secondBytes);
        Assert.Equal(firstBytes, secondBytes);
    }
}
