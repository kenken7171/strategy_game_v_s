// =============================================================================
//  ChronicleKnights.Tests — Core/Shop/ShopInventoryBuyTests.cs
// -----------------------------------------------------------------------------
//  「何を買うか」を先に選ぶ購入（ShopService.TryBuyEquipmentToInventory）の単体テスト。
//  ユニットを介さず、新品 Lv1 装備を持ち物（BrigadeInventory 相当の ImmutableList）へ
//  末尾追加する純粋関数。検証観点:
//    - 残高十分 → 持ち物へ Lv1 新装備を末尾追加・残高は cost 分減算・消費累計 +cost
//    - 購入装備は Lv1 / 指定 ItemId / 既存個体とは別 Guid
//    - 既存の持ち物は保持（順序不変・末尾に 1 個増える）
//    - cost 0 → 残高据え置きで成功
//    - 残高不足 / 負コスト → null（例外を投げない）・入力は不変
//    - economy / inventory が null → ArgumentNullException
//  ShopService は Godot 非依存ゆえ xUnit で実行できる。
// =============================================================================

using System;
using System.Collections.Immutable;
using ChronicleKnights.Core.Job;
using ChronicleKnights.Core.Managers;
using ChronicleKnights.Core.Shop;
using ChronicleKnights.Core.Units;
using Xunit;

namespace ChronicleKnights.Tests.Core.Shop;

public sealed class ShopInventoryBuyTests
{
    private static PointsEconomy EconomyWithBalance(int balance)
        => PointsEconomy.CreateInitial().EarnDirect(balance);

    private static Equipment MakeEquipment(ItemId itemId = ItemId.SwordKnight, int level = 1) => new()
    {
        Id     = Guid.NewGuid(),
        ItemId = itemId,
        Level  = level,
    };

    [Fact]
    public void SufficientBalance_AddsLv1ItemToInventory_AndDeductsCost()
    {
        var economy = EconomyWithBalance(5);
        var inventory = ImmutableList<Equipment>.Empty;

        var result = ShopService.TryBuyEquipmentToInventory(
            economy, inventory, ItemId.BowSniper, cost: 5);

        Assert.NotNull(result);
        Assert.Equal(Equipment.MinEquipmentLevel, result!.PurchasedEquipment.Level);
        Assert.Equal(ItemId.BowSniper, result.PurchasedEquipment.ItemId);
        Assert.Single(result.NewInventory);
        Assert.Equal(result.PurchasedEquipment.Id, result.NewInventory[0].Id);
        Assert.Equal(0, result.NewEconomy.CurrentBalance);
        Assert.Equal(5, result.NewEconomy.TotalSpent);
    }

    [Fact]
    public void KeepsExistingInventory_AndAppendsToTail()
    {
        var existing = MakeEquipment(ItemId.StaffMage, level: 3);
        var inventory = ImmutableList.Create(existing);
        var economy = EconomyWithBalance(10);

        var result = ShopService.TryBuyEquipmentToInventory(
            economy, inventory, ItemId.RingPurelove, cost: 5);

        Assert.NotNull(result);
        Assert.Equal(2, result!.NewInventory.Count);
        Assert.Equal(existing.Id, result.NewInventory[0].Id);              // 既存は先頭で保持
        Assert.Equal(result.PurchasedEquipment.Id, result.NewInventory[1].Id); // 新品は末尾
    }

    [Fact]
    public void PurchasedEquipment_HasUniqueId()
    {
        var economy = EconomyWithBalance(100);
        var inv = ImmutableList<Equipment>.Empty;

        var a = ShopService.TryBuyEquipmentToInventory(economy, inv, ItemId.SwordKnight, 5);
        var b = ShopService.TryBuyEquipmentToInventory(economy, inv, ItemId.SwordKnight, 5);

        Assert.NotNull(a);
        Assert.NotNull(b);
        Assert.NotEqual(a!.PurchasedEquipment.Id, b!.PurchasedEquipment.Id);
    }

    [Fact]
    public void ZeroCost_Succeeds_WithBalanceUnchanged()
    {
        var economy = EconomyWithBalance(0);
        var result = ShopService.TryBuyEquipmentToInventory(
            economy, ImmutableList<Equipment>.Empty, ItemId.CoinGreed, cost: 0);

        Assert.NotNull(result);
        Assert.Equal(0, result!.NewEconomy.CurrentBalance);
        Assert.Single(result.NewInventory);
    }

    [Fact]
    public void InsufficientBalance_ReturnsNull_AndDoesNotMutateInputs()
    {
        var economy = EconomyWithBalance(4); // < cost 5
        var inventory = ImmutableList<Equipment>.Empty;

        var result = ShopService.TryBuyEquipmentToInventory(
            economy, inventory, ItemId.BowSniper, cost: 5);

        Assert.Null(result);
        Assert.Equal(4, economy.CurrentBalance); // 入力は不変
        Assert.Empty(inventory);
    }

    [Fact]
    public void NegativeCost_ReturnsNull()
    {
        var economy = EconomyWithBalance(100);
        var result = ShopService.TryBuyEquipmentToInventory(
            economy, ImmutableList<Equipment>.Empty, ItemId.SwordKnight, cost: -1);
        Assert.Null(result);
    }

    [Fact]
    public void NullArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(() =>
            ShopService.TryBuyEquipmentToInventory(null!, ImmutableList<Equipment>.Empty, ItemId.SwordKnight, 5));
        Assert.Throws<ArgumentNullException>(() =>
            ShopService.TryBuyEquipmentToInventory(EconomyWithBalance(5), null!, ItemId.SwordKnight, 5));
    }
}
