// =============================================================================
//  ChronicleKnights.Tests — ShopServiceTests.cs
// -----------------------------------------------------------------------------
//  旅団兵器廠の純粋ロジック (ShopService) の単体テスト。
//
//  検証観点:
//    [購入 TryBuyEquipment]
//      - 残高十分        → 対象へ Lv1 新装備を装着・残高は cost 分だけ減算・消費累計 +cost
//      - 装備済みへ購入  → 旧装備が ReplacedEquipment で返り、対象は新装備に差し替わる
//      - 未装備へ購入    → ReplacedEquipment は null
//      - 残高不足 / 負コスト / 対象不在 → null（例外を投げない）
//      - コスト 0        → 残高据え置きで購入成功
//      - economy / roster が null → ArgumentNullException
//      - 入力の経済・ロスターを破壊しない（不変・副作用なし）・並び順保持
//    [強化 TryUpgradeEquipment]
//      - 残高十分        → 現装備が Lv+1（Id は不変）・残高は cost 分だけ減算
//      - 装備なし / 上限到達 / 対象不在 / 残高不足 / 負コスト → null
//      - 入力を破壊しない（不変・副作用なし）
//    [コスト SoT]
//      - UpgradeCostFor は現レベルに比例（BaseUpgradeCost × level）
//
//  ★ 開発憲法 ①（日本語直接書き込み禁止）順守: 文字列リテラルは ASCII のみ。
//    名前キー等はすべて "first-test" のような ASCII プレースホルダを用いる。
// =============================================================================

using System;
using System.Collections.Immutable;
using ChronicleKnights.Core.Job;
using ChronicleKnights.Core.Managers;
using ChronicleKnights.Core.Shop;
using ChronicleKnights.Core.Units;
using Xunit;

namespace ChronicleKnights.Tests.Core.Shop;

public class ShopServiceTests
{
    // ─── テスト用ファクトリ ────────────────────────────────────────────────

    // 残高だけを設定した経済を作る小ヘルパー（EarnDirect で残高を積む）。
    private static PointsEconomy EconomyWithBalance(int balance)
        => PointsEconomy.CreateInitial().EarnDirect(balance);

    // 生存・Lv1 のテストユニット（装備は引数で差し込む）。
    private static Unit MakeUnit(Guid id, Equipment? equipment = null) => new()
    {
        Id            = id,
        Job           = JobId.IronWallKnight,
        Age           = 25,
        MaxAge        = 60,
        FirstNameKey  = "first-test",
        LastNameKey   = "last-test",
        MainEquipment = equipment,
    };

    // 固有 Guid を持つ装備（種別・レベルは引数で指定）。
    private static Equipment MakeEquipment(ItemId itemId = ItemId.SwordKnight, int level = 1) => new()
    {
        Id     = Guid.NewGuid(),
        ItemId = itemId,
        Level  = level,
    };

    // ════════════════════════════════════════════════════════════════════════
    //  購入 (TryBuyEquipment)
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public void TryBuyEquipment_WithSufficientBalance_EquipsAndDeductsCost()
    {
        var id = Guid.NewGuid();
        var roster = ImmutableList.Create(MakeUnit(id));            // 未装備
        var economy = EconomyWithBalance(5);

        var result = ShopService.TryBuyEquipment(
            economy, roster, id, ItemId.BowSniper, cost: 5);

        Assert.NotNull(result);
        // 新装備は Lv1 / 指定 ItemId
        Assert.Equal(Equipment.MinEquipmentLevel, result!.PurchasedEquipment.Level);
        Assert.Equal(ItemId.BowSniper, result.PurchasedEquipment.ItemId);
        // 対象に装着され、購入装備と同一個体
        Assert.NotNull(result.EquippedUnit.MainEquipment);
        Assert.Equal(result.PurchasedEquipment.Id, result.EquippedUnit.MainEquipment!.Id);
        // ロスターは件数据え置きで対象だけ差し替わる
        Assert.Equal(roster.Count, result.NewRoster.Count);
        Assert.Equal(result.PurchasedEquipment.Id, result.NewRoster[0].MainEquipment!.Id);
        // 未装備への購入なので旧装備はなし
        Assert.Null(result.ReplacedEquipment);
        // 残高は cost 分だけ減り、消費累計が増える
        Assert.Equal(0, result.NewEconomy.CurrentBalance);
        Assert.Equal(5, result.NewEconomy.TotalSpent);
    }

    [Fact]
    public void TryBuyEquipment_OntoEquippedUnit_ReturnsReplacedEquipment()
    {
        var id = Guid.NewGuid();
        var oldEquip = MakeEquipment(ItemId.SwordKnight, level: 3);
        var roster = ImmutableList.Create(MakeUnit(id, oldEquip));   // 装備済み
        var economy = EconomyWithBalance(10);

        var result = ShopService.TryBuyEquipment(
            economy, roster, id, ItemId.StaffMage, cost: 5);

        Assert.NotNull(result);
        // 旧装備は差し替えで返却（完全ロストの素材）
        Assert.NotNull(result!.ReplacedEquipment);
        Assert.Equal(oldEquip.Id, result.ReplacedEquipment!.Id);
        Assert.Equal(ItemId.SwordKnight, result.ReplacedEquipment.ItemId);
        // 対象は新装備（別種別・別 Guid）へ差し替わる
        Assert.Equal(ItemId.StaffMage, result.EquippedUnit.MainEquipment!.ItemId);
        Assert.NotEqual(oldEquip.Id, result.EquippedUnit.MainEquipment.Id);
    }

    [Fact]
    public void TryBuyEquipment_InsufficientBalance_ReturnsNull()
    {
        var id = Guid.NewGuid();
        var roster = ImmutableList.Create(MakeUnit(id));

        var result = ShopService.TryBuyEquipment(
            EconomyWithBalance(4), roster, id, ItemId.SwordKnight, cost: 5);

        Assert.Null(result);
    }

    [Fact]
    public void TryBuyEquipment_NegativeCost_ReturnsNull()
    {
        var id = Guid.NewGuid();
        var roster = ImmutableList.Create(MakeUnit(id));

        var result = ShopService.TryBuyEquipment(
            EconomyWithBalance(100), roster, id, ItemId.SwordKnight, cost: -1);

        Assert.Null(result);
    }

    [Fact]
    public void TryBuyEquipment_MissingUnit_ReturnsNull()
    {
        var roster = ImmutableList.Create(MakeUnit(Guid.NewGuid()));

        var result = ShopService.TryBuyEquipment(
            EconomyWithBalance(100), roster, Guid.NewGuid(), ItemId.SwordKnight, cost: 5);

        Assert.Null(result);
    }

    [Fact]
    public void TryBuyEquipment_ZeroCost_SucceedsWithoutDeductingBalance()
    {
        var id = Guid.NewGuid();
        var roster = ImmutableList.Create(MakeUnit(id));
        var economy = PointsEconomy.CreateInitial(); // 残高 0

        var result = ShopService.TryBuyEquipment(
            economy, roster, id, ItemId.CoinGreed, cost: 0);

        Assert.NotNull(result);
        Assert.Equal(0, result!.NewEconomy.CurrentBalance);
        Assert.Equal(0, result.NewEconomy.TotalSpent);
        Assert.NotNull(result.EquippedUnit.MainEquipment);
    }

    [Fact]
    public void TryBuyEquipment_NullArguments_Throw()
    {
        var roster = ImmutableList.Create(MakeUnit(Guid.NewGuid()));
        var economy = EconomyWithBalance(5);

        Assert.Throws<ArgumentNullException>(
            () => ShopService.TryBuyEquipment(null!, roster, Guid.NewGuid(), ItemId.SwordKnight, 5));
        Assert.Throws<ArgumentNullException>(
            () => ShopService.TryBuyEquipment(economy, null!, Guid.NewGuid(), ItemId.SwordKnight, 5));
    }

    [Fact]
    public void TryBuyEquipment_DoesNotMutateInputs()
    {
        var id = Guid.NewGuid();
        var roster = ImmutableList.Create(MakeUnit(id));
        var economy = EconomyWithBalance(5);

        _ = ShopService.TryBuyEquipment(economy, roster, id, ItemId.SwordKnight, cost: 5);

        // 入力は完全に不変（新スナップショットだけが変化する）
        Assert.Equal(5, economy.CurrentBalance);
        Assert.Equal(0, economy.TotalSpent);
        Assert.Null(roster[0].MainEquipment); // 元ユニットは未装備のまま
    }

    [Fact]
    public void TryBuyEquipment_PreservesRosterOrder()
    {
        var first = MakeUnit(Guid.NewGuid());
        var target = MakeUnit(Guid.NewGuid());
        var last = MakeUnit(Guid.NewGuid());
        var roster = ImmutableList.Create(first, target, last);

        var result = ShopService.TryBuyEquipment(
            EconomyWithBalance(5), roster, target.Id, ItemId.SwordKnight, cost: 5);

        Assert.NotNull(result);
        Assert.Equal(3, result!.NewRoster.Count);
        // 前後の隣人は同一個体のまま、対象（index 1）だけ装着される。
        Assert.Equal(first.Id, result.NewRoster[0].Id);
        Assert.Null(result.NewRoster[0].MainEquipment);
        Assert.Equal(target.Id, result.NewRoster[1].Id);
        Assert.NotNull(result.NewRoster[1].MainEquipment);
        Assert.Equal(last.Id, result.NewRoster[2].Id);
        Assert.Null(result.NewRoster[2].MainEquipment);
    }

    // ════════════════════════════════════════════════════════════════════════
    //  強化 (TryUpgradeEquipment)
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public void TryUpgradeEquipment_WithSufficientBalance_IncrementsLevelKeepingId()
    {
        var id = Guid.NewGuid();
        var equip = MakeEquipment(ItemId.SwordKnight, level: 2);
        var roster = ImmutableList.Create(MakeUnit(id, equip));
        var cost = ShopService.UpgradeCostFor(2); // 2 × 2 = 4（デフレ物価 BaseUpgradeCost=2）
        var economy = EconomyWithBalance(cost);

        var result = ShopService.TryUpgradeEquipment(economy, roster, id, cost);

        Assert.NotNull(result);
        // レベルは +1、装備の個体 Id は不変（同じ装備が育つ）。
        Assert.Equal(3, result!.UpgradedEquipment.Level);
        Assert.Equal(equip.Id, result.UpgradedEquipment.Id);
        Assert.Equal(equip.Id, result.UpgradedUnit.MainEquipment!.Id);
        Assert.Equal(3, result.UpgradedUnit.MainEquipment.Level);
        // 残高は cost 分だけ減る。
        Assert.Equal(0, result.NewEconomy.CurrentBalance);
        Assert.Equal(cost, result.NewEconomy.TotalSpent);
        // ロスターは件数据え置きで対象だけ差し替わる。
        Assert.Equal(roster.Count, result.NewRoster.Count);
    }

    [Fact]
    public void TryUpgradeEquipment_NoEquipment_ReturnsNull()
    {
        var id = Guid.NewGuid();
        var roster = ImmutableList.Create(MakeUnit(id)); // 未装備

        var result = ShopService.TryUpgradeEquipment(
            EconomyWithBalance(100), roster, id, cost: 3);

        Assert.Null(result);
    }

    [Fact]
    public void TryUpgradeEquipment_AtMaxLevel_ReturnsNull()
    {
        var id = Guid.NewGuid();
        var maxed = MakeEquipment(ItemId.SwordKnight, level: Equipment.MaxEquipmentLevel);
        var roster = ImmutableList.Create(MakeUnit(id, maxed));

        var result = ShopService.TryUpgradeEquipment(
            EconomyWithBalance(100), roster, id, cost: 3);

        Assert.Null(result); // 上限到達は浪費防止で失敗
    }

    [Fact]
    public void TryUpgradeEquipment_MissingUnit_ReturnsNull()
    {
        var roster = ImmutableList.Create(MakeUnit(Guid.NewGuid(), MakeEquipment(level: 2)));

        var result = ShopService.TryUpgradeEquipment(
            EconomyWithBalance(100), roster, Guid.NewGuid(), cost: 3);

        Assert.Null(result);
    }

    [Fact]
    public void TryUpgradeEquipment_InsufficientBalance_ReturnsNull()
    {
        var id = Guid.NewGuid();
        var roster = ImmutableList.Create(MakeUnit(id, MakeEquipment(level: 2)));

        var result = ShopService.TryUpgradeEquipment(
            EconomyWithBalance(2), roster, id, cost: 6);

        Assert.Null(result);
    }

    [Fact]
    public void TryUpgradeEquipment_NegativeCost_ReturnsNull()
    {
        var id = Guid.NewGuid();
        var roster = ImmutableList.Create(MakeUnit(id, MakeEquipment(level: 2)));

        var result = ShopService.TryUpgradeEquipment(
            EconomyWithBalance(100), roster, id, cost: -1);

        Assert.Null(result);
    }

    [Fact]
    public void TryUpgradeEquipment_DoesNotMutateInputs()
    {
        var id = Guid.NewGuid();
        var equip = MakeEquipment(ItemId.SwordKnight, level: 2);
        var roster = ImmutableList.Create(MakeUnit(id, equip));
        var economy = EconomyWithBalance(6);

        _ = ShopService.TryUpgradeEquipment(economy, roster, id, cost: 6);

        Assert.Equal(6, economy.CurrentBalance);
        Assert.Equal(0, economy.TotalSpent);
        Assert.Equal(2, roster[0].MainEquipment!.Level); // 元装備は Lv2 のまま
    }

    // ════════════════════════════════════════════════════════════════════════
    //  コスト SoT (UpgradeCostFor)
    // ════════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(1, 2)]
    [InlineData(2, 4)]
    [InlineData(3, 6)]
    [InlineData(4, 8)]
    public void UpgradeCostFor_ScalesLinearlyWithCurrentLevel(int level, int expectedCost)
    {
        Assert.Equal(expectedCost, ShopService.UpgradeCostFor(level));
        // 単一 SoT の式（BaseUpgradeCost × level）を満たす。
        Assert.Equal(ShopService.BaseUpgradeCost * level, ShopService.UpgradeCostFor(level));
    }
}
