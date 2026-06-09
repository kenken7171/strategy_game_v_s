// =============================================================================
//  ChronicleKnights.Tests — BattlePassiveTests.cs
// -----------------------------------------------------------------------------
//  TypeScript 版 passive_ability.test.ts の C# 移植。ジョブ・パッシブの結合挙動を
//  JobMaster の実データ値で厳密検証し、戦闘解決の信頼性を数値で固定する。
//
//  検証する 3 大パッシブ（TS 版と同一の観点）:
//    1. 狙撃兵 × 戦術官の速度支援 → 連続攻撃（二の矢）成立
//       戦術官の InitiativeBuff 撒布が狙撃兵の実効速度を押し上げ、行動順の先頭を
//       奪取 → ConsecutiveStrike が発動して攻撃回数が 2 倍になる。
//    2. 鉄壁騎士のダメージ軽減（BattalionDefense + SquadDefense）
//       前衛の鉄壁騎士が大隊全体軽減と自分隊軽減を加算で提供する。
//    3. 衛生兵の継続回復（TurnEndSquadHeal）
//       自分隊への毎ターン終了回復と、最大 HP での頭打ち（clamp）。
//
//  ★ C# 版の設計差（TS との違い）:
//    - Unit は戦闘ステータスも現在 HP も持たない。能力値はすべて JobMaster.All から
//      引く。よって本テストは「実 JobMaster 値 + 純粋関数」で組み立てる。
//    - Squad / Enemy クラスは存在しないため、行動順はユニット粒度で構築し、
//      敵は整数 1 つ（敵速度）でモデル化する。回復・ダメージは整数入力で検証する。
//  ★ 表示文字列は一切使わず、能力値・回数・ダメージの整数のみで判定（ASCII 規約）。
// =============================================================================

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using ChronicleKnights.Core.Job;
using ChronicleKnights.Core.Managers;
using ChronicleKnights.Core.Units;
using Xunit;

namespace ChronicleKnights.Tests.Core.Managers;

public class BattlePassiveTests
{
    // ─── 既知の固定 Guid（行動順の先頭判定をアサートしやすくするため） ──────

    private static readonly Guid SniperId =
        Guid.Parse("a0000000-0000-0000-0000-000000000001");
    private static readonly Guid TacticianId =
        Guid.Parse("a0000000-0000-0000-0000-000000000002");
    private static readonly Guid BearerId =
        Guid.Parse("a0000000-0000-0000-0000-000000000003");
    private static readonly Guid IronWallId =
        Guid.Parse("a0000000-0000-0000-0000-000000000004");
    private static readonly Guid MedicId =
        Guid.Parse("a0000000-0000-0000-0000-000000000005");
    private static readonly Guid ScoutId =
        Guid.Parse("a0000000-0000-0000-0000-000000000006");
    private static readonly Guid SorcererId =
        Guid.Parse("a0000000-0000-0000-0000-000000000007");

    private static Unit MakeUnit(JobId job, Guid id, bool isDead = false)
    {
        var unit = new Unit
        {
            Id           = id,
            Job          = job,
            Age          = 25,
            MaxAge       = 60,
            FirstNameKey = "name-sample-passive",
            LastNameKey  = "name-family-sample",
        };
        return isDead ? unit.MarkDeadInBattle() : unit;
    }

    // ════════════════════════════════════════════════════════════════════════
    //  1. 狙撃兵 × 戦術官 — 速度支援が連続攻撃を成立させる
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public void TacticianBroadcastsSpeedAndAttackToSniper_NotToSelf()
    {
        var sniper = MakeUnit(JobId.Sniper, SniperId);
        var tactician = MakeUnit(JobId.Tactician, TacticianId);
        var battalion = new List<Unit> { sniper, tactician };

        // 戦術官 → 狙撃兵: 速度 +35 (Tactician.Speed), 攻撃 +20 (Tactician.InitiativeBuff)。
        Assert.Equal(35, BattleManager.CalculateBroadcastSpeedBonus(sniper, battalion));
        Assert.Equal(20, BattleManager.CalculateBroadcastAttackBonus(sniper, battalion));

        // 狙撃兵は InitiativeBuff を持たないので、戦術官へは何も撒かない（自己除外含む）。
        Assert.Equal(0, BattleManager.CalculateBroadcastSpeedBonus(tactician, battalion));
        Assert.Equal(0, BattleManager.CalculateBroadcastAttackBonus(tactician, battalion));
    }

    [Fact]
    public void SniperEffectiveSpeed_RisesWithTacticianSupport()
    {
        var sniper = MakeUnit(JobId.Sniper, SniperId);
        var tactician = MakeUnit(JobId.Tactician, TacticianId);

        // 単独: 基礎速度 40 のみ。
        Assert.Equal(40, BattleManager.ResolveEffectiveSpeed(
            sniper, new List<Unit> { sniper }));

        // 戦術官同伴: 40 + 35 = 75。
        Assert.Equal(75, BattleManager.ResolveEffectiveSpeed(
            sniper, new List<Unit> { sniper, tactician }));
    }

    [Fact]
    public void InitiativeBuffStacks_FromMultipleBroadcasters()
    {
        var sniper = MakeUnit(JobId.Sniper, SniperId);
        var tactician = MakeUnit(JobId.Tactician, TacticianId);
        var bearer = MakeUnit(JobId.StandardBearer, BearerId);
        var battalion = new List<Unit> { sniper, tactician, bearer };

        // 速度: Tactician.Speed(35) + StandardBearer.Speed(20) = 55。
        Assert.Equal(55, BattleManager.CalculateBroadcastSpeedBonus(sniper, battalion));
        // 攻撃: Tactician.InitiativeBuff(20) + StandardBearer.InitiativeBuff(40) = 60。
        Assert.Equal(60, BattleManager.CalculateBroadcastAttackBonus(sniper, battalion));
        // 実効速度 = 40 + 55 = 95。
        Assert.Equal(95, BattleManager.ResolveEffectiveSpeed(sniper, battalion));
    }

    [Fact]
    public void SniperWithoutSupport_DoesNotLeadFasterEnemy_SoSingleStrike()
    {
        var sniper = MakeUnit(JobId.Sniper, SniperId);
        var battalion = new List<Unit> { sniper };

        // 敵速度 50 > 狙撃兵 40 → 先頭は敵。連続攻撃は不発（1 回）。
        var order = BattleManager.BuildInitiativeOrder(battalion, enemySpeed: 50);

        Assert.True(order[0].IsEnemy);
        Assert.Equal(1, BattleManager.ResolveAttackRepetitions(sniper, order));
    }

    [Fact]
    public void SniperWithTacticianSupport_LeadsInitiative_SoConsecutiveStrike()
    {
        var sniper = MakeUnit(JobId.Sniper, SniperId);
        var tactician = MakeUnit(JobId.Tactician, TacticianId);
        var battalion = new List<Unit> { sniper, tactician };

        // 速度: 狙撃兵 75 > 敵 50 > 戦術官 35 → 先頭は狙撃兵。
        var order = BattleManager.BuildInitiativeOrder(battalion, enemySpeed: 50);

        Assert.False(order[0].IsEnemy);
        Assert.Equal(SniperId, order[0].UnitId);
        // ConsecutiveStrike 発動 → 2 回攻撃。
        Assert.Equal(2, BattleManager.ResolveAttackRepetitions(sniper, order));
    }

    [Fact]
    public void SniperOffenseDamage_DoublesWhenConsecutiveStrikeTriggers()
    {
        var sniper = MakeUnit(JobId.Sniper, SniperId);
        var tactician = MakeUnit(JobId.Tactician, TacticianId);
        var battalion = new List<Unit> { sniper, tactician };

        // 支援あり・先頭奪取: (RearAttack 90 + 攻撃バフ 20) × 2 回 = 220。
        var leadOrder = BattleManager.BuildInitiativeOrder(battalion, enemySpeed: 50);
        Assert.Equal(220, BattleManager.ResolveOffenseDamage(
            sniper, SquadRow.RearLeft, battalion, leadOrder));

        // 支援なし・敵が速い: (RearAttack 90 + 0) × 1 回 = 90。
        var soloBattalion = new List<Unit> { sniper };
        var soloOrder = BattleManager.BuildInitiativeOrder(soloBattalion, enemySpeed: 50);
        Assert.Equal(90, BattleManager.ResolveOffenseDamage(
            sniper, SquadRow.RearLeft, soloBattalion, soloOrder));
    }

    [Fact]
    public void NonConsecutiveStrikeJob_NeverDoubles_EvenWhenLeading()
    {
        var scout = MakeUnit(JobId.Scout, ScoutId);
        var battalion = new List<Unit> { scout };

        // 斥候は最速 (60) で先頭を取るが ConsecutiveStrike を持たない → 常に 1 回。
        var order = BattleManager.BuildInitiativeOrder(battalion, enemySpeed: 10);
        Assert.False(order[0].IsEnemy);
        Assert.Equal(ScoutId, order[0].UnitId);
        Assert.Equal(1, BattleManager.ResolveAttackRepetitions(scout, order));
    }

    [Fact]
    public void InitiativeTieBreak_FavorsAllyOverEnemy()
    {
        var scout = MakeUnit(JobId.Scout, ScoutId);
        var battalion = new List<Unit> { scout };

        // 斥候 60 と敵 60 が同速 → 安定ソートで味方が先頭（味方優先のタイブレーク）。
        var order = BattleManager.BuildInitiativeOrder(battalion, enemySpeed: 60);
        Assert.False(order[0].IsEnemy);
        Assert.Equal(ScoutId, order[0].UnitId);
    }

    [Fact]
    public void DeadBroadcaster_ProvidesNoBuff()
    {
        var sniper = MakeUnit(JobId.Sniper, SniperId);
        var deadTactician = MakeUnit(JobId.Tactician, TacticianId, isDead: true);
        var battalion = new List<Unit> { sniper, deadTactician };

        // 戦死した戦術官はバフを撒かない。
        Assert.Equal(0, BattleManager.CalculateBroadcastSpeedBonus(sniper, battalion));
        Assert.Equal(0, BattleManager.CalculateBroadcastAttackBonus(sniper, battalion));
        Assert.Equal(40, BattleManager.ResolveEffectiveSpeed(sniper, battalion));
    }

    // ════════════════════════════════════════════════════════════════════════
    //  2. 鉄壁騎士 — ダメージ軽減（BattalionDefense + SquadDefense）
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public void IronWall_ProvidesBattalionAndSquadDefense()
    {
        var ironWall = MakeUnit(JobId.IronWallKnight, IronWallId);
        var front = new List<Unit> { ironWall };

        // 大隊軽減 10、分隊軽減 15（JobMaster の実値）。
        Assert.Equal(10, BattleManager.CalculateBattalionDefense(front));
        Assert.Equal(15, BattleManager.CalculateSquadDefense(front));
    }

    [Fact]
    public void IncomingDamage_ReducedByBattalionDefenseOnly_ForRearSquad()
    {
        var ironWall = MakeUnit(JobId.IronWallKnight, IronWallId);
        var sorcerer = MakeUnit(JobId.Sorcerer, SorcererId);
        var front = new List<Unit> { ironWall };
        var rearSquad = new List<Unit> { sorcerer }; // 呪術師に SquadDefense なし

        // 後衛分隊への被弾: 100 - BattalionDefense(10) - SquadDefense(0) = 90。
        Assert.Equal(90, BattleManager.ResolveIncomingDamage(100, front, rearSquad));
    }

    [Fact]
    public void IncomingDamage_ReducedByBattalionAndSquadDefense_ForFrontSquad()
    {
        var ironWall = MakeUnit(JobId.IronWallKnight, IronWallId);
        var front = new List<Unit> { ironWall };

        // 前衛分隊（鉄壁自身が属する）への被弾: 100 - 10 - 15 = 75。
        Assert.Equal(75, BattleManager.ResolveIncomingDamage(100, front, front));
    }

    [Fact]
    public void IncomingDamage_NeverBelowMinimumFloor()
    {
        var ironWall = MakeUnit(JobId.IronWallKnight, IronWallId);
        var front = new List<Unit> { ironWall };

        // 5 - 25 = -20 だが最低保証 1 で下げ止まる。
        Assert.Equal(BattleManager.MinimumDamageAfterReduction,
            BattleManager.ResolveIncomingDamage(5, front, front));
    }

    [Fact]
    public void SquadDefense_StacksAcrossHolders()
    {
        var ironWall = MakeUnit(JobId.IronWallKnight, IronWallId);
        var bearer = MakeUnit(JobId.StandardBearer, BearerId); // SquadDefense 5
        var squad = new List<Unit> { ironWall, bearer };

        // 鉄壁 15 + 旗手 5 = 20。
        Assert.Equal(20, BattleManager.CalculateSquadDefense(squad));
    }

    [Fact]
    public void DeadIronWall_ProvidesNoDefense()
    {
        var deadIronWall = MakeUnit(JobId.IronWallKnight, IronWallId, isDead: true);
        var front = new List<Unit> { deadIronWall };

        Assert.Equal(0, BattleManager.CalculateBattalionDefense(front));
        Assert.Equal(0, BattleManager.CalculateSquadDefense(front));
        // 軽減ゼロ → ダメージは素通り。
        Assert.Equal(100, BattleManager.ResolveIncomingDamage(100, front, front));
    }

    [Fact]
    public void NonDefensiveJob_ProvidesNoDefense()
    {
        var sniper = MakeUnit(JobId.Sniper, SniperId);
        var front = new List<Unit> { sniper };

        Assert.Equal(0, BattleManager.CalculateBattalionDefense(front));
        Assert.Equal(0, BattleManager.CalculateSquadDefense(front));
    }

    // ════════════════════════════════════════════════════════════════════════
    //  3. 衛生兵 — 継続回復（TurnEndSquadHeal）
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Medic_ProvidesSquadTurnEndHeal()
    {
        var medic = MakeUnit(JobId.Medic, MedicId);
        var sorcerer = MakeUnit(JobId.Sorcerer, SorcererId);
        var squad = new List<Unit> { medic, sorcerer };

        // 衛生兵 1 名: 30（呪術師は回復を持たない）。
        Assert.Equal(30, BattleManager.CalculateSquadTurnEndHeal(squad));
    }

    [Fact]
    public void MultipleMedics_StackHeal()
    {
        var medicA = MakeUnit(JobId.Medic, MedicId);
        var medicB = MakeUnit(JobId.Medic, ScoutId); // 別 Id で 2 人目の衛生兵
        var squad = new List<Unit> { medicA, medicB };

        Assert.Equal(60, BattleManager.CalculateSquadTurnEndHeal(squad));
    }

    [Fact]
    public void SquadWithoutMedic_HealsZero()
    {
        var sorcerer = MakeUnit(JobId.Sorcerer, SorcererId);
        var squad = new List<Unit> { sorcerer };

        Assert.Equal(0, BattleManager.CalculateSquadTurnEndHeal(squad));
    }

    [Fact]
    public void DeadMedic_ProvidesNoHeal()
    {
        var deadMedic = MakeUnit(JobId.Medic, MedicId, isDead: true);
        var squad = new List<Unit> { deadMedic };

        Assert.Equal(0, BattleManager.CalculateSquadTurnEndHeal(squad));
    }

    [Theory]
    [InlineData(5, 40, 30, 35)]    // 5 + 30 = 35（上限 40 未満）
    [InlineData(30, 40, 30, 40)]   // 30 + 30 = 60 → 上限 40 で頭打ち
    [InlineData(0, 100, 0, 0)]     // 回復 0 は据え置き
    [InlineData(40, 40, 30, 40)]   // 既に満タンなら据え置き
    public void ApplyHealClamped_AddsHealWithMaxCap(
        int currentHp, int maxHp, int heal, int expected)
    {
        Assert.Equal(expected, BattleManager.ApplyHealClamped(currentHp, maxHp, heal));
    }

    [Fact]
    public void ApplyHealClamped_NegativeHeal_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => BattleManager.ApplyHealClamped(10, 100, -1));
    }

    [Fact]
    public void MedicHeal_RestoresWoundedSorcererWithinMaxHp()
    {
        var medic = MakeUnit(JobId.Medic, MedicId);
        var sorcerer = MakeUnit(JobId.Sorcerer, SorcererId);
        var squad = new List<Unit> { medic, sorcerer };

        // 呪術師 (MaxHp 40) が HP 5 まで負傷 → 衛生兵の 30 回復で 35 へ。
        var sorcererMaxHp = JobMaster.All[JobId.Sorcerer].Stats.MaxHp;
        var heal = BattleManager.CalculateSquadTurnEndHeal(squad);

        Assert.Equal(40, sorcererMaxHp);
        Assert.Equal(30, heal);
        Assert.Equal(35, BattleManager.ApplyHealClamped(5, sorcererMaxHp, heal));
    }
}
