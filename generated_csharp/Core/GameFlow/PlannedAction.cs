// =============================================================================
//  ChronicleKnights — PlannedAction.cs
// -----------------------------------------------------------------------------
//  The action the player commits to for the current year, chosen before leaving
//  the Formation phase:
//
//    - March : sortie. Formation -> Battle (fight this year).
//    - Rest  : stand down. Formation -> Chronicle, skipping the Battle phase
//              entirely (a safe year: aging / income / next prophecy still pass,
//              but no enemy, no battle, no spoils).
//
//  ActionPhaseRouter is the pure, Godot-independent decision used by
//  ChronicleGlobal.AdvancePhase to route out of Formation, so "the chosen action
//  contradicts the phase" can never happen. Unit-tested directly.
// =============================================================================

namespace ChronicleKnights.Core.GameFlow;

/// <summary>The committed action for the year, decided at the end of Formation.</summary>
public enum PlannedAction
{
    /// <summary>Sortie: proceed Formation -> Battle and fight this year.</summary>
    March,

    /// <summary>Stand down: skip the Battle phase and pass the year safely (rest).</summary>
    Rest,
}

/// <summary>Pure router: which phase to enter when leaving Formation, per action.</summary>
public static class ActionPhaseRouter
{
    /// <summary>
    /// The phase entered when leaving Formation. March -> Battle (fight);
    /// Rest -> Chronicle (skip the battle, close the year safely).
    /// </summary>
    public static GamePhase PhaseAfterFormation(PlannedAction action)
        => action == PlannedAction.March ? GamePhase.Battle : GamePhase.Chronicle;

    /// <summary>True when the action bypasses the Battle phase entirely (Rest).</summary>
    public static bool SkipsBattle(PlannedAction action)
        => action == PlannedAction.Rest;
}
