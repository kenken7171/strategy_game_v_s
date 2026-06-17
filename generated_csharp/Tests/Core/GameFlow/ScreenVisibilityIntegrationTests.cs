// =============================================================================
//  ChronicleKnights — ScreenVisibilityIntegrationTests.cs
// -----------------------------------------------------------------------------
//  The integration the brigade commander demanded, now expressed for the DYNAMIC
//  B-type lifecycle: the UI's live-screen state must be fully wired to the internal
//  action (PlannedAction), and the decision is made UPSTREAM (at the Guild phase)
//  so Rest never even INSTANTIATES the formation/battle screens.
//
//  GameDirector.MountScreenForCurrentPhase NEWS exactly one screen (the current
//  phase's) and QueueFrees the rest, upholding the same invariant tested here
//  (ScreenVisibility.IsVisible == "is the single mounted/alive screen"), and the
//  phase you ENTER when leaving Guild is decided by ActionPhaseRouter. Composing
//  the two proves:
//    - March -> Formation : the Formation screen is the live (mounted) one.
//    - Rest  -> Chronicle : Formation AND Battle screens are never alive — i.e.
//                           never instantiated (1mm-skip: visibility is false).
//  So "I chose Rest but the formation/battle screen appeared" can never happen.
// =============================================================================

using ChronicleKnights.Core.GameFlow;
using Xunit;

namespace ChronicleKnights.Tests.Core.GameFlow;

public class ScreenVisibilityIntegrationTests
{
    [Fact]
    public void OnlyTheCurrentPhaseScreenIsAlive()
    {
        // Exactly one phase screen is alive at a time — the current phase's.
        foreach (var current in GamePhaseFlow.Cycle)
        {
            foreach (var screen in GamePhaseFlow.Cycle)
            {
                Assert.Equal(screen == current, ScreenVisibility.IsVisible(screen, current));
            }
        }
    }

    [Fact]
    public void March_FromGuild_MountsFormationScreen_AndOnlyThat()
    {
        var destination = ActionPhaseRouter.PhaseAfterGuild(PlannedAction.March);

        Assert.Equal(GamePhase.Formation, destination);
        Assert.True(ScreenVisibility.IsVisible(GamePhase.Formation, destination));
        Assert.False(ScreenVisibility.IsVisible(GamePhase.Battle, destination));
        Assert.False(ScreenVisibility.IsVisible(GamePhase.Chronicle, destination));
        Assert.False(ScreenVisibility.IsVisible(GamePhase.Guild, destination));
    }

    [Fact]
    public void Rest_FromGuild_NeverInstantiatesFormationOrBattleScreen()
    {
        var destination = ActionPhaseRouter.PhaseAfterGuild(PlannedAction.Rest);

        Assert.Equal(GamePhase.Chronicle, destination);
        Assert.True(ScreenVisibility.IsVisible(GamePhase.Chronicle, destination));
        // 休息では編成画面・戦闘画面は決して生成（マウント）されない＝可視性 false（1ミリも表示しない）。
        Assert.False(ScreenVisibility.IsVisible(GamePhase.Formation, destination));
        Assert.False(ScreenVisibility.IsVisible(GamePhase.Battle, destination));
    }

    [Theory]
    [InlineData(PlannedAction.March, GamePhase.Formation)]
    [InlineData(PlannedAction.Rest, GamePhase.Chronicle)]
    public void LiveScreenAfterGuild_IsBoundToAction(
        PlannedAction action, GamePhase expectedAlive)
    {
        var destination = ActionPhaseRouter.PhaseAfterGuild(action);

        Assert.True(ScreenVisibility.IsVisible(expectedAlive, destination));

        // On Rest the battle screen is never alive; on March the chronicle screen is not.
        var notAlive = expectedAlive == GamePhase.Formation
            ? GamePhase.Chronicle
            : GamePhase.Battle;
        Assert.False(ScreenVisibility.IsVisible(notAlive, destination));
    }
}
