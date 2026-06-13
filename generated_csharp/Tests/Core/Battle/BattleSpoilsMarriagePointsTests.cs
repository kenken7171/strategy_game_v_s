// =============================================================================
//  ChronicleKnights.Tests — BattleSpoilsMarriagePointsTests.cs
// -----------------------------------------------------------------------------
//  「戦果 → 経済」資源ループの純粋層を網羅検証する。
//
//  対象:
//    1. BattleSpoils.CalculateEarnedMarriagePoints()
//         戦果決算から獲得婚姻ポイントを決定論的に弾き出す純粋写像。
//         勝利時のみ加点し、完全ロストは減点、最終値は 0 で底打ち。
//    2. 資源ループの合成（戦果 → ポイント → PointsEconomy.EarnDirect）
//         算出ポイントが経済 SoT へ「不変的に合算」されること、空コンテキスト
//         （獲得 0）の番兵ガードが経済を 1 ミリも触らず例外も投げないこと。
//
//  ★ 算出式（BattleSpoils 側の private const と一致させる。本テストは式で再現）:
//      勝利時: Max(0, 5 + 昇級数×2 + 装備進化数×1 - 完全ロスト数×3)
//      敗北・未決着: 常に 0
//
//  ★ ChronicleGlobal.ApplyBattleSpoils（Autoload・Godot.Node 継承）はテスト
//    プロジェクトの取り込み対象外（../Core の外）のため直接は検証できない。
//    本テストは ApplyBattleSpoils が内部で行う純粋合成
//    「earned = spoils.CalculateEarnedMarriagePoints(); economy.EarnDirect(earned)」
//    を Core 層だけで等価再現し、資源ループの不変合算と番兵スキップを封じる。
//
//  ★ 開発憲法 ①（日本語直接書き込み禁止）順守: 文字列リテラルは ASCII のみ。
// =============================================================================

using System;
using System.Collections.Immutable;
using ChronicleKnights.Core.Battle;
using ChronicleKnights.Core.Managers;
using ChronicleKnights.Core.Units;
using Xunit;

namespace ChronicleKnights.Tests.Core.Battle;

public class BattleSpoilsMarriagePointsTests
{
    // ─── テスト用ファクトリ（戦果の件数だけを制御して決算を組む） ─────────────

    private static ImmutableArray<UnitLevelGain> BuildLevelGains(int count)
    {
        var builder = ImmutableArray.CreateBuilder<UnitLevelGain>();
        for (var i = 0; i < count; i++)
        {
            builder.Add(new UnitLevelGain
            {
                UnitId    = Guid.NewGuid(),
                FromLevel = 1,
                ToLevel   = 2,
            });
        }
        return builder.ToImmutable();
    }

    private static ImmutableArray<EquipmentEvolution> BuildEvolutions(int count)
    {
        var builder = ImmutableArray.CreateBuilder<EquipmentEvolution>();
        for (var i = 0; i < count; i++)
        {
            builder.Add(new EquipmentEvolution
            {
                UnitId    = Guid.NewGuid(),
                ItemId    = ItemId.SwordKnight,
                FromLevel = 1,
                ToLevel   = 2,
            });
        }
        return builder.ToImmutable();
    }

    private static ImmutableArray<Guid> BuildLostIds(int count)
    {
        var builder = ImmutableArray.CreateBuilder<Guid>();
        for (var i = 0; i < count; i++) builder.Add(Guid.NewGuid());
        return builder.ToImmutable();
    }

    /// <summary>件数だけを制御した戦果決算を組む（装備喪失はポイント無関係なので常に空）。</summary>
    private static BattleSpoils MakeSpoils(
        BattleOutcome outcome, int levelGains, int evolutions, int losses) => new()
    {
        Outcome                = outcome,
        UnitLevelGains         = BuildLevelGains(levelGains),
        PermanentlyLostUnitIds = BuildLostIds(losses),
        EquipmentEvolutions    = BuildEvolutions(evolutions),
        EquipmentLosses        = ImmutableArray<EquipmentLoss>.Empty,
    };

    // ════════════════════════════════════════════════════════════════════════
    //  1. 純粋写像 — 勝利時の加点・減点・底打ちが式どおり弾ける
    // ════════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(0, 0, 0, 5)]   // 基礎報酬のみ
    [InlineData(3, 0, 0, 11)]  // 5 + 3*2
    [InlineData(0, 2, 0, 7)]   // 5 + 2*1
    [InlineData(0, 0, 1, 2)]   // 5 - 1*3
    [InlineData(2, 1, 1, 7)]   // 5 + 2*2 + 1*1 - 1*3
    [InlineData(1, 1, 2, 2)]   // 5 + 1*2 + 1*1 - 2*3
    [InlineData(0, 0, 4, 0)]   // 5 - 4*3 = -7 -> Max(0,...) = 0（底打ち）
    public void CalculateEarnedMarriagePoints_Victory_FollowsRewardFormula(
        int levelGains, int evolutions, int losses, int expected)
    {
        var spoils = MakeSpoils(BattleOutcome.BattalionVictory, levelGains, evolutions, losses);

        Assert.Equal(expected, spoils.CalculateEarnedMarriagePoints());
    }

    [Fact]
    public void CalculateEarnedMarriagePoints_NeverNegative_EvenWithCatastrophicLosses()
    {
        // 勝利したが大量の完全ロスト。減点が報酬を大きく上回っても負へは沈まない。
        var spoils = MakeSpoils(BattleOutcome.BattalionVictory, levelGains: 0, evolutions: 0, losses: 99);

        Assert.Equal(0, spoils.CalculateEarnedMarriagePoints());
    }

    // ─── 2. 勝利以外は戦果が豊富でも常にゼロ ───────────────────────────────

    [Theory]
    [InlineData(BattleOutcome.BattalionDefeat)]
    [InlineData(BattleOutcome.Ongoing)]
    public void CalculateEarnedMarriagePoints_NonVictory_EarnsZero_EvenWithRichSpoils(
        BattleOutcome outcome)
    {
        // 勝利でなければ昇級・進化があっても褒賞は立たない（婚姻は勝利の褒賞）。
        var spoils = MakeSpoils(outcome, levelGains: 3, evolutions: 2, losses: 0);

        Assert.Equal(0, spoils.CalculateEarnedMarriagePoints());
    }

    [Fact]
    public void CalculateEarnedMarriagePoints_Empty_EarnsZero()
    {
        // 既定の空決算（Ongoing・戦果なし）は番兵として安全にゼロ。
        Assert.Equal(0, BattleSpoils.Empty.CalculateEarnedMarriagePoints());
    }

    // ─── 3. 純粋性（決定論・無副作用） ─────────────────────────────────────

    [Fact]
    public void CalculateEarnedMarriagePoints_IsDeterministic_AndDoesNotMutate()
    {
        var spoils = MakeSpoils(BattleOutcome.BattalionVictory, levelGains: 2, evolutions: 1, losses: 1);

        var first  = spoils.CalculateEarnedMarriagePoints();
        var second = spoils.CalculateEarnedMarriagePoints();

        Assert.Equal(first, second); // 同じ入力からは何度呼んでも同じ点数
        Assert.Equal(7, first);

        // 算出後も戦果レコードは 1 ミリも変わらない（件数が保たれる）。
        Assert.Equal(2, spoils.UnitLevelGains.Length);
        Assert.Equal(1, spoils.EquipmentEvolutions.Length);
        Assert.Single(spoils.PermanentlyLostUnitIds);
        Assert.Equal(BattleOutcome.BattalionVictory, spoils.Outcome);
    }

    // ════════════════════════════════════════════════════════════════════════
    //  3.5 算出内訳（式の見える化）— DescribeMarriagePoints が UI へ渡す派生射影
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public void DescribeMarriagePoints_Victory_ExposesEveryComponent()
    {
        // 勝利・昇級2・進化1・ロスト1。内訳の各小計とレートが式どおり展開される。
        var spoils    = MakeSpoils(BattleOutcome.BattalionVictory, levelGains: 2, evolutions: 1, losses: 1);
        var breakdown = spoils.DescribeMarriagePoints();

        Assert.True(breakdown.IsVictory);
        Assert.Equal(5, breakdown.VictoryBase);

        Assert.Equal(2, breakdown.LevelGainCount);
        Assert.Equal(2, breakdown.LevelGainRate);
        Assert.Equal(4, breakdown.LevelGainBonus);   // 2 * 2

        Assert.Equal(1, breakdown.EvolutionCount);
        Assert.Equal(1, breakdown.EvolutionRate);
        Assert.Equal(1, breakdown.EvolutionBonus);   // 1 * 1

        Assert.Equal(1, breakdown.LossCount);
        Assert.Equal(3, breakdown.LossRate);
        Assert.Equal(3, breakdown.LossPenalty);      // 1 * 3

        Assert.Equal(10, breakdown.Gross);           // 5 + 4 + 1
        Assert.Equal(7, breakdown.Total);            // 10 - 3
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(3, 0, 0)]
    [InlineData(0, 2, 0)]
    [InlineData(0, 0, 1)]
    [InlineData(2, 1, 1)]
    [InlineData(1, 1, 2)]
    [InlineData(0, 0, 4)]   // 底打ちケース（Gross < Penalty）
    [InlineData(0, 0, 99)]  // 破滅的損失でも Total は CalculateEarnedMarriagePoints と一致
    public void DescribeMarriagePoints_Total_AlwaysEquals_CalculateEarnedMarriagePoints(
        int levelGains, int evolutions, int losses)
    {
        var spoils = MakeSpoils(BattleOutcome.BattalionVictory, levelGains, evolutions, losses);

        // 内訳の Total と純粋写像の戻り値は常に 1 ビットも違わない（式の単一の真実）。
        Assert.Equal(spoils.CalculateEarnedMarriagePoints(), spoils.DescribeMarriagePoints().Total);
    }

    [Theory]
    [InlineData(BattleOutcome.BattalionDefeat)]
    [InlineData(BattleOutcome.Ongoing)]
    public void DescribeMarriagePoints_NonVictory_AllComponentsZero_ButRatesPreserved(
        BattleOutcome outcome)
    {
        // 勝利でなければ件数・基礎・小計はすべて 0（Total も 0）。
        var breakdown = MakeSpoils(outcome, levelGains: 3, evolutions: 2, losses: 1).DescribeMarriagePoints();

        Assert.False(breakdown.IsVictory);
        Assert.Equal(0, breakdown.VictoryBase);
        Assert.Equal(0, breakdown.LevelGainCount);
        Assert.Equal(0, breakdown.EvolutionCount);
        Assert.Equal(0, breakdown.LossCount);
        Assert.Equal(0, breakdown.Gross);
        Assert.Equal(0, breakdown.Total);

        // ただしレートは「式の見える化」のため参照値として保たれる（UI が ×n を出せる）。
        Assert.Equal(2, breakdown.LevelGainRate);
        Assert.Equal(1, breakdown.EvolutionRate);
        Assert.Equal(3, breakdown.LossRate);
    }

    [Fact]
    public void DescribeMarriagePoints_FlooredAtZero_WhenPenaltyExceedsGross()
    {
        // 勝利・損失4。Gross 5 < Penalty 12 でも Total は 0 で底打ち（負へ沈まない）。
        var breakdown = MakeSpoils(BattleOutcome.BattalionVictory, 0, 0, 4).DescribeMarriagePoints();

        Assert.Equal(5, breakdown.Gross);
        Assert.Equal(12, breakdown.LossPenalty);
        Assert.Equal(0, breakdown.Total);
    }

    // ════════════════════════════════════════════════════════════════════════
    //  4. 資源ループの合成 — 戦果 → ポイント → 経済 SoT の不変合算
    //     （ApplyBattleSpoils が内部で行う純粋合成を Core 層だけで等価再現）
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public void ResourceLoop_VictorySpoils_SumIntoEconomy_Immutably()
    {
        var spoils  = MakeSpoils(BattleOutcome.BattalionVictory, levelGains: 2, evolutions: 1, losses: 0);
        var earned  = spoils.CalculateEarnedMarriagePoints(); // 5 + 4 + 1 = 10
        Assert.Equal(10, earned);

        var before = PointsEconomy.CreateInitial();
        var after  = before.EarnDirect(earned);

        // 経済 SoT へ不変的に合算される（残高・累計が earned ぶん増える）。
        Assert.Equal(10, after.CurrentBalance);
        Assert.Equal(10, after.TotalEarned);
        Assert.Equal(0, after.TotalSpent);

        // 元インスタンスは一切変化しない（完全イミュータブル）。
        Assert.Equal(0, before.CurrentBalance);
        Assert.Equal(0, before.TotalEarned);
    }

    [Fact]
    public void ResourceLoop_AddsOnTopOfExistingBalance_WithoutLosingHistory()
    {
        // 既に残高のある経済へ戦果ポイントを重ねても、過去の累計を壊さず合算する。
        var spoils = MakeSpoils(BattleOutcome.BattalionVictory, levelGains: 0, evolutions: 0, losses: 0); // 5
        var earned = spoils.CalculateEarnedMarriagePoints();

        var seeded = PointsEconomy.CreateInitial().EarnFromTimeSkip(4); // 残高 4
        var after  = seeded.EarnDirect(earned);                          // +5

        Assert.Equal(9, after.CurrentBalance);
        Assert.Equal(9, after.TotalEarned);
    }

    [Fact]
    public void ResourceLoop_Accumulates_AcrossConsecutiveVictories()
    {
        // 連戦の戦果を順に決算すると、経済は環として点数を積み増していく。
        var firstEarned  = MakeSpoils(BattleOutcome.BattalionVictory, 1, 0, 0).CalculateEarnedMarriagePoints(); // 7
        var secondEarned = MakeSpoils(BattleOutcome.BattalionVictory, 0, 2, 0).CalculateEarnedMarriagePoints(); // 7

        var economy = PointsEconomy.CreateInitial()
            .EarnDirect(firstEarned)
            .EarnDirect(secondEarned);

        Assert.Equal(firstEarned + secondEarned, economy.CurrentBalance);
        Assert.Equal(14, economy.CurrentBalance);
        Assert.Equal(14, economy.TotalEarned);
    }

    // ─── 5. 番兵ガード — 戦果なし / 勝利以外は経済を触らず例外も投げない ─────

    [Fact]
    public void ResourceLoop_EmptySpoils_LeavesEconomyUntouched_NoException()
    {
        // ApplyBattleSpoils の番兵（earned <= 0 なら EarnDirect を呼ばずスキップ）を等価再現。
        var earned = BattleSpoils.Empty.CalculateEarnedMarriagePoints();
        Assert.Equal(0, earned);

        var before = PointsEconomy.CreateInitial().EarnFromTimeSkip(7); // 残高 7
        var after  = earned > 0 ? before.EarnDirect(earned) : before;   // スキップ経路

        // 番兵により経済 SoT は寸分も変わらない（値等価で同一）。
        Assert.Equal(before, after);
        Assert.Equal(7, after.CurrentBalance);
    }

    [Fact]
    public void ResourceLoop_DefeatSpoils_LeavesEconomyUntouched()
    {
        // 敗北は戦果が豊富でも獲得 0 → 経済は据え置き。
        var earned = MakeSpoils(BattleOutcome.BattalionDefeat, 5, 3, 0).CalculateEarnedMarriagePoints();
        Assert.Equal(0, earned);

        var before = PointsEconomy.CreateInitial().EarnDirect(12);
        var after  = earned > 0 ? before.EarnDirect(earned) : before;

        Assert.Equal(before, after);
        Assert.Equal(12, after.CurrentBalance);
    }

    [Fact]
    public void ResourceLoop_ZeroEarned_IsSafeForEarnDirect_NoThrow()
    {
        // earned が 0 でも EarnDirect(0) 自体は例外を投げず据え置きを返す（番兵の二重防壁）。
        var economy = PointsEconomy.CreateInitial().EarnDirect(3);

        var result = economy.EarnDirect(0);

        Assert.Equal(3, result.CurrentBalance);
        Assert.Equal(3, result.TotalEarned);
    }
}
