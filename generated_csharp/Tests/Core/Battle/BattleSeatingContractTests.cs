// =============================================================================
//  ChronicleKnights — BattleSeatingContractTests.cs
// -----------------------------------------------------------------------------
//  Regression lock for the "blank battalion" bug: the units seated on the
//  FormationBoard must be carried into the battle as combatants. Proves that
//  BattleResolver.CreateInitial (which StartBattle calls with CurrentFormation +
//  BattalionRoster) seats exactly the board occupants, so a formed battalion is
//  never blank at battle start.
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using ChronicleKnights.Core.Battle;
using ChronicleKnights.Core.Formation;
using ChronicleKnights.Core.Job;
using ChronicleKnights.Core.Units;
using Xunit;

namespace ChronicleKnights.Tests.Core.Battle;

public class BattleSeatingContractTests
{
    private static Unit MakeUnit(Guid? id = null) => new()
    {
        Id           = id ?? Guid.NewGuid(),
        Job          = JobId.IronWallKnight,
        Age          = 30,
        MaxAge       = 60,
        FirstNameKey = "name-test",
        LastNameKey  = "name-test-family",
    };

    private static EnemyState Enemy()
        => EnemyState.CreateAtFullHealth(EnemyArchetype.TrialGuardian, 500, 40, 20);

    [Fact]
    public void SeatedUnits_AreCarriedIntoBattleAsCombatants()
    {
        var a = MakeUnit();
        var b = MakeUnit();
        var bench = MakeUnit(); // rostered but NOT placed on the board
        var roster = new List<Unit> { a, b, bench };

        var board = FormationBoard.Empty()
            .WithUnitAt(new SlotCoordinate(SquadRow.Front, 0), a.Id)
            .WithUnitAt(new SlotCoordinate(SquadRow.RearLeft, 1), b.Id);

        var snapshot = BattleResolver.CreateInitial(board, roster, Enemy(), new Random(1));

        // Exactly the two seated units are combatants; the unplaced bench unit is not.
        Assert.Equal(2, snapshot.Combatants.Count);
        Assert.True(snapshot.Combatants.ContainsKey(a.Id));
        Assert.True(snapshot.Combatants.ContainsKey(b.Id));
        Assert.False(snapshot.Combatants.ContainsKey(bench.Id));

        // Each seated combatant has a positive max hit-point entry (battle-ready).
        Assert.All(snapshot.Combatants.Keys, id => Assert.True(snapshot.UnitHitPoints[id] > 0));
    }

    [Fact]
    public void EmptyBoard_YieldsNoCombatants()
    {
        var roster = new List<Unit> { MakeUnit(), MakeUnit() };
        var snapshot = BattleResolver.CreateInitial(FormationBoard.Empty(), roster, Enemy(), new Random(1));
        Assert.Empty(snapshot.Combatants);
    }

    [Fact]
    public void StaleBoardId_NotInRoster_IsNotACombatant()
    {
        // A board occupant whose id is absent from the roster must not appear
        // (and must not crash). Guards against carrying ghost ids into battle.
        var live = MakeUnit();
        var roster = new List<Unit> { live };
        var board = FormationBoard.Empty()
            .WithUnitAt(new SlotCoordinate(SquadRow.Front, 0), live.Id)
            .WithUnitAt(new SlotCoordinate(SquadRow.Front, 1), Guid.NewGuid()); // stale id

        var snapshot = BattleResolver.CreateInitial(board, roster, Enemy(), new Random(1));

        var only = Assert.Single(snapshot.Combatants);
        Assert.Equal(live.Id, only.Key);
    }
}
