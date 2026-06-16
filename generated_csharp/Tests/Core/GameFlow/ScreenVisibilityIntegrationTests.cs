// =============================================================================
//  ChronicleKnights — ScreenVisibilityIntegrationTests.cs
// -----------------------------------------------------------------------------
//  The integration the brigade commander demanded: the UI's screen-visibility
//  state must be fully wired to the internal action (PlannedAction).
//
//  GameDirector.RenderCurrentPhase sets each phase screen's Visible via the SAME
//  pure rule tested here (ScreenVisibility.IsVisible), and the phase you ENTER when
//  leaving Formation is decided by ActionPhaseRouter. Composing the two proves:
//    - March -> Battle  : the Battle screen becomes visible (and only it).
//    - Rest  -> Chronicle: the Battle screen is NEVER visible (battle bypassed).
//  So "I chose Rest but the battle screen showed" can never happen.
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
    public void March_FromFormation_ShowsBattleScreen_AndOnlyThat()
    {
        var destination = ActionPhaseRouter.PhaseAfterFormation(PlannedAction.March);

        Assert.Equal(GamePhase.Battle, destination);
        Assert.True(ScreenVisibility.IsVisible(GamePhase.Battle, destination));
        Assert.False(ScreenVisibility.IsVisible(GamePhase.Formation, destination));
        Assert.False(ScreenVisibility.IsVisible(GamePhase.Chronicle, destination));
        Assert.False(ScreenVisibility.IsVisible(GamePhase.Guild, destination));
    }

    [Fact]
    public void Rest_FromFormation_NeverShowsBattleScreen()
    {
        var destination = ActionPhaseRouter.PhaseAfterFormation(PlannedAction.Rest);

        Assert.Equal(GamePhase.Chronicle, destination);
        Assert.True(ScreenVisibility.IsVisible(GamePhase.Chronicle, destination));
        // 戦闘画面は休息では決して可視にならない（戦闘フェーズの完全バイパス）。
        Assert.False(ScreenVisibility.IsVisible(GamePhase.Battle, destination));
        Assert.False(ScreenVisibility.IsVisible(GamePhase.Formation, destination));
    }

    [Theory]
    [InlineData(PlannedAction.March, GamePhase.Battle)]
    [InlineData(PlannedAction.Rest, GamePhase.Chronicle)]
    public void VisibleScreenAfterFormation_IsBoundToAction(
        PlannedAction action, GamePhase expectedVisible)
    {
        var destination = ActionPhaseRouter.PhaseAfterFormation(action);

        Assert.True(ScreenVisibility.IsVisible(expectedVisible, destination));

        // The OTHER terminal screen must be hidden (the binding is exclusive).
        var other = expectedVisible == GamePhase.Battle
            ? GamePhase.Chronicle
            : GamePhase.Battle;
        Assert.False(ScreenVisibility.IsVisible(other, destination));
    }
}
