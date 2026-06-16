// =============================================================================
//  ChronicleKnights — RestOutcome.cs
// -----------------------------------------------------------------------------
//  The pure resolution of a "Rest" year (the action chosen at Formation when the
//  player stands the battalion down instead of marching to battle).
//
//  ★ What rest yields (and what it does NOT):
//    - The roster Unit carries NO persistent HP field — survivors are always whole
//      between battles (battle damage lives only inside the per-battle snapshot).
//      So "the battalion recovered" is the narrative truth of standing down: every
//      living member is rested and battle-ready, no casualties this year.
//    - The concrete, persistent reward is POINTS (the single-currency economy).
//      Resting forgoes the kill reward of a battle but earns a modest rest bonus
//      via EarnDirect (a special-effect entry outside the SoT income formulas).
//
//  ★ Godot-independent pure function (same layering as ScoutService /
//    RosterAdminService): (roster, economy) -> (nextEconomy, outcome), no side
//    effects. ChronicleGlobal.ExecuteRest only swaps in the new economy and
//    publishes the outcome; all the arithmetic is here and is fully unit-tested.
//
//  Constitution I: ASCII only for identifiers/logs (Japanese lives in the UI).
// =============================================================================

using System;
using System.Collections.Generic;
using ChronicleKnights.Core.Managers;
using ChronicleKnights.Core.Units;

namespace ChronicleKnights.Core.GameFlow;

/// <summary>
/// Immutable summary of one Rest year, read by the rest-result UI. Holds the
/// number of living members who rested, the points reward granted, and the
/// resulting balance (so the popup can show the before/after without re-deriving).
/// </summary>
public sealed record RestOutcome
{
    /// <summary>Count of living battalion members who rested this year (not dead).</summary>
    public required int RestedUnitCount { get; init; }

    /// <summary>Points granted by standing down (the rest bonus).</summary>
    public required int PointsReward { get; init; }

    /// <summary>Points balance after the reward was applied.</summary>
    public required int BalanceAfter { get; init; }

    /// <summary>The inert "nothing rested" outcome (uninitialized / no-op default).</summary>
    public static readonly RestOutcome None = new()
    {
        RestedUnitCount = 0,
        PointsReward = 0,
        BalanceAfter = 0,
    };
}

/// <summary>The pure result of resolving a rest: the next economy plus its summary.</summary>
public readonly record struct RestResolution(PointsEconomy NextEconomy, RestOutcome Outcome);

/// <summary>
/// Godot-independent pure resolver for a Rest year. Stateless; computes the rested
/// headcount and the points reward, returning the new economy and a summary.
/// </summary>
public static class RestService
{
    /// <summary>
    /// [SoT] Points granted for standing the battalion down for a year. A modest,
    /// fixed bonus — the real cost of resting is the OPPORTUNITY cost (forgoing the
    /// battle's kill reward), so this stays small to keep the golden balance intact.
    /// </summary>
    public const int RestPointsReward = 2;

    /// <summary>
    /// Resolve one rest year purely. Counts living members (IsDead == false) as the
    /// rested headcount, grants <see cref="RestPointsReward"/> via the economy's
    /// special-effect entry, and bundles the new economy with an immutable summary.
    /// Inputs are never mutated (the roster is read-only, the economy is immutable).
    /// </summary>
    /// <param name="roster">Current battalion roster (read-only, not null).</param>
    /// <param name="economy">Current points economy (immutable, not null).</param>
    /// <returns>The next economy paired with the rest outcome summary.</returns>
    public static RestResolution Resolve(IReadOnlyList<Unit> roster, PointsEconomy economy)
    {
        ArgumentNullException.ThrowIfNull(roster);
        ArgumentNullException.ThrowIfNull(economy);

        int rested = 0;
        foreach (var unit in roster)
        {
            if (!unit.IsDead) rested++;
        }

        var nextEconomy = economy.EarnDirect(RestPointsReward);

        var outcome = new RestOutcome
        {
            RestedUnitCount = rested,
            PointsReward = RestPointsReward,
            BalanceAfter = nextEconomy.CurrentBalance,
        };

        return new RestResolution(nextEconomy, outcome);
    }
}
