using Bogus;

namespace Rowles.DataForge;

public sealed class DataForgeBogusRandomizer : Randomizer
{
    public DataForgeBogusRandomizer(ulong seed)
    {
        localSeed = new DataForgeSystemRandom(seed);
    }
}

public sealed class DataForgeFaker
{
    private readonly Faker faker;

    internal DataForgeFaker(Faker faker) => this.faker = faker;

    public string FirstName() => faker.Name.FirstName();

    public string LastName() => faker.Name.LastName();

    public string UserName() => faker.Internet.UserName();

    public string CompanyName() => faker.Company.CompanyName();

    public string JobTitle() => faker.Name.JobTitle();

    public string StreetAddress() => faker.Address.StreetAddress();

    public string City() => faker.Address.City();

    public string PostalCode() => faker.Address.ZipCode();

    public string Phone() => faker.Phone.PhoneNumber();

    public string Email() => faker.Internet.Email();

    public string DomainName() => faker.Internet.DomainName();

    public string Url() => faker.Internet.Url();

    public void ResetRandomiser(ulong seed) => faker.Random = new DataForgeBogusRandomizer(seed);

    public string LoremWords(int count)
    {
        if (count < 1)
            throw new ArgumentOutOfRangeException(nameof(count));
        return string.Join(' ', faker.Lorem.Words(count));
    }

    public string ProductName() => faker.Commerce.ProductName();

    public DateTime DateTimeReference => faker.DateTimeReference ?? throw new InvalidOperationException("DataForge Faker must have a fixed date-time reference.");
}

public static class DataForgeFakerFactory
{
    private static readonly DateTime FixedDateTimeReference = new(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public static DataForgeFaker Create(string locale, ulong seed)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(locale);
        var faker = new Faker(locale)
        {
            Random = new DataForgeBogusRandomizer(seed),
            DateTimeReference = FixedDateTimeReference
        };
        return new DataForgeFaker(faker);
    }
}
