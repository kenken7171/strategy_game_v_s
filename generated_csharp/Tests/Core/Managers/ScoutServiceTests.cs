// =============================================================================
//  ChronicleKnights.Tests — ScoutServiceTests.cs
// -----------------------------------------------------------------------------
//  外様スカウト純粋ロジック (ScoutService) の単体テスト。
//
//  検証観点:
//    - 残高十分 → ロスター +1 名・残高は cost 分だけ減算・消費累計 +cost
//    - 生成された外様の既定値（Lv1・装備なし・好感度空・未死亡・年齢/寿命レンジ）
//    - 残高不足 / 負コスト → null（例外を投げない）
//    - コスト 0 → 残高据え置きで採用成功（CanAfford(0) == true）
//    - 入力の経済・ロスターを破壊しない（不変・副作用なし）
//    - seeded Random で再現性（同一シードなら同じジョブ・年齢の外様）
// =============================================================================

using System;
using System.Collections.Immutable;
using ChronicleKnights.Core.Job;
using ChronicleKnights.Core.Managers;
using ChronicleKnights.Core.Units;
using Xunit;

namespace ChronicleKnights.Tests.Core.Managers;

public class ScoutServiceTests
{
    // テストで決定論を得るための固定シード乱数。
    private static Random SeededRng(int seed = 12345) => new(seed);

    // 残高だけを設定した経済を作る小ヘルパー（EarnDirect で残高を積む）。
    private static PointsEconomy EconomyWithBalance(int balance)
        => PointsEconomy.CreateInitial().EarnDirect(balance);

    // ─── 成功: ロスター増加 + 残高減算 ─────────────────────────────────────

    [Fact]
    public void TryScout_WithSufficientBalance_IncrementsRosterAndDeductsBalance()
    {
        var economy = EconomyWithBalance(5);
        var roster = ImmutableList<Unit>.Empty;

        var result = ScoutService.TryScout(economy, roster, cost: 3, SeededRng());

        Assert.NotNull(result);
        // ロスターは 1 名増える
        Assert.Equal(roster.Count + 1, result!.NewRoster.Count);
        // 末尾に採用ユニットが追加されている
        Assert.Equal(result.Recruit, result.NewRoster[^1]);
        // 残高は cost 分だけ減り、消費累計が増える
        Assert.Equal(2, result.NewEconomy.CurrentBalance);
        Assert.Equal(5, result.NewEconomy.TotalEarned);
        Assert.Equal(3, result.NewEconomy.TotalSpent);
    }

    [Fact]
    public void TryScout_AppendsToExistingRoster_PreservingOrder()
    {
        var existing = ScoutService.CreateOutsiderUnit(SeededRng(1));
        var roster = ImmutableList.Create(existing);
        var economy = EconomyWithBalance(10);

        var result = ScoutService.TryScout(economy, roster, cost: 4, SeededRng(2));

        Assert.NotNull(result);
        Assert.Equal(2, result!.NewRoster.Count);
        Assert.Equal(existing, result.NewRoster[0]); // 既存メンバーは先頭に保持
        Assert.Equal(result.Recruit, result.NewRoster[1]);
    }

    // ─── 採用ユニットの既定値 ─────────────────────────────────────────────

    [Fact]
    public void TryScout_Recruit_HasOutsiderDefaults()
    {
        var result = ScoutService.TryScout(
            EconomyWithBalance(3), ImmutableList<Unit>.Empty, cost: 3, SeededRng());

        Assert.NotNull(result);
        var recruit = result!.Recruit;

        Assert.Equal(Unit.InitialLevel, recruit.Level);     // Lv1
        Assert.Null(recruit.MainEquipment);                 // 装備なし
        Assert.Empty(recruit.BattleAffinity);               // 好感度なし
        Assert.False(recruit.IsDead);                       // 生存
        Assert.NotEqual(Guid.Empty, recruit.Id);            // Id 採番済み
        Assert.Contains(recruit.Job, JobMaster.DisplayOrder); // 定義済みジョブ
        Assert.Equal("name-family-outsider", recruit.LastNameKey);
        Assert.StartsWith("name-outsider-", recruit.FirstNameKey);
    }

    [Fact]
    public void CreateOutsiderUnit_AgeAndLifespan_WithinConfiguredRanges()
    {
        // 多数回生成して、年齢・寿命が常に定数レンジ内に収まることを確認。
        var rng = SeededRng(777);
        for (var i = 0; i < 500; i++)
        {
            var unit = ScoutService.CreateOutsiderUnit(rng);

            Assert.InRange(unit.Age,
                ScoutService.ScoutMinInitialAge, ScoutService.ScoutMaxInitialAge);
            Assert.InRange(unit.MaxAge,
                ScoutService.ScoutMinLifespan, ScoutService.ScoutMaxLifespan);
            // 成人かつ寿命に未到達（即戦力）であること
            Assert.True(unit.IsAlive);
        }
    }

    // ─── 失敗系: null を返す（例外を投げない） ─────────────────────────────

    [Fact]
    public void TryScout_InsufficientBalance_ReturnsNull()
    {
        var result = ScoutService.TryScout(
            EconomyWithBalance(2), ImmutableList<Unit>.Empty, cost: 3, SeededRng());

        Assert.Null(result);
    }

    [Fact]
    public void TryScout_NegativeCost_ReturnsNull()
    {
        var result = ScoutService.TryScout(
            EconomyWithBalance(100), ImmutableList<Unit>.Empty, cost: -1, SeededRng());

        Assert.Null(result);
    }

    [Fact]
    public void TryScout_ZeroCost_SucceedsWithoutDeductingBalance()
    {
        var economy = PointsEconomy.CreateInitial(); // 残高 0
        var result = ScoutService.TryScout(
            economy, ImmutableList<Unit>.Empty, cost: 0, SeededRng());

        Assert.NotNull(result);
        Assert.Single(result!.NewRoster);
        Assert.Equal(0, result.NewEconomy.CurrentBalance);
        Assert.Equal(0, result.NewEconomy.TotalSpent);
    }

    // ─── 不変性 ───────────────────────────────────────────────────────────

    [Fact]
    public void TryScout_DoesNotMutateInputs()
    {
        var economy = EconomyWithBalance(5);
        var roster = ImmutableList<Unit>.Empty;

        _ = ScoutService.TryScout(economy, roster, cost: 3, SeededRng());

        // 入力は完全に不変（新スナップショットだけが変化する）
        Assert.Equal(5, economy.CurrentBalance);
        Assert.Equal(0, economy.TotalSpent);
        Assert.Empty(roster);
    }

    // ─── 再現性（seeded Random） ───────────────────────────────────────────

    [Fact]
    public void TryScout_SameSeed_ProducesReproducibleRecruit()
    {
        var economy = EconomyWithBalance(10);
        var roster = ImmutableList<Unit>.Empty;

        var first = ScoutService.TryScout(economy, roster, cost: 3, SeededRng(2024));
        var second = ScoutService.TryScout(economy, roster, cost: 3, SeededRng(2024));

        Assert.NotNull(first);
        Assert.NotNull(second);
        // Id / 名前キーは Guid.NewGuid 由来で毎回変わるが、乱数依存の
        // ジョブ・年齢・寿命は同一シードで完全再現する。
        Assert.Equal(first!.Recruit.Job, second!.Recruit.Job);
        Assert.Equal(first.Recruit.Age, second.Recruit.Age);
        Assert.Equal(first.Recruit.MaxAge, second.Recruit.MaxAge);
    }
}
