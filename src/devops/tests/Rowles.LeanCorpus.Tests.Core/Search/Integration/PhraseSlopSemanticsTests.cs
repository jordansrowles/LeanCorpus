using FsCheck.Xunit;
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Search.Queries;
using Rowles.LeanCorpus.Search.Searcher;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Tests.Shared.Fixtures;

namespace Rowles.LeanCorpus.Tests.Core.Search;

[Category(TestCategory.Integration)]
[Area(TestArea.Search)]
public sealed class PhraseSlopSemanticsTests : IClassFixture<TestDirectoryFixture>
{
    private static readonly string[] Vocabulary =
    [
        "alpha", "bravo", "charlie", "delta", "echo", "foxtrot", "golf", "hotel"
    ];

    private static readonly PhraseProbeCase[] ProbeCases =
    [
        new("stop-gap-3 slop2", "said in the of government", ["said", "government"], 2, false),
        new("stop-gap-2 slop2", "said in the government", ["said", "government"], 2, true),
        new("stop-gap-3 slop3", "said in the of government", ["said", "government"], 3, true),
        new("stop-gap-3 exact", "said in the of government", ["said", "government"], 0, false),
        new("stop-gap-1 exact", "said the government", ["said", "government"], 0, false),
        new("no-stop slop2", "said alpha beta government", ["said", "government"], 2, true),
        new("no-stop gap3 slop2", "said alpha beta gamma government", ["said", "government"], 2, false),
        new("adjacent", "said government", ["said", "government"], 0, true),
        new("transposed slop1", "government said", ["said", "government"], 1, false),
        new("transposed slop2", "government said", ["said", "government"], 2, true),
        new("cumulative 3-term slop1", "aa xx bb yy cc", ["aa", "bb", "cc"], 1, false),
        new("cumulative 3-term slop2", "aa xx bb yy cc", ["aa", "bb", "cc"], 2, true),
        new("cumulative 4-term slop2", "aa xx bb yy cc zz dd", ["aa", "bb", "cc", "dd"], 2, false),
        new("cumulative 4-term slop3", "aa xx bb yy cc zz dd", ["aa", "bb", "cc", "dd"], 3, true),
        new("repeated term single occurrence", "aa", ["aa", "aa"], 1, false),
        new("repeated term two occurrences", "aa aa", ["aa", "aa"], 0, true),
        new("repeated term gapped slop0", "aa xx aa", ["aa", "aa"], 0, false),
        new("repeated term gapped slop1", "aa xx aa", ["aa", "aa"], 1, true),
    ];

    private readonly TestDirectoryFixture _fixture;

    public PhraseSlopSemanticsTests(TestDirectoryFixture fixture) => _fixture = fixture;

    public static IEnumerable<object[]> ProbeCaseData => ProbeCases.Select(static testCase =>
        new object[] { testCase.Name, testCase.Document, testCase.Terms, testCase.Slop, testCase.ExpectedMatch });

    [Theory(DisplayName = "PhraseQuery: matches the frozen phrase slop probes")]
    [MemberData(nameof(ProbeCaseData))]
    public void PhraseQuery_MatchesFrozenProbeCases(
        string name,
        string body,
        string[] terms,
        int slop,
        bool expectedMatch)
    {
        using var directory = CreateIndex($"phrase_probe_{name}", [body]);
        using var searcher = new IndexSearcher(directory);

        var results = searcher.Search(new PhraseQuery("body", slop, terms), 10, TestContext.Current.CancellationToken);

        Assert.Equal(expectedMatch, results.TotalHits > 0);
    }

    [Property(
        DisplayName = "PhraseQuery: generated distinct-term searches agree with the positional reference",
        MaxTest = 30,
        StartSize = 1,
        EndSize = 16)]
    public void PhraseQuery_GeneratedDistinctTermSearchesAgreeWithReference(int seed)
    {
        var random = new Random(seed);
        int documentLength = random.Next(2, 13);
        string[] documentTokens = new string[documentLength];
        for (int i = 0; i < documentTokens.Length; i++)
            documentTokens[i] = Vocabulary[random.Next(Vocabulary.Length)];

        string firstTerm = Vocabulary[random.Next(Vocabulary.Length)];
        string secondTerm;
        do
        {
            secondTerm = Vocabulary[random.Next(Vocabulary.Length)];
        }
        while (string.Equals(firstTerm, secondTerm, StringComparison.Ordinal));

        string[] terms = [firstTerm, secondTerm];
        int slop = random.Next(4);
        int[][] positions = terms.Select(term => Enumerable.Range(0, documentTokens.Length)
            .Where(index => string.Equals(documentTokens[index], term, StringComparison.Ordinal))
            .ToArray()).ToArray();
        bool expectedMatch = ReferenceMatches(positions, [0, 1], slop);

        using var directory = CreateIndex($"phrase_property_{Guid.NewGuid():N}", [string.Join(' ', documentTokens)]);
        using var searcher = new IndexSearcher(directory);
        var results = searcher.Search(new PhraseQuery("body", slop, terms), 10, TestContext.Current.CancellationToken);

        Assert.Equal(expectedMatch, results.TotalHits > 0);
    }

    [Fact(DisplayName = "PhraseQuery: phrase frequency ranks the tighter sloppy match first")]
    public void PhraseQuery_SloppyPhraseFrequency_RanksTighterMatchFirst()
    {
        using var directory = CreateIndex("phrase_frequency_ranking", ["aa bb cc", "aa cc bb"], ["tight", "loose"]);
        using var searcher = new IndexSearcher(directory);

        var results = searcher.Search(new PhraseQuery("body", 2, "aa", "bb"), 10, TestContext.Current.CancellationToken);

        Assert.Equal(2, results.TotalHits);
        Assert.True(results.ScoreDocs[0].Score > results.ScoreDocs[1].Score);
        Assert.Equal("tight", searcher.GetStoredFields(results.ScoreDocs[0].DocId)["id"][0]);
        Assert.Equal("loose", searcher.GetStoredFields(results.ScoreDocs[1].DocId)["id"][0]);
    }

    private MMapDirectory CreateIndex(string name, string[] bodies, string[]? ids = null)
    {
        string path = Path.Combine(_fixture.Path, $"{name}_{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        var directory = new MMapDirectory(path);

        using (var writer = new IndexWriter(directory, new IndexWriterConfig()))
        {
            for (int i = 0; i < bodies.Length; i++)
            {
                var document = new LeanDocument();
                if (ids is not null)
                    document.Add(new StringField("id", ids[i]));
                document.Add(new TextField("body", bodies[i]));
                writer.AddDocument(document);
            }

            writer.Commit();
        }

        return directory;
    }

    private static bool ReferenceMatches(IReadOnlyList<int[]> positions, IReadOnlyList<int> queryPositions, int slop)
    {
        if (positions.Count == 0 || positions.Count != queryPositions.Count || slop < 0)
            return false;

        var selectedPositions = new int[positions.Count];
        return SelectPosition(0);

        bool SelectPosition(int slot)
        {
            if (slot == positions.Count)
            {
                int minimumOffset = int.MaxValue;
                int maximumOffset = int.MinValue;
                for (int i = 0; i < selectedPositions.Length; i++)
                {
                    for (int j = 0; j < i; j++)
                    {
                        if (selectedPositions[i] == selectedPositions[j])
                            return false;
                    }

                    int offset = selectedPositions[i] - queryPositions[i];
                    minimumOffset = Math.Min(minimumOffset, offset);
                    maximumOffset = Math.Max(maximumOffset, offset);
                }

                return maximumOffset - minimumOffset <= slop;
            }

            foreach (int position in positions[slot])
            {
                selectedPositions[slot] = position;
                if (SelectPosition(slot + 1))
                    return true;
            }

            return false;
        }
    }

    private sealed record PhraseProbeCase(string Name, string Document, string[] Terms, int Slop, bool ExpectedMatch);
}
