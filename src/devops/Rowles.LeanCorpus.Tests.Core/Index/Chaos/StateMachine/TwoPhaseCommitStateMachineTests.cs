using FsCheck;
using FsCheck.Experimental;
using FsCheck.Xunit;
using FsCheckStateMachine = FsCheck.Experimental.StateMachine;

namespace Rowles.LeanCorpus.Tests.Core.Index.Chaos.StateMachine;

[Category(TestCategory.Chaos)]
[Area(TestArea.Index)]
public sealed class TwoPhaseCommitStateMachineTests
{
    [Property(DisplayName = "Two-phase commit state machine preserves prepared and published state", MaxTest = 30, StartSize = 1, EndSize = 30, Parallelism = 1)]
    public Property Two_phase_operations_match_the_model() => FsCheckStateMachine.ToProperty(new TwoPhaseCommitMachine());
}
