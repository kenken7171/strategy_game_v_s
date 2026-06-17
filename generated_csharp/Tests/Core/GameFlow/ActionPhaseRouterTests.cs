// =============================================================================
//  ChronicleKnights — ActionPhaseRouterTests.cs
// -----------------------------------------------------------------------------
//  Locks the "the chosen action must match the phase you enter" rule for leaving
//  the Guild (home-base) phase — the action is decided UPSTREAM of Formation:
//
//    - March (sortie) -> Formation   : deploy, then fight this year.
//    - Rest  (stand down) -> Chronicle: skip BOTH Formation and Battle entirely,
//                                       pass the year safely (rest), no deployment.
//
//  This is the pure decision consumed by ChronicleGlobal.AdvancePhase, so the
//  absurd state "I chose Rest but was still made to place units / dropped into a
//  battle" can never occur.
// =============================================================================

using ChronicleKnights.Core.GameFlow;
using Xunit;

namespace ChronicleKnights.Tests.Core.GameFlow;

public class ActionPhaseRouterTests
{
    [Fact]
    public void March_LeavesGuild_IntoFormation()
    {
        Assert.Equal(GamePhase.Formation, ActionPhaseRouter.PhaseAfterGuild(PlannedAction.March));
    }

    [Fact]
    public void Rest_LeavesGuild_IntoChronicle_SkippingFormationAndBattle()
    {
        // Rest must NOT enter Formation or Battle — it goes straight to Chronicle.
        var next = ActionPhaseRouter.PhaseAfterGuild(PlannedAction.Rest);
        Assert.Equal(GamePhase.Chronicle, next);
        Assert.NotEqual(GamePhase.Formation, next);
        Assert.NotEqual(GamePhase.Battle, next);
    }

    [Fact]
    public void SkipsBattle_IsTrue_OnlyForRest()
    {
        Assert.True(ActionPhaseRouter.SkipsBattle(PlannedAction.Rest));
        Assert.False(ActionPhaseRouter.SkipsBattle(PlannedAction.March));
    }

    [Theory]
    [InlineData(PlannedAction.March, GamePhase.Formation)]
    [InlineData(PlannedAction.Rest, GamePhase.Chronicle)]
    public void PhaseAfterGuild_IsTotal(PlannedAction action, GamePhase expected)
    {
        // Every action routes to exactly one destination (no undefined branch).
        Assert.Equal(expected, ActionPhaseRouter.PhaseAfterGuild(action));
    }
}
