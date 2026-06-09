// =============================================================================
//  ChronicleKnights — BattleManager.cs
// -----------------------------------------------------------------------------
//  戦闘解決の静的サービス層。
//
//  責務 (4 つの core 機能):
//    1. 防衛力計算: BattalionDefense / SquadDefense をデータ駆動で集計し、
//       被弾ダメージを軽減する純粋関数を提供する（型安全・文字列マッチなし）。
//    2. ラストヒット (LH) 成長 + Lv5 破壊判定: ExecuteLastHit で:
//         - ユニット Lv<3 なら確定 +1（Lv3 で overflow）
//         - 装備 Lv<5 なら確定 +1
//         - 装備 Lv=5 + CoinGreed → +1 pt 強奪 + 100% 破壊
//         - 装備 Lv=5 + 通常装備 → 50% で破壊（ポイント追加なし）
//    3. 完全ロスト: ApplyLethalDamage で死亡マーク + 装備消失を同時実行。
//    4. 自然婚姻ポイント蓄積 + AffinityMultiplier 適用:
//       AccumulateMutualBattleAffinity が各ユニットの装備 AffinityMultiplier
//       (1.1〜2.25 倍) を base 値に乗算してから蓄積する。
//
//  ハクスラ・ローグライト設計憲法 (docs/MIGRATION_GODOT_HACK_AND_SLASH.md):
//    - 略称 BDF/SDF/AB/HL は本ファイル実コードに一切出現しない
//    - 文字列マッチ unit.Job == JobId.XXX は使わず JobMaster.HasPassive 経由
//    - すべての破壊判定・成長判定は決定論的（rng は呼び出し側責任）
//    - 戻り値は LastHitResult record で集約（out 多用を避ける）
// =============================================================================

using System;
using System.Collections.Generic;
using ChronicleKnights.Core.Job;
using ChronicleKnights.Core.Units;

namespace ChronicleKnights.Core.Managers;

// ─── ラストヒット解決結果 ───────────────────────────────────────────────────

/// <summary>
/// ExecuteLastHit の解決結果を集約する完全不変なレコード。
///
/// 呼び出し側（戦闘オーケストレータ）は本レコードを受け取って:
///   - NewUnit を旅団ロスタに書き戻し
///   - GreedPointsStolen > 0 なら PointsEconomy.EarnDirect(GreedPointsStolen)
///   - ログ / UI に LevelOverflow / ItemLevelUpTriggered / ItemDestroyed を反映
/// する責務を持つ。BattleManager 自体は経済状態に直接触らない（純粋関数）。
/// </summary>
public sealed record LastHitResult
{
    /// <summary>成長・破壊判定後の新しいユニットスナップショット。</summary>
    public required Unit NewUnit { get; init; }

    /// <summary>ユニットが既に Lv3 で成長が無駄になった場合 true。</summary>
    public required bool LevelOverflow { get; init; }

    /// <summary>装備のレベルが今回の LH で +1 された場合 true（Lv1〜4 のみ）。</summary>
    public required bool ItemLevelUpTriggered { get; init; }

    /// <summary>装備が Lv5 破壊判定で消滅した場合 true（NewUnit.MainEquipment は null）。</summary>
    public required bool ItemDestroyed { get; init; }

    /// <summary>
    /// CoinGreed の Lv5 LH で強奪したポイント量（既定 0）。
    /// 0 より大きい場合、呼び出し側は PointsEconomy.EarnDirect で残高に加算する。
    /// </summary>
    public required int GreedPointsStolen { get; init; }
}

/// <summary>
/// 戦闘解決のための純粋関数群を提供する静的クラス。
/// </summary>
public static class BattleManager
{
    // ─── 戦闘バランス用定数 ────────────────────────────────────────────────

    /// <summary>軽減後の最低保証ダメージ（被弾の停滞防止）。</summary>
    public const int MinimumDamageAfterReduction = 1;

    /// <summary>
    /// 装備 Lv5 でラストヒットを取った時、通常装備（CoinGreed 以外）が破壊される確率。
    /// 50% コイントス（憲法仕様）。
    /// </summary>
    public const double Lv5NormalItemDestructionProbability = 0.5;

    /// <summary>
    /// CoinGreed が Lv5 LH 時に強奪するポイント数（残高に直接加算）。
    /// CoinGreed は同時に 100% 破壊もされる（代償）。
    /// </summary>
    public const int Lv5GreedPointsStolen = 1;

    /// <summary>
    /// CoinGreed の Lv5 LH 時の破壊確率（憲法仕様: 100% 確定）。
    /// 明示的に 1.0 として宣言し、コインフリップが入らないことを型レベルで宣言する。
    /// </summary>
    public const double Lv5GreedItemDestructionProbability = 1.0;

    // ─── 防衛力計算 (Phase C — Damage Scope) ───────────────────────────────

    /// <summary>
    /// 大隊全体に届くダメージ軽減量（旧 BDF）を集計する。
    /// FRONT 配置にいるユニットのうち、JobMaster.HasPassive で BattalionDefense
    /// パッシブを持つと判定されたものから Stats.BattalionDefense を合算する。
    /// </summary>
    public static int CalculateBattalionDefense(IReadOnlyList<Unit> frontRowUnits)
    {
        ArgumentNullException.ThrowIfNull(frontRowUnits);
        var total = 0;
        foreach (var unit in frontRowUnits)
        {
            if (!unit.IsAlive) continue;
            if (!JobMaster.HasPassive(unit.Job, PassiveKind.BattalionDefense)) continue;
            var def = JobMaster.Find(unit.Job);
            if (def is null) continue;
            total += def.Stats.BattalionDefense;
        }
        return total;
    }

    /// <summary>
    /// 指定分隊内に発生するダメージ軽減量（旧 SDF）を集計する。
    /// 分隊内ユニットのうち、JobMaster.HasPassive で SquadDefense パッシブを
    /// 持つと判定されたものから Stats.SquadDefense を合算する。
    /// </summary>
    public static int CalculateSquadDefense(IReadOnlyList<Unit> squadUnits)
    {
        ArgumentNullException.ThrowIfNull(squadUnits);
        var total = 0;
        foreach (var unit in squadUnits)
        {
            if (!unit.IsAlive) continue;
            if (!JobMaster.HasPassive(unit.Job, PassiveKind.SquadDefense)) continue;
            var def = JobMaster.Find(unit.Job);
            if (def is null) continue;
            total += def.Stats.SquadDefense;
        }
        return total;
    }

    /// <summary>
    /// 被弾時の実効ダメージを算出する純粋関数。
    /// 計算式: max(MinimumDamageAfterReduction,
    ///             baseDamage - BattalionDefense総和 - SquadDefense総和)
    /// </summary>
    public static int ResolveIncomingDamage(
        int baseDamage,
        IReadOnlyList<Unit> frontRowUnits,
        IReadOnlyList<Unit> targetSquadUnits)
    {
        var battalionReduction = CalculateBattalionDefense(frontRowUnits);
        var squadReduction = CalculateSquadDefense(targetSquadUnits);
        var totalReduction = battalionReduction + squadReduction;
        return Math.Max(MinimumDamageAfterReduction, baseDamage - totalReduction);
    }

    // ─── ラストヒット (LH) 成長 + Lv5 破壊判定 ────────────────────────────

    /// <summary>
    /// ラストヒット時のユニット成長 + 装備成長/破壊を一括解決する。
    ///
    /// 【ユニット成長】
    ///   Lv1〜2 → 確定 +1（WithLevelUp）
    ///   Lv3    → 成長なし、LevelOverflow = true
    ///
    /// 【装備成長 (Lv1〜4)】
    ///   装備があり Lv<5 → 確定 +1（WithLevelUp）、ItemLevelUpTriggered = true
    ///
    /// 【装備 Lv5 の冷徹な破壊ルール】
    ///   - CoinGreed (HasGreedEffect == true):
    ///       +1 pt 強奪 (GreedPointsStolen = Lv5GreedPointsStolen)
    ///       同時に 100% 破壊 (ItemDestroyed = true, MainEquipment = null)
    ///   - 通常装備（聖剣・魔弓・破滅杖・誓輪）:
    ///       ポイント追加なし
    ///       50% コイントスで破壊判定（Lv5NormalItemDestructionProbability）
    ///       当選で MainEquipment = null
    ///
    /// 死亡ユニットには成長は発生しない（NewUnit = unit のまま全フラグ false）。
    /// </summary>
    /// <param name="unit">LH を取ったユニット（生存中であること）</param>
    /// <param name="rng">破壊判定用の乱数発生器</param>
    public static LastHitResult ExecuteLastHit(Unit unit, Random rng)
    {
        ArgumentNullException.ThrowIfNull(unit);
        ArgumentNullException.ThrowIfNull(rng);

        // 死亡ユニットには何も発生させない
        if (!unit.IsAlive)
        {
            return new LastHitResult
            {
                NewUnit = unit,
                LevelOverflow = false,
                ItemLevelUpTriggered = false,
                ItemDestroyed = false,
                GreedPointsStolen = 0,
            };
        }

        var current = unit;
        bool levelOverflow = false;
        bool itemLevelUpTriggered = false;
        bool itemDestroyed = false;
        int greedPointsStolen = 0;

        // ── 1. ユニット成長（Lv<3 で確定 +1、Lv=3 で overflow）──────────
        if (current.Level < Unit.MaxUnitLevel)
        {
            current = current.WithLevelUp(out _);
        }
        else
        {
            levelOverflow = true;
        }

        // ── 2. 装備の成長 or Lv5 破壊判定 ──────────────────────────────
        if (current.MainEquipment is { } equip)
        {
            if (equip.Level < Equipment.MaxEquipmentLevel)
            {
                // Lv1〜4: 確定 +1
                var upgraded = equip.WithLevelUp();
                current = current.WithEquipment(upgraded);
                itemLevelUpTriggered = true;
            }
            else
            {
                // Lv5 到達済み: 破壊判定 (CoinGreed / 通常装備で分岐)
                if (equip.HasGreedEffect)
                {
                    // 強欲の古銭: +1 pt 強奪 + 100% 確定破壊
                    greedPointsStolen = Lv5GreedPointsStolen;
                    current = current.WithEquipment(null);
                    itemDestroyed = true;
                }
                else
                {
                    // 通常装備: 50% コイントス破壊
                    if (rng.NextDouble() < Lv5NormalItemDestructionProbability)
                    {
                        current = current.WithEquipment(null);
                        itemDestroyed = true;
                    }
                    // 50% は生存（itemDestroyed = false のまま）
                }
            }
        }

        return new LastHitResult
        {
            NewUnit = current,
            LevelOverflow = levelOverflow,
            ItemLevelUpTriggered = itemLevelUpTriggered,
            ItemDestroyed = itemDestroyed,
            GreedPointsStolen = greedPointsStolen,
        };
    }

    // ─── 完全ロスト ────────────────────────────────────────────────────────

    /// <summary>
    /// 戦闘中に致死ダメージを受けたユニットを「完全ロスト状態」にマークする。
    /// IsDead = true + MainEquipment = null（装備も同時にロスト）。
    /// </summary>
    public static Unit ApplyLethalDamage(Unit victim)
    {
        ArgumentNullException.ThrowIfNull(victim);
        var dead = victim.MarkDeadInBattle();
        return dead with { MainEquipment = null };
    }

    // ─── 自然婚姻ポイント蓄積（AffinityMultiplier 適用） ──────────────────

    /// <summary>
    /// 同分隊で同戦闘を生き残った 2 ユニットに、相互に BattleAffinity を加算する。
    ///
    /// ★ 各ユニットの装備が持つ AffinityMultiplier (1.1〜2.25 倍) を base 値に
    ///   乗算してから蓄積する（純愛ブースト）。各ユニットの増加量は自分自身の
    ///   装備倍率に依存するため、結果は**非対称**になり得る:
    ///
    ///     A の獲得 = floor(baseAmount × A.MainEquipment?.AffinityMultiplier ?? 1.0)
    ///     B の獲得 = floor(baseAmount × B.MainEquipment?.AffinityMultiplier ?? 1.0)
    ///
    ///   テーマ的解釈: A が RingPurelove を装備していると、A の心は B に対して
    ///   速く開く（1.65 倍）が、B の心の開き方は B 自身の装備による。
    ///
    /// 両者が IsAlive でない場合は何もせず据え置く。
    /// </summary>
    /// <param name="a">蓄積対象ユニット A</param>
    /// <param name="b">蓄積対象ユニット B</param>
    /// <param name="baseAmount">乗算前の基礎蓄積量（正の整数想定）</param>
    public static (Unit, Unit) AccumulateMutualBattleAffinity(Unit a, Unit b, int baseAmount)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        if (!a.IsAlive || !b.IsAlive) return (a, b);
        if (a.Id == b.Id) return (a, b);

        // 各ユニットの装備による倍率を取得（未装備は等倍 1.0）
        var aMultiplier = a.MainEquipment?.AffinityMultiplier ?? 1.0;
        var bMultiplier = b.MainEquipment?.AffinityMultiplier ?? 1.0;

        // floor で整数化（NaturalMarriageThreshold が整数しきい値のため）
        var aGain = (int)Math.Floor(baseAmount * aMultiplier);
        var bGain = (int)Math.Floor(baseAmount * bMultiplier);

        var newA = a.WithAddedAffinity(b.Id, aGain);
        var newB = b.WithAddedAffinity(a.Id, bGain);
        return (newA, newB);
    }
}
