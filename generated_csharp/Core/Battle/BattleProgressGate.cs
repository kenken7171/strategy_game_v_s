// =============================================================================
//  ChronicleKnights — BattleProgressGate.cs
// -----------------------------------------------------------------------------
//  Pure guard for whether the game may LEAVE the Battle phase (Battle -> Chronicle
//  year-send). Leaving is allowed only when there is no active battle, or the
//  active battle has CONCLUDED (BattalionVictory / BattalionDefeat). While a
//  battle is still Ongoing, leaving is refused -- closing the "press Next to skip
//  the whole battle" bug.
//
//  Godot-independent and side-effect free, so the rule is unit-tested directly and
//  reused by ChronicleGlobal.AdvancePhase (the SoT phase-advance guard).
// =============================================================================

namespace ChronicleKnights.Core.Battle;

/// <summary>Pure guard: the Battle phase may be left only once its battle concludes.</summary>
public static class BattleProgressGate
{
    /// <summary>
    /// True when the Battle phase may advance to the next phase: either no battle is
    /// active (<paramref name="battle"/> is null) or the battle has concluded
    /// (Outcome != <see cref="BattleOutcome.Ongoing"/>). An ongoing battle returns
    /// false, so the phase cannot be skipped mid-fight.
    /// </summary>
    public static bool CanLeaveBattlePhase(BattleSnapshot? battle)
        => battle is null || battle.Outcome != BattleOutcome.Ongoing;
}
