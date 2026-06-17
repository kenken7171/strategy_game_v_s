// =============================================================================
//  ChronicleKnights — PlannedAction.cs
// -----------------------------------------------------------------------------
//  The action the player commits to for the current year, chosen UPSTREAM of the
//  Formation phase (at the Guild / home-base phase), so the decision gates whether
//  the formation screen is ever entered at all:
//
//    - March : sortie. Guild -> Formation -> Battle (fight this year).
//    - Rest  : stand down. Guild -> Chronicle, skipping BOTH the Formation screen
//              and the Battle screen entirely (a safe year: aging / income / next
//              prophecy still pass, but no deployment, no enemy, no battle).
//
//  ★ Why the decision lives at Guild, not Formation:
//    Choosing Rest from inside the Formation screen was the absurdity the brigade
//    commander rooted out — "you picked rest yet I still made you place units".
//    Deciding before Formation means Rest never renders the formation/battle UI
//    (their Visible stays false). Only March opens the Formation screen.
//
//  ActionPhaseRouter is the pure, Godot-independent decision used by
//  ChronicleGlobal.AdvancePhase to route out of the Guild phase, so "the chosen
//  action contradicts the phase" can never happen. Unit-tested directly.
// =============================================================================

namespace ChronicleKnights.Core.GameFlow;

/// <summary>The committed action for the year, decided at the Guild (home-base) phase.</summary>
public enum PlannedAction
{
    /// <summary>Sortie: proceed Guild -> Formation -> Battle and fight this year.</summary>
    March,

    /// <summary>Stand down: skip BOTH Formation and Battle and pass the year safely (rest).</summary>
    Rest,
}

/// <summary>Pure router: which phase to enter when leaving the Guild phase, per action.</summary>
public static class ActionPhaseRouter
{
    /// <summary>
    /// The phase entered when leaving the Guild phase. March -> Formation (deploy,
    /// then fight); Rest -> Chronicle (skip Formation AND Battle, close the year safely).
    /// </summary>
    public static GamePhase PhaseAfterGuild(PlannedAction action)
        => action == PlannedAction.March ? GamePhase.Formation : GamePhase.Chronicle;

    /// <summary>True when the action bypasses the Formation and Battle phases entirely (Rest).</summary>
    public static bool SkipsBattle(PlannedAction action)
        => action == PlannedAction.Rest;

    /// <summary>
    /// The enemy-generation結界: enemy / battle instances may be spawned ONLY when the
    /// player committed to March (sortie). For Rest (or any non-March action) this is
    /// false, so the enemy-generation path is touched 0 times — no enemy data is ever
    /// created behind the scenes, permanently sealing the "battle phase poltergeist".
    /// (Logical inverse of <see cref="SkipsBattle"/>, named for the spawn-site guard.)
    /// </summary>
    public static bool MayGenerateEnemy(PlannedAction action)
        => action == PlannedAction.March;
}
