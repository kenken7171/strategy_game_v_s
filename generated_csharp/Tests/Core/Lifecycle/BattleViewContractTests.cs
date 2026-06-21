// =============================================================================
//  ChronicleKnights.Tests — BattleViewContractTests.cs
// -----------------------------------------------------------------------------
//  戦場画面（BattleView）が ChronicleGlobal.CurrentBattle（BattleSnapshot）から
//  Push バインドする値の源を純粋層で固定する。
//
//  ★ なぜ純粋層なのか:
//    BattleView は Godot.Control、CurrentBattle / BattleChanged は Godot.Node（ChronicleGlobal）
//    ゆえテストプロジェクト（Core/** のみコンパイル）から直接は叩けない。交戦ライフサイクル
//    （BattleChanged 購読 → CurrentBattle 読み直して再描画、_ExitTree で完全解除、_battleNodes 台帳の
//    全 QueueFree）は論理検証で担保し、本テストは「HP バーが映す値の源」を包囲する:
//      - 味方 HP バー: UnitHitPoints[id]（現在）／ JobMaster.Find(job).Stats.MaxHp（最大）。
//      - 敵 HP バー: EnemyState.Hp / MaxHp。
//      - 盤面に居る者だけが Combatants（= HP バー化される）。
//      - 開戦直後は Ongoing（RESOLVE TURN の IsConcluded ガードの前提）。
//
//  ★ 開発憲法①順守: 文字列リテラルは ASCII のみ。
// =============================================================================

using System;
using System.Collections.Immutable;
using ChronicleKnights.Core.Battle;
using ChronicleKnights.Core.Formation;
using ChronicleKnights.Core.Job;
using ChronicleKnights.Core.Units;
using Xunit;

namespace ChronicleKnights.Tests.Core.Lifecycle;

public class BattleViewContractTests
{
    private static Unit MakeUnit(JobId job, Guid id) => new()
    {
        Id           = id,
        Job          = job,
        // 全盛期年齢（UnitStatProfile.MaturityAge〜DeclineAge）＝成長係数 1.0。素のジョブ値で HP 契約を検証。
        Age          = 25,
        MaxAge       = 60,
        FirstNameKey = "first-battle",
        LastNameKey  = "last-battle",
    };

    [Fact]
    public void AllyHpBar_Source_IsUnitHitPointsOverJobMasterMaxHp_OnlyForSeatedUnits()
    {
        var knightId  = Guid.NewGuid();
        var benchedId = Guid.NewGuid();
        var knight  = MakeUnit(JobId.IronWallKnight, knightId);
        var benched = MakeUnit(JobId.Sorcerer, benchedId);

        // 盤面には鉄壁騎士だけを着席（呪術師はロスタに居るが未着席）。
        var board    = FormationBoard.Empty().WithUnitAt(new SlotCoordinate(SquadRow.Front, 0), knightId);
        var enemy    = EnemyState.CreateAtFullHealth(EnemyArchetype.TrialGuardian, 1000, 30, 50);
        var snapshot = BattleResolver.CreateInitial(board, new[] { knight, benched }, enemy, new Random(0));

        // BattleView は Combatants を回し、各 id の HP バーを UnitHitPoints[id] / JobMaster.MaxHp で描く。
        var maxHp = JobMaster.Find(JobId.IronWallKnight)!.Stats.MaxHp;
        Assert.True(maxHp > 0);
        Assert.True(snapshot.Combatants.ContainsKey(knightId)); // 盤面の者だけが HP バー化
        Assert.Equal(maxHp, snapshot.HitPointsOf(knightId));    // 開戦時は満タン（current == max）

        // 未着席のロスタ員は Combatants に居ない → HP バーも描かれない。
        Assert.False(snapshot.Combatants.ContainsKey(benchedId));
    }

    [Fact]
    public void EnemyHpBar_Source_IsEnemyStateHp()
    {
        var enemy = EnemyState.CreateAtFullHealth(EnemyArchetype.EternalSovereign, 500, 90, 40);

        Assert.Equal(500, enemy.MaxHp);
        Assert.Equal(500, enemy.Hp); // 満タン
        Assert.False(enemy.IsDefeated);
        Assert.True(enemy.IsAlive);
        Assert.Equal(1.0, enemy.HpRatio, 5);
    }

    [Fact]
    public void FreshSnapshot_IsOngoing_NotConcluded()
    {
        var knightId = Guid.NewGuid();
        var knight   = MakeUnit(JobId.IronWallKnight, knightId);
        var board    = FormationBoard.Empty().WithUnitAt(new SlotCoordinate(SquadRow.Front, 0), knightId);
        var enemy    = EnemyState.CreateAtFullHealth(EnemyArchetype.TrialGuardian, 1000, 30, 50);
        var snapshot = BattleResolver.CreateInitial(board, new[] { knight }, enemy, new Random(0));

        // 開戦直後は進行中。RESOLVE TURN の IsConcluded ガード（決着済みは no-op）の前提。
        Assert.Equal(0, snapshot.TurnNumber);
        Assert.False(snapshot.IsConcluded);
        Assert.Equal(BattleOutcome.Ongoing, snapshot.Outcome);
    }

    // ─── 敵意の赤枠脈動 & 戦果還流の契約 ─────────────────────────────────────

    [Fact]
    public void IntentRedFrame_TargetsOnlyUnitsInIntentRows()
    {
        var frontId = Guid.NewGuid();
        var rearId  = Guid.NewGuid();
        var board = FormationBoard.Empty()
            .WithUnitAt(new SlotCoordinate(SquadRow.Front, 0), frontId)
            .WithUnitAt(new SlotCoordinate(SquadRow.RearLeft, 0), rearId);

        // 敵意が FRONT 行だけを狙う想定（BattleView の赤枠判定と同式: 行 → Targets(row)）。
        var intent = new AttackIntent
        {
            Kind          = AttackPatternKind.SingleStrike,
            SkillNameKey  = "skill-single-strike",
            TargetRows    = ImmutableArray.Create(SquadRow.Front),
            DamagePerUnit = 10,
        };

        var frontRow = board.CoordinateOf(frontId)?.Row;
        var rearRow  = board.CoordinateOf(rearId)?.Row;

        Assert.True(frontRow is { } fr && intent.Targets(fr));  // FRONT 行 = 赤枠脈動
        Assert.False(rearRow is { } rr && intent.Targets(rr));  // REAR-L 行 = 対象外
    }

    [Fact]
    public void EmptySpoils_EarnZero_NoReflowIntoEconomy()
    {
        // 敗北・戦果なしの還流は no-op（ApplyBattleSpoils が 0 を返し経済は不変）。
        var spoils = BattleSpoils.Empty;

        Assert.False(spoils.HasAnySpoils);
        Assert.Equal(0, spoils.CalculateEarnedMarriagePoints());
    }
}
