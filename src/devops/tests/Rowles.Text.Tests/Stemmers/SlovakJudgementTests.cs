using System.Text;
using Rowles.LeanCorpus.Analysis;
using Rowles.LeanCorpus.Analysis.Analysers;

namespace Rowles.Text.Tests.Stemmers;

/// <summary>
/// Asserts the linguistic judgements in <c>fixtures/slovak/judgements.tsv</c> against the default
/// Slovak pipeline: every form of a <c>join</c> family analyses to one term, and no form of an
/// <c>!apart</c> family shares a term with the other family. <c>report</c> families are P1 coverage
/// that 4.0 defers; they are listed but not asserted.
/// </summary>
[Category(TestCategory.Unit)]
[Area(TestArea.Stemmers)]
public sealed class SlovakJudgementTests
{
    private static readonly Lazy<Judgements> Fixture = new(Judgements.Load);

    public static TheoryData<string> JoinFamilies()
    {
        var data = new TheoryData<string>();
        foreach (var (name, family) in Fixture.Value.Families)
        {
            if (family.Expectation == "join")
                data.Add(name);
        }
        return data;
    }

    public static TheoryData<string, string> ApartPairs()
    {
        var data = new TheoryData<string, string>();
        foreach (var (first, second) in Fixture.Value.Apart)
            data.Add(first, second);
        return data;
    }

    [Theory(DisplayName = "Slovak judgements: every form of the family shares one term")]
    [MemberData(nameof(JoinFamilies))]
    public void JoinFamily_SharesOneTerm(string family)
    {
        var terms = Fixture.Value.Families[family].Forms.GroupBy(Term).ToArray();

        Assert.True(terms.Length == 1,
            $"{family}: " + string.Join(" | ", terms.Select(static group => $"{group.Key} <- {string.Join(", ", group)}")));
    }

    [Theory(DisplayName = "Slovak judgements: negative families share no term")]
    [MemberData(nameof(ApartPairs))]
    public void ApartFamilies_ShareNoTerm(string first, string second)
    {
        var a = Fixture.Value.Families[first].Forms.Select(Term).ToHashSet(StringComparer.Ordinal);
        var b = Fixture.Value.Families[second].Forms.Select(Term).ToHashSet(StringComparer.Ordinal);

        Assert.Empty(a.Intersect(b, StringComparer.Ordinal));
    }

    [Fact(DisplayName = "Slovak judgements: the fixture is well formed")]
    public void Fixture_IsWellFormed()
    {
        var fixture = Fixture.Value;
        Assert.All(fixture.Rows, static row =>
        {
            Assert.Equal(8, row.Length);
            Assert.Contains(row[3], new[] { "P0", "P1" });
            Assert.Contains(row[4], new[] { "join", "report" });
            Assert.True(row[7] == "paradigm" || row[7].StartsWith("ud:", StringComparison.Ordinal), row[7]);
            Assert.True(row[5].IsNormalized(NormalizationForm.FormC), row[5]);
            Assert.Equal(row[5].ToLowerInvariant(), row[5]);
        });
        Assert.All(fixture.Apart, pair =>
        {
            Assert.True(fixture.Families.ContainsKey(pair.Item1), pair.Item1);
            Assert.True(fixture.Families.ContainsKey(pair.Item2), pair.Item2);
        });
        Assert.All(fixture.Families.Values.Where(static f => f.Priority == "P0"), static f => Assert.Equal("join", f.Expectation));
    }

    /// <summary>
    /// Writes the deferred P1 coverage to the test output directory, so a run records how far
    /// those families are from joining without failing on them.
    /// </summary>
    [Fact(DisplayName = "Slovak judgements: P1 coverage is reported")]
    public void ReportFamilies_AreWritten()
    {
        var report = new StringBuilder();
        foreach (var (name, family) in Fixture.Value.Families.Where(static f => f.Value.Expectation == "report"))
        {
            var terms = family.Forms.GroupBy(Term).ToArray();
            report.Append(name).Append('\t').Append(terms.Length).Append('\t')
                .AppendJoin(" | ", terms.Select(static group => $"{group.Key} <- {string.Join(", ", group)}")).Append('\n');
        }

        string path = Path.Combine(AppContext.BaseDirectory, "slovak-p1-coverage.tsv");
        File.WriteAllText(path, report.ToString(), new UTF8Encoding(false));
        Assert.True(report.Length > 0);
    }

    private static string Term(string form)
    {
        var sink = new MaterialisingTokenSink();
        AnalyserFactory.Create("sk").Analyse(form, sink);
        return Assert.Single(sink.Tokens).Text;
    }

    private sealed record Family(string Priority, string Expectation, List<string> Forms);

    private sealed record Judgements(List<string[]> Rows, Dictionary<string, Family> Families, List<(string, string)> Apart)
    {
        public static Judgements Load()
        {
            string path = Path.Combine(Rowles.LeanCorpus.Tests.Shared.Fixtures.RepositoryTemporaryStorage.RepositoryRoot,
                "src", "devops", "tests", "fixtures", "slovak", "judgements.tsv");
            var rows = new List<string[]>();
            var families = new Dictionary<string, Family>(StringComparer.Ordinal);
            var apart = new List<(string, string)>();
            foreach (var line in File.ReadAllLines(path, new UTF8Encoding(false, true)))
            {
                if (line.Length == 0 || line[0] == '#')
                    continue;
                var cells = line.Split('\t');
                if (cells[0] == "!apart")
                {
                    apart.Add((cells[1], cells[2]));
                    continue;
                }

                rows.Add(cells);
                if (!families.TryGetValue(cells[0], out var family))
                    families[cells[0]] = family = new Family(cells[3], cells[4], []);
                family.Forms.Add(cells[5]);
            }

            return new Judgements(rows, families, apart);
        }
    }
}
