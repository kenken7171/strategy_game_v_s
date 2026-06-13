// =============================================================================
//  ChronicleKnights — UniverseEvaluator.cs
// -----------------------------------------------------------------------------
//  「多重宇宙」マクロ集約器。複数回（50〜100 回）の 100 年シミュレーション結果
//  （それぞれ MetricsCollector が弾いた ChronicleMetrics）を 1 つの統計へ畳み込む純粋関数。
//
//  ★ 役割（ノブ黄金比の羅針盤）:
//    単発の歴史の揺らぎを超え、章（Epoch）ごとの平均勝率・平均生存率・平均獲得/消費/純増・
//    章ボス突破率を宇宙横断で集約し、さらに「家系断絶絶滅率（Extinction Rate）」＝途中で人口や
//    ポイントが尽きてゲームオーバーになった（＝100 年に届かず戦闘数が満たない）宇宙の割合を弾く。
//
//  ★ 絶滅判定（ChronicleMetrics から決定論的に導出）:
//    100 年完走した宇宙は 1 年 1 戦で TotalBattles == TotalYears(100)。途中で絶滅した宇宙は
//    戦闘数が満たない（TotalBattles < TotalYears）。よって TotalBattles < TotalYears を絶滅とみなす。
//
//  ★ 集計方式（センチネル混入を避けるプール集計）:
//    章別の勝率・生存率・章ボス突破率は、宇宙横断で生の合計を取ってから比を弾く（プール率）。
//    これにより「戦闘 0 の章（絶滅後に未到達の章）」が分母を汚さない。獲得/消費/純増は宇宙あたりの
//    平均（合計 / 宇宙数）で出し、「典型的な 1 宇宙でこの章がどれだけ稼ぐか」を表す。
//
//  ★ 完全不変（②）・副作用ゼロ・Godot 非依存:
//    入力 ChronicleMetrics 群から出力 UniverseMetrics を生成するだけ。グローバル SoT を 1 ミリも触らず、
//    同一入力からは常に同一統計（決定論）。内部の可変アキュムレータは関数ローカルに閉じる。
//
//  ★ 日本語ハードコード禁止（①）: 数値と ASCII の章キーだけを運び、表示テキストを持たない。
//
//  略称（BDF/SDF/AB/HL — 正式名称のみを使う方針）は本ファイルでも完全未使用。
// =============================================================================

using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace ChronicleKnights.Core.Chronicle;

// ─── 1 章の多重宇宙集約統計（不変・派生統計を計算ゲッタで提供） ───────────────

/// <summary>
/// 1 つの章（Epoch）を多重宇宙横断で集約した完全不変の統計。生の合計（required init）を保持し、
/// プール勝率・プール生存率・宇宙あたり平均獲得/消費/純増・章ボス突破率を派生ゲッタで弾く。
/// </summary>
public sealed record UniverseEpochMetrics
{
    /// <summary>章の識別子。</summary>
    public required EpochId Id { get; init; }

    /// <summary>章名の localization キー（ASCII・例 "epoch-dawn"）。</summary>
    public required string EpochNameKey { get; init; }

    /// <summary>集計に寄与した宇宙数（全宇宙。未到達の章でも分母に含む）。</summary>
    public required int UniverseCount { get; init; }

    /// <summary>全宇宙合計のこの章の戦闘数。</summary>
    public required int BattleCount { get; init; }

    /// <summary>全宇宙合計のこの章の勝利数。</summary>
    public required int Victories { get; init; }

    /// <summary>全宇宙合計のこの章の獲得婚姻ポイント。</summary>
    public required int TotalEarned { get; init; }

    /// <summary>全宇宙合計のこの章の消費婚姻ポイント。</summary>
    public required int TotalSpent { get; init; }

    /// <summary>全宇宙合計のこの章の延べ出撃数。</summary>
    public required int TotalCombatants { get; init; }

    /// <summary>全宇宙合計のこの章の総完全ロスト数。</summary>
    public required int TotalLost { get; init; }

    /// <summary>全宇宙合計のこの章の章ボス戦数。</summary>
    public required int BossBattles { get; init; }

    /// <summary>全宇宙合計のこの章の章ボス勝利数。</summary>
    public required int BossVictories { get; init; }

    /// <summary>全宇宙合計の純黒字幅（獲得 − 消費）。</summary>
    public int NetSurplus => TotalEarned - TotalSpent;

    /// <summary>平均勝率（プール: 全宇宙の勝利 / 戦闘）。戦闘 0 のときは 0。</summary>
    public double AvgWinrate => BattleCount <= 0 ? 0.0 : (double)Victories / BattleCount;

    /// <summary>平均生存率（プール: (出撃 − ロスト) / 出撃）。母数 0 のときは 1.0。</summary>
    public double AvgSurvival
        => TotalCombatants <= 0 ? 1.0 : (double)(TotalCombatants - TotalLost) / TotalCombatants;

    /// <summary>宇宙あたり平均獲得（合計 / 宇宙数）。</summary>
    public double AvgEarned => UniverseCount <= 0 ? 0.0 : (double)TotalEarned / UniverseCount;

    /// <summary>宇宙あたり平均消費（合計 / 宇宙数）。</summary>
    public double AvgSpent => UniverseCount <= 0 ? 0.0 : (double)TotalSpent / UniverseCount;

    /// <summary>宇宙あたり平均純増（合計純黒字 / 宇宙数）。</summary>
    public double AvgNet => UniverseCount <= 0 ? 0.0 : (double)NetSurplus / UniverseCount;

    /// <summary>章ボス突破率（プール: 章ボス勝利 / 章ボス戦）。章ボス戦 0 のときは 0。</summary>
    public double BossClearRate => BossBattles <= 0 ? 0.0 : (double)BossVictories / BossBattles;
}

// ─── 多重宇宙全体の集約統計（章配列 + 絶滅率 + 総計） ─────────────────────────

/// <summary>
/// 多重宇宙全体のマクロ統計を写し取った完全不変レコード。4 章ぶんの
/// <see cref="UniverseEpochMetrics"/>（章順）と、絶滅率・全体総計を保持する。
/// </summary>
public sealed record UniverseMetrics
{
    /// <summary>章順（黎明 → 激動 → 斜陽 → 終焉）に並ぶ章別の多重宇宙統計（常に 4 つ）。</summary>
    public required ImmutableArray<UniverseEpochMetrics> Epochs { get; init; }

    /// <summary>集計した宇宙数。</summary>
    public required int UniverseCount { get; init; }

    /// <summary>絶滅した宇宙数（100 年に届かず戦闘数が満たない宇宙）。</summary>
    public required int ExtinctCount { get; init; }

    /// <summary>全宇宙合計の全戦闘数。</summary>
    public required int TotalBattles { get; init; }

    /// <summary>全宇宙合計の全勝利数。</summary>
    public required int TotalVictories { get; init; }

    /// <summary>全宇宙合計の全獲得婚姻ポイント。</summary>
    public required int TotalEarned { get; init; }

    /// <summary>全宇宙合計の全消費婚姻ポイント。</summary>
    public required int TotalSpent { get; init; }

    /// <summary>全宇宙合計の延べ出撃数。</summary>
    public required int TotalCombatants { get; init; }

    /// <summary>全宇宙合計の総完全ロスト数。</summary>
    public required int TotalLost { get; init; }

    /// <summary>全宇宙合計の純黒字幅。</summary>
    public int NetSurplus => TotalEarned - TotalSpent;

    /// <summary>家系断絶絶滅率（絶滅宇宙数 / 宇宙数）。宇宙 0 のときは 0。</summary>
    public double ExtinctionRate => UniverseCount <= 0 ? 0.0 : (double)ExtinctCount / UniverseCount;

    /// <summary>全体プール勝率。</summary>
    public double OverallWinrate => TotalBattles <= 0 ? 0.0 : (double)TotalVictories / TotalBattles;

    /// <summary>全体プール生存率。</summary>
    public double OverallSurvival
        => TotalCombatants <= 0 ? 1.0 : (double)(TotalCombatants - TotalLost) / TotalCombatants;

    /// <summary>宇宙あたり平均純増（全純黒字 / 宇宙数）。</summary>
    public double AvgNetPerUniverse => UniverseCount <= 0 ? 0.0 : (double)NetSurplus / UniverseCount;
}

// ─── 純粋な多重宇宙集約器（静的・関数型・SoT 不可侵） ────────────────────────

/// <summary>
/// 複数の <see cref="ChronicleMetrics"/>（多重宇宙）を 1 つの <see cref="UniverseMetrics"/> へ
/// 畳み込む純粋な静的集約器。グローバル状態を持たず、同一入力からは 100% 同一統計を返す。
/// </summary>
public static class UniverseEvaluator
{
    /// <summary>100 年完走の戦闘数（1 年 1 戦）。これに満たない宇宙を絶滅とみなす境界。</summary>
    private const int FullRunBattles = ChronicleTimelineConfig.TotalYears;

    /// <summary>
    /// 多重宇宙（複数回の 100 年集約）を章別・全体・絶滅率のマクロ統計へ畳み込む。各章は
    /// 宇宙横断でプール集計され、戦闘の無い章も 0 として必ず含む。null 宇宙は安全にスキップ。
    /// </summary>
    /// <param name="universes">各宇宙の 100 年集約（null 不可・要素 null は無視）。</param>
    /// <returns>章別 + 全体 + 絶滅率の多重宇宙統計。</returns>
    /// <exception cref="ArgumentNullException">universes が null の場合。</exception>
    public static UniverseMetrics Evaluate(IEnumerable<ChronicleMetrics> universes)
    {
        ArgumentNullException.ThrowIfNull(universes);

        var accumulators = new Dictionary<EpochId, EpochAccumulator>();
        foreach (var epoch in ChronicleTimelineConfig.Epochs)
        {
            accumulators[epoch.Id] = new EpochAccumulator(epoch);
        }

        var universeCount = 0;
        var extinctCount = 0;
        var totalBattles = 0;
        var totalVictories = 0;
        var totalEarned = 0;
        var totalSpent = 0;
        var totalCombatants = 0;
        var totalLost = 0;

        foreach (var universe in universes)
        {
            if (universe is null)
            {
                continue; // null 宇宙は安全にスキップ（集計を落とさない）。
            }

            universeCount++;
            if (universe.TotalBattles < FullRunBattles)
            {
                extinctCount++; // 100 年に届かなかった＝途中でゲームオーバー（絶滅）。
            }

            totalBattles += universe.TotalBattles;
            totalVictories += universe.TotalVictories;
            totalEarned += universe.TotalEarned;
            totalSpent += universe.TotalSpent;
            totalCombatants += universe.TotalCombatants;
            totalLost += universe.TotalLost;

            foreach (var epochMetrics in universe.Epochs)
            {
                if (accumulators.TryGetValue(epochMetrics.Id, out var accumulator))
                {
                    accumulator.Add(epochMetrics);
                }
            }
        }

        var epochBuilder = ImmutableArray.CreateBuilder<UniverseEpochMetrics>(ChronicleTimelineConfig.Epochs.Length);
        foreach (var epoch in ChronicleTimelineConfig.Epochs)
        {
            epochBuilder.Add(accumulators[epoch.Id].ToMetrics(universeCount));
        }

        return new UniverseMetrics
        {
            Epochs          = epochBuilder.ToImmutable(),
            UniverseCount   = universeCount,
            ExtinctCount    = extinctCount,
            TotalBattles    = totalBattles,
            TotalVictories  = totalVictories,
            TotalEarned     = totalEarned,
            TotalSpent      = totalSpent,
            TotalCombatants = totalCombatants,
            TotalLost       = totalLost,
        };
    }

    /// <summary>
    /// 1 章ぶんを宇宙横断で畳み込む関数ローカルの可変アキュムレータ。Evaluate の内部にのみ存在し、
    /// 外へ変異が漏れない（純粋関数の実装詳細）。最後に不変の <see cref="UniverseEpochMetrics"/> へ封じる。
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

        public void Add(EpochMetrics metrics)
        {
            _battleCount += metrics.BattleCount;
            _victories += metrics.Victories;
            _totalEarned += metrics.TotalEarned;
            _totalSpent += metrics.TotalSpent;
            _totalCombatants += metrics.TotalCombatants;
            _totalLost += metrics.TotalLost;
            _bossBattles += metrics.BossBattles;
            _bossVictories += metrics.BossVictories;
        }

        public UniverseEpochMetrics ToMetrics(int universeCount) => new()
        {
            Id              = _epoch.Id,
            EpochNameKey    = _epoch.NameKey,
            UniverseCount   = universeCount,
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
