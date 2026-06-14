// =============================================================================
//  ChronicleKnights.Tests — MultiverseSimulationRunner.cs
// -----------------------------------------------------------------------------
//  50 宇宙（計 5000 年分）を裏側で連続巡航し、3 ノブ最適化の生きた基準点（ベースライン）を
//  露出させるモンテカルロ連続実走ランナー。
//
//  ★ なぜ「シミュレーション模型」なのか（環境の制約）:
//    実機の 100 年ループを司る ChronicleGlobal は Godot.Node（Autoload）であり、xUnit からは
//    インスタンス化できない（テストプロジェクトは Core/** のみをコンパイルし Godot を含まない）。
//    そこで本ランナーは、ゲームの「3 ノブ」をそのまま叩く決定論的な Core 専用模型として再構成する:
//      - 難易度ノブ : EnemyScalingResolver.ComposeBattleEnemy（章の DifficultyScale/Environment を畳んだ
//                     実難易度曲線 + ±15% 個体差ジッタ）でその年の敵を生成。
//      - 報酬ノブ   : 婚姻ポイント算出の模型レート（BattleSpoils と同型・基礎報酬のみ黄金均衡へ調律）で
//                     獲得婚姻ポイントを算出。
//      - 物価ノブ   : 強化投資コスト（UpgradeCost）でポイントを消費し、投資が大隊戦力へ還元される。
//    戦闘そのもの（BattleResolver の多ターン解決）は headless 再構成が重く脆いため、戦力 vs 敵攻撃の
//    透明な勝敗・損失模型で代理する。よって本ベースラインは「3 ノブ感度のベースライン」である。
//
//  ★ 純粋・決定論・宇宙独立（副作用ゼロ）:
//    各宇宙は固定シードからの純粋計算で、グローバル SoT を一切共有しない（1 宇宙ごとに完全独立＝
//    リセット不要）。同一シードからは常に同一の ChronicleMetrics を返す。
//
//  ★ 例外不在の死守:
//    全演算は Min/Max クランプと整数演算で境界づけられ、添字アクセスを行わない。婚姻ポイントは
//    獲得（≥0）で増え、消費は残高 ≥ UpgradeCost のときだけ起きるため負へ沈まない。絶滅（大隊全滅）は
//    途中打ち切りで TotalBattles < 100 として現れる。
//
//  ★ 開発憲法①: 数値と ASCII の章キーだけを扱い、表示テキストを持たない（レポート整形は別層）。
// =============================================================================

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using ChronicleKnights.Core.Battle;
using ChronicleKnights.Core.Chronicle;
using Xunit;

namespace ChronicleKnights.Tests.Core.Chronicle;

public class MultiverseSimulationRunner
{
    // ─── 巡航パラメタ ─────────────────────────────────────────────────────

    /// <summary>連続実走する宇宙数（計 50 × 100 年 = 5000 年分）。</summary>
    public const int DefaultUniverseCount = 50;

    // ─── 模型パラメタ（ベースラインの土台。実機 SoT ではなく本ハーネスの仮定値） ──

    /// <summary>大隊定員（3×3）。人口生存率の母数。</summary>
    private const int BattalionSize = 9;

    /// <summary>開始時の婚姻ポイント残高。</summary>
    private const int StartingPoints = 12;

    /// <summary>
    /// 初期大隊の戦力（敵攻撃力と突き合わせる攻撃力相当の代理値）。黎明期の安全マージンを縮小し
    /// 難易度ラダーが噛むよう 36 → 30 へ締め上げ済み。
    /// </summary>
    private const int BasePower = 30;

    /// <summary>
    /// 強化 1 回あたりの戦力還元。雪だるまインフレ（投資が難易度成長を凌駕する暴走）を止めるため
    /// 8 → 3 へ締め上げ済み（投資の戦力還元を鈍化させ、難易度曲線に牙を取り戻させる）。
    /// </summary>
    private const int InvestmentPerUpgrade = 3;

    /// <summary>
    /// 強化投資 1 回のコスト（ShopService.BaseUpgradeCost 相当の物価ノブ）。投資効率を改善し後半の
    /// インフレに戦力を追従させるため 3 → 2 へ引き下げ済み（黄金均衡への調律）。
    /// </summary>
    private const int UpgradeCost = 2;

    /// <summary>
    /// 戦力不足ぶんを完全ロスト数へ写す除数（不利なほど損失が増える）。不利な戦闘の損耗を倍に
    /// 厳罰化するため 16 → 8 へ締め上げ済み（後半章で絶滅が現実に発生するよう牙を立てる）。
    /// </summary>
    private const int LossDivisor = 8;

    // ─── 報酬ノブ（婚姻ポイント算出の模型レート。BattleSpoils と同型・基礎のみ調律） ──

    /// <summary>勝利の基礎報酬（報酬ノブ）。壊滅からの復旧・経済循環を加速するため 5 → 6 へ底上げ済み。</summary>
    private const int BaseMarriagePoints = 6;

    /// <summary>昇級 1 件あたりの加点（BattleSpoils の LevelGainBounty と同レート）。</summary>
    private const int LevelGainBounty = 2;

    /// <summary>装備進化 1 件あたりの加点（BattleSpoils の EquipmentEvolutionBounty と同レート）。</summary>
    private const int EvolutionBounty = 1;

    /// <summary>完全ロスト 1 件あたりの減点（BattleSpoils の PermanentLossPenalty と同レート）。</summary>
    private const int LossPenalty = 3;

    /// <summary>ベースライン測定に用いる固定シード列（1..DefaultUniverseCount・決定論的に不変）。</summary>
    public static ImmutableArray<int> BaselineSeeds { get; } =
        Enumerable.Range(1, DefaultUniverseCount).ToImmutableArray();

    // ─── 連続実走ランナー ─────────────────────────────────────────────────

    /// <summary>
    /// 与えられたシード列の各宇宙を直列に巡航し、1 宇宙ぶんずつ <see cref="ChronicleMetrics"/> を
    /// 抽出して 50 宇宙分のコレクションとして返す。各宇宙は固定シードからの純粋計算で完全に独立。
    /// </summary>
    public static ImmutableArray<ChronicleMetrics> RunUniverses(IEnumerable<int> seeds)
    {
        ArgumentNullException.ThrowIfNull(seeds);

        var builder = ImmutableArray.CreateBuilder<ChronicleMetrics>();
        foreach (var seed in seeds)
        {
            builder.Add(SimulateUniverse(seed));
        }

        return builder.ToImmutable();
    }

    /// <summary>
    /// 1 宇宙（固定シード）の 100 年を巡航し、年ごとの計測標本を集約した <see cref="ChronicleMetrics"/> を返す。
    /// 難易度（ComposeBattleEnemy）・報酬（BaseMarriagePoints）・物価（UpgradeCost）の 3 ノブを
    /// 実値で叩き、大隊全滅で打ち切る（絶滅 = TotalBattles &lt; 100）。
    /// </summary>
    public static ChronicleMetrics SimulateUniverse(int seed)
    {
        var rng = new Random(seed);
        var samples = new List<BattleMetricSample>();

        var marriagePoints = StartingPoints;
        var investment = 0;

        for (var year = ChronicleTimelineConfig.FirstYear; year <= ChronicleTimelineConfig.TotalYears; year++)
        {
            var archetype = ChronicleTimelineConfig.BattleArchetypeForYear(year);
            var enemy = EnemyScalingResolver.ComposeBattleEnemy(year, archetype, rng); // 実難易度曲線 + 個体差

            var brigadePower = BasePower + investment;
            var deficit = enemy.Attack - brigadePower;
            var victory = deficit <= 0;
            var lost = deficit <= 0 ? 0 : Math.Min(BattalionSize, 1 + deficit / LossDivisor);

            // 報酬は件数のみで決まる模型レート（BattleSpoils と同型・基礎のみ調律）: 勝利でその年の
            // 英雄が 1 名育ち、章ボス勝利で装備が 1 件進化。Max(0,…) で底打ち（負の報酬は経済へ流さない）。
            var levelGains = victory ? 1 : 0;
            var evolutions = victory && ChronicleTimelineConfig.IsEpochBossYear(year) ? 1 : 0;
            var earned = victory
                ? Math.Max(0, BaseMarriagePoints
                    + levelGains * LevelGainBounty
                    + evolutions * EvolutionBounty
                    - lost * LossPenalty)
                : 0;
            marriagePoints += earned;

            // 物価: 残高が許す限り強化へ投資し、戦力へ還元する。
            var spent = 0;
            if (marriagePoints >= UpgradeCost)
            {
                marriagePoints -= UpgradeCost;
                investment += InvestmentPerUpgrade;
                spent = UpgradeCost;
            }

            samples.Add(new BattleMetricSample
            {
                Year           = year,
                EarnedPoints   = earned,
                SpentPoints    = spent,
                CombatantCount = BattalionSize,
                LostCount      = lost,
                Outcome        = victory ? BattleOutcome.BattalionVictory : BattleOutcome.BattalionDefeat,
                IsEpochBoss    = ChronicleTimelineConfig.IsEpochBossYear(year),
            });

            if (lost >= BattalionSize)
            {
                break; // 大隊全滅＝ゲームオーバー（途中絶滅。以後の年は刻まれない）。
            }
        }

        return MetricsCollector.Aggregate(samples);
    }

    // ════════════════════════════════════════════════════════════════════════
    //  xUnit: 50 宇宙連続自動巡航の安全性包囲（例外不在・決定論）
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Run50UniversesAndVerifyNoException()
    {
        var metrics = RunUniverses(BaselineSeeds);

        // 50 宇宙ぶんが例外なく回収され、各宇宙が健全な範囲に収まる。
        Assert.Equal(DefaultUniverseCount, metrics.Length);
        foreach (var universe in metrics)
        {
            Assert.True(universe.TotalBattles >= 0);
            Assert.True(universe.TotalBattles <= ChronicleTimelineConfig.TotalYears);
            Assert.Equal(4, universe.Epochs.Length);
        }
    }

    [Fact]
    public void SimulateUniverse_IsDeterministic_ForSameSeed()
    {
        var first = SimulateUniverse(7);
        var second = SimulateUniverse(7);

        Assert.Equal(first.TotalBattles, second.TotalBattles);
        Assert.Equal(first.TotalEarned, second.TotalEarned);
        Assert.Equal(first.TotalSpent, second.TotalSpent);
        Assert.Equal(first.NetSurplus, second.NetSurplus);
        Assert.Equal(first.TotalVictories, second.TotalVictories);
    }

    // ════════════════════════════════════════════════════════════════════════
    //  黄金均衡の包囲（絶滅率 0.0% ＆ 全章黒字 ＆ 美しい傾斜壁）
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public void GoldenEquilibrium_FiftyUniverses_ZeroExtinction_PositiveNet_BossGradient()
    {
        var metrics = UniverseEvaluator.Evaluate(RunUniverses(BaselineSeeds));

        // 🎯 絶対条件: 絶滅率 0.0%（全 50 宇宙が 100 年を完走・1 宇宙もゲームオーバーにならない）。
        Assert.Equal(DefaultUniverseCount, metrics.UniverseCount);
        Assert.Equal(0, metrics.ExtinctCount);
        Assert.Equal(0.0, metrics.ExtinctionRate, 6);

        // 🎯 経済条件: 全章で平均純増が黒字（健全な黒字踏破が可能）。
        foreach (var epoch in metrics.Epochs)
        {
            Assert.True(
                epoch.AvgNet > 0.0,
                $"{epoch.EpochNameKey} avg_net must stay positive but was {epoch.AvgNet}");
        }

        // 🎯 難易度条件: 章ボス突破率が黎明（易）→ 終焉（最難関）へ傾斜。0% 即死トラップでも、
        //    すべて 1.000 のぬるま湯でもないこと。
        var dawn = metrics.Epochs.Single(e => e.Id == EpochId.Dawn);
        var twilight = metrics.Epochs.Single(e => e.Id == EpochId.Twilight);
        Assert.Equal(1.0, dawn.BossClearRate, 6);                  // 黎明の章ボスは確実に超えられる
        Assert.True(twilight.BossClearRate > 0.0, "twilight boss must not be a 0% death trap");
        Assert.True(twilight.BossClearRate < 1.0, "twilight boss must remain the hardest wall");
        Assert.True(
            dawn.BossClearRate > twilight.BossClearRate,
            "boss difficulty must slope from dawn (easiest) to twilight (hardest)");
    }

    // ════════════════════════════════════════════════════════════════════════
    //  初期ベースライン・多重宇宙レポートの完全ダンプ（現行 3 ノブ設定）
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// 50 宇宙を連続巡航 → UniverseEvaluator.Evaluate で多重宇宙集約 → 標準出力へ ASCII カラム表として
    /// ダンプする。冒頭に「=== MULTIVERSE TUNED BALANCE REPORT ===」を付与し、3 ノブ最適化後の黄金均衡
    /// （絶滅率 0.0%・全章黒字・黎明→終焉の美しい傾斜壁）を人間が一目で読み取れる状態にする。
    /// </summary>
    [Fact]
    public void DumpBaselineMultiverseReport()
    {
        var metrics = RunUniverses(BaselineSeeds);
        var universe = UniverseEvaluator.Evaluate(metrics);

        Console.WriteLine("=== MULTIVERSE TUNED BALANCE REPORT ===");
        Console.WriteLine("config: golden equilibrium (difficulty ladder + reward + price tuned)");
        Console.WriteLine(
            "seeds: 1.." + DefaultUniverseCount
            + "   years-per-universe: " + ChronicleTimelineConfig.TotalYears
            + "   total-years: " + (DefaultUniverseCount * ChronicleTimelineConfig.TotalYears));
        UniverseEvaluator.DumpUniverseReport(universe);

        // 露出が健全に成立すること（例外なく 50 宇宙の統計が出揃う）を最小アサート。
        Assert.Equal(DefaultUniverseCount, universe.UniverseCount);
        Assert.Equal(4, universe.Epochs.Length);
    }
}
