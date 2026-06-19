// =============================================================================
//  ChronicleKnights — TimelineEngine.cs
// -----------------------------------------------------------------------------
//  予言の提示・選択・タイムスキップによる一斉加齢を司る不変エンジン。
//
//  ハクスラ・ローグライト設計憲法 (docs/MIGRATION_GODOT_HACK_AND_SLASH.md
//  セクション 1 + フェーズ 3) のタイムライン中核ロジック:
//
//    [毎ターン]
//       1. CurrentOptions に 3 つの Prophecy が提示される
//       2. プレイヤーは ID で 1 つを SelectProphecy
//       3. ApplyTimeSkipToRoster で全生存ユニットに SkipYears を一斉適用
//       4. AdvanceToNextTurn で次の 3 つの Prophecy を新規生成
//          ★ 「次の未来」は本 record にネストしない（選択するまで不可視）
//
//  完全不変 (sealed record)。状態更新は必ず with 式または専用メソッドで
//  新インスタンスを返す。リロール不可制約は構造的に保証:
//    - CurrentOptions は init-only の ImmutableArray<Prophecy>
//    - 一度作られた CurrentOptions の中身は差し替え不可
//    - AdvanceToNextTurn を通じてしか次の予言を得られない
//    - 次の予言リストを保持する状態フィールドは存在しない
// =============================================================================

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using ChronicleKnights.Core.Timeline;
using ChronicleKnights.Core.Units;

namespace ChronicleKnights.Core.Managers;

/// <summary>
/// 予言生成のための差し込み可能なデリゲート。
///
/// 引数:
///   turn — 現在ターン番号（1 から始まる）
///   rng  — 乱数発生器（再現性確保のため呼び出し側責任で渡す）
///
/// 戻り値: 必ず長さ 3 の不変配列。これにより「3 つの選択肢」が型レベルで強制される。
/// </summary>
public delegate ImmutableArray<Prophecy> ProphecyGenerator(int turn, Random rng);

/// <summary>
/// プレイヤーが手動婚姻システムと並行して操る「予言タイムライン」の
/// 完全不変なエンジン状態。
/// </summary>
public sealed record TimelineEngine
{
    // ─── 定数 ─────────────────────────────────────────────────────────────

    /// <summary>1 ターンあたりに提示される予言の数。リロール不可制約により固定。</summary>
    public const int OptionsPerTurn = 3;

    /// <summary>典型的な SkipYears の下限。デフォルトジェネレータが参照。</summary>
    public const int DefaultMinSkipYears = 2;

    /// <summary>典型的な SkipYears の上限。デフォルトジェネレータが参照。</summary>
    public const int DefaultMaxSkipYears = 4;

    // ─── 必須プロパティ ───────────────────────────────────────────────────

    /// <summary>現在のターン番号（1 から始まる）。</summary>
    public required int Turn { get; init; }

    /// <summary>
    /// プレイヤーに提示中の 3 つの予言。長さは常に OptionsPerTurn (=3)。
    /// 次のターンの予言は保持されない（憲法仕様）。
    /// </summary>
    public required ImmutableArray<Prophecy> CurrentOptions { get; init; }

    // ─── ファクトリ ────────────────────────────────────────────────────────

    /// <summary>
    /// 初期状態を作成（ターン 1 の予言を生成）。
    /// </summary>
    public static TimelineEngine CreateInitial(ProphecyGenerator generator, Random rng)
    {
        ArgumentNullException.ThrowIfNull(generator);
        ArgumentNullException.ThrowIfNull(rng);

        var initialTurn = 1;
        var options = ValidateOptions(generator(initialTurn, rng));
        return new TimelineEngine
        {
            Turn = initialTurn,
            CurrentOptions = options,
        };
    }

    // ─── 予言の選択（リロール不可の強制） ──────────────────────────────────

    /// <summary>
    /// 指定 ID の予言が現在の選択肢に含まれているか。
    /// </summary>
    public bool IsValidSelection(Guid prophecyId)
    {
        foreach (var p in CurrentOptions)
        {
            if (p.Id == prophecyId) return true;
        }
        return false;
    }

    /// <summary>
    /// 選択された予言を取り出す（リロール / 任意の予言指定はここで拒否される）。
    /// 不正な ID なら ArgumentException。
    /// </summary>
    public Prophecy GetSelectionOrThrow(Guid prophecyId)
    {
        foreach (var p in CurrentOptions)
        {
            if (p.Id == prophecyId) return p;
        }
        throw new ArgumentException(
            $"Prophecy id {prophecyId} is not in current options (only 3 listed prophecies can be selected).",
            nameof(prophecyId));
    }

    // ─── 次ターンへの遷移 ──────────────────────────────────────────────────

    /// <summary>
    /// 暦を <paramref name="yearsToAdvance"/> 年ぶん進め、新たな 3 つの予言を生成した不変エンジン状態を返す。
    ///
    /// <see cref="Turn"/> は「暦の年」であり、予言の SkipYears ぶん一気に進む（既定 1）。これにより
    /// 「○年経過」が暦・加齢・収入と同一年数で整合する。加齢自体（全旅団員への WithAgeProgress）は
    /// ロスタ側 RosterLifecycle.AdvanceGeneration で同じ年数を用いて別途適用する。
    /// 年数は最低 1（前進保証）にクランプされる。
    /// </summary>
    /// <param name="generator">次ターンの予言を生成するジェネレータ。</param>
    /// <param name="rng">予言生成に注入する乱数。</param>
    /// <param name="yearsToAdvance">暦を進める年数（Prophecy.SkipYears 由来。既定 1）。</param>
    public TimelineEngine AdvanceToNextTurn(ProphecyGenerator generator, Random rng, int yearsToAdvance = 1)
    {
        ArgumentNullException.ThrowIfNull(generator);
        ArgumentNullException.ThrowIfNull(rng);

        var nextTurn = Turn + Math.Max(1, yearsToAdvance);
        var nextOptions = ValidateOptions(generator(nextTurn, rng));
        return this with
        {
            Turn = nextTurn,
            CurrentOptions = nextOptions,
        };
    }

    // ─── タイムスキップによる一斉加齢 ──────────────────────────────────────

    /// <summary>
    /// 旅団の全ユニットに対し、指定年数を一斉に加齢する純粋関数。
    ///
    /// - 生存ユニット (Unit.IsAlive == true)  → Unit.WithAgeProgress(years) 適用
    /// - 既に死亡 / 寿命到達のユニット         → そのまま据え置き
    ///   （リスト上には残るが、roster 管理側が IsRemovedFromRoster で除外する）
    ///
    /// 本メソッドは状態を持たない static として実装し、Engine インスタンスに
    /// 依存しない（旅団全体のスナップショット変換）。
    /// </summary>
    /// <param name="roster">旅団員の不変リスト</param>
    /// <param name="years">タイムスキップ年数（Prophecy.SkipYears の値）</param>
    /// <returns>加齢適用後の新しい不変リスト</returns>
    public static ImmutableArray<Unit> ApplyTimeSkipToRoster(
        IReadOnlyList<Unit> roster,
        int years)
    {
        ArgumentNullException.ThrowIfNull(roster);
        if (years < 0)
            throw new ArgumentOutOfRangeException(
                nameof(years), years, "years must be non-negative for time-skip semantics");

        var builder = ImmutableArray.CreateBuilder<Unit>(roster.Count);
        foreach (var u in roster)
        {
            // 生存ユニットのみ加齢、死亡 / 寿命到達は据え置き
            builder.Add(u.IsAlive ? u.WithAgeProgress(years) : u);
        }
        return builder.ToImmutable();
    }

    // ─── デフォルト予言ジェネレータ ────────────────────────────────────────

    /// <summary>
    /// 取りこぼし防止のデフォルト予言ジェネレータ。
    /// ターン番号 × 乱数で 3 種類の Prophecy を生成する。
    ///
    /// 本実装は最小構成の例示で、ProphecyMaster が翻訳されたら本ジェネレータは
    /// 「ProphecyMaster.Generate(turn, rng)」へ置き換える想定。
    /// </summary>
    public static ImmutableArray<Prophecy> DefaultGenerator(int turn, Random rng)
    {
        ArgumentNullException.ThrowIfNull(rng);

        // ProphecyKind を均等に巡回するシンプルな実装。
        var kinds = Enum.GetValues<ProphecyKind>();
        var builder = ImmutableArray.CreateBuilder<Prophecy>(OptionsPerTurn);
        for (var i = 0; i < OptionsPerTurn; i++)
        {
            var kind = kinds[rng.Next(kinds.Length)];
            var skipYears = rng.Next(DefaultMinSkipYears, DefaultMaxSkipYears + 1);
            var value = ComputeDefaultValue(kind, turn, rng);
            builder.Add(new Prophecy
            {
                Id = Guid.NewGuid(),
                Kind = kind,
                SkipYears = skipYears,
                Value = value,
                // 日本語表示テキストは Config から DescriptionKey で引く
                DescriptionKey = $"prophecy-{kind}-t{turn}-{i}",
            });
        }
        return builder.ToImmutable();
    }

    /// <summary>
    /// Kind ごとの暫定 Value 計算ヘルパー。将来 ProphecyMaster がバランス調整版を
    /// 提供したらそちらに移送する。
    /// </summary>
    private static int ComputeDefaultValue(ProphecyKind kind, int turn, Random rng) => kind switch
    {
        ProphecyKind.RewardPoints  => 2 + rng.Next(0, turn + 1),     // 2〜2+turn pt
        ProphecyKind.Battle        => Math.Max(1, turn + rng.Next(-1, 2)), // turn±1
        ProphecyKind.ScoutReward   => 1,                              // 1 人加入
        ProphecyKind.EquipmentDrop => 1 + rng.Next(0, 3),             // Lv1〜Lv3 装備
        ProphecyKind.Rest          => 1,
        _ => 1,
    };

    // ─── 内部バリデーション ────────────────────────────────────────────────

    /// <summary>
    /// ジェネレータの返却を検証し、必ず長さ OptionsPerTurn の不変配列であることを保証。
    /// </summary>
    private static ImmutableArray<Prophecy> ValidateOptions(ImmutableArray<Prophecy> options)
    {
        if (options.IsDefault || options.Length != OptionsPerTurn)
        {
            throw new InvalidOperationException(
                $"ProphecyGenerator must return exactly {OptionsPerTurn} prophecies (got {(options.IsDefault ? "default" : options.Length.ToString())}).");
        }
        return options;
    }
}
