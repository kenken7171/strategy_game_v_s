// =============================================================================
//  ChronicleKnights.Tests — UniverseEvaluatorTests.cs
// -----------------------------------------------------------------------------
//  多重宇宙モンテカルロ集約 (UniverseEvaluator) の純粋ロジックを網羅検証する。
//
//  検証観点:
//    1. 絶滅率   : 100 年に届かなかった（戦闘数が満たない）宇宙の割合が正しく弾ける。
//    2. プール集計: 章別の平均勝率・生存率・宇宙あたり平均純増・章ボス突破率が宇宙横断で
//                  1 ビットの狂いもなく集約される。
//    3. 決定論   : 固定シード群で擬似的な 100 年プレイを連続直列実行し、何回走らせても
//                  同一の統計値へ収束・一致する（統計の決定論的再現性）。
//    4. 契約     : null は例外、空入力は宇宙 0、要素 null はスキップ、章は時系列順。
//
//  ★ テスト規約: 文字列リテラルは ASCII のみ。浮動小数は precision 比較、整数累計は厳密一致。
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using ChronicleKnights.Core.Battle;
using ChronicleKnights.Core.Chronicle;
using Xunit;

namespace ChronicleKnights.Tests.Core.Chronicle;

public class UniverseEvaluatorTests
{
    private const int RatePrecision = 6;

    /// <summary>勝利のみで years 年を完走（years &lt; 100 なら絶滅扱い）する 1 宇宙を組む。</summary>
    private static ChronicleMetrics BuildUniverse(
        int years, int earned = 5, int spent = 2, int combatants = 9, int lost = 1)
    {
        var samples = new List<BattleMetricSample>();
        for (var year = 1; year <= years; year++)
        {
            samples.Add(new BattleMetricSample
            {
                Year           = year,
                EarnedPoints   = earned,
                SpentPoints    = spent,
                CombatantCount = combatants,
                LostCount      = lost,
                Outcome        = BattleOutcome.BattalionVictory,
                IsEpochBoss    = ChronicleTimelineConfig.IsEpochBossYear(year),
            });
        }

        return MetricsCollector.Aggregate(samples);
    }

    /// <summary>シードから決定論的に 1 宇宙を生成（seed%5==0 は 30 年で絶滅）。</summary>
    private static ChronicleMetrics GenerateUniverse(int seed)
    {
        var rng = new Random(seed);
        var years = seed % 5 == 0 ? 30 : 100;

        var samples = new List<BattleMetricSample>();
        for (var year = 1; year <= years; year++)
        {
            var victory = rng.NextDouble() < 0.8;
            var earned = victory ? 5 + rng.Next(0, 6) : 0;
            var lost = rng.Next(0, 3);
            samples.Add(new BattleMetricSample
            {
                Year           = year,
                EarnedPoints   = earned,
                SpentPoints    = 2,
                CombatantCount = 9,
                LostCount      = lost,
                Outcome        = victory ? BattleOutcome.BattalionVictory : BattleOutcome.BattalionDefeat,
                IsEpochBoss    = ChronicleTimelineConfig.IsEpochBossYear(year),
            });
        }

        return MetricsCollector.Aggregate(samples);
    }

    private static UniverseEpochMetrics EpochOf(UniverseMetrics metrics, EpochId id)
        => metrics.Epochs.Single(e => e.Id == id);

    // ─── 1. 絶滅率（100 年に届かなかった宇宙の割合） ───────────────────────

    [Fact]
    public void Evaluate_ExtinctionRate_CountsUniversesThatDidNotCompleteCentury()
    {
        var universes = new[]
        {
            BuildUniverse(100), // 完走
            BuildUniverse(100), // 完走
            BuildUniverse(40),  // 40 年で絶滅
            BuildUniverse(10),  // 10 年で絶滅
        };

        var metrics = UniverseEvaluator.Evaluate(universes);

        Assert.Equal(4, metrics.UniverseCount);
        Assert.Equal(2, metrics.ExtinctCount);
        Assert.Equal(0.5, metrics.ExtinctionRate, RatePrecision);
    }

    // ─── 2. プール集計（章別の平均が宇宙横断で式どおり） ───────────────────

    [Fact]
    public void Evaluate_PerEpoch_PoolsAndAveragesAcrossUniverses()
    {
        var universes = new[] { BuildUniverse(100), BuildUniverse(100) };

        var metrics = UniverseEvaluator.Evaluate(universes);
        Assert.Equal(0.0, metrics.ExtinctionRate, RatePrecision);
        Assert.Equal(4, metrics.Epochs.Length);

        var dawn = EpochOf(metrics, EpochId.Dawn);
        Assert.Equal("epoch-dawn", dawn.EpochNameKey);
        Assert.Equal(2, dawn.UniverseCount);

        // 生の合計（25 戦/宇宙 × 2 宇宙）。
        Assert.Equal(50, dawn.BattleCount);
        Assert.Equal(50, dawn.Victories);
        Assert.Equal(250, dawn.TotalEarned);     // 5 * 25 * 2
        Assert.Equal(100, dawn.TotalSpent);      // 2 * 25 * 2
        Assert.Equal(150, dawn.NetSurplus);
        Assert.Equal(450, dawn.TotalCombatants); // 9 * 25 * 2
        Assert.Equal(50, dawn.TotalLost);        // 1 * 25 * 2
        Assert.Equal(2, dawn.BossBattles);       // year25 ボス × 2 宇宙
        Assert.Equal(2, dawn.BossVictories);

        // 派生統計。
        Assert.Equal(1.0, dawn.AvgWinrate, RatePrecision);             // 50/50
        Assert.Equal(400.0 / 450.0, dawn.AvgSurvival, RatePrecision);  // (450-50)/450
        Assert.Equal(125.0, dawn.AvgEarned, RatePrecision);            // 250/2
        Assert.Equal(50.0, dawn.AvgSpent, RatePrecision);              // 100/2
        Assert.Equal(75.0, dawn.AvgNet, RatePrecision);                // 150/2
        Assert.Equal(1.0, dawn.BossClearRate, RatePrecision);          // 2/2
    }

    // ─── 3. 決定論（多重宇宙モンテカルロの再現性） ─────────────────────────

    [Fact]
    public void Evaluate_MonteCarlo_ConvergesToIdenticalStatistics_AcrossRuns()
    {
        var firstRun = UniverseEvaluator.Evaluate(Enumerable.Range(1, 50).Select(GenerateUniverse));
        var secondRun = UniverseEvaluator.Evaluate(Enumerable.Range(1, 50).Select(GenerateUniverse));

        // 絶滅は seed%5==0 の 10 宇宙（5,10,...,50）＝ Random アルゴリズムに依存しない不変量。
        Assert.Equal(50, firstRun.UniverseCount);
        Assert.Equal(10, firstRun.ExtinctCount);
        Assert.Equal(0.2, firstRun.ExtinctionRate, RatePrecision);

        // 何回走らせても 1 ビットの狂いもなく同一統計へ収束する。
        Assert.Equal(firstRun.UniverseCount, secondRun.UniverseCount);
        Assert.Equal(firstRun.ExtinctCount, secondRun.ExtinctCount);
        Assert.Equal(firstRun.TotalBattles, secondRun.TotalBattles);
        Assert.Equal(firstRun.TotalVictories, secondRun.TotalVictories);
        Assert.Equal(firstRun.TotalEarned, secondRun.TotalEarned);
        Assert.Equal(firstRun.TotalSpent, secondRun.TotalSpent);
        Assert.Equal(firstRun.NetSurplus, secondRun.NetSurplus);
        Assert.Equal(firstRun.ExtinctionRate, secondRun.ExtinctionRate, 12);
        Assert.Equal(firstRun.OverallWinrate, secondRun.OverallWinrate, 12);

        for (var i = 0; i < firstRun.Epochs.Length; i++)
        {
            Assert.Equal(firstRun.Epochs[i].AvgWinrate, secondRun.Epochs[i].AvgWinrate, 12);
            Assert.Equal(firstRun.Epochs[i].AvgNet, secondRun.Epochs[i].AvgNet, 12);
            Assert.Equal(firstRun.Epochs[i].AvgSurvival, secondRun.Epochs[i].AvgSurvival, 12);
            Assert.Equal(firstRun.Epochs[i].BossClearRate, secondRun.Epochs[i].BossClearRate, 12);
        }
    }

    // ─── 4. 契約（null・空・要素 null・章順） ──────────────────────────────

    [Fact]
    public void Evaluate_Null_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => UniverseEvaluator.Evaluate(null!));
    }

    [Fact]
    public void Evaluate_Empty_YieldsZeroUniversesWithFourEpochs()
    {
        var metrics = UniverseEvaluator.Evaluate(Array.Empty<ChronicleMetrics>());

        Assert.Equal(0, metrics.UniverseCount);
        Assert.Equal(0, metrics.ExtinctCount);
        Assert.Equal(0.0, metrics.ExtinctionRate, RatePrecision);
        Assert.Equal(4, metrics.Epochs.Length);
    }

    [Fact]
    public void Evaluate_SkipsNullUniverses_WithoutThrowing()
    {
        var universes = new[] { BuildUniverse(100), null!, BuildUniverse(100) };

        var metrics = UniverseEvaluator.Evaluate(universes);

        Assert.Equal(2, metrics.UniverseCount);
    }

    [Fact]
    public void Evaluate_EpochsAreInChronologicalOrder()
    {
        var metrics = UniverseEvaluator.Evaluate(Array.Empty<ChronicleMetrics>());

        Assert.Equal(
            new[] { EpochId.Dawn, EpochId.Upheaval, EpochId.Decline, EpochId.Twilight },
            metrics.Epochs.Select(e => e.Id));
    }
}
