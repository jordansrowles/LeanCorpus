using FsCheck.Xunit;
using Rowles.LeanCorpus.Codecs.DocValues;
using Rowles.LeanCorpus.Tests.Chaos.Infrastructure;

namespace Rowles.LeanCorpus.Tests.Chaos.Index;

[Trait("Category", "Chaos")]
[Trait("Category", "Index")]
public sealed class NumericDocValuesFuzzTests : IClassFixture<ChaosDirectoryFixture>
{
    private readonly ChaosDirectoryFixture _fixture;
    public NumericDocValuesFuzzTests(ChaosDirectoryFixture fixture) => _fixture = fixture;

    [Property(MaxTest = 200)]
    public void WriteRead_DoubleValues_RoundTrip(double[] values)
    {
        if (values.Length == 0 || values.Length > 1000) return;

        var fieldMap = new Dictionary<string, double[]>(StringComparer.Ordinal)
        {
            ["f"] = values
        };

        string basePath = System.IO.Path.Combine(_fixture.Path, Guid.NewGuid().ToString("N"));
        NumericDocValuesWriter.Write(basePath, fieldMap, values.Length);

        Assert.True(File.Exists(basePath), "Numeric doc values file not written");

        var (readValues, _) = NumericDocValuesReader.Read(basePath);
        Assert.True(readValues.TryGetValue("f", out var actual), "Field 'f' not found in read-back");

        Assert.Equal(values.Length, actual.Length);
        for (int i = 0; i < values.Length; i++)
            Assert.Equal(values[i], actual[i], 0.0001);
    }
}
