// =============================================================================
//  ChronicleKnights.Tests — EquipmentStatCorrectionTests.cs
// -----------------------------------------------------------------------------
//  装備が戦闘ステータスへ「血を通わせる」純粋配線の網羅検証。
//  BattleManager.EquipmentAttackBonus / EquipmentDefenseBonus / EquipmentSpeedBonus
//  が既存 Equipment SoT（CurrentAttackPower 等）を floor 整数化して取り出し、
//  ResolveOffenseDamage / CalculateSquadDefense / ResolveEffectiveSpeed の素手計算へ
//  1 ビットの狂いもなく合流すること、そして「外せば元値へ完璧に戻ること」を固定する。
//
//  ★ 検証戦略（JobMaster 実値への結合を避ける差分アサート）:
//    同一ジョブの「装備あり」「装備なし」を並べ、その差 (delta) が装備ボーナスと
//    厳密一致することを確認する。基礎ステの絶対値（90 等）には依存しないため、
//    JobMaster 調整があっても本テストの意味は壊れない。
//
//  ★ 補正の孤立化:
//    - 単独ユニット大隊 → CalculateBroadcastSpeedBonus / AttackBonus = 0
//    - order = ImmutableArray<InitiativeSlot>.Empty → ResolveAttackRepetitions = 1
//    これにより装備由来の差分だけを純粋に取り出せる。
//
//  ★ Lv1 基礎ステ floor（Equipment.BaseStatsRegistry, ComputeScaledStat の Lv1=基礎値）:
//      SwordKnight  ATK 3 / DEF 1 / SPD 0
//      BowSniper    ATK 4 / DEF 0 / SPD 0
//      StaffMage    ATK 2 / DEF 0 / SPD 1
//      RingPurelove ATK 1 / DEF 0 / SPD 0
//      CoinGreed    ATK 1 / DEF 0 / SPD 0
//
//  ★ 開発憲法 ①（日本語直接書き込み禁止）順守: 文字列リテラルは ASCII のみ。
// =============================================================================

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using ChronicleKnights.Core.Job;
using ChronicleKnights.Core.Managers;
using ChronicleKnights.Core.Units;
using Xunit;

namespace ChronicleKnights.Tests.Core.Managers;

public class EquipmentStatCorrectionTests
{
    private static readonly Guid CarrierId =
        Guid.Parse("c0000000-0000-0000-0000-000000000001");
    private static readonly Guid EquipmentId =
        Guid.Parse("c0000000-0000-0000-0000-0000000000ff");

    // ─── テスト用ファクトリ ────────────────────────────────────────────────

    /// <summary>装備なしのユニット（補正ゼロの基準点）。既定は守護パッシブを持たない狙撃兵。</summary>
    private static Unit MakeBareUnit(JobId job = JobId.Sniper) => new()
    {
        Id           = CarrierId,
        Job          = job,
        Age          = 25,
        MaxAge       = 60,
        FirstNameKey = "first-a",
        LastNameKey  = "last-a",
    };

    /// <summary>指定種別の Lv1 装備を着せたユニット。</summary>
    private static Unit MakeEquippedUnit(ItemId itemId, int level = 1, JobId job = JobId.Sniper)
        => MakeBareUnit(job).WithEquipment(new Equipment
        {
            Id     = EquipmentId,
            ItemId = itemId,
            Level  = level,
        });

    // ════════════════════════════════════════════════════════════════════════
    //  1. 単体ボーナス — Lv1 各アイテムの floor 整数が 1 ビット差なく出る
    // ════════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(ItemId.SwordKnight,  3)]
    [InlineData(ItemId.BowSniper,    4)]
    [InlineData(ItemId.StaffMage,    2)]
    [InlineData(ItemId.RingPurelove, 1)]
    [InlineData(ItemId.CoinGreed,    1)]
    public void EquipmentAttackBonus_MatchesFloorOfLevelOneBase(ItemId itemId, int expected)
    {
        var unit = MakeEquippedUnit(itemId);
        Assert.Equal(expected, BattleManager.EquipmentAttackBonus(unit));
    }

    [Theory]
    [InlineData(ItemId.SwordKnight,  1)]
    [InlineData(ItemId.BowSniper,    0)]
    [InlineData(ItemId.StaffMage,    0)]
    [InlineData(ItemId.RingPurelove, 0)]
    [InlineData(ItemId.CoinGreed,    0)]
    public void EquipmentDefenseBonus_MatchesFloorOfLevelOneBase(ItemId itemId, int expected)
    {
        var unit = MakeEquippedUnit(itemId);
        Assert.Equal(expected, BattleManager.EquipmentDefenseBonus(unit));
    }

    [Theory]
    [InlineData(ItemId.SwordKnight,  0)]
    [InlineData(ItemId.BowSniper,    0)]
    [InlineData(ItemId.StaffMage,    1)]
    [InlineData(ItemId.RingPurelove, 0)]
    [InlineData(ItemId.CoinGreed,    0)]
    public void EquipmentSpeedBonus_MatchesFloorOfLevelOneBase(ItemId itemId, int expected)
    {
        var unit = MakeEquippedUnit(itemId);
        Assert.Equal(expected, BattleManager.EquipmentSpeedBonus(unit));
    }

    // ─── 2. 未装備は全補正ゼロ（番兵ガード） ──────────────────────────────

    [Fact]
    public void BareUnit_HasZeroEquipmentBonuses()
    {
        var bare = MakeBareUnit();
        Assert.Equal(0, BattleManager.EquipmentAttackBonus(bare));
        Assert.Equal(0, BattleManager.EquipmentDefenseBonus(bare));
        Assert.Equal(0, BattleManager.EquipmentSpeedBonus(bare));
    }

    // ─── 3. レベル乗算も floor で正しく合流（決定論） ─────────────────────

    [Fact]
    public void EquipmentBonuses_FollowLevelScaling_WithFloor()
    {
        // SwordKnight Lv3 ATK = floor((3+1)*1.2*1.3) = floor(6.24) = 6。
        // SwordKnight Lv3 DEF = floor((1+1)*1.2*1.3) = floor(3.12) = 3。
        var lv3Sword = MakeEquippedUnit(ItemId.SwordKnight, level: 3);
        Assert.Equal(6, BattleManager.EquipmentAttackBonus(lv3Sword));
        Assert.Equal(3, BattleManager.EquipmentDefenseBonus(lv3Sword));

        // StaffMage Lv2 SPD = floor(1+1) = 2。
        var lv2Staff = MakeEquippedUnit(ItemId.StaffMage, level: 2);
        Assert.Equal(2, BattleManager.EquipmentSpeedBonus(lv2Staff));
    }

    // ─── 4. ヌル契約 ───────────────────────────────────────────────────────

    [Fact]
    public void EquipmentBonuses_NullUnit_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => BattleManager.EquipmentAttackBonus(null!));
        Assert.Throws<ArgumentNullException>(() => BattleManager.EquipmentDefenseBonus(null!));
        Assert.Throws<ArgumentNullException>(() => BattleManager.EquipmentSpeedBonus(null!));
    }

    // ════════════════════════════════════════════════════════════════════════
    //  5. 攻撃ダメージへの合流 — 装備差分が素手攻撃へ 1 ビット差なく乗る
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public void ResolveOffenseDamage_AddsEquipmentAttack_AsExactDelta()
    {
        var bare = MakeBareUnit();
        var equipped = MakeEquippedUnit(ItemId.BowSniper); // ATK +4

        var bareBattalion = new List<Unit> { bare };
        var equippedBattalion = new List<Unit> { equipped };
        var order = ImmutableArray<InitiativeSlot>.Empty; // 反復 1 回に固定。

        var bareDamage = BattleManager.ResolveOffenseDamage(
            bare, SquadRow.RearLeft, bareBattalion, order);
        var equippedDamage = BattleManager.ResolveOffenseDamage(
            equipped, SquadRow.RearLeft, equippedBattalion, order);

        // 差分は装備 ATK ボーナスと厳密一致（基礎値そのものには依存しない）。
        Assert.Equal(BattleManager.EquipmentAttackBonus(equipped), equippedDamage - bareDamage);
        Assert.Equal(4, equippedDamage - bareDamage);
    }

    // ─── 6. 分隊守護への合流 — 職パッシブ非依存で誰が着けても効く ─────────

    [Fact]
    public void CalculateSquadDefense_AddsEquipmentDefense_EvenForNonDefensiveJob()
    {
        // 狙撃兵は SquadDefense パッシブを持たない → 素手の分隊守護は 0。
        var bareSquad = new List<Unit> { MakeBareUnit() };
        Assert.Equal(0, BattleManager.CalculateSquadDefense(bareSquad));

        // 聖剣 Lv1（DEF 1）を着せると、職パッシブが無くても分隊守護が 1 立つ。
        var equippedSquad = new List<Unit> { MakeEquippedUnit(ItemId.SwordKnight) };
        Assert.Equal(1, BattleManager.CalculateSquadDefense(equippedSquad));
    }

    // ─── 7. 実効速度への合流 — 単独大隊で撒布ゼロ、装備差分だけが乗る ─────

    [Fact]
    public void ResolveEffectiveSpeed_AddsEquipmentSpeed_AsExactDelta()
    {
        var bare = MakeBareUnit();
        var equipped = MakeEquippedUnit(ItemId.StaffMage); // SPD +1

        var bareSpeed = BattleManager.ResolveEffectiveSpeed(bare, new List<Unit> { bare });
        var equippedSpeed = BattleManager.ResolveEffectiveSpeed(
            equipped, new List<Unit> { equipped });

        Assert.Equal(BattleManager.EquipmentSpeedBonus(equipped), equippedSpeed - bareSpeed);
        Assert.Equal(1, equippedSpeed - bareSpeed);
    }

    // ════════════════════════════════════════════════════════════════════════
    //  8. 復帰の完全性 — 外せば（MainEquipment=null）元のステータスへ寸分違わず戻る
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Unequipping_RestoresAllStatsToOriginal_BitPerfect()
    {
        var bare = MakeBareUnit();
        var equipped = MakeEquippedUnit(ItemId.SwordKnight); // ATK+3 / DEF+1
        var stripped = equipped.WithEquipment(null);         // 取り外し（None 相当）。

        var order = ImmutableArray<InitiativeSlot>.Empty;

        // 攻撃力: 素手 と 取り外し後 が完全一致。
        var bareDamage = BattleManager.ResolveOffenseDamage(
            bare, SquadRow.Front, new List<Unit> { bare }, order);
        var strippedDamage = BattleManager.ResolveOffenseDamage(
            stripped, SquadRow.Front, new List<Unit> { stripped }, order);
        Assert.Equal(bareDamage, strippedDamage);

        // 分隊守護: 素手 と 取り外し後 が完全一致（どちらも 0）。
        Assert.Equal(
            BattleManager.CalculateSquadDefense(new List<Unit> { bare }),
            BattleManager.CalculateSquadDefense(new List<Unit> { stripped }));

        // 実効速度: 素手 と 取り外し後 が完全一致。
        Assert.Equal(
            BattleManager.ResolveEffectiveSpeed(bare, new List<Unit> { bare }),
            BattleManager.ResolveEffectiveSpeed(stripped, new List<Unit> { stripped }));

        // 単体ボーナスもすべてゼロへ復帰。
        Assert.Equal(0, BattleManager.EquipmentAttackBonus(stripped));
        Assert.Equal(0, BattleManager.EquipmentDefenseBonus(stripped));
        Assert.Equal(0, BattleManager.EquipmentSpeedBonus(stripped));
    }
}
