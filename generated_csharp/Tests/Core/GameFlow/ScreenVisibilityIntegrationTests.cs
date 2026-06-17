// =============================================================================
//  ChronicleKnights — ScreenVisibilityIntegrationTests.cs
// -----------------------------------------------------------------------------
//  The integration the brigade commander demanded: the UI's screen-visibility
//  state must be fully wired to the internal action (PlannedAction), and the
//  decision is made UPSTREAM (at the Guild phase) so Rest never even renders the
//  formation/battle screens.
//
//  GameDirector.RenderCurrentPhase sets each phase screen's Visible via the SAME
//  pure rule tested here (ScreenVisibility.IsVisible), and the phase you ENTER when
//  leaving Guild is decided by ActionPhaseRouter. Composing the two proves:
//    - March -> Formation : the Formation screen becomes visible (Battle later).
//    - Rest  -> Chronicle : BOTH Formation AND Battle stay hidden (1mm-skip).
//  So "I chose Rest but the formation/battle screen showed" can never happen.
// =============================================================================

using ChronicleKnights.Core.GameFlow;
using Xunit;

namespace ChronicleKnights.Tests.Core.GameFlow;

public class ScreenVisibilityIntegrationTests
{
    [Fact]
    public void OnlyTheCurrentPhaseScreenIsVisible()
    {
        foreach (var current in GamePhaseFlow.Cycle)
        {
            foreach (var screen in GamePhaseFlow.Cycle)
            {
                Assert.Equal(screen == current, ScreenVisibility.IsVisible(screen, current));
            }
        }
    }

    [Fact]
    public void March_FromGuild_ShowsFormationScreen_AndOnlyThat()
    {
        var destination = ActionPhaseRouter.PhaseAfterGuild(PlannedAction.March);

        Assert.Equal(GamePhase.Formation, destination);
        Assert.True(ScreenVisibility.IsVisible(GamePhase.Formation, destination));
        Assert.False(ScreenVisibility.IsVisible(GamePhase.Battle, destination));
        Assert.False(ScreenVisibility.IsVisible(GamePhase.Chronicle, destination));
        Assert.False(ScreenVisibility.IsVisible(GamePhase.Guild, destination));
    }

    [Fact]
    public void Rest_FromGuild_NeverShowsFormationOrBattleScreen()
    {
        var destination = ActionPhaseRouter.PhaseAfterGuild(PlannedAction.Rest);

        Assert.Equal(GamePhase.Chronicle, destination);
        Assert.True(ScreenVisibility.IsVisible(GamePhase.Chronicle, destination));
        // 休息では編成画面・戦闘画面は決して可視にならない（物理的に1ミリも表示しない）。
        Assert.False(ScreenVisibility.IsVisible(GamePhase.Formation, destination));
        Assert.False(ScreenVisibility.IsVisible(GamePhase.Battle, destination));
    }

    [Theory]
    [InlineData(PlannedAction.March, GamePhase.Formation)]
    [InlineData(PlannedAction.Rest, GamePhase.Chronicle)]
    public void VisibleScreenAfterGuild_IsBoundToAction(
        PlannedAction action, GamePhase expectedVisible)
    {
        var destination = ActionPhaseRouter.PhaseAfterGuild(action);

        Assert.True(ScreenVisibility.IsVisible(expectedVisible, destination));

        // On Rest the battle screen is hidden; on March the chronicle screen is hidden.
        var hiddenTerminal = expectedVisible == GamePhase.Formation
            ? GamePhase.Chronicle
            : GamePhase.Battle;
        Assert.False(ScreenVisibility.IsVisible(hiddenTerminal, destination));
    }
}
