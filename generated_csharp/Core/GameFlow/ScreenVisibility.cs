// =============================================================================
//  ChronicleKnights — ScreenVisibility.cs
// -----------------------------------------------------------------------------
//  The single, Godot-independent definition of "which phase screen is ALIVE".
//
//  ★ Under the dynamic B-type lifecycle (no eager BuildScreens):
//    GameDirector no longer keeps all four screens resident and toggles Visible.
//    Instead, MountScreenForCurrentPhase NEWS exactly one screen — the current
//    phase's — and FreeCurrentScreen QueueFrees the old one. So at any instant
//    there is AT MOST ONE phase screen in the tree: the current phase's.
//
//    This pure predicate models that invariant: a phase's screen "exists / is the
//    live screen" exactly when that phase is the current phase. ("Visible" in the
//    old A-type sense now means "is the single mounted/alive screen".) It lets
//    xUnit bind the UI lifecycle to the action routing WITHOUT a Godot runtime:
//    given a PlannedAction, ActionPhaseRouter says which phase you ENTER, and this
//    predicate says which screen is therefore alive (and which are never created).
//    GameDirector upholds the same invariant by construction (it only ever news the
//    current phase's screen); this is its formal, tested specification.
//
//  Constitution I: ASCII only for identifiers/logs.
// =============================================================================

namespace ChronicleKnights.Core.GameFlow;

/// <summary>
/// Pure rule for the live phase-screen: under the on-demand B-type lifecycle, the
/// screen for a phase is the single mounted/alive one exactly when that phase is the
/// current phase. Every other phase screen does not exist (never instantiated).
/// </summary>
public static class ScreenVisibility
{
    /// <summary>True when the screen for <paramref name="screenPhase"/> is the live
    /// (mounted) screen — i.e. it is the current phase. Every other phase screen is
    /// not instantiated at all (the dynamic B-type guarantee).</summary>
    public static bool IsVisible(GamePhase screenPhase, GamePhase currentPhase)
        => screenPhase == currentPhase;
}
