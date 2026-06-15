// =============================================================================
//  ChronicleKnights — DeploymentGate.cs
// -----------------------------------------------------------------------------
//  Pure guard for whether the battalion may march to battle.
//
//  Rule: the FormationBoard must hold at least MinimumDeployedToMarch units.
//  An empty board is refused outright -- no "ghost march" into battle with zero
//  combatants (the bug this gate closes).
//
//  Godot-independent and side-effect free, so the rule is unit-tested directly
//  and reused by both the SoT phase-advance guard (ChronicleGlobal.AdvancePhase)
//  and the UI presentation guard (the advance button / battle-start button).
// =============================================================================

namespace ChronicleKnights.Core.Formation;

/// <summary>Pure deploy-to-march guard: refuse marching when no unit is deployed.</summary>
public static class DeploymentGate
{
    /// <summary>Minimum number of deployed units required to march to battle.</summary>
    public const int MinimumDeployedToMarch = 1;

    /// <summary>
    /// True when the board has at least <see cref="MinimumDeployedToMarch"/> units
    /// deployed. A null board (defensive) or an empty board returns false.
    /// </summary>
    public static bool CanMarch(FormationBoard board)
        => board is not null && board.OccupiedCount >= MinimumDeployedToMarch;
}
