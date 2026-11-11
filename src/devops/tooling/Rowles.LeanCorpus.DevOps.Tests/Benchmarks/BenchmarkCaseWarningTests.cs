using Rowles.LeanCorpus.DevOps.Benchmarks;

namespace Rowles.LeanCorpus.DevOps.Tests.Benchmarks;

public sealed class BenchmarkCaseWarningTests
{
    [Fact]
    public void CoverageReadsPerCaseWarningCodesFromTheCanonicalReport()
    {
        string path = Path.Combine(RepositoryTemporaryStorage.GetAreaRoot("devops-tests"), "benchmark-warnings-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllText(path, """
                {
                  "schemaVersion": 6,
                  "suites": [ { "benchmarks": [
                    { "displayInfo": "A.'one': DefaultJob", "warnings": [ { "code": "MinIterationTime" }, { "code": "BimodalDistribution" } ] },
                    { "displayInfo": "A.'two': DefaultJob", "warnings": [] },
                    { "displayInfo": "A.'three': DefaultJob", "warnings": [ { "code": "NoCanonicalStatistics" } ] }
                  ] } ]
                }
                """);

            var warnings = BenchmarkCaseCoverage.ReadCaseWarnings(path);

            Assert.Equal(["BimodalDistribution", "MinIterationTime"], warnings["A.'one': DefaultJob"]);
            Assert.False(warnings.ContainsKey("A.'two': DefaultJob"));
            Assert.Equal(["NoCanonicalStatistics"], warnings["A.'three': DefaultJob"]);
            Assert.Empty(BenchmarkCaseCoverage.ReadCaseWarnings(path + ".missing"));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
