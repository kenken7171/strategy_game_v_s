// =============================================================================
//  ChronicleKnights — DeploymentGateTests.cs
// -----------------------------------------------------------------------------
//  Locks the "no ghost march" rule: marching to battle is refused when zero
//  units are deployed, and allowed once at least one is on the board.
// =============================================================================

using System;
using ChronicleKnights.Core.Formation;
using ChronicleKnights.Core.Job;
using Xunit;

namespace ChronicleKnights.Tests.Core.Formation;

public class DeploymentGateTests
{
    [Fact]
    public void EmptyBoard_CannotMarch()
    {
        Assert.False(DeploymentGate.CanMarch(FormationBoard.Empty()));
    }

    [Fact]
    public void BoardWithOneDeployedUnit_CanMarch()
    {
        var board = FormationBoard.Empty()
            .WithUnitAt(new SlotCoordinate(SquadRow.Front, 0), Guid.NewGuid());

        Assert.True(DeploymentGate.CanMarch(board));
    }

    [Fact]
    public void BoardWithMultipleDeployedUnits_CanMarch()
    {
        var board = FormationBoard.Empty()
            .WithUnitAt(new SlotCoordinate(SquadRow.Front, 0), Guid.NewGuid())
            .WithUnitAt(new SlotCoordinate(SquadRow.RearLeft, 2), Guid.NewGuid());

        Assert.True(DeploymentGate.CanMarch(board));
    }

    [Fact]
    public void ClearingTheLastDeployedUnit_BlocksMarchAgain()
    {
        var coordinate = new SlotCoordinate(SquadRow.Front, 1);
        var seeded = FormationBoard.Empty().WithUnitAt(coordinate, Guid.NewGuid());
        Assert.True(DeploymentGate.CanMarch(seeded));

        var cleared = seeded.ClearedAt(coordinate);
        Assert.False(DeploymentGate.CanMarch(cleared));
    }
}
