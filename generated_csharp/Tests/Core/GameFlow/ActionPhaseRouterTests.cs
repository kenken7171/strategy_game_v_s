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
using ChronicleKnights.Core.Timeline;
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

    [Fact]
    public void MayGenerateEnemy_IsTrue_OnlyForMarch()
    {
        // 敵・戦闘インスタンスの生成は出撃(March)のときだけ許される。
        Assert.True(ActionPhaseRouter.MayGenerateEnemy(PlannedAction.March));
        // 休息(Rest)では敵生成へ一切触れない＝完全隔離（戦闘フェーズ怪奇現象の構造的封鎖）。
        Assert.False(ActionPhaseRouter.MayGenerateEnemy(PlannedAction.Rest));
    }

    [Fact]
    public void MayGenerateEnemy_IsExactInverseOf_SkipsBattle()
    {
        foreach (var action in new[] { PlannedAction.March, PlannedAction.Rest })
        {
            Assert.Equal(
                ActionPhaseRouter.MayGenerateEnemy(action),
                !ActionPhaseRouter.SkipsBattle(action));
        }
    }

    [Theory]
    [InlineData(PlannedAction.March, GamePhase.Formation)]
    [InlineData(PlannedAction.Rest, GamePhase.Chronicle)]
    public void PhaseAfterGuild_IsTotal(PlannedAction action, GamePhase expected)
    {
        // Every action routes to exactly one destination (no undefined branch).
        Assert.Equal(expected, ActionPhaseRouter.PhaseAfterGuild(action));
    }

    // ─── Prophecy kind -> action (the Chronicle choice gates the year) ───────────
    //  Picking a prophecy at the Chronicle phase must decide whether this is a
    //  fighting year. ONLY a Battle prophecy marches; every other kind rests
    //  (skips Formation AND Battle). This is the fix for "I chose a non-battle
    //  prophecy yet was still dropped into deployment -> battle".

    [Theory]
    [InlineData(ProphecyKind.Battle, PlannedAction.March)]
    [InlineData(ProphecyKind.Rest, PlannedAction.Rest)]
    [InlineData(ProphecyKind.RewardPoints, PlannedAction.Rest)]
    [InlineData(ProphecyKind.ScoutReward, PlannedAction.Rest)]
    [InlineData(ProphecyKind.EquipmentDrop, PlannedAction.Rest)]
    public void ActionForProphecy_OnlyBattleMarches_EveryOtherKindRests(
        ProphecyKind kind, PlannedAction expected)
    {
        Assert.Equal(expected, ActionPhaseRouter.ActionForProphecy(kind));
    }

    // ─── Epoch-boss years are MANDATORY battles (cannot rest past the boss) ──────

    [Theory]
    // Boss year: ALWAYS March, regardless of prophecy kind (even a Rest prophecy).
    [InlineData(ProphecyKind.Battle, true, PlannedAction.March)]
    [InlineData(ProphecyKind.Rest, true, PlannedAction.March)]
    [InlineData(ProphecyKind.RewardPoints, true, PlannedAction.March)]
    [InlineData(ProphecyKind.ScoutReward, true, PlannedAction.March)]
    [InlineData(ProphecyKind.EquipmentDrop, true, PlannedAction.March)]
    // Non-boss year: defer to the prophecy kind (Battle marches, everything else rests).
    [InlineData(ProphecyKind.Battle, false, PlannedAction.March)]
    [InlineData(ProphecyKind.Rest, false, PlannedAction.Rest)]
    [InlineData(ProphecyKind.RewardPoints, false, PlannedAction.Rest)]
    public void ActionForProphecyAtYear_BossYearForcesMarch_ElseFollowsKind(
        ProphecyKind kind, bool isBossYear, PlannedAction expected)
    {
        Assert.Equal(expected, ActionPhaseRouter.ActionForProphecyAtYear(kind, isBossYear));
    }

    [Fact]
    public void ActionForProphecy_NonBattle_NeverEntersFormationOrBattle()
    {
        // End-to-end of the pure decision: a non-battle prophecy -> Rest -> Chronicle,
        // so neither Formation nor Battle is ever entered.
        foreach (var kind in new[]
                 {
                     ProphecyKind.Rest, ProphecyKind.RewardPoints,
                     ProphecyKind.ScoutReward, ProphecyKind.EquipmentDrop,
                 })
        {
            var action = ActionPhaseRouter.ActionForProphecy(kind);
            var next = ActionPhaseRouter.PhaseAfterGuild(action);
            Assert.Equal(GamePhase.Chronicle, next);
            Assert.NotEqual(GamePhase.Formation, next);
            Assert.NotEqual(GamePhase.Battle, next);
        }
    }
}
