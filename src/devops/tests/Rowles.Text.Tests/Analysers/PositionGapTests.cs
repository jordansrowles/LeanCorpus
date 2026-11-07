using FsCheck;
using FsCheck.Xunit;
using Rowles.LeanCorpus.Tests.Shared.Analysis;

namespace Rowles.Text.Tests;

[Category(TestCategory.Unit)]
[Area(TestArea.Analysers)]
[Technique(TestTechnique.PropertyBased)]
public sealed class PositionGapTests
{
    [Theory]
    [MemberData(nameof(PositionGapCases.RemovalCases), MemberType = typeof(PositionGapCases))]
    public void StandardAnalyser_RemovedStopWordsKeepTheirPosition(string input, string expected)
        => PositionGapCases.StandardAnalyser_RemovedStopWordsKeepTheirPosition(input, expected);

    [Property(MaxTest = 300, StartSize = 1, EndSize = 40)]
    public void IncrementsSumToTheOriginalPosition(NonEmptyArray<byte> input)
        => PositionGapCases.IncrementsSumToTheOriginalPosition(input);

    [Fact]
    public void AllAffectedAnalysersMatchTheSharedOutput()
        => PositionGapCases.AllAffectedAnalysersMatchTheSharedOutput();

    [Fact]
    public void StandardAnalyser_OffsetsAreUnchanged()
        => PositionGapCases.StandardAnalyser_OffsetsAreUnchanged();

    [Fact]
    public void DerivedAnalysers_KeepRemovedPositions()
        => PositionGapCases.DerivedAnalysers_KeepRemovedPositions();

    [Fact]
    public void StopWordFilter_PassesTheRemovedIncrementOn()
        => PositionGapCases.StopWordFilter_PassesTheRemovedIncrementOn();

    [Fact]
    public void StopWordFilter_FinishDiscardsTrailingRemovals()
        => PositionGapCases.StopWordFilter_FinishDiscardsTrailingRemovals();

    [Fact]
    public void StopWordFilter_CloneIsIndependent()
        => PositionGapCases.StopWordFilter_CloneIsIndependent();

    [Fact]
    public void Analyse_DoesNotLeakSkippedPositionsBetweenDocumentsOrThreads()
        => PositionGapCases.Analyse_DoesNotLeakSkippedPositionsBetweenDocumentsOrThreads();

    [Fact]
    public void Analyse_FailureLeavesNoSkippedPositionsBehind()
        => PositionGapCases.Analyse_FailureLeavesNoSkippedPositionsBehind();
}
