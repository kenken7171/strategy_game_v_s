// =============================================================================
//  ChronicleKnights.Tests — EquipmentServiceTests.cs
// -----------------------------------------------------------------------------
//  装備の「無償の直接脱着」純粋ロジック EquipmentService.TryEquip / TryUnequip の
//  網羅検証。ChronicleGlobal.EquipItem / UnequipItem が叩く純粋な土台が、入力の
//  静止画から決定論的に新ロスタを織り上げることを 1 ビット単位で確認する。
//
//  検証の柱:
//    1. 装着       … 指定 Id へ Lv1 新品を装着し、本体・新ロスタ・差し替えを返す
//    2. 差し替え   … 既装備があれば ReplacedEquipment にその個体が乗る
//    3. 取り外し   … MainEquipment を null にし、外した個体を ReplacedEquipment へ
//    4. 未装備の取り外し … RosterMutated=false の no-op（ロスタは同一参照）
//    5. 並び順保持 … 触れていない旅団員は元の登録順を崩さない
//    6. 対象不在   … ロスタに居ない Id は null（no-op・例外なし）
//    7. 不変性     … 入力ロスタを一切破壊しない（新スナップショットのみ変化）
//    8. ヌル契約   … roster が null なら ArgumentNullException
//    9. 往復同型   … 装着→取り外しで MainEquipment が元の未装備状態へ完璧に戻る
//   10. 決定論注入 … equipmentId オーバーロードで個体 Guid を厳密にアサート
//
//  ★ 開発憲法 ①（日本語直接書き込み禁止）順守: 文字列リテラルは ASCII のみ。
//    名前キー等はすべて "first-a" のような ASCII プレースホルダを用いる。
// =============================================================================

using System;
using System.Collections.Immutable;
using ChronicleKnights.Core.Job;
using ChronicleKnights.Core.Units;
using Xunit;

namespace ChronicleKnights.Tests.Core.Units;

public class EquipmentServiceTests
{
    // ─── 既知の固定 Guid（個体・対象のアサートを厳密化） ───────────────────

    private static readonly Guid AlphaId =
        Guid.Parse("b0000000-0000-0000-0000-000000000001");
    private static readonly Guid BravoId =
        Guid.Parse("b0000000-0000-0000-0000-000000000002");
    private static readonly Guid CharlieId =
        Guid.Parse("b0000000-0000-0000-0000-000000000003");
    private static readonly Guid FittedEquipmentId =
        Guid.Parse("b0000000-0000-0000-0000-0000000000ff");
    private static readonly Guid PriorEquipmentId =
        Guid.Parse("b0000000-0000-0000-0000-0000000000a0");

    // ─── テスト用ファクトリ ────────────────────────────────────────────────

    /// <summary>生存・Lv1・装備なしのテストユニットを作る（脱着の基準点）。</summary>
    private static Unit MakeUnit(Guid id, JobId job = JobId.Sniper) => new()
    {
        Id           = id,
        Job          = job,
        Age          = 20,
        MaxAge       = 60,
        FirstNameKey = "first-a",
        LastNameKey  = "last-a",
    };

    /// <summary>既装備（Lv1）を着せたテストユニットを作る（差し替え・取り外しの基準点）。</summary>
    private static Unit MakeEquippedUnit(Guid id, ItemId itemId, Guid equipmentId)
        => MakeUnit(id).WithEquipment(new Equipment
        {
            Id     = equipmentId,
            ItemId = itemId,
            Level  = Equipment.MinEquipmentLevel,
        });

    // ════════════════════════════════════════════════════════════════════════
    //  1. 装着 — 指定 1 名へ Lv1 新品を着せ、本体・新ロスタ・差し替えを返す
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public void TryEquip_OnBareUnit_FitsLevelOneItem_AndMutatesRoster()
    {
        var roster = ImmutableList.Create(
            MakeUnit(AlphaId),
            MakeUnit(BravoId),
            MakeUnit(CharlieId));

        var result = EquipmentService.TryEquip(
            roster, BravoId, ItemId.SwordKnight, FittedEquipmentId);

        Assert.NotNull(result);
        Assert.True(result!.RosterMutated);
        Assert.Null(result.ReplacedEquipment); // 素手ユニットなので旧装備は無い。

        // 装着後ユニット本体（個体 Guid・種別・Lv まで決定論的に固定）。
        var fitted = result.AffectedUnit.MainEquipment;
        Assert.NotNull(fitted);
        Assert.Equal(FittedEquipmentId, fitted!.Id);
        Assert.Equal(ItemId.SwordKnight, fitted.ItemId);
        Assert.Equal(Equipment.MinEquipmentLevel, fitted.Level);

        // 派生射影（単一 SoT）も整合する。
        Assert.Equal(ItemId.SwordKnight, result.AffectedUnit.EquippedItemId);
    }

    [Fact]
    public void TryEquip_PlacesAffectedUnitAtSameIndex_AndPreservesOrder()
    {
        var roster = ImmutableList.Create(
            MakeUnit(AlphaId),
            MakeUnit(BravoId),
            MakeUnit(CharlieId));

        var result = EquipmentService.TryEquip(
            roster, BravoId, ItemId.BowSniper, FittedEquipmentId);

        Assert.NotNull(result);
        Assert.Equal(3, result!.NewRoster.Count);
        // 両端は同一参照のまま（触れていない旅団員は不変）。
        Assert.Same(roster[0], result.NewRoster[0]);
        Assert.Same(roster[2], result.NewRoster[2]);
        // 中央のみ差し替わり、本体は AffectedUnit と一致。
        Assert.Equal(BravoId, result.NewRoster[1].Id);
        Assert.Equal(result.AffectedUnit, result.NewRoster[1]);
    }

    // ─── 2. 差し替え — 既装備は ReplacedEquipment に乗る ───────────────────

    [Fact]
    public void TryEquip_OverExistingEquipment_HandsBackReplacedIndividual()
    {
        var equipped = MakeEquippedUnit(AlphaId, ItemId.StaffMage, PriorEquipmentId);
        var roster = ImmutableList.Create(equipped);

        var result = EquipmentService.TryEquip(
            roster, AlphaId, ItemId.CoinGreed, FittedEquipmentId);

        Assert.NotNull(result);
        Assert.True(result!.RosterMutated);

        // 旧装備は差し替えで手放され、個体まで一致する。
        Assert.NotNull(result.ReplacedEquipment);
        Assert.Equal(PriorEquipmentId, result.ReplacedEquipment!.Id);
        Assert.Equal(ItemId.StaffMage, result.ReplacedEquipment.ItemId);

        // 新装備は CoinGreed Lv1 へ確定。
        Assert.Equal(ItemId.CoinGreed, result.AffectedUnit.EquippedItemId);
        Assert.Equal(FittedEquipmentId, result.AffectedUnit.MainEquipment!.Id);
    }

    // ─── 3. 対象不在 → null（no-op） ──────────────────────────────────────

    [Fact]
    public void TryEquip_UnknownId_ReturnsNull()
    {
        var roster = ImmutableList.Create(MakeUnit(AlphaId));

        var result = EquipmentService.TryEquip(
            roster, BravoId, ItemId.SwordKnight, FittedEquipmentId);

        Assert.Null(result);
    }

    [Fact]
    public void TryEquip_EmptyRoster_ReturnsNull()
    {
        var result = EquipmentService.TryEquip(
            ImmutableList<Unit>.Empty, AlphaId, ItemId.SwordKnight, FittedEquipmentId);

        Assert.Null(result);
    }

    // ─── 4. 不変性（入力を破壊しない） ─────────────────────────────────────

    [Fact]
    public void TryEquip_DoesNotMutateInputRoster()
    {
        var roster = ImmutableList.Create(MakeUnit(AlphaId), MakeUnit(BravoId));

        _ = EquipmentService.TryEquip(roster, AlphaId, ItemId.SwordKnight, FittedEquipmentId);

        // 入力ロスタは完全に不変（着けたのは新スナップショットだけ）。
        Assert.Equal(2, roster.Count);
        Assert.Null(roster[0].MainEquipment);
        Assert.Null(roster[1].MainEquipment);
    }

    // ─── 5. ヌル契約 ───────────────────────────────────────────────────────

    [Fact]
    public void TryEquip_NullRoster_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => EquipmentService.TryEquip(null!, AlphaId, ItemId.SwordKnight, FittedEquipmentId));
    }

    [Fact]
    public void TryEquip_DefaultOverload_AssignsFreshGuid()
    {
        var roster = ImmutableList.Create(MakeUnit(AlphaId));

        var result = EquipmentService.TryEquip(roster, AlphaId, ItemId.RingPurelove);

        Assert.NotNull(result);
        // 既定オーバーロードは Guid.NewGuid を採番する（空 Guid ではない）。
        Assert.NotEqual(Guid.Empty, result!.AffectedUnit.MainEquipment!.Id);
        Assert.Equal(ItemId.RingPurelove, result.AffectedUnit.EquippedItemId);
    }

    // ════════════════════════════════════════════════════════════════════════
    //  6. 取り外し — MainEquipment を null にし、外した個体を返す
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public void TryUnequip_OnEquippedUnit_StripsEquipment_AndMutatesRoster()
    {
        var equipped = MakeEquippedUnit(AlphaId, ItemId.SwordKnight, PriorEquipmentId);
        var roster = ImmutableList.Create(equipped, MakeUnit(BravoId));

        var result = EquipmentService.TryUnequip(roster, AlphaId);

        Assert.NotNull(result);
        Assert.True(result!.RosterMutated);
        // 本体は素手化（MainEquipment == null）。
        Assert.Null(result.AffectedUnit.MainEquipment);
        Assert.Null(result.AffectedUnit.EquippedItemId);
        // 外した個体は ReplacedEquipment へ（Guid まで一致）。
        Assert.NotNull(result.ReplacedEquipment);
        Assert.Equal(PriorEquipmentId, result.ReplacedEquipment!.Id);
        Assert.Equal(ItemId.SwordKnight, result.ReplacedEquipment.ItemId);
        // 触れていない 2 人目は不変。
        Assert.Same(roster[1], result.NewRoster[1]);
    }

    // ─── 7. 未装備からの取り外し → no-op（RosterMutated=false） ────────────

    [Fact]
    public void TryUnequip_OnBareUnit_IsNoOp_AndKeepsSameRosterReference()
    {
        var roster = ImmutableList.Create(MakeUnit(AlphaId), MakeUnit(BravoId));

        var result = EquipmentService.TryUnequip(roster, AlphaId);

        Assert.NotNull(result);
        Assert.False(result!.RosterMutated);
        Assert.Null(result.ReplacedEquipment);
        // ロスタは作り直さず同一参照を返す（発火抑止情報）。
        Assert.Same(roster, result.NewRoster);
        Assert.Same(roster[0], result.AffectedUnit);
    }

    // ─── 8. 対象不在・ヌル契約 ─────────────────────────────────────────────

    [Fact]
    public void TryUnequip_UnknownId_ReturnsNull()
    {
        var roster = ImmutableList.Create(MakeUnit(AlphaId));

        var result = EquipmentService.TryUnequip(roster, BravoId);

        Assert.Null(result);
    }

    [Fact]
    public void TryUnequip_EmptyRoster_ReturnsNull()
    {
        var result = EquipmentService.TryUnequip(ImmutableList<Unit>.Empty, AlphaId);

        Assert.Null(result);
    }

    [Fact]
    public void TryUnequip_NullRoster_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => EquipmentService.TryUnequip(null!, AlphaId));
    }

    [Fact]
    public void TryUnequip_DoesNotMutateInputRoster()
    {
        var equipped = MakeEquippedUnit(AlphaId, ItemId.BowSniper, PriorEquipmentId);
        var roster = ImmutableList.Create(equipped);

        _ = EquipmentService.TryUnequip(roster, AlphaId);

        // 入力ロスタの該当ユニットは依然として装備済み（破壊されていない）。
        Assert.NotNull(roster[0].MainEquipment);
        Assert.Equal(PriorEquipmentId, roster[0].MainEquipment!.Id);
    }

    // ════════════════════════════════════════════════════════════════════════
    //  9. 往復同型 — 装着 → 取り外しで元の未装備状態へ完璧に戻る
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public void EquipThenUnequip_RestoresBareUnitExactly()
    {
        var original = MakeUnit(AlphaId);
        var roster = ImmutableList.Create(original);

        var equipResult = EquipmentService.TryEquip(
            roster, AlphaId, ItemId.SwordKnight, FittedEquipmentId);
        Assert.NotNull(equipResult);

        var unequipResult = EquipmentService.TryUnequip(equipResult!.NewRoster, AlphaId);
        Assert.NotNull(unequipResult);

        // 装着で持ち込んだ個体が取り外しで丸ごと返ってくる。
        Assert.Equal(FittedEquipmentId, unequipResult!.ReplacedEquipment!.Id);
        // 取り外し後のユニットは最初の素手ユニットと完全等価（record の値等価）。
        Assert.Null(unequipResult.AffectedUnit.MainEquipment);
        Assert.Equal(original, unequipResult.AffectedUnit);
    }
}
