// =============================================================================
//  ChronicleKnights — RestOutcome.cs
// -----------------------------------------------------------------------------
//  The pure resolution of a "peaceful year" — any year the player does NOT fight
//  (every non-Battle prophecy routes to Rest). It applies the chosen prophecy's
//  reward and reports an immutable summary for the rest-result UI.
//
//  ★ Why here (the wiring the brigade commander demanded):
//    The prophecy picked at the Chronicle phase carries a Kind + Value, but until
//    now ONLY its SkipYears (time-skip) was consumed — the reward (points / a new
//    recruit / an equipment drop) was never applied. This resolver finally cashes
//    the card in, at the moment the peaceful year is closed (ChronicleGlobal.ExecuteRest):
//      - RewardPoints  -> +Value points (the card's points are actually granted).
//      - ScoutReward   -> +Value free recruits join the roster (no cost).
//      - EquipmentDrop -> one equipment (level Value) is dropped onto a member.
//      - Rest / null   -> the modest fixed rest bonus (RestPointsReward).
//    Battle prophecies never reach here (they march to battle; spoils reward them).
//
//  ★ What a peaceful year still does NOT do:
//    The roster Unit carries NO persistent HP — survivors are always whole between
//    battles. So "the battalion recovered" is the narrative truth of standing down.
//
//  ★ Godot-independent pure function. (roster, economy, prophecy, rng) ->
//    (nextEconomy, nextRoster, outcome), no side effects. ChronicleGlobal swaps in
//    the results and publishes the outcome; all arithmetic is here and unit-tested.
//
//  Constitution I: ASCII only for identifiers/logs (Japanese lives in the UI).
// =============================================================================

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using ChronicleKnights.Core.Managers;
using ChronicleKnights.Core.Timeline;
using ChronicleKnights.Core.Units;

namespace ChronicleKnights.Core.GameFlow;

/// <summary>
/// Immutable summary of one peaceful (rest) year, read by the rest-result UI. Holds
/// the rested headcount, the points granted, the resulting balance, and the
/// prophecy reward that materialized (recruits joined / equipment dropped).
/// </summary>
public sealed record RestOutcome
{
    /// <summary>Count of living battalion members who rested this year (not dead).</summary>
    public required int RestedUnitCount { get; init; }

    /// <summary>Points granted this peaceful year (RewardPoints value, or the fixed rest bonus).</summary>
    public required int PointsReward { get; init; }

    /// <summary>Points balance after the reward was applied.</summary>
    public required int BalanceAfter { get; init; }

    /// <summary>Number of free recruits that joined this year (ScoutReward prophecy). 0 otherwise.</summary>
    public int RecruitedCount { get; init; }

    /// <summary>The item dropped this year (EquipmentDrop prophecy), or null if none.</summary>
    public ItemId? DroppedItemId { get; init; }

    /// <summary>The level of the dropped item (only meaningful when <see cref="DroppedItemId"/> is set).</summary>
    public int DroppedItemLevel { get; init; }

    /// <summary>
    /// The 3-choice drop candidates generated this year (EquipmentDrop prophecy), empty otherwise.
    /// The player picks ONE (it goes to the brigade inventory); the rest are discarded. The roster is
    /// NOT touched by the drop anymore — claiming is an interactive step (ChronicleGlobal.ChooseDroppedEquipment).
    /// </summary>
    public ImmutableArray<Equipment> DropCandidates { get; init; } = ImmutableArray<Equipment>.Empty;

    /// <summary>The inert "nothing rested" outcome (uninitialized / no-op default).</summary>
    public static readonly RestOutcome None = new()
    {
        RestedUnitCount = 0,
        PointsReward = 0,
        BalanceAfter = 0,
    };
}

/// <summary>The pure result of resolving a peaceful year: next economy + next roster + summary.</summary>
public readonly record struct RestResolution(
    PointsEconomy NextEconomy, ImmutableList<Unit> NextRoster, RestOutcome Outcome);

/// <summary>
/// Godot-independent pure resolver for a peaceful (rest) year. Stateless; applies the
/// chosen prophecy's reward (points / recruits / equipment) and returns the new economy,
/// the new roster, and an immutable summary.
/// </summary>
public static class RestService
{
    /// <summary>
    /// [SoT] Points granted for a plain rest year (the Rest prophecy / no prophecy). A modest,
    /// fixed bonus — the real cost of resting is the OPPORTUNITY cost (forgoing the battle's
    /// kill reward), so this stays small to keep the golden balance intact.
    /// </summary>
    public const int RestPointsReward = 2;

    /// <summary>
    /// Resolve a plain rest year (no prophecy reward): grants <see cref="RestPointsReward"/>.
    /// Backwards-compatible overload; delegates to the prophecy-aware resolver with a null
    /// prophecy (no recruits / drops, so the Random is never consumed).
    /// </summary>
    public static RestResolution Resolve(IReadOnlyList<Unit> roster, PointsEconomy economy)
        => Resolve(roster, economy, prophecy: null, rng: new Random(0));

    /// <summary>
    /// Resolve one peaceful year purely, cashing in the chosen prophecy's reward. Counts living
    /// members (IsDead == false) as the rested headcount, applies the Kind-specific reward
    /// (points / free recruits / an equipment drop), and bundles the new economy + new roster
    /// with an immutable summary. Inputs are never mutated.
    /// </summary>
    /// <param name="roster">Current battalion roster (read-only, not null).</param>
    /// <param name="economy">Current points economy (immutable, not null).</param>
    /// <param name="prophecy">The prophecy chosen for this year (null = a plain rest year).</param>
    /// <param name="rng">RNG for recruit / drop generation (not null).</param>
    /// <returns>The next economy + next roster paired with the peaceful-year summary.</returns>
    public static RestResolution Resolve(
        IReadOnlyList<Unit> roster, PointsEconomy economy, Prophecy? prophecy, Random rng)
    {
        ArgumentNullException.ThrowIfNull(roster);
        ArgumentNullException.ThrowIfNull(economy);
        ArgumentNullException.ThrowIfNull(rng);

        int rested = 0;
        foreach (var unit in roster)
        {
            if (!unit.IsDead) rested++;
        }

        var working = ImmutableList.CreateRange(roster);
        var pointsReward = 0;
        var recruitedCount = 0;
        var droppedItemLevel = 0;
        var dropCandidates = ImmutableArray<Equipment>.Empty;

        // Kind-specific reward. A plain rest (Rest / null / any non-reward kind) grants the
        // fixed rest bonus; the reward kinds cash in their card value instead.
        switch (prophecy?.Kind)
        {
            case ProphecyKind.RewardPoints:
                pointsReward = Math.Max(0, prophecy.Value);
                break;

            case ProphecyKind.ScoutReward:
                recruitedCount = Math.Max(1, prophecy.Value);
                working = AddRecruits(working, recruitedCount, rng);
                break;

            case ProphecyKind.EquipmentDrop:
                droppedItemLevel = Math.Clamp(
                    prophecy.Value, Equipment.MinEquipmentLevel, Equipment.MaxEquipmentLevel);
                // 自動装着はやめ、3 択候補を生成する。プレイヤーが 1 つ選び持ち物へ入れる
                // （残りは破棄）。ロスタはここでは動かさない（claim は別の対話ステップ）。
                dropCandidates = EquipmentDropService.RollCandidates(droppedItemLevel, rng);
                break;

            case ProphecyKind.Battle:
                // No peaceful-year reward — a Battle prophecy is rewarded by its spoils
                // (resolved at the battle epilogue, not here). Applying this resolver to a
                // Battle prophecy is therefore a no-op (used as a safe guard on the battle path).
                break;

            default: // Rest or null (a plain rest year) -> the fixed rest bonus.
                pointsReward = RestPointsReward;
                break;
        }

        var nextEconomy = pointsReward > 0 ? economy.EarnDirect(pointsReward) : economy;

        var outcome = new RestOutcome
        {
            RestedUnitCount = rested,
            PointsReward = pointsReward,
            BalanceAfter = nextEconomy.CurrentBalance,
            RecruitedCount = recruitedCount,
            // 装着はせず、選択前の 3 択候補だけを報告する（DroppedItem* は旧自動装着の名残で常に空）。
            DroppedItemId = null,
            DroppedItemLevel = dropCandidates.IsDefaultOrEmpty ? 0 : droppedItemLevel,
            DropCandidates = dropCandidates,
        };

        return new RestResolution(nextEconomy, working, outcome);
    }

    /// <summary>Generate <paramref name="count"/> free outsider recruits and append them (unique names).</summary>
    private static ImmutableList<Unit> AddRecruits(ImmutableList<Unit> roster, int count, Random rng)
    {
        var usedNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var u in roster) usedNames.Add(u.FirstNameKey);

        var next = roster;
        for (var i = 0; i < count; i++)
        {
            var recruit = ScoutService.CreateOutsiderUnit(rng, usedNames);
            usedNames.Add(recruit.FirstNameKey);
            next = next.Add(recruit);
        }
        return next;
    }
}
