using Rowles.LeanCorpus.Codecs.Fst;

namespace Rowles.LeanCorpus.Tests.Core.Codecs;

/// <summary>Checks the accepted byte language and allocation bound of Levenshtein construction.</summary>
[Category(TestCategory.Unit)]
[Area(TestArea.CodecKit)]
public sealed class LevenshteinConstructionTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [Technique(TestTechnique.PropertyBased)]
    public void MatchesReferenceByteDistance(int maxEdits)
    {
        var random = new Random(90481);
        foreach (int length in new[] { 0, 3, 20, 21, 31, 40, 63, 64, 65 })
        {
            var term = new byte[length];
            random.NextBytes(term);
            var automaton = new LevenshteinAutomaton(term, maxEdits);
            for (int sample = 0; sample < 80; sample++)
            {
                var candidate = term.ToList();
                int mutations = sample % 5;
                for (int edit = 0; edit < mutations; edit++)
                {
                    int position = random.Next(candidate.Count + 1);
                    switch (random.Next(3))
                    {
                        case 0: candidate.Insert(position, (byte)random.Next(256)); break;
                        case 1 when position < candidate.Count: candidate.RemoveAt(position); break;
                        case 2 when position < candidate.Count: candidate[position] = (byte)random.Next(256); break;
                    }
                }
                byte[] bytes = candidate.ToArray();
                int expectedDistance = Distance(term, bytes);
                int state = automaton.Start;
                foreach (byte value in bytes)
                    state = automaton.Step(state, value);
                Assert.Equal(expectedDistance <= maxEdits, automaton.IsAccept(state));
                if (automaton.IsAccept(state))
                {
                    Assert.Equal(expectedDistance, automaton.MinDistance(state));
                    Assert.True(automaton.CanMatch(state));
                }
            }
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [Technique(TestTechnique.PropertyBased)]
    public void EveryInputBytePreservesDistance(int maxEdits)
    {
        byte[] term = System.Text.Encoding.UTF8.GetBytes("zzzzfuzzycache-nohit-00000000");
        var automaton = new LevenshteinAutomaton(term, maxEdits);
        foreach (int position in new[] { 0, 10, term.Length - 1 })
        {
            for (int value = 0; value < 256; value++)
            {
                byte[] candidate = term.ToArray();
                candidate[position] = (byte)value;
                int expected = Distance(term, candidate);
                int state = automaton.Start;
                foreach (byte input in candidate) state = automaton.Step(state, input);
                Assert.Equal(expected <= maxEdits, automaton.IsAccept(state));
                if (automaton.IsAccept(state)) Assert.Equal(expected, automaton.MinDistance(state));
            }
        }
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(2, true)]
    public void TranspositionCostsTwoEdits(int maxEdits, bool expected)
    {
        // Keep the swapped bytes away from neighbours and exercise the wide compiler at edit distance two.
        byte[] term = System.Text.Encoding.UTF8.GetBytes("abcdefghijklmnopqrstuvwx");
        byte[] candidate = term.ToArray();
        (candidate[10], candidate[11]) = (candidate[11], candidate[10]);
        var automaton = new LevenshteinAutomaton(term, maxEdits);
        int state = automaton.Start;
        foreach (byte value in candidate)
            state = automaton.Step(state, value);
        Assert.Equal(expected, automaton.IsAccept(state));
    }

    [Fact]
    public void LongTermConstructionStaysWithinAllocationBudget()
    {
        const string term = "zzzzfuzzycache-nohit-00000000";
        _ = new LevenshteinAutomaton(term, 2);
        long before = GC.GetAllocatedBytesForCurrentThread();
        var automaton = new LevenshteinAutomaton(term, 2);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        // Includes the retained dense DFA; excludes the old per-transition sets and string keys.
        Assert.True(allocated < 2_000_000, $"Construction allocated {allocated:N0} bytes; budget is 2,000,000.");
        GC.KeepAlive(automaton);
    }

    private static int Distance(byte[] left, byte[] right)
    {
        var matrix = new int[left.Length + 1, right.Length + 1];
        for (int i = 0; i <= left.Length; i++) matrix[i, 0] = i;
        for (int j = 0; j <= right.Length; j++) matrix[0, j] = j;
        for (int i = 1; i <= left.Length; i++)
            for (int j = 1; j <= right.Length; j++)
                matrix[i, j] = Math.Min(Math.Min(matrix[i - 1, j] + 1, matrix[i, j - 1] + 1),
                    matrix[i - 1, j - 1] + (left[i - 1] == right[j - 1] ? 0 : 1));
        return matrix[left.Length, right.Length];
    }
}
