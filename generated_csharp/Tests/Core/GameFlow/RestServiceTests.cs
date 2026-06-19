// =============================================================================
//  ChronicleKnights — RestServiceTests.cs
// -----------------------------------------------------------------------------
//  Locks the pure resolution of a Rest year (the logic ChronicleGlobal.ExecuteRest
//  delegates to): the rested headcount counts only living members, the points
//  reward is granted via the economy, the summary mirrors the new balance, and the
//  immutable inputs are never mutated.
// =============================================================================

using System;
using System.Collections.Immutable;
using ChronicleKnights.Core.GameFlow;
using ChronicleKnights.Core.Job;
using ChronicleKnights.Core.Managers;
using ChronicleKnights.Core.Timeline;
using ChronicleKnights.Core.Units;
using Xunit;

namespace ChronicleKnights.Tests.Core.GameFlow;

public class RestServiceTests
{
    private static Unit MakeUnit(bool isDead) => new()
    {
        Id           = Guid.NewGuid(),
        Job          = JobId.Sniper,
        Age          = 25,
        MaxAge       = 60,
        FirstNameKey = "name-test",
        LastNameKey  = "name-family-test",
        IsDead       = isDead,
    };

    private static PointsEconomy EconomyWith(int balance) => new()
    {
        CurrentBalance = balance,
        TotalEarned    = balance,
        TotalSpent     = 0,
    };

    [Fact]
    public void Resolve_GrantsTheRestReward_AndMirrorsBalance()
    {
        var roster = ImmutableList.Create(MakeUnit(isDead: false));

        var result = RestService.Resolve(roster, EconomyWith(5));

        Assert.Equal(RestService.RestPointsReward, result.Outcome.PointsReward);
        Assert.Equal(5 + RestService.RestPointsReward, result.NextEconomy.CurrentBalance);
        // The outcome summary mirrors the post-reward balance (UI reads it directly).
        Assert.Equal(result.NextEconomy.CurrentBalance, result.Outcome.BalanceAfter);
    }

    [Fact]
    public void Resolve_CountsOnlyLivingMembersAsRested()
    {
        var roster = ImmutableList.Create(
            MakeUnit(isDead: false),
            MakeUnit(isDead: true),
            MakeUnit(isDead: false));

        var result = RestService.Resolve(roster, EconomyWith(0));

        Assert.Equal(2, result.Outcome.RestedUnitCount); // the dead member is excluded
    }

    [Fact]
    public void Resolve_EmptyRoster_ZeroRested_StillRewards()
    {
        var result = RestService.Resolve(ImmutableList<Unit>.Empty, EconomyWith(0));

        Assert.Equal(0, result.Outcome.RestedUnitCount);
        Assert.Equal(RestService.RestPointsReward, result.Outcome.PointsReward);
    }

    [Fact]
    public void Resolve_DoesNotMutateInputEconomy()
    {
        var economy = EconomyWith(10);

        RestService.Resolve(ImmutableList<Unit>.Empty, economy);

        Assert.Equal(10, economy.CurrentBalance); // immutable input untouched
    }

    [Fact]
    public void Resolve_NullArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(
            () => RestService.Resolve(null!, EconomyWith(0)));
        Assert.Throws<ArgumentNullException>(
            () => RestService.Resolve(ImmutableList<Unit>.Empty, null!));
    }

    // ─── Prophecy rewards are cashed in at the peaceful-year resolution ──────────

    private static Prophecy MakeProphecy(ProphecyKind kind, int value) => new()
    {
        Id             = Guid.NewGuid(),
        Kind           = kind,
        SkipYears      = 2,
        Value          = value,
        DescriptionKey = "prophecy-test",
    };

    [Fact]
    public void Resolve_RewardPointsProphecy_GrantsCardValueAsPoints()
    {
        var roster = ImmutableList.Create(MakeUnit(isDead: false));

        var result = RestService.Resolve(
            roster, EconomyWith(5), MakeProphecy(ProphecyKind.RewardPoints, 7), new Random(1));

        Assert.Equal(7, result.Outcome.PointsReward);              // the card's points are granted
        Assert.Equal(5 + 7, result.NextEconomy.CurrentBalance);
        Assert.Equal(0, result.Outcome.RecruitedCount);
        Assert.Equal(roster.Count, result.NextRoster.Count);       // roster unchanged
    }

    [Fact]
    public void Resolve_ScoutRewardProphecy_AddsFreeRecruits_NoCost()
    {
        var roster = ImmutableList.Create(MakeUnit(isDead: false));

        var result = RestService.Resolve(
            roster, EconomyWith(5), MakeProphecy(ProphecyKind.ScoutReward, 2), new Random(1));

        Assert.Equal(2, result.Outcome.RecruitedCount);            // recruits joined
        Assert.Equal(roster.Count + 2, result.NextRoster.Count);
        Assert.Equal(0, result.Outcome.PointsReward);
        Assert.Equal(5, result.NextEconomy.CurrentBalance);        // free (economy untouched)
    }

    [Fact]
    public void Resolve_EquipmentDropProphecy_DropsEquipmentOntoLivingMember()
    {
        var roster = ImmutableList.Create(MakeUnit(isDead: false));

        var result = RestService.Resolve(
            roster, EconomyWith(0), MakeProphecy(ProphecyKind.EquipmentDrop, 3), new Random(1));

        Assert.NotNull(result.Outcome.DroppedItemId);
        Assert.Equal(3, result.Outcome.DroppedItemLevel);
        Assert.True(result.NextRoster[0].HasEquipment);            // the member now carries it
        Assert.Equal(3, result.NextRoster[0].MainEquipment!.Level);
    }

    [Fact]
    public void Resolve_EquipmentDrop_ClampsLevelToValidRange()
    {
        var roster = ImmutableList.Create(MakeUnit(isDead: false));

        var result = RestService.Resolve(
            roster, EconomyWith(0), MakeProphecy(ProphecyKind.EquipmentDrop, 99), new Random(1));

        Assert.Equal(Equipment.MaxEquipmentLevel, result.Outcome.DroppedItemLevel);
    }

    [Fact]
    public void Resolve_EquipmentDrop_NoLivingRecipient_ForgoesDrop()
    {
        var roster = ImmutableList.Create(MakeUnit(isDead: true)); // only a fallen member

        var result = RestService.Resolve(
            roster, EconomyWith(0), MakeProphecy(ProphecyKind.EquipmentDrop, 2), new Random(1));

        Assert.Null(result.Outcome.DroppedItemId);                 // nobody to receive it
    }
}
