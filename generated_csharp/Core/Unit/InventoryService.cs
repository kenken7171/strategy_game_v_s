// =============================================================================
//  ChronicleKnights — InventoryService.cs
// -----------------------------------------------------------------------------
//  旅団共有の「持ち物（インベントリ）」とユニットの装備の間で、装備個体を
//  非破壊に往復させる Godot 非依存の純粋層。
//
//  ★ 背景 — なぜ持ち物が要るか:
//    EquipmentService（無償の直接脱着）は「外した装備はロストする」設計だった。
//    Affix 付きの貴重なドロップが外した瞬間に消えるのは「付け外し」として成立しない。
//    そこで旅団が所有する未装着の装備を貯める ImmutableList<Equipment> を SoT に持ち、
//    本サービスが「持ち物 → ユニット（装着）」「ユニット → 持ち物（取り外し）」を
//    個体（Guid）を保ったまま移し替える。装備は決して複製・消滅しない（保存則）。
//
//  ★ 層別（EquipmentService / ScoutService と同じ純粋・静的）:
//    元データ（不変ロスタ＋不変インベントリ）から新しい不変ペアを返すだけで副作用なし。
//    xUnit で個体の往復を 1 ビット単位で検証でき、ChronicleGlobal は状態差し替えと
//    シグナル発火に専念できる（単一 SoT・設計憲法②③）。
//
//  ★ ヌル安全契約（番兵）:
//    対象ユニット不在・指定装備が持ち物に無い等は例外を投げず null（no-op）を返す。
//
//  略称（正式名称のみを使う方針）は本ファイルでも完全未使用。
// =============================================================================

using System;
using System.Collections.Immutable;

namespace ChronicleKnights.Core.Units;

// ─── 持ち物⇄装備の移し替え結果（不変スナップショット） ───────────────────────

/// <summary>
/// 持ち物とユニット装備の間で装備個体を移し替えた結果の束。
/// 呼び出し側（ChronicleGlobal）はロスタと持ち物を丸ごと差し替える。
/// </summary>
public sealed record InventoryChangeResult
{
    /// <summary>移し替えを反映した新しい旅団員リスト（元の並び順を保持）。</summary>
    public required ImmutableList<Unit> NewRoster { get; init; }

    /// <summary>移し替えを反映した新しい持ち物リスト。</summary>
    public required ImmutableList<Equipment> NewInventory { get; init; }

    /// <summary>操作対象のユニット本体（UI の「○○が装備した／外した」演出に使う）。</summary>
    public required Unit AffectedUnit { get; init; }

    /// <summary>今回の操作で状態が実際に変化したか（no-op 時は false で発火抑止）。</summary>
    public required bool Mutated { get; init; }
}

// ─── 持ち物サービス本体（純粋・静的） ───────────────────────────────────────

/// <summary>
/// 旅団共有の持ち物とユニット装備の間で装備個体を非破壊に往復させる純粋ユーティリティ。
/// </summary>
public static class InventoryService
{
    /// <summary>
    /// 持ち物の中の指定装備（<paramref name="equipmentId"/>）を、指定ユニットへ装着する。
    /// ユニットが既に装備していれば、その旧装備は持ち物へ戻る（個体は消えない＝保存則）。
    ///
    /// 判定:
    ///   - roster / inventory が null → 例外（呼び出し前提の契約違反）。
    ///   - 対象ユニットが見つからない → null（no-op）。
    ///   - 指定装備が持ち物に無い → null（no-op）。
    /// </summary>
    public static InventoryChangeResult? EquipFromInventory(
        ImmutableList<Unit> roster,
        ImmutableList<Equipment> inventory,
        Guid unitId,
        Guid equipmentId)
    {
        ArgumentNullException.ThrowIfNull(roster);
        ArgumentNullException.ThrowIfNull(inventory);

        var unitIndex = roster.FindIndex(u => u.Id == unitId);
        if (unitIndex < 0) return null;

        var itemIndex = inventory.FindIndex(e => e.Id == equipmentId);
        if (itemIndex < 0) return null;

        var target = roster[unitIndex];
        var fitted = inventory[itemIndex];

        // 持ち物から取り出し、ユニットの旧装備（あれば）を持ち物へ戻す。
        var nextInventory = inventory.RemoveAt(itemIndex);
        var replaced = target.MainEquipment;
        if (replaced is not null)
        {
            nextInventory = nextInventory.Add(replaced);
        }

        var equippedUnit = target.WithEquipment(fitted);

        return new InventoryChangeResult
        {
            NewRoster    = roster.SetItem(unitIndex, equippedUnit),
            NewInventory = nextInventory,
            AffectedUnit = equippedUnit,
            Mutated      = true,
        };
    }

    /// <summary>
    /// 指定ユニットの現装備を取り外し、持ち物へ戻す（個体は消えない＝保存則）。
    ///
    /// 判定:
    ///   - roster / inventory が null → 例外。
    ///   - 対象ユニットが見つからない → null（no-op）。
    ///   - 元から未装備 → Mutated=false の no-op 結果（同一参照）。
    /// </summary>
    public static InventoryChangeResult? UnequipToInventory(
        ImmutableList<Unit> roster,
        ImmutableList<Equipment> inventory,
        Guid unitId)
    {
        ArgumentNullException.ThrowIfNull(roster);
        ArgumentNullException.ThrowIfNull(inventory);

        var unitIndex = roster.FindIndex(u => u.Id == unitId);
        if (unitIndex < 0) return null;

        var target = roster[unitIndex];
        var removed = target.MainEquipment;

        if (removed is null)
        {
            return new InventoryChangeResult
            {
                NewRoster    = roster,
                NewInventory = inventory,
                AffectedUnit = target,
                Mutated      = false,
            };
        }

        var barehandedUnit = target.WithEquipment(null);

        return new InventoryChangeResult
        {
            NewRoster    = roster.SetItem(unitIndex, barehandedUnit),
            NewInventory = inventory.Add(removed),
            AffectedUnit = barehandedUnit,
            Mutated      = true,
        };
    }
}
