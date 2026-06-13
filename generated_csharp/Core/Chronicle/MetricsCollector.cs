// =============================================================================
//  ChronicleKnights — MetricsCollector.cs
// -----------------------------------------------------------------------------
//  100 年通しプレイの「経済と難易度の起伏」を冷徹な数値へ畳み込む純粋な集約器。
//
//  ★ 役割（マクロ実測台の心臓部）:
//    1 戦闘＝1 標本（BattleMetricSample）として蓄積された SoT のスナップショット
//    （獲得ポイント・消費ポイント・出撃数・完全ロスト数・勝敗・章ボス戦か）を入力に取り、
//    章（Epoch）ごとの「平均黒字幅」「人口生存率」「投資効率」「勝率」「章ボス勝率」と、
//    100 年全体の総計を決定論的に弾き出す。グローバル SoT を 1 ミリも触らず、同じ標本列からは
//    常に同じ統計を返す純粋関数（Godot 非依存・xUnit で完全検証可能）。
//
//  ★ 完全不変（設計憲法 ②）・副作用ゼロ:
//    入力標本・出力統計はいずれも init 専用の不変レコード。Aggregate は入力だけから出力を
//    生成し、内部の可変アキュムレータは関数ローカルに閉じる（外へ漏れる変異は皆無）。
//
//  ★ 章の引き当て（単一 SoT）:
//    標本がどの章に属するかは年（Year）から ChronicleTimelineConfig.EpochForYear で引く
//    （年→章の真実は 1 箇所）。範囲外の年もクランプ番兵で安全に最寄りの章へ畳まれる。
//
//  ★ 日本語ハードコード禁止（設計憲法 ①）:
//    本ファイルは数値と ASCII の章キー（NameKey "epoch-*"）だけを運び、表示テキストを持たない。
//
//  略称（BDF/SDF/AB/HL — 正式名称のみを使う方針）は本ファイルでも完全未使用。
// =============================================================================

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using ChronicleKnights.Core.Battle;

namespace ChronicleKnights.Core.Chronicle;

// ─── 1 戦闘の計測標本（不変・表示テキストを持たない） ────────────────────────

/// <summary>
/// 1 戦闘（≒ 1 年）の決算スナップショットを写し取った完全不変の計測標本。どの章に属するかは
/// <see cref="Year"/> から決定論的に引く（年→章は ChronicleTimelineConfig が単一の真実）。
/// </summary>
public sealed record BattleMetricSample
{
    /// <summary>この戦闘が起きた年（範囲外でもクランプして最寄りの章へ畳まれる）。</summary>
    public required int Year { get; init; }

    /// <summary>この戦闘で獲得した婚姻ポイント（常に 0 以上の想定）。</summary>
    public required int EarnedPoints { get; init; }

    /// <summary>この戦闘の前後で消費した婚姻ポイント（採用・商店等。0 以上の想定）。</summary>
    public required int SpentPoints { get; init; }

    /// <summary>この戦闘へ出撃した味方数（人口生存率の母数）。</summary>
    public required int CombatantCount { get; init; }

    /// <summary>この戦闘で完全ロスト（戦闘死）した味方数。</summary>
    public required int LostCount { get; init; }

    /// <summary>この戦闘の決着状態。</summary>
    public required BattleOutcome Outcome { get; init; }

    /// <summary>この戦闘が章ボス戦だったか。</summary>
    public required bool IsEpochBoss { get; init; }

    /// <summary>大隊の勝利だったか（婚姻ポイント加点と同じ勝利定義）。</summary>
    public bool IsVictory => Outcome == BattleOutcome.BattalionVictory;
}

// ─── 1 章の集約統計（不変・派生統計を計算ゲッタで提供） ──────────────────────

/// <summary>
/// 1 つの章（Epoch）に属する戦闘群を集約した完全不変の統計。生の累計（required init）を保持し、
/// 平均黒字幅・人口生存率・投資効率・勝率を派生ゲッタで弾く。母数 0 の境界は安全な番兵値へ倒す。
/// </summary>
public sealed record EpochMetrics
{
    /// <summary>章の識別子。</summary>
    public required EpochId Id { get; init; }

    /// <summary>章名の localization キー（ASCII・例 "epoch-dawn"）。</summary>
    public required string EpochNameKey { get; init; }

    /// <summary>この章で戦った戦闘数。</summary>
    public required int BattleCount { get; init; }

    /// <summary>この章での勝利数。</summary>
    public required int Victories { get; init; }

    /// <summary>この章での総獲得婚姻ポイント。</summary>
    public required int TotalEarned { get; init; }

    /// <summary>この章での総消費婚姻ポイント。</summary>
    public required int TotalSpent { get; init; }

    /// <summary>この章での延べ出撃数（人口生存率の母数）。</summary>
    public required int TotalCombatants { get; init; }

    /// <summary>この章での総完全ロスト数。</summary>
    public required int TotalLost { get; init; }

    /// <summary>この章での章ボス戦数。</summary>
    public required int BossBattles { get; init; }

    /// <summary>この章での章ボス勝利数。</summary>
    public required int BossVictories { get; init; }

    /// <summary>純黒字幅（総獲得 − 総消費）。負にもなり得る。</summary>
    public int NetSurplus => TotalEarned - TotalSpent;

    /// <summary>1 戦あたりの平均黒字幅。戦闘 0 のときは 0。</summary>
    public double AverageNetSurplus => BattleCount <= 0 ? 0.0 : (double)NetSurplus / BattleCount;

    /// <summary>人口生存率（(出撃 − ロスト) / 出撃）。母数 0 のときは 1.0（完全生存）。</summary>
    public double PopulationSurvivalRate
        => TotalCombatants <= 0 ? 1.0 : (double)(TotalCombatants - TotalLost) / TotalCombatants;

    /// <summary>投資効率（総獲得 / 総消費）。消費 0 のときは未定義として 0.0。</summary>
    public double InvestmentEfficiency => TotalSpent <= 0 ? 0.0 : (double)TotalEarned / TotalSpent;

    /// <summary>勝率（勝利 / 戦闘数）。戦闘 0 のときは 0。</summary>
    public double VictoryRate => BattleCount <= 0 ? 0.0 : (double)Victories / BattleCount;

    /// <summary>章ボス勝率（章ボス勝利 / 章ボス戦）。章ボス戦 0 のときは 0。</summary>
    public double BossVictoryRate => BossBattles <= 0 ? 0.0 : (double)BossVictories / BossBattles;
}

// ─── 100 年全体の集約統計（章配列 + 総計） ───────────────────────────────────

/// <summary>
/// 100 年史全体のマクロ統計を写し取った完全不変レコード。4 章ぶんの <see cref="EpochMetrics"/>
/// （章順）と全体の総計を保持し、全体の生存率・投資効率・勝率を派生ゲッタで弾く。
/// </summary>
public sealed record ChronicleMetrics
{
    /// <summary>章順（黎明 → 激動 → 斜陽 → 終焉）に並ぶ章別統計。戦闘の無い章も 0 で必ず含む。</summary>
    public required ImmutableArray<EpochMetrics> Epochs { get; init; }

    /// <summary>全戦闘数。</summary>
    public required int TotalBattles { get; init; }

    /// <summary>全勝利数。</summary>
    public required int TotalVictories { get; init; }

    /// <summary>全獲得婚姻ポイント。</summary>
    public required int TotalEarned { get; init; }

    /// <summary>全消費婚姻ポイント。</summary>
    public required int TotalSpent { get; init; }

    /// <summary>延べ出撃数。</summary>
    public required int TotalCombatants { get; init; }

    /// <summary>総完全ロスト数。</summary>
    public required int TotalLost { get; init; }

    /// <summary>全章ボス戦数。</summary>
    public required int TotalBossBattles { get; init; }

    /// <summary>全章ボス勝利数。</summary>
    public required int TotalBossVictories { get; init; }

    /// <summary>全体の純黒字幅（全獲得 − 全消費）。</summary>
    public int NetSurplus => TotalEarned - TotalSpent;

    /// <summary>全体の人口生存率。母数 0 のときは 1.0。</summary>
    public double OverallSurvivalRate
        => TotalCombatants <= 0 ? 1.0 : (double)(TotalCombatants - TotalLost) / TotalCombatants;

    /// <summary>全体の投資効率。消費 0 のときは 0.0。</summary>
    public double OverallInvestmentEfficiency
        => TotalSpent <= 0 ? 0.0 : (double)TotalEarned / TotalSpent;

    /// <summary>全体の勝率。戦闘 0 のときは 0。</summary>
    public double OverallVictoryRate => TotalBattles <= 0 ? 0.0 : (double)TotalVictories / TotalBattles;
}

// ─── 純粋な集約器（静的・関数型・SoT 不可侵） ────────────────────────────────

/// <summary>
/// 計測標本列を章別・全体のマクロ統計へ畳み込む純粋な静的集約器。グローバル状態を持たず、
/// 同一入力からは 100% 同一の <see cref="ChronicleMetrics"/> を返す（決定論・副作用ゼロ）。
/// </summary>
public static class MetricsCollector
{
    /// <summary>
    /// 計測標本列を 4 章ぶんの <see cref="EpochMetrics"/>（章順）と全体総計へ集約する。各標本は
    /// 年から <see cref="ChronicleTimelineConfig.EpochForYear"/> で属する章へ振り分けられる。
    /// 戦闘の無い章も 0 の統計として必ず含む。null 標本は安全に読み飛ばす。
    /// </summary>
    /// <param name="samples">1 戦闘ごとの計測標本列（null 不可・要素 null は読み飛ばす）。</param>
    /// <returns>章別 + 全体のマクロ統計。</returns>
    /// <exception cref="ArgumentNullException">samples が null の場合。</exception>
    public static ChronicleMetrics Aggregate(IEnumerable<BattleMetricSample> samples)
    {
        ArgumentNullException.ThrowIfNull(samples);

        // 4 章ぶんの可変アキュムレータを章順で初期化（戦闘の無い章も 0 で必ず出る）。
        var accumulators = new Dictionary<EpochId, EpochAccumulator>();
        foreach (var epoch in ChronicleTimelineConfig.Epochs)
        {
            accumulators[epoch.Id] = new EpochAccumulator(epoch);
        }

        foreach (var sample in samples)
        {
            if (sample is null)
            {
                continue; // null 標本は安全にスキップ（画面・集計を落とさない）。
            }

            var epoch = ChronicleTimelineConfig.EpochForYear(sample.Year);
            accumulators[epoch.Id].Add(sample);
        }

        var epochMetrics = ImmutableArray.CreateBuilder<EpochMetrics>(ChronicleTimelineConfig.Epochs.Length);
        var totalBattles = 0;
        var totalVictories = 0;
        var totalEarned = 0;
        var totalSpent = 0;
        var totalCombatants = 0;
        var totalLost = 0;
        var totalBossBattles = 0;
        var totalBossVictories = 0;

        foreach (var epoch in ChronicleTimelineConfig.Epochs)
        {
            var metrics = accumulators[epoch.Id].ToMetrics();
            epochMetrics.Add(metrics);

            totalBattles += metrics.BattleCount;
            totalVictories += metrics.Victories;
            totalEarned += metrics.TotalEarned;
            totalSpent += metrics.TotalSpent;
            totalCombatants += metrics.TotalCombatants;
            totalLost += metrics.TotalLost;
            totalBossBattles += metrics.BossBattles;
            totalBossVictories += metrics.BossVictories;
        }

        return new ChronicleMetrics
        {
            Epochs             = epochMetrics.ToImmutable(),
            TotalBattles       = totalBattles,
            TotalVictories     = totalVictories,
            TotalEarned        = totalEarned,
            TotalSpent         = totalSpent,
            TotalCombatants    = totalCombatants,
            TotalLost          = totalLost,
            TotalBossBattles   = totalBossBattles,
            TotalBossVictories = totalBossVictories,
        };
    }

    /// <summary>
    /// 1 章ぶんの累計を畳み込む関数ローカルの可変アキュムレータ。Aggregate の内部にのみ存在し、
    /// 外へ変異が漏れない（純粋関数の実装詳細）。最後に不変の <see cref="EpochMetrics"/> へ封じる。
    /// </summary>
    private sealed class EpochAccumulator
    {
        private readonly Epoch _epoch;
        private int _battleCount;
        private int _victories;
        private int _totalEarned;
        private int _totalSpent;
        private int _totalCombatants;
        private int _totalLost;
        private int _bossBattles;
        private int _bossVictories;

        public EpochAccumulator(Epoch epoch) => _epoch = epoch;

        public void Add(BattleMetricSample sample)
        {
            _battleCount++;
            if (sample.IsVictory)
            {
                _victories++;
            }

            _totalEarned += sample.EarnedPoints;
            _totalSpent += sample.SpentPoints;
            _totalCombatants += sample.CombatantCount;
            _totalLost += sample.LostCount;

            if (sample.IsEpochBoss)
            {
                _bossBattles++;
                if (sample.IsVictory)
                {
                    _bossVictories++;
                }
            }
        }

        public EpochMetrics ToMetrics() => new()
        {
            Id              = _epoch.Id,
            EpochNameKey    = _epoch.NameKey,
            BattleCount     = _battleCount,
            Victories       = _victories,
            TotalEarned     = _totalEarned,
            TotalSpent      = _totalSpent,
            TotalCombatants = _totalCombatants,
            TotalLost       = _totalLost,
            BossBattles     = _bossBattles,
            BossVictories   = _bossVictories,
        };
    }
}
