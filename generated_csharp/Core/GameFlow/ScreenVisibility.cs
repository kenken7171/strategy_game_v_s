// =============================================================================
//  ChronicleKnights — ScreenVisibility.cs
// -----------------------------------------------------------------------------
//  The single, Godot-independent definition of "which phase screen is visible".
//
//  ★ Why this exists (脳と身体の分離 / brain-body split):
//    GameDirector.RenderCurrentPhase shows exactly one of the four phase screens
//    (Chronicle / Guild / Formation / Battle) — the one whose phase equals the
//    current phase. That rule used to be an inline `phase == current` buried in a
//    Godot loop, untestable without a running engine. Lifting it here lets xUnit
//    bind the UI's visibility to the action routing WITHOUT the Godot runtime:
//    given a PlannedAction, ActionPhaseRouter says which phase you enter, and this
//    helper says which screen that makes visible. GameDirector calls IsVisible so
//    the production path and the test share one definition (no drift).
//
//  Constitution I: ASCII only for identifiers/logs.
// =============================================================================

namespace ChronicleKnights.Core.GameFlow;

/// <summary>
/// Pure rule for phase-screen visibility: a screen is visible exactly when its
/// phase is the current phase (single-screen-at-a-time, the GameDirector contract).
/// </summary>
public static class ScreenVisibility
{
    /// <summary>True when the screen for <paramref name="screenPhase"/> should be shown,
    /// i.e. it is the current phase. Every other phase screen is hidden.</summary>
    public static bool IsVisible(GamePhase screenPhase, GamePhase currentPhase)
        => screenPhase == currentPhase;
}
