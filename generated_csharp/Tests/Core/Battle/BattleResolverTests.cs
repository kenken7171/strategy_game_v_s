// =============================================================================
//  ChronicleKnights — BattleResolverTests.cs
// -----------------------------------------------------------------------------
//  1 ターン戦闘解決リゾルバ BattleResolver を、3 つの純粋部品の「化学反応」まで
//  含めて網羅検証する。
//
//  検証の柱:
//    1. 初期化: CreateInitial が盤面配置者のみを参加者とし、現在 HP を
//       ジョブ最大 HP（JobMaster 由来）で満たす。未配置のロスタ員は除外。
//    2. 味方攻撃 → 敵 HP 減少（不変）と、とどめ → 勝利 + ラストヒット成長。
//    3. 敵攻撃 → FRONT 行味方の HP マップ減少（防御で軽減）と、
//       致死 → 完全ロスト + 敗北。
//    4. 陣形と戦闘の化学反応: 同一初期局面でも回転の有無で
//       「敵が誰を狙うか」「味方の与ダメージ」が劇的に変わる。
//    5. 防御: null 乱数で例外、決着済みターンは無変更で返す。
//
//  ★ 乱数は 100% 外部注入。決定論を保つため、本テストの局面は
//    Lv5 装備の破壊コイントスを踏まないよう構成し（装備なしユニット）、
//    ラストヒットの乱数消費が発生しない＝環境非依存で同一結果になる。
//  ★ 開発憲法 ①（日本語直接書き込み禁止）順守: 文字列リテラルは ASCII のみ。
// =============================================================================

using System;
using System.Linq;
using ChronicleKnights.Core.Battle;
using ChronicleKnights.Core.Formation;
using ChronicleKnights.Core.Job;
using ChronicleKnights.Core.Units;
using Xunit;

namespace ChronicleKnights.Tests.Core.Battle;

public class BattleResolverTests
{
    // ─── テスト用ファクトリ ────────────────────────────────────────────────

    /// <summary>生存（Age &lt; MaxAge）・Lv1・装備なしのテストユニットを作る。</summary>
    private static Unit MakeUnit(JobId job, Guid id) => new()
    {
        Id = id,
        Job = job,
        Age = 20,
        MaxAge = 60,
        FirstNameKey = "first-test",
        LastNameKey = "last-test",
    };

    private static SlotCoordinate At(SquadRow row, int column) => new(row, column);

    // 乱数は本テスト局面では消費されない（装備なし）。固定シードで十分。
    private static Random Rng() => new(0);

    // ─── 1. 初期化 ─────────────────────────────────────────────────────────

    [Fact]
    public void CreateInitial_SeedsHitPointsToJobMaxHp_AndExcludesUnplaced()
    {
        var knightId = Guid.NewGuid();
        var benchedId = Guid.NewGuid();
        var knight = MakeUnit(JobId.IronWallKnight, knightId);
        var benched = MakeUnit(JobId.Sorcerer, benchedId);

        // 盤面には鉄壁騎士だけを配置（呪術師はロスタに居るが未配置）。
        var board = FormationBoard.Empty().WithUnitAt(At(SquadRow.Front, 0), knightId);
        var enemy = EnemyState.CreateAtFullHealth(EnemyArchetype.TrialGuardian, 1000, 30, 50);

        var snapshot = BattleResolver.CreateInitial(board, new[] { knight, benched }, enemy);

        // 鉄壁騎士: 参加者で HP は JobMaster の MaxHp (250)。
        Assert.True(snapshot.Combatants.ContainsKey(knightId));
        Assert.Equal(250, snapshot.HitPointsOf(knightId));
        Assert.True(snapshot.IsCombatantAlive(knightId));

        // 未配置の呪術師は参加者から除外される。
        Assert.False(snapshot.Combatants.ContainsKey(benchedId));

        Assert.Equal(0, snapshot.TurnNumber);
        Assert.Equal(BattleOutcome.Ongoing, snapshot.Outcome);
        Assert.False(snapshot.IsConcluded);
    }

    // ─── 2. 味方攻撃で敵 HP 減少（不変）+ とどめで勝利 ─────────────────────

    [Fact]
    public void ResolveTurn_LethalAllyOffense_EndsInVictory_AndLastHitLevelsUpKiller()
    {
        var sorcererId = Guid.NewGuid();
        var sorcerer = MakeUnit(JobId.Sorcerer, sorcererId);

        // 後衛の呪術師 (RearAttack=120) を後列へ。敵 HP=50 なので一撃で撃破。
        var board = FormationBoard.Empty().WithUnitAt(At(SquadRow.RearLeft, 0), sorcererId);
        // 敵速度 1 < 呪術師速度 15 なので呪術師が先制し、敵は反撃前に倒れる。
        var enemy = EnemyState.CreateAtFullHealth(EnemyArchetype.TrialGuardian, 50, 5, 1);
        var initial = BattleResolver.CreateInitial(board, new[] { sorcerer }, enemy);

        var result = BattleResolver.ResolveTurn(initial, rotation: null, Rng());

        // 敵は撃破され、勝利で決着。
        Assert.Equal(0, result.Snapshot.Enemy.Hp);
        Assert.True(result.Snapshot.Enemy.IsDefeated);
        Assert.Equal(BattleOutcome.BattalionVictory, result.Snapshot.Outcome);
        Assert.Equal(1, result.Snapshot.TurnNumber);

        // とどめを刺した呪術師は Lv1 → Lv2 に成長（ラストヒット）。
        Assert.Equal(2, result.Snapshot.CombatantOf(sorcererId)!.Level);

        // イベントログ: 与ダメージ 120・残 HP 0、ラストヒット、勝利決着が並ぶ。
        var offense = Assert.Single(result.Events.OfType<AllyOffenseEvent>());
        Assert.Equal(sorcererId, offense.AttackerId);
        Assert.Equal(120, offense.Damage);
        Assert.Equal(0, offense.EnemyHpAfter);
        Assert.Contains(result.Events, e => e is LastHitResolvedEvent);
        Assert.Contains(result.Events,
            e => e is BattleConcludedEvent { Outcome: BattleOutcome.BattalionVictory });

        // 入力スナップショットは 1 ミリも変わらない（完全不変）。
        Assert.Equal(50, initial.Enemy.Hp);
        Assert.Equal(BattleOutcome.Ongoing, initial.Outcome);
        Assert.Equal(1, initial.CombatantOf(sorcererId)!.Level);
    }

    // ─── 3a. 敵攻撃は防御で軽減され、致死でなければ生存 ─────────────────────

    [Fact]
    public void ResolveTurn_EnemyOffense_ReducedByBattalionAndSquadDefense_NonLethal()
    {
        var knightId = Guid.NewGuid();
        var knight = MakeUnit(JobId.IronWallKnight, knightId); // MaxHp 250, 防御 10+15

        var board = FormationBoard.Empty().WithUnitAt(At(SquadRow.Front, 0), knightId);
        // 敵速度 100 > 騎士速度 10 → 敵が先制。HP 巨大で味方は勝てない（継続）。
        var enemy = EnemyState.CreateAtFullHealth(EnemyArchetype.TrialGuardian, 100000, 60, 100);
        var initial = BattleResolver.CreateInitial(board, new[] { knight }, enemy);

        var result = BattleResolver.ResolveTurn(initial, rotation: null, Rng());

        // 60 - 大隊防御 10 - 分隊防御 15 = 35 を被弾し、250 → 215 で生存。
        var enemyOffense = Assert.Single(result.Events.OfType<EnemyOffenseEvent>());
        Assert.Equal(SquadRow.Front, enemyOffense.TargetRow);
        Assert.Equal(35, enemyOffense.DamagePerUnit);
        Assert.Equal(215, result.Snapshot.HitPointsOf(knightId));
        Assert.True(result.Snapshot.IsCombatantAlive(knightId));
        Assert.Equal(BattleOutcome.Ongoing, result.Snapshot.Outcome);

        // 反撃で敵にも傷（FrontAttack 50）。継続中。
        Assert.Equal(100000 - 50, result.Snapshot.Enemy.Hp);

        // 入力は不変。
        Assert.Equal(250, initial.HitPointsOf(knightId));
    }

    // ─── 3b. 敵攻撃が致死なら完全ロスト + 敗北 ──────────────────────────────

    [Fact]
    public void ResolveTurn_LethalEnemyOffense_DefeatsFrontUnit_AndEndsInDefeat()
    {
        var sorcererId = Guid.NewGuid();
        var sorcerer = MakeUnit(JobId.Sorcerer, sorcererId); // MaxHp 40, 防御なし

        var board = FormationBoard.Empty().WithUnitAt(At(SquadRow.Front, 0), sorcererId);
        // 敵速度 100 で先制、攻撃 100 で MaxHp 40 を確実に撃破。
        var enemy = EnemyState.CreateAtFullHealth(EnemyArchetype.TrialGuardian, 100000, 100, 100);
        var initial = BattleResolver.CreateInitial(board, new[] { sorcerer }, enemy);

        var result = BattleResolver.ResolveTurn(initial, rotation: null, Rng());

        // 唯一の前衛が倒れ、HP 0・完全ロスト・敗北で決着。
        Assert.Equal(0, result.Snapshot.HitPointsOf(sorcererId));
        Assert.False(result.Snapshot.IsCombatantAlive(sorcererId));
        Assert.True(result.Snapshot.CombatantOf(sorcererId)!.IsDead);
        Assert.Equal(BattleOutcome.BattalionDefeat, result.Snapshot.Outcome);

        Assert.Contains(result.Events, e => e is UnitDefeatedEvent u && u.UnitId == sorcererId);
        Assert.Contains(result.Events,
            e => e is BattleConcludedEvent { Outcome: BattleOutcome.BattalionDefeat });

        // 入力は不変（HP 満タン・生存）。
        Assert.Equal(40, initial.HitPointsOf(sorcererId));
        Assert.False(initial.CombatantOf(sorcererId)!.IsDead);
    }

    // ─── 4. 陣形と戦闘の化学反応（回転の有無で結果が激変） ──────────────────

    [Fact]
    public void ResolveTurn_Rotation_RedirectsEnemyTarget_AndChangesAllyDamage()
    {
        var knightId = Guid.NewGuid();   // 鉄壁騎士: Front 配置、堅い (MaxHp 250, 防御 10+15)
        var sorcererId = Guid.NewGuid(); // 呪術師:   RearLeft 配置、脆い (MaxHp 40, RA 120)
        var knight = MakeUnit(JobId.IronWallKnight, knightId);
        var sorcerer = MakeUnit(JobId.Sorcerer, sorcererId);

        var board = FormationBoard.Empty()
            .WithUnitAt(At(SquadRow.Front, 0), knightId)
            .WithUnitAt(At(SquadRow.RearLeft, 0), sorcererId);

        // 敵速度 100 で常に先制。攻撃 60、HP 巨大で勝敗はつかない（継続）。
        var enemy = EnemyState.CreateAtFullHealth(EnemyArchetype.TrialGuardian, 100000, 60, 100);
        var initial = BattleResolver.CreateInitial(board, new[] { knight, sorcerer }, enemy);

        // ── 回転なし: 敵は FRONT の鉄壁騎士を狙う（35 軽減被弾、生存）──
        //   後列の呪術師は無傷で RearAttack 120、騎士も FrontAttack 50 を入れる。
        var noRotation = BattleResolver.ResolveTurn(initial, rotation: null, Rng());

        Assert.Equal(215, noRotation.Snapshot.HitPointsOf(knightId));    // 250 - 35
        Assert.Equal(40, noRotation.Snapshot.HitPointsOf(sorcererId));   // 後列なので無傷
        Assert.True(noRotation.Snapshot.IsCombatantAlive(sorcererId));
        Assert.Equal(100000 - 120 - 50, noRotation.Snapshot.Enemy.Hp);   // 呪術師 120 + 騎士 50
        Assert.Equal(BattleOutcome.Ongoing, noRotation.Snapshot.Outcome);

        // ── 時計回り: RearLeft → Front。脆い呪術師が前面に出て狙われ即死、──
        //   騎士は RearRight へ下がり RearAttack 10 しか出せない。同じ初期局面・
        //   同じ敵なのに、回転一つで「誰が死ぬか」「総与ダメージ」が激変する。
        var rotated = BattleResolver.ResolveTurn(initial, RotationDirection.Clockwise, Rng());

        // 盤面が回転している（呪術師 → Front / 騎士 → RearRight）。
        Assert.Equal(SquadRow.Front, rotated.Snapshot.Board.CoordinateOf(sorcererId)!.Value.Row);
        Assert.Equal(SquadRow.RearRight, rotated.Snapshot.Board.CoordinateOf(knightId)!.Value.Row);

        // 敵の標的が呪術師へ移り、防御 0 で 60 被弾 → 即死。
        Assert.Equal(0, rotated.Snapshot.HitPointsOf(sorcererId));
        Assert.False(rotated.Snapshot.IsCombatantAlive(sorcererId));
        // 騎士は後列に退避して無傷。
        Assert.Equal(250, rotated.Snapshot.HitPointsOf(knightId));
        // 与ダメージは騎士の RearAttack 10 のみ（呪術師は行動前に死亡）。
        Assert.Equal(100000 - 10, rotated.Snapshot.Enemy.Hp);
        Assert.Equal(BattleOutcome.Ongoing, rotated.Snapshot.Outcome);

        // 同じ initial から 2 通りに分岐しても、initial 自身は不変。
        Assert.Equal(40, initial.HitPointsOf(sorcererId));
        Assert.Equal(250, initial.HitPointsOf(knightId));
        Assert.Equal(100000, initial.Enemy.Hp);
    }

    // ─── 5. 引数防御・決着済みターン ───────────────────────────────────────

    [Fact]
    public void ResolveTurn_NullRng_Throws()
    {
        var id = Guid.NewGuid();
        var board = FormationBoard.Empty().WithUnitAt(At(SquadRow.Front, 0), id);
        var enemy = EnemyState.CreateAtFullHealth(EnemyArchetype.TrialGuardian, 100, 10, 10);
        var initial = BattleResolver.CreateInitial(board, new[] { MakeUnit(JobId.Scout, id) }, enemy);

        Assert.Throws<ArgumentNullException>(
            () => BattleResolver.ResolveTurn(initial, rotation: null, null!));
    }

    [Fact]
    public void ResolveTurn_AlreadyConcluded_ReturnsSnapshotUnchangedWithNoEvents()
    {
        var sorcererId = Guid.NewGuid();
        var sorcerer = MakeUnit(JobId.Sorcerer, sorcererId);
        var board = FormationBoard.Empty().WithUnitAt(At(SquadRow.RearLeft, 0), sorcererId);
        var enemy = EnemyState.CreateAtFullHealth(EnemyArchetype.TrialGuardian, 50, 5, 1);
        var initial = BattleResolver.CreateInitial(board, new[] { sorcerer }, enemy);

        // 1 ターン目で勝利して決着させる。
        var victory = BattleResolver.ResolveTurn(initial, rotation: null, Rng()).Snapshot;
        Assert.Equal(BattleOutcome.BattalionVictory, victory.Outcome);

        // 決着済みスナップショットを再度解決しても、何も起きず同一インスタンスを返す。
        var again = BattleResolver.ResolveTurn(victory, rotation: null, Rng());
        Assert.Same(victory, again.Snapshot);
        Assert.Empty(again.Events);
    }
}
