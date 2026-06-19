// =============================================================================
//  ChronicleKnights.Tests — PointsEconomyTests.cs
// -----------------------------------------------------------------------------
//  ポイント一元経済 (PointsEconomy) の残高検証ロジックの単体テスト。
//
//  検証観点:
//    - 収入 SoT #1 (EarnFromTimeSkip = years × 1)
//    - 特殊収入 (EarnDirect) ← 戦果決算 / 予言報酬 / CoinGreed 等の共通入口
//    - 消費 (SpendPoints) の成功・残高不足例外・負コスト例外
//    - 不変性（操作後も元インスタンスが変化しない）
//
//  PointsEconomy はスカラー専用 record のため、レコード全体の値等価が信頼でき、
//  Assert.Equal(record, record) がそのまま使える。
// =============================================================================

using System;
using ChronicleKnights.Core.Managers;
using Xunit;

namespace ChronicleKnights.Tests.Core.Managers;

public class PointsEconomyTests
{
    // ─── ファクトリ ────────────────────────────────────────────────────────

    [Fact]
    public void CreateInitial_AllZero()
    {
        var economy = PointsEconomy.CreateInitial();

        Assert.Equal(0, economy.CurrentBalance);
        Assert.Equal(0, economy.TotalEarned);
        Assert.Equal(0, economy.TotalSpent);
    }

    // ─── 収入 SoT #1: タイムスキップ年次収入 ───────────────────────────────

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(4, 4)]
    [InlineData(30, 30)]
    public void EarnFromTimeSkip_AddsOnePointPerYear(int years, int expectedDelta)
    {
        var economy = PointsEconomy.CreateInitial().EarnFromTimeSkip(years);

        Assert.Equal(expectedDelta, economy.CurrentBalance);
        Assert.Equal(expectedDelta, economy.TotalEarned);
        Assert.Equal(0, economy.TotalSpent);
    }

    [Fact]
    public void EarnFromTimeSkip_NegativeYears_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => PointsEconomy.CreateInitial().EarnFromTimeSkip(-1));
    }

    // ─── 特殊収入: EarnDirect ──────────────────────────────────────────────

    [Fact]
    public void EarnDirect_AddsExactDelta()
    {
        var economy = PointsEconomy.CreateInitial().EarnDirect(7);

        Assert.Equal(7, economy.CurrentBalance);
        Assert.Equal(7, economy.TotalEarned);
        Assert.Equal(0, economy.TotalSpent);
    }

    [Fact]
    public void EarnDirect_NegativeDelta_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => PointsEconomy.CreateInitial().EarnDirect(-1));
    }

    // ─── 累計の重ね掛け ───────────────────────────────────────────────────

    [Fact]
    public void Earnings_Accumulate_AcrossMultipleIncomeSources()
    {
        var economy = PointsEconomy.CreateInitial()
            .EarnFromTimeSkip(3)   // +3  (SoT #1)
            .EarnDirect(7);        // +7  (戦果決算 / 予言報酬等の特殊経路)

        Assert.Equal(10, economy.CurrentBalance);
        Assert.Equal(10, economy.TotalEarned);
        Assert.Equal(0, economy.TotalSpent);
    }

    // ─── 残高検証: CanAfford ──────────────────────────────────────────────

    [Theory]
    [InlineData(5, 5, true)]
    [InlineData(5, 4, true)]
    [InlineData(5, 6, false)]
    [InlineData(0, 0, true)]
    public void CanAfford_ComparesBalanceToCost(int balance, int cost, bool expected)
    {
        var economy = PointsEconomy.CreateInitial().EarnDirect(balance);

        Assert.Equal(expected, economy.CanAfford(cost));
    }

    // ─── 消費: SpendPoints ─────────────────────────────────────────────────

    [Fact]
    public void SpendPoints_DeductsBalance_AndTracksTotalSpent()
    {
        var economy = PointsEconomy.CreateInitial().EarnDirect(10).SpendPoints(3);

        Assert.Equal(7, economy.CurrentBalance);
        Assert.Equal(10, economy.TotalEarned); // 獲得累計は消費で減らない
        Assert.Equal(3, economy.TotalSpent);
    }

    [Fact]
    public void SpendPoints_ExactBalance_LeavesZero()
    {
        var economy = PointsEconomy.CreateInitial().EarnDirect(3).SpendPoints(3);

        Assert.Equal(0, economy.CurrentBalance);
        Assert.Equal(3, economy.TotalSpent);
    }

    [Fact]
    public void SpendPoints_InsufficientBalance_ThrowsInvalidOperation()
    {
        var economy = PointsEconomy.CreateInitial().EarnDirect(2);

        Assert.Throws<InvalidOperationException>(() => economy.SpendPoints(3));
    }

    [Fact]
    public void SpendPoints_NegativeCost_ThrowsArgumentOutOfRange()
    {
        var economy = PointsEconomy.CreateInitial().EarnDirect(5);

        Assert.Throws<ArgumentOutOfRangeException>(() => economy.SpendPoints(-1));
    }

    // ─── 不変性 ───────────────────────────────────────────────────────────

    [Fact]
    public void Operations_DoNotMutateOriginalInstance()
    {
        var original = PointsEconomy.CreateInitial().EarnDirect(10);

        _ = original.SpendPoints(4);
        _ = original.EarnFromTimeSkip(3);

        // 元インスタンスは一切変化していない（完全イミュータブル）
        Assert.Equal(10, original.CurrentBalance);
        Assert.Equal(10, original.TotalEarned);
        Assert.Equal(0, original.TotalSpent);
    }

    [Fact]
    public void ScalarRecord_ValueEquality_Holds()
    {
        var a = new PointsEconomy { CurrentBalance = 9, TotalEarned = 11, TotalSpent = 2 };
        var b = new PointsEconomy { CurrentBalance = 9, TotalEarned = 11, TotalSpent = 2 };

        // スカラーのみの record なので値等価が信頼できる
        Assert.Equal(a, b);
    }
}
