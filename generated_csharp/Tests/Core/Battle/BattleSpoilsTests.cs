// =============================================================================
//  ChronicleKnights — BattleSpoilsTests.cs
// -----------------------------------------------------------------------------
//  戦果決算の差分ファクトリ BattleSpoils.FromBattle を網羅検証する。
//
//  検証の柱（開戦時 vs 終了時の Guid 突合で抽出される 4 種の不変戦果）:
//    1. 昇級         … after.Level > before.Level（1 段・複数段の両方）
//    2. 完全ロスト   … !before.IsDead && after.IsDead（戦闘死）
//    3. 装備の進化   … 同一 ItemId でレベルが上昇
//    4. 装備の喪失   … before 装備あり → after 装備なし（Lv5 破壊・戦闘死消失）
//    5. 特筆戦果なし … 全員無傷・成長なしで HasAnySpoils が false
//    6. 突合規律     … 終了時に引き当て不能な参加者は安全にスキップ
//    7. ヌル安全     … いずれかの辞書が null でも Outcome だけ反映した空決算
//    8. 完全不変     … 入力辞書は 1 ミリも変わらない
//
//  ★ 開発憲法 ①（日本語直接書き込み禁止）順守: 文字列リテラルは ASCII のみ。
//    名前キー等はすべて "first-test" のような ASCII プレースホルダを用いる。
// =============================================================================

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using ChronicleKnights.Core.Battle;
using ChronicleKnights.Core.Job;
using ChronicleKnights.Core.Units;
using Xunit;

namespace ChronicleKnights.Tests.Core.Battle;

public class BattleSpoilsTests
{
    // ─── テスト用ファクトリ ────────────────────────────────────────────────

    /// <summary>生存・Lv1・装備なしのテストユニットを作る（突合の基準点）。</summary>
    private static Unit MakeUnit(Guid id, JobId job = JobId.IronWallKnight) => new()
    {
        Id = id,
        Job = job,
        Age = 20,
        MaxAge = 60,
        FirstNameKey = "first-test",
        LastNameKey = "last-test",
    };

    /// <summary>指定 ItemId・レベルの装備を作る（個体 Guid は突合に無関係なので新規）。</summary>
    private static Equipment MakeEquipment(ItemId itemId, int level) => new()
    {
        Id = Guid.NewGuid(),
        ItemId = itemId,
        Level = level,
    };

    /// <summary>Id → Unit の不変辞書を組み立てる（開戦時/終了時の静止画用）。</summary>
    private static ImmutableDictionary<Guid, Unit> Combatants(params Unit[] units)
        => units.ToImmutableDictionary(u => u.Id);

    // ─── 1. 昇級（1 段・複数段） ───────────────────────────────────────────

    [Fact]
    public void FromBattle_DetectsSingleLevelGain()
    {
        var id = Guid.NewGuid();
        var before = MakeUnit(id);                       // Lv1
        var after = before with { Level = 2 };           // Lv1 → Lv2

        var spoils = BattleSpoils.FromBattle(
            Combatants(before), Combatants(after), BattleOutcome.BattalionVictory);

        var gain = Assert.Single(spoils.UnitLevelGains);
        Assert.Equal(id, gain.UnitId);
        Assert.Equal(1, gain.FromLevel);
        Assert.Equal(2, gain.ToLevel);
        Assert.True(spoils.HasAnySpoils);
        Assert.Equal(BattleOutcome.BattalionVictory, spoils.Outcome);

        // 昇級以外の戦果は発生していない。
        Assert.Empty(spoils.PermanentlyLostUnitIds);
        Assert.Empty(spoils.EquipmentEvolutions);
        Assert.Empty(spoils.EquipmentLosses);
    }

    [Fact]
    public void FromBattle_DetectsMultiLevelGain()
    {
        var id = Guid.NewGuid();
        var before = MakeUnit(id);                       // Lv1
        var after = before with { Level = 3 };           // Lv1 → Lv3（複数段）

        var spoils = BattleSpoils.FromBattle(
            Combatants(before), Combatants(after), BattleOutcome.BattalionVictory);

        var gain = Assert.Single(spoils.UnitLevelGains);
        Assert.Equal(1, gain.FromLevel);
        Assert.Equal(3, gain.ToLevel);
    }

    [Fact]
    public void FromBattle_NoLevelGain_WhenLevelUnchanged()
    {
        var id = Guid.NewGuid();
        var before = MakeUnit(id) with { Level = 2 };
        var after = before with { };                     // 同一レベル

        var spoils = BattleSpoils.FromBattle(
            Combatants(before), Combatants(after), BattleOutcome.BattalionVictory);

        Assert.Empty(spoils.UnitLevelGains);
    }

    // ─── 2. 完全ロスト（戦闘死） ───────────────────────────────────────────

    [Fact]
    public void FromBattle_DetectsPermanentLoss_OnBattleDeath()
    {
        var id = Guid.NewGuid();
        var before = MakeUnit(id);                       // 生存
        var after = before.MarkDeadInBattle();           // IsDead = true

        var spoils = BattleSpoils.FromBattle(
            Combatants(before), Combatants(after), BattleOutcome.BattalionDefeat);

        var lostId = Assert.Single(spoils.PermanentlyLostUnitIds);
        Assert.Equal(id, lostId);
        Assert.True(spoils.HasAnySpoils);
        Assert.Equal(BattleOutcome.BattalionDefeat, spoils.Outcome);
    }

    [Fact]
    public void FromBattle_NoLoss_WhenAlreadyDeadAtOpening()
    {
        // 開戦時点で既に IsDead（理論上の縁ケース）なら、この戦闘での新規ロストではない。
        var id = Guid.NewGuid();
        var before = MakeUnit(id).MarkDeadInBattle();
        var after = before with { };

        var spoils = BattleSpoils.FromBattle(
            Combatants(before), Combatants(after), BattleOutcome.BattalionDefeat);

        Assert.Empty(spoils.PermanentlyLostUnitIds);
    }

    // ─── 3. 装備の進化（同一 ItemId でレベル上昇） ─────────────────────────

    [Fact]
    public void FromBattle_DetectsEquipmentEvolution()
    {
        var id = Guid.NewGuid();
        var before = MakeUnit(id) with { MainEquipment = MakeEquipment(ItemId.SwordKnight, 1) };
        var after = before with { MainEquipment = MakeEquipment(ItemId.SwordKnight, 3) };

        var spoils = BattleSpoils.FromBattle(
            Combatants(before), Combatants(after), BattleOutcome.BattalionVictory);

        var evolution = Assert.Single(spoils.EquipmentEvolutions);
        Assert.Equal(id, evolution.UnitId);
        Assert.Equal(ItemId.SwordKnight, evolution.ItemId);
        Assert.Equal(1, evolution.FromLevel);
        Assert.Equal(3, evolution.ToLevel);
        Assert.True(spoils.HasAnySpoils);

        // 進化は喪失と排他（同時に喪失へは載らない）。
        Assert.Empty(spoils.EquipmentLosses);
    }

    [Fact]
    public void FromBattle_NoEvolution_WhenEquipmentLevelUnchanged()
    {
        var id = Guid.NewGuid();
        var before = MakeUnit(id) with { MainEquipment = MakeEquipment(ItemId.BowSniper, 2) };
        var after = before with { MainEquipment = MakeEquipment(ItemId.BowSniper, 2) };

        var spoils = BattleSpoils.FromBattle(
            Combatants(before), Combatants(after), BattleOutcome.BattalionVictory);

        Assert.Empty(spoils.EquipmentEvolutions);
        Assert.Empty(spoils.EquipmentLosses);
    }

    // ─── 4. 装備の喪失（Lv5 破壊・戦闘死消失） ─────────────────────────────

    [Fact]
    public void FromBattle_DetectsEquipmentLoss_OnLevelFiveDestruction()
    {
        // Lv5 装備がとどめ破壊された（before 装備あり → after 装備なし、ユニットは生存）。
        var id = Guid.NewGuid();
        var before = MakeUnit(id) with { MainEquipment = MakeEquipment(ItemId.StaffMage, 5) };
        var after = before with { MainEquipment = null };

        var spoils = BattleSpoils.FromBattle(
            Combatants(before), Combatants(after), BattleOutcome.BattalionVictory);

        var loss = Assert.Single(spoils.EquipmentLosses);
        Assert.Equal(id, loss.UnitId);
        Assert.Equal(ItemId.StaffMage, loss.ItemId);
        Assert.True(spoils.HasAnySpoils);

        // 喪失は進化と排他。
        Assert.Empty(spoils.EquipmentEvolutions);
    }

    [Fact]
    public void FromBattle_DetectsEquipmentLoss_OnBattleDeath()
    {
        // 戦闘死に伴う装備消失（完全ロストと装備喪失が同時に立つ）。
        var id = Guid.NewGuid();
        var before = MakeUnit(id) with { MainEquipment = MakeEquipment(ItemId.CoinGreed, 2) };
        var after = before with { MainEquipment = null, IsDead = true };

        var spoils = BattleSpoils.FromBattle(
            Combatants(before), Combatants(after), BattleOutcome.BattalionDefeat);

        var lostId = Assert.Single(spoils.PermanentlyLostUnitIds);
        Assert.Equal(id, lostId);
        var loss = Assert.Single(spoils.EquipmentLosses);
        Assert.Equal(ItemId.CoinGreed, loss.ItemId);
    }

    // ─── 5. 特筆戦果なし（全員無傷・成長なし） ─────────────────────────────

    [Fact]
    public void FromBattle_NoSpoils_WhenNothingChanged()
    {
        var alpha = MakeUnit(Guid.NewGuid(), JobId.IronWallKnight);
        var bravo = MakeUnit(Guid.NewGuid(), JobId.Sorcerer)
            with { MainEquipment = MakeEquipment(ItemId.SwordKnight, 2) };

        var opening = Combatants(alpha, bravo);
        // 終了時も完全同一（誰も育たず・死なず・装備も不変）。
        var final = Combatants(alpha, bravo);

        var spoils = BattleSpoils.FromBattle(opening, final, BattleOutcome.BattalionVictory);

        Assert.False(spoils.HasAnySpoils);
        Assert.Empty(spoils.UnitLevelGains);
        Assert.Empty(spoils.PermanentlyLostUnitIds);
        Assert.Empty(spoils.EquipmentEvolutions);
        Assert.Empty(spoils.EquipmentLosses);
        Assert.Equal(BattleOutcome.BattalionVictory, spoils.Outcome);
    }

    [Fact]
    public void Empty_HasNoSpoils_AndIsOngoing()
    {
        Assert.False(BattleSpoils.Empty.HasAnySpoils);
        Assert.Equal(BattleOutcome.Ongoing, BattleSpoils.Empty.Outcome);
        Assert.Empty(BattleSpoils.Empty.UnitLevelGains);
        Assert.Empty(BattleSpoils.Empty.PermanentlyLostUnitIds);
        Assert.Empty(BattleSpoils.Empty.EquipmentEvolutions);
        Assert.Empty(BattleSpoils.Empty.EquipmentLosses);
    }

    // ─── 6. 複合戦果と突合規律 ─────────────────────────────────────────────

    [Fact]
    public void FromBattle_AggregatesMixedSpoils_AcrossManyUnits()
    {
        var grower = Guid.NewGuid();   // 昇級
        var faller = Guid.NewGuid();   // 戦死 + 装備喪失
        var smith = Guid.NewGuid();    // 装備進化
        var idle = Guid.NewGuid();     // 無変化

        var growerBefore = MakeUnit(grower);
        var fallerBefore = MakeUnit(faller) with { MainEquipment = MakeEquipment(ItemId.SwordKnight, 1) };
        var smithBefore = MakeUnit(smith) with { MainEquipment = MakeEquipment(ItemId.BowSniper, 2) };
        var idleBefore = MakeUnit(idle);

        var opening = Combatants(growerBefore, fallerBefore, smithBefore, idleBefore);
        var final = Combatants(
            growerBefore with { Level = 2 },
            fallerBefore with { MainEquipment = null, IsDead = true },
            smithBefore with { MainEquipment = MakeEquipment(ItemId.BowSniper, 4) },
            idleBefore with { });

        var spoils = BattleSpoils.FromBattle(opening, final, BattleOutcome.BattalionVictory);

        Assert.Equal(grower, Assert.Single(spoils.UnitLevelGains).UnitId);
        Assert.Equal(faller, Assert.Single(spoils.PermanentlyLostUnitIds));
        Assert.Equal(smith, Assert.Single(spoils.EquipmentEvolutions).UnitId);
        Assert.Equal(faller, Assert.Single(spoils.EquipmentLosses).UnitId);
        Assert.True(spoils.HasAnySpoils);
    }

    [Fact]
    public void FromBattle_SkipsUnitsUnmatchedAtClose()
    {
        // 終了時に引き当てられない参加者は安全にスキップ（戦果に出さない）。
        var matched = Guid.NewGuid();
        var vanished = Guid.NewGuid();

        var matchedBefore = MakeUnit(matched);
        var vanishedBefore = MakeUnit(vanished);

        var opening = Combatants(matchedBefore, vanishedBefore);
        // vanished は終了時の静止画から欠落（matched のみ存在、しかも昇級）。
        var final = Combatants(matchedBefore with { Level = 2 });

        var spoils = BattleSpoils.FromBattle(opening, final, BattleOutcome.BattalionVictory);

        var gain = Assert.Single(spoils.UnitLevelGains);
        Assert.Equal(matched, gain.UnitId);
        // 引き当て不能な vanished はロストにもならない（突合できないため安全スキップ）。
        Assert.Empty(spoils.PermanentlyLostUnitIds);
    }

    // ─── 7. ヌル安全 ───────────────────────────────────────────────────────

    [Fact]
    public void FromBattle_NullOpening_ReturnsEmptyWithOutcome()
    {
        var final = Combatants(MakeUnit(Guid.NewGuid()));

        var spoils = BattleSpoils.FromBattle(null, final, BattleOutcome.BattalionDefeat);

        Assert.False(spoils.HasAnySpoils);
        Assert.Equal(BattleOutcome.BattalionDefeat, spoils.Outcome);
    }

    [Fact]
    public void FromBattle_NullFinal_ReturnsEmptyWithOutcome()
    {
        var opening = Combatants(MakeUnit(Guid.NewGuid()));

        var spoils = BattleSpoils.FromBattle(opening, null, BattleOutcome.BattalionVictory);

        Assert.False(spoils.HasAnySpoils);
        Assert.Equal(BattleOutcome.BattalionVictory, spoils.Outcome);
    }

    [Fact]
    public void FromBattle_BothNull_ReturnsEmptyWithOutcome()
    {
        var spoils = BattleSpoils.FromBattle(null, null, BattleOutcome.Ongoing);

        Assert.False(spoils.HasAnySpoils);
        Assert.Equal(BattleOutcome.Ongoing, spoils.Outcome);
    }

    // ─── 8. 完全不変（入力は変化しない） ───────────────────────────────────

    [Fact]
    public void FromBattle_DoesNotMutateInputs()
    {
        var id = Guid.NewGuid();
        var before = MakeUnit(id) with { MainEquipment = MakeEquipment(ItemId.SwordKnight, 1) };
        var after = before with { Level = 2, MainEquipment = MakeEquipment(ItemId.SwordKnight, 3) };

        var opening = Combatants(before);
        var final = Combatants(after);

        _ = BattleSpoils.FromBattle(opening, final, BattleOutcome.BattalionVictory);

        // 算出後も両静止画は 1 ミリも変わらない。
        Assert.Equal(1, opening[id].Level);
        Assert.Equal(1, opening[id].MainEquipment!.Level);
        Assert.Equal(2, final[id].Level);
        Assert.Equal(3, final[id].MainEquipment!.Level);
    }

    // ─── 9. 統合台帳（遅延算出: 戦闘中の成長 + とどめの成長を 1 枚へ合算） ─────
    //
    //  ChronicleGlobal は決算の確定を EndBattle ではなく FinalizeBattleSpoils へ
    //  遅延させ、「開戦時の参加者静止画」と「とどめ完了後の正本ロスタ」を突合する。
    //  これにより 1 枚の台帳が「ターン制戦闘の成長」と「とどめ(ResolveLastHit)の
    //  成長/喪失」の両方を含む。以下はその純粋層の算術（FromBattle が終了側に
    //  post-last-hit ロスタを受け取れば両フェーズを合算する）を検証する。
    //  終了側辞書は FinalizeBattleSpoils と同じく ToImmutableDictionary(u => u.Id)
    //  相当（テストでは Combatants ヘルパ）で組み立てる。

    [Fact]
    public void FromBattle_IntegratedLedger_SumsBattleThenLastHitGrowth()
    {
        // 開戦 Lv1 → 戦闘中に Lv2 → とどめで Lv3。開戦時 vs とどめ完了後を突合すると、
        // 両フェーズぶんが 1 件の昇級（1 → 3）として 1 枚の台帳へ合算される。
        var id = Guid.NewGuid();
        var opening = MakeUnit(id);                          // Lv1（開戦時の基準点）
        var afterBattle = opening with { Level = 2 };        // 戦闘中の成長（EndBattle 書き戻し）
        var afterLastHit = afterBattle with { Level = 3 };   // とどめの成長（ResolveLastHit）

        // FinalizeBattleSpoils は「開戦時」と「とどめ完了後ロスタ」を突合する。
        var spoils = BattleSpoils.FromBattle(
            Combatants(opening), Combatants(afterLastHit), BattleOutcome.BattalionVictory);

        var gain = Assert.Single(spoils.UnitLevelGains);
        Assert.Equal(id, gain.UnitId);
        Assert.Equal(1, gain.FromLevel);   // 開戦時の素のレベル
        Assert.Equal(3, gain.ToLevel);     // 戦闘中 + とどめ の到達レベル
    }

    [Fact]
    public void FromBattle_IntegratedLedger_CapturesLastHitOnlyGrowth()
    {
        // ★ 遅延算出の核心（退行防止）:
        //   戦闘中は無成長で、とどめだけで Lv2 → Lv3 へ昇級したケース。
        //   旧実装（EndBattle 時に「開戦時 vs 戦闘後 combatants」を突合）は、とどめが
        //   まだ走っていないため昇級を取りこぼす（Lv2 == Lv2 で差分ゼロ）。
        //   遅延算出（開戦時 vs とどめ完了後ロスタ）なら、とどめ単独の成長も確実に拾う。
        var id = Guid.NewGuid();
        var opening = MakeUnit(id) with { Level = 2 };       // 開戦 Lv2
        var afterBattle = opening with { };                  // 戦闘中は無成長（Lv2 のまま）
        var afterLastHit = afterBattle with { Level = 3 };   // とどめで Lv2 → Lv3

        // 旧来の突合相手（戦闘後 combatants）では昇級は現れない（取りこぼしの証左）。
        var legacy = BattleSpoils.FromBattle(
            Combatants(opening), Combatants(afterBattle), BattleOutcome.BattalionVictory);
        Assert.Empty(legacy.UnitLevelGains);

        // 統合台帳（とどめ完了後ロスタ）なら、とどめ単独の昇級を確実に台帳へ載せる。
        var ledger = BattleSpoils.FromBattle(
            Combatants(opening), Combatants(afterLastHit), BattleOutcome.BattalionVictory);

        var gain = Assert.Single(ledger.UnitLevelGains);
        Assert.Equal(id, gain.UnitId);
        Assert.Equal(2, gain.FromLevel);
        Assert.Equal(3, gain.ToLevel);
        Assert.True(ledger.HasAnySpoils);
    }

    [Fact]
    public void FromBattle_IntegratedLedger_CapturesLastHitEquipmentEvolution()
    {
        // とどめによる装備進化（Lv1 → Lv2）も、開戦時 vs とどめ完了後の突合で 1 枚へ載る。
        var id = Guid.NewGuid();
        var opening = MakeUnit(id) with { MainEquipment = MakeEquipment(ItemId.SwordKnight, 1) };
        var afterBattle = opening with { };                  // 戦闘中は装備不変
        var afterLastHit = afterBattle with { MainEquipment = MakeEquipment(ItemId.SwordKnight, 2) };

        var spoils = BattleSpoils.FromBattle(
            Combatants(opening), Combatants(afterLastHit), BattleOutcome.BattalionVictory);

        var evolution = Assert.Single(spoils.EquipmentEvolutions);
        Assert.Equal(id, evolution.UnitId);
        Assert.Equal(ItemId.SwordKnight, evolution.ItemId);
        Assert.Equal(1, evolution.FromLevel);
        Assert.Equal(2, evolution.ToLevel);
    }
}
