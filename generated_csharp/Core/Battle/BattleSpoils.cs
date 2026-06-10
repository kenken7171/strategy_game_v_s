// =============================================================================
//  ChronicleKnights — BattleSpoils.cs
// -----------------------------------------------------------------------------
//  1 戦闘の「戦果決算」を写し取る完全不変レコードと、その純粋な差分ファクトリ。
//
//  ★ 設計意図（開戦時 vs 終了時の Guid 突合）:
//    戦闘は BattleSnapshot.Combatants（Id → Unit）の上で進行し、とどめ成長
//    （ExecuteLastHit による昇級・装備進化/Lv5 破壊）や完全ロスト（ApplyLethalDamage
//    による IsDead マーク）はこの参加者複製へ刻まれる。本レコードは「開戦時の参加者
//    静止画」と「終了時の参加者静止画」を同じ Guid で突き合わせ、この 1 戦闘で
//    起きた変化（誰が育ち・誰を永久に失い・どの装備が進化/破壊されたか）だけを
//    集約した薄い決算データである。
//
//    戦果のロスタ正本化（ChronicleGlobal.EndBattle の書き戻し）とは関心を分離する:
//    あちらは「終了時の状態を正本へ確定する」責務、本レコードは「開戦時からの差分を
//    プレイヤーへ提示する」責務。後者は次段の戦果決算スクリーン（無状態 UI）が
//    読み取るだけの公開スナップショットになる。
//
//  ★ 完全不変（設計憲法 ②）・単一 SoT（設計憲法 ③）:
//    全フィールドは init 専用。FromBattle は入力の 2 つの静止画から新規生成するだけで
//    副作用を持たない純粋関数。Godot に 1 ミリも依存しない（xUnit で完全検証可能）。
//
//  ★ 日本語ハードコード禁止（設計憲法 ①）:
//    本レコードは Guid / ItemId(enum) / 数値だけを持ち、表示用テキストは一切持たない。
//    ジョブ名・アイテム名・スキル名等の表示解決は UI 層が localization 経由で行う。
//
//  略称（BDF/SDF/AB/HL — 正式名称のみを使う方針）は本ファイルでも完全未使用。
// =============================================================================

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using ChronicleKnights.Core.Units;

namespace ChronicleKnights.Core.Battle;

// ─── 戦果の構成要素（いずれも完全不変・表示テキストを持たない） ────────────────

/// <summary>
/// 1 ユニットのレベルアップ（とどめ成長による昇級）を表す不変レコード。
/// 表示名は持たず、突合キー（UnitId）と前後のレベルだけを運ぶ。
/// </summary>
public sealed record UnitLevelGain
{
    /// <summary>昇級したユニットの突合キー。</summary>
    public required Guid UnitId { get; init; }

    /// <summary>開戦時のレベル。</summary>
    public required int FromLevel { get; init; }

    /// <summary>終了時のレベル（FromLevel より大きい）。</summary>
    public required int ToLevel { get; init; }
}

/// <summary>
/// 1 ユニットの装備進化（とどめ成長による装備レベルアップ）を表す不変レコード。
/// </summary>
public sealed record EquipmentEvolution
{
    /// <summary>装備が進化したユニットの突合キー。</summary>
    public required Guid UnitId { get; init; }

    /// <summary>進化した装備の識別子（表示名は localization 経由で解決する）。</summary>
    public required ItemId ItemId { get; init; }

    /// <summary>開戦時の装備レベル。</summary>
    public required int FromLevel { get; init; }

    /// <summary>終了時の装備レベル（FromLevel より大きい）。</summary>
    public required int ToLevel { get; init; }
}

/// <summary>
/// 1 ユニットの装備喪失（Lv5 装備のとどめ破壊、または戦闘死に伴う装備消失）を
/// 表す不変レコード。
/// </summary>
public sealed record EquipmentLoss
{
    /// <summary>装備を失ったユニットの突合キー。</summary>
    public required Guid UnitId { get; init; }

    /// <summary>失われた装備の識別子（表示名は localization 経由で解決する）。</summary>
    public required ItemId ItemId { get; init; }
}

// ─── 戦果決算（開戦時 vs 終了時の Guid 突合の集約） ──────────────────────────

/// <summary>
/// 1 戦闘の戦果決算を集約した完全不変レコード。開戦時の参加者静止画と終了時の
/// 参加者静止画を同じ <see cref="Guid"/> で突き合わせ、この戦闘で起きた変化だけを
/// 4 つの不変配列に集約する。次段の戦果決算スクリーン（無状態 UI）が読み取る公開
/// スナップショットの足場。
/// </summary>
public sealed record BattleSpoils
{
    /// <summary>この戦果が確定した時点の決着状態。</summary>
    public required BattleOutcome Outcome { get; init; }

    /// <summary>この戦闘で昇級したユニットの一覧（発生順は問わない集合的事実）。</summary>
    public required ImmutableArray<UnitLevelGain> UnitLevelGains { get; init; }

    /// <summary>この戦闘で完全ロスト（戦闘死）したユニットの突合キー一覧。</summary>
    public required ImmutableArray<Guid> PermanentlyLostUnitIds { get; init; }

    /// <summary>この戦闘で装備が進化（レベルアップ）したユニットの一覧。</summary>
    public required ImmutableArray<EquipmentEvolution> EquipmentEvolutions { get; init; }

    /// <summary>この戦闘で装備を喪失（Lv5 破壊・戦闘死）したユニットの一覧。</summary>
    public required ImmutableArray<EquipmentLoss> EquipmentLosses { get; init; }

    /// <summary>
    /// 戦果が一切無い空の決算（非戦闘・未決着の既定値）。Outcome は Ongoing。
    /// ChronicleGlobal.LastBattleSpoils の初期値・戦闘未実施時のフォールバックに使う。
    /// </summary>
    public static BattleSpoils Empty { get; } = new()
    {
        Outcome                = BattleOutcome.Ongoing,
        UnitLevelGains         = ImmutableArray<UnitLevelGain>.Empty,
        PermanentlyLostUnitIds = ImmutableArray<Guid>.Empty,
        EquipmentEvolutions    = ImmutableArray<EquipmentEvolution>.Empty,
        EquipmentLosses        = ImmutableArray<EquipmentLoss>.Empty,
    };

    /// <summary>
    /// 何らかの戦果（昇級・完全ロスト・装備進化/喪失のいずれか）が存在するか。
    /// 決算スクリーンが「特筆すべき戦果なし」を判定する補助。
    /// </summary>
    public bool HasAnySpoils =>
        !UnitLevelGains.IsEmpty
        || !PermanentlyLostUnitIds.IsEmpty
        || !EquipmentEvolutions.IsEmpty
        || !EquipmentLosses.IsEmpty;

    /// <summary>
    /// 開戦時と終了時の参加者静止画（いずれも Id → Unit）を Guid で突き合わせ、
    /// この戦闘で起きた変化だけを抽出した戦果決算を純粋生成する。
    ///
    /// 突合規律:
    ///   - 開戦時に居たユニットだけを基準に終了時を引き当てる（途中追加はとどめ成長の
    ///     仕様上発生しないため考慮不要だが、引き当て不能なら安全にスキップ）。
    ///   - 昇級: 終了時 Level &gt; 開戦時 Level。
    ///   - 完全ロスト: 開戦時 IsDead=false → 終了時 IsDead=true。
    ///   - 装備進化: 装備あり同士で終了時 Level &gt; 開戦時 Level。
    ///   - 装備喪失: 開戦時は装備あり → 終了時は装備なし（Lv5 破壊・戦闘死の消失）。
    ///
    /// いずれの入力が null でも例外を投げず、Outcome だけを反映した空決算を返す
    /// （ヌル安全フォールバック・画面が落ちない方針）。
    /// </summary>
    /// <param name="openingCombatants">開戦時の参加者静止画（Id → Unit）。</param>
    /// <param name="finalCombatants">終了時の参加者静止画（Id → Unit）。</param>
    /// <param name="outcome">この戦果が確定した時点の決着状態。</param>
    public static BattleSpoils FromBattle(
        IReadOnlyDictionary<Guid, Unit>? openingCombatants,
        IReadOnlyDictionary<Guid, Unit>? finalCombatants,
        BattleOutcome outcome)
    {
        if (openingCombatants is null || finalCombatants is null)
        {
            return Empty with { Outcome = outcome };
        }

        var levelGains  = ImmutableArray.CreateBuilder<UnitLevelGain>();
        var lostUnitIds = ImmutableArray.CreateBuilder<Guid>();
        var evolutions  = ImmutableArray.CreateBuilder<EquipmentEvolution>();
        var losses      = ImmutableArray.CreateBuilder<EquipmentLoss>();

        foreach (var (unitId, before) in openingCombatants)
        {
            if (before is null) continue;
            if (!finalCombatants.TryGetValue(unitId, out var after) || after is null)
            {
                continue; // 終了時に突合できない参加者は安全にスキップ。
            }

            // ── ユニット成長（レベルアップ） ──────────────────────────────
            if (after.Level > before.Level)
            {
                levelGains.Add(new UnitLevelGain
                {
                    UnitId    = unitId,
                    FromLevel = before.Level,
                    ToLevel   = after.Level,
                });
            }

            // ── 完全ロスト（開戦時生存 → 終了時 IsDead） ─────────────────
            if (!before.IsDead && after.IsDead)
            {
                lostUnitIds.Add(unitId);
            }

            // ── 装備の進化 / 喪失 ─────────────────────────────────────────
            var beforeItem = before.MainEquipment;
            var afterItem  = after.MainEquipment;

            if (beforeItem is not null && afterItem is null)
            {
                // Lv5 とどめ破壊、または戦闘死に伴う装備消失。
                losses.Add(new EquipmentLoss
                {
                    UnitId = unitId,
                    ItemId = beforeItem.ItemId,
                });
            }
            else if (beforeItem is not null && afterItem is not null
                     && afterItem.Level > beforeItem.Level)
            {
                evolutions.Add(new EquipmentEvolution
                {
                    UnitId    = unitId,
                    ItemId    = afterItem.ItemId,
                    FromLevel = beforeItem.Level,
                    ToLevel   = afterItem.Level,
                });
            }
        }

        return new BattleSpoils
        {
            Outcome                = outcome,
            UnitLevelGains         = levelGains.ToImmutable(),
            PermanentlyLostUnitIds = lostUnitIds.ToImmutable(),
            EquipmentEvolutions    = evolutions.ToImmutable(),
            EquipmentLosses        = losses.ToImmutable(),
        };
    }
}
