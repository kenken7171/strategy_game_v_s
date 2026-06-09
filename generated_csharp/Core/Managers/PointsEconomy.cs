// =============================================================================
//  ChronicleKnights — PointsEconomy.cs
// -----------------------------------------------------------------------------
//  ポイント (Points) 一元経済の完全不変な財布。
//
//  ハクスラ・ローグライト設計憲法 (docs/MIGRATION_GODOT_HACK_AND_SLASH.md
//  セクション 4 改) の「ポイント一元経済」へと旧 MarriagePointsSystem を
//  全面リファクタリングしたもの。
//
//  ★ ポイントは単一通貨であり、用途を問わず以下の消費先で共有される:
//      - スカウト（新ユニット採用）
//      - 結婚（手動婚姻、MarriageService 経由）
//      - ユニット成長（任意レベルアップ等の将来拡張）
//      - 装備強化（Lv1〜4 のレベルアップ強化等の将来拡張）
//
//  ★ 収入は 2 つの SoT 式のみで規定される（特殊効果は別経路）:
//      [SoT #1] EarnFromTimeSkip(years)  : 経過年数 × YearlyMinimumIncomePerYear
//                                          (= 1 pt / 年)
//      [SoT #2] EarnFromKill(enemyLevel) : floor(enemyLevel * KillRewardMultiplier)
//                                          (= floor(Lv * 1.5))
//      [Special] EarnDirect(delta)       : CoinGreed の Lv5 LH 強奪等、特殊効果
//                                          の例外経路（SoT 式の外側）
//
//  消費は単一の汎用エントリで:
//      SpendPoints(cost) : 残高不足なら InvalidOperationException、それ以外は
//                          残高から cost を減算した新 PointsEconomy を返す。
//
//  状態保持は CurrentBalance / TotalEarned / TotalSpent の 3 つのみ。
//  TotalEarned と TotalSpent はバランス検証・実績計算のための累計値。
// =============================================================================

using System;

namespace ChronicleKnights.Core.Managers;

/// <summary>
/// 全消費先（スカウト・結婚・ユニット成長・装備強化）が共有する単一通貨の
/// 不変な財布。
/// </summary>
public sealed record PointsEconomy
{
    // ─── 収入系の SoT 定数 ────────────────────────────────────────────────

    /// <summary>
    /// [SoT #2] 敵撃破ポイントの倍率係数。
    /// 計算式: delta = floor(enemyLevel * KillRewardMultiplier)
    /// </summary>
    public const double KillRewardMultiplier = 1.5;

    /// <summary>
    /// [SoT #1] 予言タイムスキップ時に保証される 1 年あたりの収入。
    /// 計算式: delta = years * YearlyMinimumIncomePerYear
    /// </summary>
    public const int YearlyMinimumIncomePerYear = 1;

    // ─── 状態（不変） ─────────────────────────────────────────────────────

    /// <summary>現在の保有ポイント残高。SpendPoints で減算される。</summary>
    public required int CurrentBalance { get; init; }

    /// <summary>累計獲得ポイント（実績・統計用、減算されない）。</summary>
    public required int TotalEarned { get; init; }

    /// <summary>累計消費ポイント（実績・統計用、Spend のたびに増加）。</summary>
    public required int TotalSpent { get; init; }

    // ─── ファクトリ ────────────────────────────────────────────────────────

    /// <summary>残高 0 / 累計 0 の初期状態を作成。</summary>
    public static PointsEconomy CreateInitial() => new()
    {
        CurrentBalance = 0,
        TotalEarned    = 0,
        TotalSpent     = 0,
    };

    // ─── 収入: SoT #1（タイムスキップ年次収入） ───────────────────────────

    /// <summary>
    /// [SoT #1] 予言タイムスキップで経過した年数に応じて、定期収入を加算する。
    /// 計算式: delta = years * YearlyMinimumIncomePerYear (= years * 1)
    /// </summary>
    /// <param name="years">経過年数（Prophecy.SkipYears の値、非負）</param>
    public PointsEconomy EarnFromTimeSkip(int years)
    {
        if (years < 0)
            throw new ArgumentOutOfRangeException(
                nameof(years), years, "years must be non-negative");
        var delta = years * YearlyMinimumIncomePerYear;
        return AddToBalance(delta);
    }

    // ─── 収入: SoT #2（敵撃破報酬） ───────────────────────────────────────

    /// <summary>
    /// [SoT #2] 敵を撃破した際に、敵レベルから算出された撃破報酬を加算する。
    /// 計算式: delta = floor(enemyLevel * KillRewardMultiplier) (= floor(Lv * 1.5))
    /// </summary>
    /// <param name="enemyLevel">撃破した敵のレベル（非負）</param>
    public PointsEconomy EarnFromKill(int enemyLevel)
    {
        if (enemyLevel < 0)
            throw new ArgumentOutOfRangeException(
                nameof(enemyLevel), enemyLevel, "enemy level must be non-negative");
        var delta = (int)Math.Floor(enemyLevel * KillRewardMultiplier);
        return AddToBalance(delta);
    }

    // ─── 特殊効果収入（SoT 外、明示的エントリ） ──────────────────────────

    /// <summary>
    /// SoT 外の特殊効果による直接加算（例: CoinGreed の Lv5 LH での +1 pt 強奪）。
    /// 通常の財政ルートとは別経路として、呼び出し側が明示的に呼ぶことを想定。
    /// </summary>
    /// <param name="delta">加算量（正の整数）</param>
    public PointsEconomy EarnDirect(int delta)
    {
        if (delta < 0)
            throw new ArgumentOutOfRangeException(
                nameof(delta), delta, "delta must be non-negative; use SpendPoints to subtract");
        return AddToBalance(delta);
    }

    // ─── 消費（共通サイフ） ──────────────────────────────────────────────

    /// <summary>指定コストを支払う余裕があるか（純粋判定）。</summary>
    public bool CanAfford(int cost) => CurrentBalance >= cost;

    /// <summary>
    /// 共通サイフからポイントを消費する単一の窓口。スカウト・結婚・ユニット
    /// 成長・装備強化など、全消費先がこのメソッドを通る。
    /// 残高不足の場合は InvalidOperationException で拒否する。
    /// </summary>
    /// <param name="cost">消費する正の整数ポイント数</param>
    public PointsEconomy SpendPoints(int cost)
    {
        if (cost < 0)
            throw new ArgumentOutOfRangeException(
                nameof(cost), cost, "cost must be non-negative");
        if (!CanAfford(cost))
            throw new InvalidOperationException(
                $"Not enough points: need {cost}, have {CurrentBalance}");
        return this with
        {
            CurrentBalance = CurrentBalance - cost,
            TotalSpent     = TotalSpent + cost,
        };
    }

    // ─── 内部ヘルパー ─────────────────────────────────────────────────────

    /// <summary>残高と累計獲得を同時に更新する共通実装（DRY）。</summary>
    private PointsEconomy AddToBalance(int delta)
        => this with
        {
            CurrentBalance = CurrentBalance + delta,
            TotalEarned    = TotalEarned + delta,
        };
}
