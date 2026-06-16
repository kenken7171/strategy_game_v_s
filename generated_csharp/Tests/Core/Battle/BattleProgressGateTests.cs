// =============================================================================
//  ChronicleKnights — BattleProgressGateTests.cs
// -----------------------------------------------------------------------------
//  Locks the "Next must not skip the battle" rule: the Battle phase may be left
//  only when there is no battle or the battle has concluded. An ongoing battle
//  must NOT be skippable into the next phase.
//
//  This guards the GameDirector rewire (Battle "Next" advances a turn) + the
//  ChronicleGlobal.AdvancePhase structural block that both consult this gate.
// =============================================================================

using System;
using System.Collections.Generic;
using ChronicleKnights.Core.Battle;
using ChronicleKnights.Core.Formation;
using ChronicleKnights.Core.Units;
using Xunit;

namespace ChronicleKnights.Tests.Core.Battle;

public class BattleProgressGateTests
{
    private static BattleSnapshot OngoingSnapshot()
    {
        var enemy = EnemyState.CreateAtFullHealth(EnemyArchetype.TrialGuardian, 100, 10, 10);
        var snapshot = BattleResolver.CreateInitial(
            FormationBoard.Empty(), new List<Unit>(), enemy, new Random(1));
        Assert.Equal(BattleOutcome.Ongoing, snapshot.Outcome); // sanity: CreateInitial is Ongoing
        return snapshot;
    }

    [Fact]
    public void NoActiveBattle_CanLeave()
    {
        Assert.True(BattleProgressGate.CanLeaveBattlePhase(null));
    }

    [Fact]
    public void OngoingBattle_CannotLeave()
    {
        Assert.False(BattleProgressGate.CanLeaveBattlePhase(OngoingSnapshot()));
    }

    [Fact]
    public void ConcludedBattle_CanLeave()
    {
        var ongoing = OngoingSnapshot();
        Assert.True(BattleProgressGate.CanLeaveBattlePhase(
            ongoing with { Outcome = BattleOutcome.BattalionVictory }));
        Assert.True(BattleProgressGate.CanLeaveBattlePhase(
            ongoing with { Outcome = BattleOutcome.BattalionDefeat }));
    }
}
