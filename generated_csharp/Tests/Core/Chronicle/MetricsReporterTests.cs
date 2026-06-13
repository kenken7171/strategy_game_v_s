// =============================================================================
//  ChronicleKnights.Tests — MetricsReporterTests.cs
// -----------------------------------------------------------------------------
//  実走ログ逆シリアル化 (MetricsReporter) と「ログ → 統計集約」一気通貫の結合検証。
//
//  検証観点:
//    1. 可逆往復 : FormatSample が吐いた文字列をパーサに流すと、完全に一致する
//                  BattleMetricSample が復元される（勝利/敗北）。
//    2. 頑健性   : 破損 JSON・空行・無関係なデバッグ出力・他イベント（battle-start/
//                  battle-settlement）・Object でない JSON・必須欠落が混ざっても例外を
//                  投げず、有効な battle-sample 行だけを安全に抽出する。
//    3. 一気通貫 : 復元したサンプルを MetricsCollector.Aggregate に流すと、元サンプルの
//                  直接集約と各章の黒字幅・勝率・生存率が 1 ビットの狂いもなく一致する。
//    4. 契約     : null は例外、空入力は空配列。
//
//  ★ テスト規約: 文字列リテラルは ASCII のみ。
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using ChronicleKnights.Core.Battle;
using ChronicleKnights.Core.Chronicle;
using Xunit;

namespace ChronicleKnights.Tests.Core.Chronicle;

public class MetricsReporterTests
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

    // ─── 1. 可逆往復（FormatSample → ParseLogLines） ───────────────────────

    [Theory]
    [InlineData(BattleOutcome.BattalionVictory)]
    [InlineData(BattleOutcome.BattalionDefeat)]
    public void RoundTrip_FormatThenParse_RestoresIdenticalSample(BattleOutcome outcome)
    {
        var original = Sample(50, earned: 7, spent: 3, combatants: 9, lost: 1, outcome, boss: true);

        var line = MetricsLogFormatter.FormatSample(original);
        var parsed = MetricsReporter.ParseLogLines(new[] { line });

        Assert.Single(parsed);
        Assert.Equal(original, parsed[0]); // record 値等価＝全 7 フィールド一致。
    }

    // ─── 2. 頑健性（破損・無関係・他イベントを安全にスキップ） ─────────────

    [Fact]
    public void ParseLogLines_SkipsCorruptBlankAndUnrelatedLines_WithoutThrowing()
    {
        var valid = MetricsLogFormatter.FormatSample(
            Sample(10, 5, 1, 9, 0, BattleOutcome.BattalionVictory));

        var lines = new[]
        {
            string.Empty,
            "   ",
            "Godot engine: random debug output without prefix",
            "[metrics] {this is not valid json",
            "[metrics] {\"event\":\"battle-start\",\"year\":10,\"enemy\":\"trial-guardian\","
                + "\"hp\":1,\"atk\":1,\"spd\":1}",
            "[metrics] {\"event\":\"battle-settlement\",\"year\":10,\"victory\":true,\"base\":5,"
                + "\"levelgain_bonus\":0,\"evolution_bonus\":0,\"loss_penalty\":0,"
                + "\"earned_total\":5,\"balance_after\":5}",
            "[metrics] [1,2,3]",
            "[metrics] {\"event\":\"battle-sample\",\"year\":10}", // 必須フィールド欠落
            valid,                                                  // 唯一の有効な battle-sample
        };

        var parsed = MetricsReporter.ParseLogLines(lines);

        Assert.Single(parsed);
        Assert.Equal(10, parsed[0].Year);
        Assert.Equal(5, parsed[0].EarnedPoints);
    }

    [Fact]
    public void ParseLogLines_EmptyInput_ReturnsEmpty()
    {
        Assert.Empty(MetricsReporter.ParseLogLines(Array.Empty<string>()));
    }

    [Fact]
    public void ParseLogLines_Null_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => MetricsReporter.ParseLogLines(null!));
    }

    // ─── 3. 一気通貫（ログ → パース → 集約 が直接集約と一致） ───────────────

    [Fact]
    public void Pipeline_FormatParseAggregate_MatchesDirectAggregate()
    {
        var samples = new[]
        {
            Sample(10, 10, 4, 10, 1, BattleOutcome.BattalionVictory),
            Sample(20, 6,  2, 10, 2, BattleOutcome.BattalionVictory),
            Sample(25, 20, 0, 10, 3, BattleOutcome.BattalionVictory, boss: true),
            Sample(40, 0,  5, 9,  9, BattleOutcome.BattalionDefeat),
            Sample(90, 5,  1, 9,  0, BattleOutcome.BattalionVictory),
        };

        // ログ（ノイズ・他イベント混入）→ パース。
        var lines = new List<string> { "noise without prefix", string.Empty };
        lines.AddRange(samples.Select(MetricsLogFormatter.FormatSample));
        lines.Add("[metrics] {\"event\":\"battle-start\",\"year\":99,\"enemy\":\"eternal-sovereign\","
            + "\"hp\":1,\"atk\":1,\"spd\":1}");

        var parsed = MetricsReporter.ParseLogLines(lines);
        Assert.Equal(samples.Length, parsed.Length); // ノイズ・他イベントは混ざらない。

        var viaPipeline = MetricsCollector.Aggregate(parsed);
        var direct = MetricsCollector.Aggregate(samples);

        // 全体総計が 1 ビットの狂いもなく一致。
        Assert.Equal(direct.TotalBattles, viaPipeline.TotalBattles);
        Assert.Equal(direct.TotalEarned, viaPipeline.TotalEarned);
        Assert.Equal(direct.TotalSpent, viaPipeline.TotalSpent);
        Assert.Equal(direct.NetSurplus, viaPipeline.NetSurplus);
        Assert.Equal(direct.TotalVictories, viaPipeline.TotalVictories);
        Assert.Equal(direct.TotalLost, viaPipeline.TotalLost);

        // 各章の黒字幅・勝率・生存率も完全一致（ノブ調整の羅針盤が再現される）。
        Assert.Equal(direct.Epochs.Length, viaPipeline.Epochs.Length);
        for (var i = 0; i < direct.Epochs.Length; i++)
        {
            Assert.Equal(direct.Epochs[i].NetSurplus, viaPipeline.Epochs[i].NetSurplus);
            Assert.Equal(direct.Epochs[i].AverageNetSurplus, viaPipeline.Epochs[i].AverageNetSurplus, RatePrecision);
            Assert.Equal(direct.Epochs[i].VictoryRate, viaPipeline.Epochs[i].VictoryRate, RatePrecision);
            Assert.Equal(direct.Epochs[i].PopulationSurvivalRate, viaPipeline.Epochs[i].PopulationSurvivalRate, RatePrecision);
        }
    }

    // ─── 4. マクロレポート（章別の勝率・生存率・黒字幅の ASCII カラム表） ───

    private static void AssertAsciiOnly(string text)
    {
        Assert.All(text, c => Assert.True(c < 128, $"non-ASCII char detected: {(int)c}"));
    }

    [Fact]
    public void FormatMacroReport_RendersAsciiColumnsWithEpochsAndTotal()
    {
        var metrics = MetricsCollector.Aggregate(new[]
        {
            Sample(10, earned: 10, spent: 4, combatants: 10, lost: 1, BattleOutcome.BattalionVictory),
            Sample(20, earned: 6,  spent: 2, combatants: 10, lost: 2, BattleOutcome.BattalionVictory),
            Sample(25, earned: 20, spent: 0, combatants: 10, lost: 3, BattleOutcome.BattalionVictory, boss: true),
        });

        var report = MetricsReporter.FormatMacroReport(metrics);

        // 見出し・列キー・全 4 章・TOTAL 行が揃う。
        Assert.Contains("chronicle-macro-metrics", report);
        Assert.Contains("winrate", report);
        Assert.Contains("survival", report);
        Assert.Contains("boss_win", report);
        Assert.Contains("epoch-dawn", report);
        Assert.Contains("epoch-upheaval", report);
        Assert.Contains("epoch-decline", report);
        Assert.Contains("epoch-twilight", report);
        Assert.Contains("TOTAL", report);

        // 黎明の特徴的なセル（勝率 1.000・生存率 0.800・平均黒字 10.00・章ボス 1/1）。
        Assert.Contains("1.000", report);
        Assert.Contains("0.800", report);
        Assert.Contains("10.00", report);
        Assert.Contains("1/1", report);

        AssertAsciiOnly(report);
    }

    [Fact]
    public void BuildReportFromLogs_EqualsDirectFormatOfAggregate()
    {
        var samples = new[]
        {
            Sample(10, 10, 4, 10, 1, BattleOutcome.BattalionVictory),
            Sample(40, 0,  5, 9,  9, BattleOutcome.BattalionDefeat),
            Sample(100, 12, 3, 9, 0, BattleOutcome.BattalionVictory, boss: true),
        };

        var lines = new List<string> { "noise" };
        lines.AddRange(samples.Select(MetricsLogFormatter.FormatSample));

        var viaLogs = MetricsReporter.BuildReportFromLogs(lines);
        var direct = MetricsReporter.FormatMacroReport(MetricsCollector.Aggregate(samples));

        Assert.Equal(direct, viaLogs); // ログ → 集約 → 整形が直接整形と完全一致。
    }

    [Fact]
    public void DumpMacroReport_DoesNotThrow()
    {
        var metrics = MetricsCollector.Aggregate(System.Array.Empty<BattleMetricSample>());
        MetricsReporter.DumpMacroReport(metrics); // 標準出力へ一過性ダンプ（例外を投げない）。
    }
}
