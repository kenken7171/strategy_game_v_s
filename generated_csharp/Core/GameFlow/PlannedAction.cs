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

using ChronicleKnights.Core.Timeline;

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

    /// <summary>
    /// The action a freshly chosen prophecy commits the year to. The prophecy picked at the
    /// Chronicle phase decides whether this is a fighting year: a <see cref="ProphecyKind.Battle"/>
    /// prophecy means March (deploy and fight), and EVERY other kind (Rest / RewardPoints /
    /// ScoutReward / EquipmentDrop) means Rest (skip BOTH Formation and Battle — a safe year).
    /// This is what wires "choose a non-battle prophecy at Chronicle" to "no battle this year".
    /// The Guild action toggle (<see cref="PlannedAction"/>) can still override it before "next".
    /// </summary>
    public static PlannedAction ActionForProphecy(ProphecyKind kind)
        => kind == ProphecyKind.Battle ? PlannedAction.March : PlannedAction.Rest;

    /// <summary>
    /// The action for a chosen prophecy, accounting for MANDATORY epoch-boss years. On an
    /// epoch-boss year (25/50/75/100) the battle is compulsory — the chapter boss cannot be
    /// skipped by resting — so this returns <see cref="PlannedAction.March"/> regardless of the
    /// prophecy kind. On any other year it defers to <see cref="ActionForProphecy"/>
    /// (Battle -> March, every other kind -> Rest). The caller supplies whether the CURRENT
    /// calendar year is a boss year (e.g. ChronicleTimelineConfig.IsEpochBossYear), keeping this
    /// pure and free of the Chronicle config dependency.
    /// </summary>
    public static PlannedAction ActionForProphecyAtYear(ProphecyKind kind, bool isBossYear)
        => isBossYear ? PlannedAction.March : ActionForProphecy(kind);

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
