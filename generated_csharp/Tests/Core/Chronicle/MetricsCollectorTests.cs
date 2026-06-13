// =============================================================================
//  ChronicleKnights.Tests — MetricsCollectorTests.cs
// -----------------------------------------------------------------------------
//  100 年マクロ統計集約 (MetricsCollector) の純粋ロジックを網羅検証する。
//
//  検証観点:
//    1. 章振り分け : 標本が年から正しい章（Epoch）へ集約される（クランプ番兵含む）。
//    2. 厳密集約   : 章別の累計（獲得・消費・黒字幅・出撃・ロスト・章ボス戦/勝利）が
//                    1 ビットの狂いもなく積算され、派生統計（平均黒字幅・生存率・投資効率・
//                    勝率・章ボス勝率）が式どおりに弾ける。
//    3. 100 年通し : 1..100 年を流し込むと各章 25 戦・全体総計が完全一致する。
//    4. 極端ケース : 全敗・全ロストでも黒字幅は負へ・生存率 0 へ正しく沈む（例外なし）。
//    5. 境界番兵   : 母数 0 の章は安全な番兵値（生存率 1.0・他 0.0）を返す。
//    6. 純粋性     : 同一標本列からは同一統計（決定論）・null は例外。
//
//  ★ テスト規約: 文字列リテラルは ASCII のみ（章キー "epoch-*" は config 由来）。
//    浮動小数の派生統計は precision 指定で比較し、整数累計は厳密一致で固定する。
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using ChronicleKnights.Core.Battle;
using ChronicleKnights.Core.Chronicle;
using Xunit;

namespace ChronicleKnights.Tests.Core.Chronicle;

public class MetricsCollectorTests
{
    private const int RatePrecision = 6;

    private static BattleMetricSample Sample(
        int year, int earned, int spent, int combatants, int lost,
        BattleOutcome outcome, bool boss = false) => new()
    {
        Year           = year,
        EarnedPoints   = earned,
        SpentPoints    = spent,
        CombatantCount = combatants,
        LostCount      = lost,
        Outcome        = outcome,
        IsEpochBoss    = boss,
    };

    private static EpochMetrics EpochOf(ChronicleMetrics metrics, EpochId id)
        => metrics.Epochs.Single(e => e.Id == id);

    // ─── 1. 章振り分け（年 → 章・クランプ番兵） ────────────────────────────

    [Theory]
    [InlineData(1, EpochId.Dawn)]
    [InlineData(25, EpochId.Dawn)]
    [InlineData(26, EpochId.Upheaval)]
    [InlineData(50, EpochId.Upheaval)]
    [InlineData(51, EpochId.Decline)]
    [InlineData(75, EpochId.Decline)]
    [InlineData(76, EpochId.Twilight)]
    [InlineData(100, EpochId.Twilight)]
    [InlineData(0, EpochId.Dawn)]       // クランプ: 0 年以下 → 黎明
    [InlineData(150, EpochId.Twilight)] // クランプ: 101 年以上 → 終焉
    public void Aggregate_AssignsSampleToEpochOfYear(int year, EpochId expected)
    {
        var metrics = MetricsCollector.Aggregate(new[]
        {
            Sample(year, 5, 0, 1, 0, BattleOutcome.BattalionVictory),
        });

        Assert.Equal(1, EpochOf(metrics, expected).BattleCount);
        foreach (var other in metrics.Epochs.Where(e => e.Id != expected))
        {
            Assert.Equal(0, other.BattleCount);
        }
    }

    // ─── 2. 厳密集約（章別の累計と派生統計が式どおり） ─────────────────────

    [Fact]
    public void Aggregate_Dawn_ComputesEveryStatExactly()
    {
        var metrics = MetricsCollector.Aggregate(new[]
        {
            Sample(10, earned: 10, spent: 4, combatants: 10, lost: 1, BattleOutcome.BattalionVictory),
            Sample(20, earned: 6,  spent: 2, combatants: 10, lost: 2, BattleOutcome.BattalionVictory),
            Sample(25, earned: 20, spent: 0, combatants: 10, lost: 3, BattleOutcome.BattalionVictory, boss: true),
        });

        var dawn = EpochOf(metrics, EpochId.Dawn);

        // 章キーと整数累計（1 ビットの狂いもなく積算）。
        Assert.Equal("epoch-dawn", dawn.EpochNameKey);
        Assert.Equal(3, dawn.BattleCount);
        Assert.Equal(3, dawn.Victories);
        Assert.Equal(36, dawn.TotalEarned);
        Assert.Equal(6, dawn.TotalSpent);
        Assert.Equal(30, dawn.NetSurplus);       // 36 - 6
        Assert.Equal(30, dawn.TotalCombatants);  // 10*3
        Assert.Equal(6, dawn.TotalLost);         // 1+2+3
        Assert.Equal(1, dawn.BossBattles);
        Assert.Equal(1, dawn.BossVictories);

        // 派生統計（浮動小数は precision 比較）。
        Assert.Equal(10.0, dawn.AverageNetSurplus, RatePrecision);     // 30 / 3
        Assert.Equal(0.8, dawn.PopulationSurvivalRate, RatePrecision); // (30-6)/30
        Assert.Equal(6.0, dawn.InvestmentEfficiency, RatePrecision);   // 36 / 6
        Assert.Equal(1.0, dawn.VictoryRate, RatePrecision);            // 3 / 3
        Assert.Equal(1.0, dawn.BossVictoryRate, RatePrecision);        // 1 / 1
    }

    [Fact]
    public void Aggregate_MixedVictoriesAndDefeats_ComputesVictoryRate()
    {
        var metrics = MetricsCollector.Aggregate(new[]
        {
            Sample(30, 5, 1, 9, 0, BattleOutcome.BattalionVictory),
            Sample(35, 0, 2, 9, 4, BattleOutcome.BattalionDefeat),
            Sample(40, 5, 1, 9, 0, BattleOutcome.BattalionVictory),
            Sample(45, 0, 0, 9, 9, BattleOutcome.Ongoing),
        });

        var upheaval = EpochOf(metrics, EpochId.Upheaval);
        Assert.Equal(4, upheaval.BattleCount);
        Assert.Equal(2, upheaval.Victories);             // 勝利のみ計上（Defeat/Ongoing は非勝利）
        Assert.Equal(0.5, upheaval.VictoryRate, RatePrecision);
    }

    // ─── 3. 100 年通し（各章 25 戦・全体総計の完全一致） ──────────────────

    [Fact]
    public void Aggregate_FullCentury_AccumulatesGrandTotalsAndPerEpochCounts()
    {
        var samples = new List<BattleMetricSample>();
        for (var year = 1; year <= 100; year++)
        {
            var boss = ChronicleTimelineConfig.IsEpochBossYear(year);
            samples.Add(Sample(year, earned: 5, spent: 2, combatants: 9, lost: 1,
                BattleOutcome.BattalionVictory, boss));
        }

        var metrics = MetricsCollector.Aggregate(samples);

        Assert.Equal(100, metrics.TotalBattles);
        Assert.Equal(100, metrics.TotalVictories);
        Assert.Equal(500, metrics.TotalEarned);     // 5 * 100
        Assert.Equal(200, metrics.TotalSpent);      // 2 * 100
        Assert.Equal(300, metrics.NetSurplus);
        Assert.Equal(900, metrics.TotalCombatants); // 9 * 100
        Assert.Equal(100, metrics.TotalLost);       // 1 * 100
        Assert.Equal(4, metrics.TotalBossBattles);  // 25/50/75/100
        Assert.Equal(4, metrics.TotalBossVictories);

        // 章は 4 つ・各章ちょうど 25 戦。
        Assert.Equal(4, metrics.Epochs.Length);
        foreach (var epoch in metrics.Epochs)
        {
            Assert.Equal(25, epoch.BattleCount);
            Assert.Equal(1, epoch.BossBattles);
        }

        // 全体派生統計。
        Assert.Equal(1.0, metrics.OverallVictoryRate, RatePrecision);
        Assert.Equal(2.5, metrics.OverallInvestmentEfficiency, RatePrecision); // 500 / 200
    }

    // ─── 4. 極端ケース（全敗・全ロスト） ───────────────────────────────────

    [Fact]
    public void Aggregate_CatastrophicLosses_YieldNegativeSurplusAndZeroSurvival()
    {
        var metrics = MetricsCollector.Aggregate(new[]
        {
            Sample(80, earned: 0, spent: 10, combatants: 9, lost: 9, BattleOutcome.BattalionDefeat),
            Sample(90, earned: 0, spent: 8,  combatants: 9, lost: 9, BattleOutcome.BattalionDefeat),
        });

        var twilight = EpochOf(metrics, EpochId.Twilight);
        Assert.Equal(2, twilight.BattleCount);
        Assert.Equal(0, twilight.Victories);
        Assert.Equal(-18, twilight.NetSurplus);                          // 0 - 18
        Assert.Equal(0.0, twilight.PopulationSurvivalRate, RatePrecision); // 18/18 ロスト
        Assert.Equal(0.0, twilight.VictoryRate, RatePrecision);
        Assert.Equal(0.0, twilight.InvestmentEfficiency, RatePrecision);   // 獲得 0
        Assert.Equal(-18, metrics.NetSurplus);
    }

    // ─── 5. 境界番兵（母数 0 の章） ────────────────────────────────────────

    [Fact]
    public void Aggregate_NoSamples_YieldsFourZeroEpochsWithSafeSentinels()
    {
        var metrics = MetricsCollector.Aggregate(Array.Empty<BattleMetricSample>());

        Assert.Equal(4, metrics.Epochs.Length);
        Assert.Equal(0, metrics.TotalBattles);
        Assert.Equal(0, metrics.NetSurplus);
        Assert.Equal(1.0, metrics.OverallSurvivalRate, RatePrecision); // 母数 0 → 完全生存
        Assert.Equal(0.0, metrics.OverallInvestmentEfficiency, RatePrecision);
        Assert.Equal(0.0, metrics.OverallVictoryRate, RatePrecision);

        var dawn = EpochOf(metrics, EpochId.Dawn);
        Assert.Equal(0, dawn.BattleCount);
        Assert.Equal(0.0, dawn.AverageNetSurplus, RatePrecision);
        Assert.Equal(1.0, dawn.PopulationSurvivalRate, RatePrecision);
        Assert.Equal(0.0, dawn.InvestmentEfficiency, RatePrecision);
        Assert.Equal(0.0, dawn.VictoryRate, RatePrecision);
        Assert.Equal(0.0, dawn.BossVictoryRate, RatePrecision);
    }

    [Fact]
    public void Aggregate_EpochsAreInChronologicalOrder()
    {
        var metrics = MetricsCollector.Aggregate(Array.Empty<BattleMetricSample>());

        Assert.Equal(
            new[] { EpochId.Dawn, EpochId.Upheaval, EpochId.Decline, EpochId.Twilight },
            metrics.Epochs.Select(e => e.Id));
    }

    // ─── 6. 純粋性（決定論・null 契約・要素 null スキップ） ─────────────────

    [Fact]
    public void Aggregate_IsDeterministic_SameSamplesSameTotals()
    {
        var samples = new[]
        {
            Sample(10, 10, 4, 10, 1, BattleOutcome.BattalionVictory),
            Sample(60, 3, 1, 9, 0, BattleOutcome.BattalionVictory, boss: false),
        };

        var first  = MetricsCollector.Aggregate(samples);
        var second = MetricsCollector.Aggregate(samples);

        Assert.Equal(first.TotalEarned, second.TotalEarned);
        Assert.Equal(first.NetSurplus, second.NetSurplus);
        Assert.Equal(EpochOf(first, EpochId.Dawn).TotalEarned, EpochOf(second, EpochId.Dawn).TotalEarned);
        Assert.Equal(EpochOf(first, EpochId.Decline).TotalEarned, EpochOf(second, EpochId.Decline).TotalEarned);
    }

    [Fact]
    public void Aggregate_NullSamples_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => MetricsCollector.Aggregate(null!));
    }

    [Fact]
    public void Aggregate_SkipsNullElements_WithoutThrowing()
    {
        var samples = new[]
        {
            Sample(10, 5, 1, 9, 0, BattleOutcome.BattalionVictory),
            null!,
            Sample(15, 5, 1, 9, 0, BattleOutcome.BattalionVictory),
        };

        var metrics = MetricsCollector.Aggregate(samples);

        Assert.Equal(2, metrics.TotalBattles); // null 標本は読み飛ばされる
        Assert.Equal(10, metrics.TotalEarned);
    }
}
