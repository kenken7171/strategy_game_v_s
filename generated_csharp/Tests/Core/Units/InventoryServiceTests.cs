// =============================================================================
//  ChronicleKnights.Tests — InventoryServiceTests.cs
// -----------------------------------------------------------------------------
//  旅団共有の持ち物（インベントリ）とユニット装備の間で、装備個体を非破壊に
//  往復させる純粋層 InventoryService の単体テスト。
//
//  検証観点:
//    - EquipFromInventory: 持ち物 → ユニット装着。旧装備は持ち物へ戻る（個体保存則）。
//    - UnequipToInventory: ユニット → 持ち物。未装備は no-op（Mutated=false）。
//    - 個体保存則: 操作前後で「装備中 + 持ち物」の総個体数は不変。
//    - ヌル安全: 不在ユニット・不在装備は null（no-op・例外を投げない）。
// =============================================================================

using System;
using System.Collections.Immutable;
using ChronicleKnights.Core.Job;
using ChronicleKnights.Core.Units;
using Xunit;

namespace ChronicleKnights.Tests.Core.Units;

public class InventoryServiceTests
{
    private static readonly Guid UnitA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid UnitB = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid SwordId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid BowId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private static Unit MakeUnit(Guid id, Equipment? equip = null) => new()
    {
        Id = id,
        Job = JobId.IronWallKnight,
        Age = 25,
        MaxAge = 60,
        FirstNameKey = "name-test",
        LastNameKey = "name-family-test",
        MainEquipment = equip,
    };

    private static Equipment MakeItem(Guid id, ItemId item = ItemId.SwordKnight, int level = 1) => new()
    {
        Id = id,
        ItemId = item,
        Level = level,
    };

    // ─── EquipFromInventory ────────────────────────────────────────────────

    [Fact]
    public void EquipFromInventory_MovesItemFromInventoryToBareUnit()
    {
        var roster = ImmutableList.Create(MakeUnit(UnitA));
        var inventory = ImmutableList.Create(MakeItem(SwordId));

        var result = InventoryService.EquipFromInventory(roster, inventory, UnitA, SwordId);

        Assert.NotNull(result);
        Assert.True(result!.Mutated);
        Assert.Equal(SwordId, result.AffectedUnit.MainEquipment!.Id);
        Assert.Empty(result.NewInventory); // 持ち物から取り出された。
    }

    [Fact]
    public void EquipFromInventory_SwapsOldEquipmentBackToInventory()
    {
        var oldItem = MakeItem(BowId, ItemId.BowSniper);
        var roster = ImmutableList.Create(MakeUnit(UnitA, oldItem));
        var inventory = ImmutableList.Create(MakeItem(SwordId));

        var result = InventoryService.EquipFromInventory(roster, inventory, UnitA, SwordId);

        Assert.NotNull(result);
        Assert.Equal(SwordId, result!.AffectedUnit.MainEquipment!.Id);  // 新装備を装着。
        Assert.Single(result.NewInventory);                              // 旧装備が戻り、個数 1 を維持。
        Assert.Equal(BowId, result.NewInventory[0].Id);                  // 戻ったのは旧装備。
    }

    [Fact]
    public void EquipFromInventory_UnknownUnit_ReturnsNull()
    {
        var roster = ImmutableList.Create(MakeUnit(UnitA));
        var inventory = ImmutableList.Create(MakeItem(SwordId));

        Assert.Null(InventoryService.EquipFromInventory(roster, inventory, UnitB, SwordId));
    }

    [Fact]
    public void EquipFromInventory_ItemNotInInventory_ReturnsNull()
    {
        var roster = ImmutableList.Create(MakeUnit(UnitA));
        var inventory = ImmutableList.Create(MakeItem(SwordId));

        Assert.Null(InventoryService.EquipFromInventory(roster, inventory, UnitA, BowId));
    }

    // ─── UnequipToInventory ────────────────────────────────────────────────

    [Fact]
    public void UnequipToInventory_MovesEquippedItemBackToInventory()
    {
        var item = MakeItem(SwordId);
        var roster = ImmutableList.Create(MakeUnit(UnitA, item));
        var inventory = ImmutableList<Equipment>.Empty;

        var result = InventoryService.UnequipToInventory(roster, inventory, UnitA);

        Assert.NotNull(result);
        Assert.True(result!.Mutated);
        Assert.Null(result.AffectedUnit.MainEquipment);  // ユニットは素手に。
        Assert.Single(result.NewInventory);              // 装備は消えず持ち物へ。
        Assert.Equal(SwordId, result.NewInventory[0].Id);
    }

    [Fact]
    public void UnequipToInventory_BareUnit_IsNoOp()
    {
        var roster = ImmutableList.Create(MakeUnit(UnitA));
        var inventory = ImmutableList.Create(MakeItem(SwordId));

        var result = InventoryService.UnequipToInventory(roster, inventory, UnitA);

        Assert.NotNull(result);
        Assert.False(result!.Mutated);
        Assert.Same(roster, result.NewRoster);
        Assert.Same(inventory, result.NewInventory);
    }

    [Fact]
    public void UnequipToInventory_UnknownUnit_ReturnsNull()
    {
        var roster = ImmutableList.Create(MakeUnit(UnitA));
        var inventory = ImmutableList<Equipment>.Empty;

        Assert.Null(InventoryService.UnequipToInventory(roster, inventory, UnitB));
    }

    // ─── 個体保存則（装備中 + 持ち物 の総数は不変） ───────────────────────

    [Fact]
    public void EquipThenUnequip_ConservesTotalItemCount()
    {
        var roster = ImmutableList.Create(MakeUnit(UnitA), MakeUnit(UnitB, MakeItem(BowId, ItemId.BowSniper)));
        var inventory = ImmutableList.Create(MakeItem(SwordId));

        // 開始時: 装備中 1（UnitB の弓）+ 持ち物 1（剣）= 総数 2。
        var equipped = InventoryService.EquipFromInventory(roster, inventory, UnitA, SwordId)!;
        var totalAfterEquip = CountEquipped(equipped.NewRoster) + equipped.NewInventory.Count;
        Assert.Equal(2, totalAfterEquip);

        var unequipped = InventoryService.UnequipToInventory(
            equipped.NewRoster, equipped.NewInventory, UnitB)!;
        var totalAfterUnequip = CountEquipped(unequipped.NewRoster) + unequipped.NewInventory.Count;
        Assert.Equal(2, totalAfterUnequip);
    }

    private static int CountEquipped(ImmutableList<Unit> roster)
    {
        var n = 0;
        foreach (var u in roster) if (u.MainEquipment is not null) n++;
        return n;
    }
}
