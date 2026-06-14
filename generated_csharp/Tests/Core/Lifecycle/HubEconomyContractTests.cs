// =============================================================================
//  ChronicleKnights.Tests — HubEconomyContractTests.cs
// -----------------------------------------------------------------------------
//  拠点経済ダッシュボード（HubView）が CurrentEconomy から Push バインドする 3 値
//  （Balance / Earned / Spent）の健全性を純粋層で固定する。
//
//  ★ なぜ純粋層なのか:
//    HubView は Godot.Control、CurrentEconomy / EconomyChanged は Godot.Node（ChronicleGlobal）
//    ゆえテストプロジェクト（Core/** のみコンパイル）から直接は叩けない。経済バインドの
//    ライフサイクル（EconomyChanged 購読 → 再描画、_ExitTree で完全解除、CountUp の Tween が
//    ラベルへバインドされ QueueFree で自動失効）は論理検証で担保し、本テストは「ダッシュボードが
//    映す SoT 値」が獲得・累計で正しく更新され内的に整合することを包囲する。
//
//  ★ 開発憲法①順守: 文字列リテラルは ASCII のみ。
// =============================================================================

using ChronicleKnights.Core.Managers;
using Xunit;

namespace ChronicleKnights.Tests.Core.Lifecycle;

public class HubEconomyContractTests
{
    [Fact]
    public void Earn_IncrementsBalanceAndEarned_LeavingSpentUntouched()
    {
        var after = PointsEconomy.CreateInitial().EarnDirect(7);

        Assert.Equal(7, after.CurrentBalance); // 残高 = ダッシュボード "Balance:"
        Assert.Equal(7, after.TotalEarned);    // 累計獲得 = "Earned:"
        Assert.Equal(0, after.TotalSpent);     // 累計消費 = "Spent:"（獲得では動かない）
    }

    [Fact]
    public void DashboardValues_StayCoherent_AcrossConsecutiveEarnings()
    {
        var economy = PointsEconomy.CreateInitial().EarnDirect(10).EarnDirect(5);

        Assert.Equal(15, economy.CurrentBalance);
        Assert.Equal(15, economy.TotalEarned);
        Assert.Equal(0, economy.TotalSpent);

        // ダッシュボードの内的整合: Balance == Earned - Spent。
        Assert.Equal(economy.TotalEarned - economy.TotalSpent, economy.CurrentBalance);
    }

    [Fact]
    public void FreshEconomy_ShowsAllZero()
    {
        var economy = PointsEconomy.CreateInitial();

        // 新規開始直後のダッシュボードは Balance/Earned/Spent すべて 0。
        Assert.Equal(0, economy.CurrentBalance);
        Assert.Equal(0, economy.TotalEarned);
        Assert.Equal(0, economy.TotalSpent);
    }
}
