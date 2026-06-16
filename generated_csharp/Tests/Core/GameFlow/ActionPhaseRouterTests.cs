// =============================================================================
//  ChronicleKnights — ActionPhaseRouterTests.cs
// -----------------------------------------------------------------------------
//  Locks the "the chosen action must match the phase you enter" rule for leaving
//  the Formation phase:
//
//    - March (sortie) -> Battle      : fight this year.
//    - Rest  (stand down) -> Chronicle: skip the Battle phase entirely, pass the
//                                       year safely (rest), no enemy/battle/spoils.
//
//  This is the pure decision consumed by ChronicleGlobal.AdvancePhase, so the
//  absurd state "I chose Rest but got dropped into a battle" can never occur.
// =============================================================================

using ChronicleKnights.Core.GameFlow;
using Xunit;

namespace ChronicleKnights.Tests.Core.GameFlow;

public class ActionPhaseRouterTests
{
    [Fact]
    public void March_LeavesFormation_IntoBattle()
    {
        Assert.Equal(GamePhase.Battle, ActionPhaseRouter.PhaseAfterFormation(PlannedAction.March));
    }

    [Fact]
    public void Rest_LeavesFormation_IntoChronicle_SkippingBattle()
    {
        // Rest must NOT enter Battle — it goes straight to Chronicle (the year closes safely).
        var next = ActionPhaseRouter.PhaseAfterFormation(PlannedAction.Rest);
        Assert.Equal(GamePhase.Chronicle, next);
        Assert.NotEqual(GamePhase.Battle, next);
    }

    [Fact]
    public void SkipsBattle_IsTrue_OnlyForRest()
    {
        Assert.True(ActionPhaseRouter.SkipsBattle(PlannedAction.Rest));
        Assert.False(ActionPhaseRouter.SkipsBattle(PlannedAction.March));
    }

    [Theory]
    [InlineData(PlannedAction.March, GamePhase.Battle)]
    [InlineData(PlannedAction.Rest, GamePhase.Chronicle)]
    public void PhaseAfterFormation_IsTotal(PlannedAction action, GamePhase expected)
    {
        // Every action routes to exactly one destination (no undefined branch).
        Assert.Equal(expected, ActionPhaseRouter.PhaseAfterFormation(action));
    }
}
