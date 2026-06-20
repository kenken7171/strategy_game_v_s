// =============================================================================
//  ChronicleKnights — ProphecyMaster.cs
// -----------------------------------------------------------------------------
//  予言生成の数値 SoT（Single Source of Truth）。年代記フェーズで毎年提示される
//  3 枚の予言カードを「設計された体験」として組み立てる本実装。
//
//  TimelineEngine.DefaultGenerator（最小構成の暫定品＝均等ランダム）を置き換え、
//  以下 4 つの設計意図をデータ駆動で実現する:
//
//    (1) 3 枚キュレーション   — 必ず相異なる Kind を 3 つ提示（「3 枚とも同じ」を構造排除）。
//    (2) 効果量の集中管理     — Kind × Rarity ごとの Value をテーブル化（ハードコード散乱の排除）。
//    (3) レア度（銅/銀/金）   — 基本ブロンズ・たまにシルバー・稀にゴールド。上位ほど Value が跳ねる。
//    (4) 暦・時代との連動     — 章（Epoch）とボス接近で Kind の出やすさを傾ける。
//
//  ★ 純粋・決定論:
//    Generate(turn, rng) は ProphecyGenerator デリゲートに合致する static 純粋関数。
//    Random は引数注入（開発憲法④）。同一 turn＋同一 rng 状態なら毎回同一の 3 枚。
//
//  ★ レイヤ規律（開発憲法①）:
//    ASCII 識別子のみ。日本語表示は Config/localization_ja.json（prophecies /
//    prophecyRarities セクション）が DescriptionKey / Rarity 経由で解決する。
// =============================================================================

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using ChronicleKnights.Core.Chronicle;
using ChronicleKnights.Core.Managers; // TimelineEngine（OptionsPerTurn / SkipYears 定数の SoT）

namespace ChronicleKnights.Core.Timeline;

/// <summary>
/// 予言タイムラインの本生成器（数値 SoT）。3 択の Kind 構成・レア度抽選・効果量・
/// タイムスキップ年数を集中管理する。状態を持たない static クラス。
/// </summary>
public static class ProphecyMaster
{
    // ─── レア度の出現率（基本ブロンズ / たまにシルバー / 稀にゴールド） ───────────

    /// <summary>[SoT] ゴールド（最上位）の抽選確率。</summary>
    public const double GoldChance = 0.06;

    /// <summary>[SoT] シルバー（上位）の抽選確率（ゴールドに当たらなかった残りから引く）。</summary>
    public const double SilverChance = 0.22;
    // → ブロンズは残余 = 1 - GoldChance - SilverChance ≈ 0.72。

    // ─── タイムスキップ年数（既定 2〜4 年） ─────────────────────────────────────

    /// <summary>[SoT] SkipYears の下限。</summary>
    public const int MinSkipYears = TimelineEngine.DefaultMinSkipYears;

    /// <summary>[SoT] SkipYears の上限。</summary>
    public const int MaxSkipYears = TimelineEngine.DefaultMaxSkipYears;

    // ─── 効果量テーブル（Kind × Rarity → Value） ──────────────────────────────
    //  Value の意味は Kind により異なる（Prophecy.cs 参照）。レア度が上がるほど
    //  「内容の効率が良い」よう単調増加させる。EquipmentDrop だけは Value=装備レベルなので
    //  範囲抽選（下記 EquipmentDropLevel）に委ねる。

    /// <summary>RewardPoints の獲得ポイント（Bronze / Silver / Gold）。</summary>
    public static int RewardPointsValue(ProphecyRarity rarity) => rarity switch
    {
        ProphecyRarity.Gold   => 14,
        ProphecyRarity.Silver => 7,
        _                     => 3,
    };

    /// <summary>ScoutReward の無償加入人数（Bronze / Silver / Gold）。</summary>
    public static int ScoutRewardValue(ProphecyRarity rarity) => rarity switch
    {
        ProphecyRarity.Gold   => 3,
        ProphecyRarity.Silver => 2,
        _                     => 1,
    };

    /// <summary>Rest の休息追加ポイント（Bronze / Silver / Gold）。RestService が現金化する。</summary>
    public static int RestValue(ProphecyRarity rarity) => rarity switch
    {
        ProphecyRarity.Gold   => 10,
        ProphecyRarity.Silver => 5,
        _                     => 2,
    };

    /// <summary>
    /// EquipmentDrop のドロップ装備レベル（Bronze=Lv1〜2 / Silver=Lv3 / Gold=Lv4〜5）。
    /// レベルが高いほど Affix が増え（Lv3+ で 2 個）強力。範囲は rng で散らす。
    /// </summary>
    public static int EquipmentDropLevel(ProphecyRarity rarity, Random rng)
    {
        ArgumentNullException.ThrowIfNull(rng);
        return rarity switch
        {
            ProphecyRarity.Gold   => 4 + rng.Next(0, 2), // 4〜5
            ProphecyRarity.Silver => 3,
            _                     => 1 + rng.Next(0, 2), // 1〜2
        };
    }

    // ─── 本生成（ProphecyGenerator デリゲートに合致する純粋関数） ────────────────

    /// <summary>
    /// 指定年（turn）の 3 枚の予言を組み立てる。必ず相異なる Kind を 3 つ提示し、各カードに
    /// レア度・効果量・SkipYears・表示キーを与える。<see cref="ProphecyGenerator"/> として
    /// TimelineEngine.CreateInitial / AdvanceToNextTurn に注入する。
    /// </summary>
    /// <param name="turn">暦の年（1 始まり）。章・ボス接近の判定に使う。</param>
    /// <param name="rng">決定論のため呼び出し側責任で渡す乱数。</param>
    /// <returns>長さ 3 の不変配列（Kind は相異なる）。</returns>
    public static ImmutableArray<Prophecy> Generate(int turn, Random rng)
    {
        ArgumentNullException.ThrowIfNull(rng);

        var kinds = PickDistinctKinds(turn, rng, TimelineEngine.OptionsPerTurn);

        var builder = ImmutableArray.CreateBuilder<Prophecy>(kinds.Count);
        foreach (var kind in kinds)
        {
            // 戦闘は暦が強さを決めるためレア度の概念外（常に Bronze）。
            var rarity = kind == ProphecyKind.Battle ? ProphecyRarity.Bronze : RollRarity(rng);
            var value = ValueFor(kind, rarity, turn, rng);
            var skipYears = MinSkipYears + rng.Next(0, MaxSkipYears - MinSkipYears + 1);

            builder.Add(new Prophecy
            {
                Id = Guid.NewGuid(),
                Kind = kind,
                Rarity = rarity,
                SkipYears = skipYears,
                Value = value,
                // 例: "RewardPoints.Gold" — prophecies セクションをフラットに引く意味的キー。
                DescriptionKey = $"{kind}.{rarity}",
            });
        }
        return builder.ToImmutable();
    }

    // ─── レア度抽選 ────────────────────────────────────────────────────────────

    /// <summary>基本ブロンズ・たまにシルバー・稀にゴールドを 1 枚分抽選する。</summary>
    public static ProphecyRarity RollRarity(Random rng)
    {
        ArgumentNullException.ThrowIfNull(rng);
        var roll = rng.NextDouble();
        if (roll < GoldChance) return ProphecyRarity.Gold;
        if (roll < GoldChance + SilverChance) return ProphecyRarity.Silver;
        return ProphecyRarity.Bronze;
    }

    // ─── 効果量解決 ────────────────────────────────────────────────────────────

    /// <summary>Kind × Rarity（＋年）から Value を解決する。意味は Kind 依存（Prophecy.cs 参照）。</summary>
    private static int ValueFor(ProphecyKind kind, ProphecyRarity rarity, int turn, Random rng) => kind switch
    {
        ProphecyKind.RewardPoints  => RewardPointsValue(rarity),
        ProphecyKind.ScoutReward   => ScoutRewardValue(rarity),
        ProphecyKind.EquipmentDrop => EquipmentDropLevel(rarity, rng),
        ProphecyKind.Rest          => RestValue(rarity),
        // Battle の Value は敵の強さヒント（敵本体は暦から CreateCurrentYearEnemy で合成）。
        ProphecyKind.Battle        => Math.Max(1, turn),
        _                          => 1,
    };

    // ─── 3 枚キュレーション（重み付き・非復元で相異なる Kind を選ぶ） ──────────────

    /// <summary>
    /// その年の重みに従い、相異なる Kind を <paramref name="count"/> 個選ぶ（非復元抽選）。
    /// 5 種すべてが候補なので 3 つの相異なる Kind は常に取れる（＝「3 枚同じ」を構造排除）。
    /// </summary>
    private static List<ProphecyKind> PickDistinctKinds(int turn, Random rng, int count)
    {
        var pool = new List<(ProphecyKind Kind, int Weight)>();
        foreach (var kind in Enum.GetValues<ProphecyKind>())
        {
            pool.Add((kind, KindWeight(kind, turn)));
        }

        var picked = new List<ProphecyKind>(count);
        for (var n = 0; n < count && pool.Count > 0; n++)
        {
            var total = 0;
            foreach (var e in pool) total += e.Weight;

            var threshold = rng.Next(0, total);
            var index = 0;
            var acc = 0;
            for (; index < pool.Count; index++)
            {
                acc += pool[index].Weight;
                if (threshold < acc) break;
            }
            if (index >= pool.Count) index = pool.Count - 1;

            picked.Add(pool[index].Kind);
            pool.RemoveAt(index); // 非復元（相異なる Kind を保証）
        }
        return picked;
    }

    /// <summary>
    /// Kind のその年の出やすさ（重み）。章（Epoch）とボス接近で傾ける（暦連動）。
    /// 重みは常に 1 以上（どの Kind も完全には消えない）。
    /// </summary>
    private static int KindWeight(ProphecyKind kind, int turn)
    {
        // 基準重み。
        var weight = kind switch
        {
            ProphecyKind.RewardPoints  => 3,
            ProphecyKind.Battle        => 3,
            ProphecyKind.ScoutReward   => 2,
            ProphecyKind.EquipmentDrop => 2,
            ProphecyKind.Rest          => 2,
            _                          => 1,
        };

        // 章ごとの色付け（黎明＝育成寄り → 黄昏＝戦乱寄り）。
        var epoch = ChronicleTimelineConfig.EpochForYear(turn);
        weight += (epoch.Id, kind) switch
        {
            (EpochId.Dawn,     ProphecyKind.ScoutReward)   => 1,
            (EpochId.Dawn,     ProphecyKind.RewardPoints)  => 1,
            (EpochId.Upheaval, ProphecyKind.Battle)        => 1,
            (EpochId.Decline,  ProphecyKind.Battle)        => 1,
            (EpochId.Decline,  ProphecyKind.EquipmentDrop) => 1,
            (EpochId.Twilight, ProphecyKind.Battle)        => 2,
            _                                              => 0,
        };

        // 章ボス接近年は戦乱の前兆として Battle を濃くする。
        if (kind == ProphecyKind.Battle)
        {
            var bossYear = epoch.BossYear;
            var distance = bossYear - turn;
            if (distance >= 0 && distance <= ChronicleTimelineConfig.EpochBossForewarnLeadTurns)
            {
                weight += 2;
            }
        }

        return Math.Max(1, weight);
    }
}
