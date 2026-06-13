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

// ─── 婚姻ポイント算出の内訳（式の見える化・純粋な派生射影） ──────────────────

/// <summary>
/// <see cref="BattleSpoils.CalculateEarnedMarriagePoints"/> の算出内訳を、UI が
/// 「なぜこの点数になったか」を一目で説明できるよう展開した完全不変の派生射影。
///
/// ★ 自前の状態（SoT）を 1 ミリも増やさない:
///   本構造体は <see cref="BattleSpoils"/> の既存フィールドからその場で算出される
///   一過性の値オブジェクトであり、どこにも保持されない。合計（<see cref="Total"/>）は
///   <see cref="BattleSpoils.CalculateEarnedMarriagePoints"/> と完全に一致する
///   （後者は本射影の Total を返すだけ＝式の単一の真実）。
///
/// ★ 表示テキストを持たない（設計憲法 ①）:
///   見出し・単位等の文言は UI 層が持ち、本構造体は数値だけを運ぶ。
/// </summary>
public readonly record struct MarriagePointsBreakdown
{
    /// <summary>勝利だったか（false のときポイントは全要素 0）。</summary>
    public required bool IsVictory { get; init; }

    /// <summary>勝利基礎報酬（勝利時のみ非 0）。</summary>
    public required int VictoryBase { get; init; }

    /// <summary>昇級した件数。</summary>
    public required int LevelGainCount { get; init; }

    /// <summary>昇級 1 件あたりの加点レート。</summary>
    public required int LevelGainRate { get; init; }

    /// <summary>昇級ボーナスの小計（件数 × レート）。</summary>
    public int LevelGainBonus => LevelGainCount * LevelGainRate;

    /// <summary>装備進化した件数。</summary>
    public required int EvolutionCount { get; init; }

    /// <summary>装備進化 1 件あたりの加点レート。</summary>
    public required int EvolutionRate { get; init; }

    /// <summary>装備進化ボーナスの小計（件数 × レート）。</summary>
    public int EvolutionBonus => EvolutionCount * EvolutionRate;

    /// <summary>完全ロストした件数。</summary>
    public required int LossCount { get; init; }

    /// <summary>完全ロスト 1 件あたりの減点レート。</summary>
    public required int LossRate { get; init; }

    /// <summary>完全ロスト罰の小計（件数 × レート）。減算される正の値。</summary>
    public int LossPenalty => LossCount * LossRate;

    /// <summary>加点の総和（基礎 + 昇級 + 進化）。罰を引く前の粗利。</summary>
    public int Gross => VictoryBase + LevelGainBonus + EvolutionBonus;

    /// <summary>最終獲得ポイント（0 で底打ち）。CalculateEarnedMarriagePoints と一致。</summary>
    public int Total => Math.Max(0, Gross - LossPenalty);
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

    // ─── 婚姻ポイント算出（戦果 → 経済 入口の純粋写像） ──────────────────────
    //
    //  戦果決算から「獲得すべき婚姻ポイント」を決定論的に弾き出す純粋関数。
    //  本レコードが既に握っている自前のフィールドだけから算出し、内部状態を
    //  1 ミリも書き換えない（完全副作用ゼロ・xUnit で完全検証可能）。
    //
    //  ★ 算出規律（戦果の質を点数へ写す）:
    //    - 勝利のときだけポイントが立つ（敗北・未決着は 0）。婚姻は勝利の褒賞。
    //    - 勝利基礎報酬: 勝ち切ったこと自体への定額。
    //    - 昇級報酬: とどめの儀式を含む「育ち」の達成度の代理指標。
    //      （UnitLevelGains は戦闘中＋とどめ成長を統合台帳として 1 枚に合算済み。）
    //    - 装備進化報酬: 武具が研がれた戦果への加点。
    //    - 完全ロスト罰: 散った味方ぶんの減点（＝「味方の生存数」の逆指標）。
    //
    //  ★ 境界ガード（設計憲法・経済の健全性）:
    //    最終値は必ず Math.Max(0, …) で底打ちし、負の婚姻ポイントが経済へ
    //    流れ込む（アンダーフロー・不正な負加算）ことを構造的に封じる。

    /// <summary>勝利そのものへの定額報酬（勝ち切った事実への基礎点）。</summary>
    private const int VictoryBaseReward = 5;

    /// <summary>ユニット 1 名の昇級ごとの加点（とどめ達成を含む「育ち」の代理指標）。</summary>
    private const int LevelGainBounty = 2;

    /// <summary>装備 1 件の進化ごとの加点（武具が研がれた戦果）。</summary>
    private const int EquipmentEvolutionBounty = 1;

    /// <summary>完全ロスト 1 名ごとの減点（散った味方ぶんの褒賞縮小）。</summary>
    private const int PermanentLossPenalty = 3;

    /// <summary>
    /// 婚姻ポイント算出の内訳を、自前の戦果フィールドからその場で展開した純粋な派生射影
    /// として返す。式の単一の真実（SoT）はここに集約され、<see cref="MarriagePointsBreakdown.Total"/>
    /// が最終獲得ポイントになる。内部状態は一切書き換えない（完全副作用ゼロ）。
    ///
    /// 算出式（勝利時のみ。敗北・未決着は全要素 0）:
    ///   gross   = VictoryBaseReward
    ///           + UnitLevelGains 件数      × LevelGainBounty
    ///           + EquipmentEvolutions 件数 × EquipmentEvolutionBounty
    ///   penalty = PermanentlyLostUnitIds 件数 × PermanentLossPenalty
    ///   Total    = Math.Max(0, gross - penalty)
    ///
    /// 勝利以外（敗北・未決着・空決算）は IsVictory=false で全要素 0 を返し、
    /// ヌル安全の番兵もここで吸収する（Total は必ず 0）。
    /// </summary>
    /// <returns>算出内訳（合計は常に 0 以上）。</returns>
    public MarriagePointsBreakdown DescribeMarriagePoints()
    {
        // 勝利以外は全要素 0（レートは「式の見える化」のため参照値として運ぶ）。
        if (Outcome != BattleOutcome.BattalionVictory)
        {
            return new MarriagePointsBreakdown
            {
                IsVictory      = false,
                VictoryBase    = 0,
                LevelGainCount = 0,
                LevelGainRate  = LevelGainBounty,
                EvolutionCount = 0,
                EvolutionRate  = EquipmentEvolutionBounty,
                LossCount      = 0,
                LossRate       = PermanentLossPenalty,
            };
        }

        return new MarriagePointsBreakdown
        {
            IsVictory      = true,
            VictoryBase    = VictoryBaseReward,
            LevelGainCount = UnitLevelGains.Length,
            LevelGainRate  = LevelGainBounty,
            EvolutionCount = EquipmentEvolutions.Length,
            EvolutionRate  = EquipmentEvolutionBounty,
            LossCount      = PermanentlyLostUnitIds.Length,
            LossRate       = PermanentLossPenalty,
        };
    }

    /// <summary>
    /// この戦果から獲得すべき婚姻ポイントを純粋に算出する。<see cref="DescribeMarriagePoints"/>
    /// の合計（<see cref="MarriagePointsBreakdown.Total"/>）をそのまま返す薄い委譲で、
    /// 式の重複を排し単一の真実を保つ。最終値は必ず 0 で底打ちされ、負のポイントが
    /// 経済へ流れ込むことはない。
    /// </summary>
    /// <returns>獲得婚姻ポイント（常に 0 以上）。</returns>
    public int CalculateEarnedMarriagePoints() => DescribeMarriagePoints().Total;

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
