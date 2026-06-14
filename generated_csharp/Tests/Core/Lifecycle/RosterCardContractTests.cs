// =============================================================================
//  ChronicleKnights.Tests — RosterCardContractTests.cs
// -----------------------------------------------------------------------------
//  拠点の現役旅団員カード（HubView ロスター）が GetAliveUnits から動的生成する際の
//  「カードの母集合」と「カードに載る ASCII ラベルの源」を純粋層で固定する。
//
//  ★ なぜ純粋層なのか:
//    HubView は Godot.Control、GetAliveUnits / RosterChanged は Godot.Node（ChronicleGlobal）
//    ゆえテストプロジェクト（Core/** のみコンパイル）から直接は叩けない。ロスター再描画の
//    ライフサイクル（RosterChanged 購読 → 台帳更地化して張り替え、_ExitTree で完全解除、
//    _rosterNodes の全 QueueFree）は論理検証で担保し、本テストは「どの旅団員がカード化されるか」
//    （= GetAliveUnits と同じ IsAlive 述語）と「カードに載るラベルが ASCII か」を包囲する。
//
//  ★ 開発憲法①順守: 文字列リテラルは ASCII のみ。
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using ChronicleKnights.Core.Managers;
using ChronicleKnights.Core.Units;
using Xunit;

namespace ChronicleKnights.Tests.Core.Lifecycle;

public class RosterCardContractTests
{
    private static Unit MakeUnit(bool isDead = false, int age = 20, int maxAge = 60, JobId job = JobId.Scout)
    {
        var unit = new Unit
        {
            Id           = Guid.NewGuid(),
            Job          = job,
            Age          = age,
            MaxAge       = maxAge,
            FirstNameKey = "name-sample-roster",
            LastNameKey  = "name-family-sample",
        };
        return isDead ? unit.MarkDeadInBattle() : unit;
    }

    [Fact]
    public void RosterCards_RenderOnlyAliveUnits_ExcludingDeadAndElders()
    {
        var alive = MakeUnit();
        var dead  = MakeUnit(isDead: true);            // IsDead → IsAlive false
        var elder = MakeUnit(age: 60, maxAge: 60);     // HasReachedMaxAge → IsAlive false

        var roster = new List<Unit> { alive, dead, elder };

        // 旅団員カードの母集合 = GetAliveUnits と同じ生存述語（IsAlive）でフィルタした集合。
        var carded = roster.Where(unit => unit.IsAlive).ToList();

        Assert.Single(carded);
        Assert.Equal(alive.Id, carded[0].Id);
    }

    [Fact]
    public void RosterCard_LabelSources_AreAscii()
    {
        var unit = MakeUnit(job: JobId.IronWallKnight);

        // カードに載るテキストの源（憲法①: ASCII）。ジョブは enum 名、レベルは既定 LV1。
        Assert.Equal("IronWallKnight", unit.Job.ToString());
        Assert.Equal(Unit.InitialLevel, unit.Level);
        Assert.True(unit.IsAlive);
    }

    [Fact]
    public void EmptyRoster_ProducesNoCards()
    {
        var roster = new List<Unit>();

        Assert.Empty(roster.Where(unit => unit.IsAlive));
    }

    // ─── 進化兵装スロット & 装備補正 POWER（BattleManager 単一 SoT 式の再利用） ──

    [Fact]
    public void EquipSlot_ReflectsEquippedItemAndLevel()
    {
        var bare = MakeUnit();
        Assert.Null(bare.EquippedItemId); // EQUIP: NONE

        var equipped = bare.WithEquipment(new Equipment
        {
            Id     = Guid.NewGuid(),
            ItemId = ItemId.SwordKnight,
            Level  = 2,
        });

        Assert.Equal(ItemId.SwordKnight, equipped.EquippedItemId); // EQUIP: SwordKnight LV2
        Assert.Equal(2, equipped.MainEquipment!.Level);
    }

    [Fact]
    public void UnitPower_EquipmentCorrection_RisesWhenUpgraded()
    {
        var bare = MakeUnit(job: JobId.IronWallKnight);
        var lv1 = bare.WithEquipment(new Equipment { Id = Guid.NewGuid(), ItemId = ItemId.SwordKnight, Level = 1 });
        var lv3 = bare.WithEquipment(new Equipment { Id = Guid.NewGuid(), ItemId = ItemId.SwordKnight, Level = 3 });

        // POWER の装備補正分 = BattleManager 単一 SoT 式の合算（カードと同式）。
        var equipPowerLv1 = BattleManager.EquipmentAttackBonus(lv1)
                          + BattleManager.EquipmentDefenseBonus(lv1)
                          + BattleManager.EquipmentSpeedBonus(lv1);
        var equipPowerLv3 = BattleManager.EquipmentAttackBonus(lv3)
                          + BattleManager.EquipmentDefenseBonus(lv3)
                          + BattleManager.EquipmentSpeedBonus(lv3);

        Assert.Equal(4, equipPowerLv1); // 聖剣 Lv1: ATK3 + DEF1 + SPD0
        Assert.Equal(9, equipPowerLv3); // 聖剣 Lv3: ATK6 + DEF3 + SPD0
        Assert.True(equipPowerLv3 > equipPowerLv1); // UPGRADE で POWER が底上げされる
    }
}
