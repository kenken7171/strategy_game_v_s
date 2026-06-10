// =============================================================================
//  ChronicleKnights — BattleResolver.cs
// -----------------------------------------------------------------------------
//  1 ターンの戦闘結果を「完全に不変な形」で解決する純粋オーケストレータ。
//
//  ★ 接着する 3 つの純粋コンポーネント:
//      - FormationBoard … 盤面（回転・行ごとの占有者の写像）
//      - BattleManager  … 攻防の純粋関数群（行動順・攻撃・防御・回復・ラストヒット）
//      - EnemyState     … 敵の不変スナップショット（被弾で新インスタンス）
//    本クラスはこれらを呼び合わせるだけで、自前の戦闘ルールは持たない。
//    入力 BattleSnapshot は一切変更せず、新しい BattleSnapshot と
//    イベントログを束ねた BattleTurnResult を返す。
//
//  ★ 1 ターン解決の因果フロー（ResolveTurn）:
//      ① 回転        : 作戦が指定されれば FormationBoard.Rotated で分隊を巡回。
//      ② 写像        : 盤面の各行の占有者 ID を、生存している Unit 実体へ解決。
//      ③ 行動順構築  : BattleManager.BuildInitiativeOrder で味方＋敵の席を速度降順に。
//      ④ 席の解決    : 行動順に各席を処理する。
//                        - 味方席 → ResolveOffenseDamage → 敵へ被ダメージ。
//                                    とどめなら ExecuteLastHit で成長を解決。
//                        - 敵席   → 最小コアでは FRONT 行を固定で狙い、
//                                    ResolveIncomingDamage で軽減 → HP マップ更新。
//                                    HP 0 到達は ApplyLethalDamage で完全ロスト。
//      ⑤ ターン終了回復: CalculateSquadTurnEndHeal + ApplyHealClamped を分隊ごとに。
//      ⑥ 決着判定    : 敵撃破→勝利 / 味方全滅→敗北 / それ以外→継続。
//
//  ⚠ 決定論の絶対保証（設計憲法・要件②）:
//    戦闘中の確率要素（ラストヒットの Lv5 装備破壊判定など）に使う乱数は、
//    **必ず引数で注入された System.Random** から取り出す。Random.Shared 等の
//    グローバル乱数を内部で呼ぶことは一切しない。同一の入力スナップショットと
//    同一シードの乱数を渡せば、どの環境でも 100% 同一の結果になる。
//
//  本ファイルは Godot に 1 ミリも依存しない純粋 C#。略称（BDF/SDF/AB/HL）も未使用。
// =============================================================================

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using ChronicleKnights.Core.Formation;
using ChronicleKnights.Core.Job;
using ChronicleKnights.Core.Managers;
using ChronicleKnights.Core.Units;

namespace ChronicleKnights.Core.Battle;

/// <summary>
/// 盤面・攻防純関数・敵を接着し、1 ターンの戦闘を不変に解決する純粋ファクトリ（静的）。
/// </summary>
public static class BattleResolver
{
    /// <summary>敵が最小コアで固定的に狙う行（将来は攻撃パターンで拡張）。</summary>
    public const SquadRow EnemyTargetRow = SquadRow.Front;

    // ─── 初期スナップショット生成 ─────────────────────────────────────────

    /// <summary>
    /// 戦闘開始時の静止画を生成する。盤面に配置済みのロスタ員だけを参加者とし、
    /// 各ユニットの現在 HP を所属ジョブの最大 HP（JobMaster 由来）で満たす。
    /// </summary>
    /// <param name="board">戦闘開始時の配置盤面。</param>
    /// <param name="roster">参加候補のユニット群（盤面に居る者のみ採用される）。</param>
    /// <param name="enemy">対戦相手の敵スナップショット。</param>
    /// <exception cref="ArgumentNullException">いずれかの引数が null の場合。</exception>
    public static BattleSnapshot CreateInitial(
        FormationBoard board, IReadOnlyCollection<Unit> roster, EnemyState enemy)
    {
        ArgumentNullException.ThrowIfNull(board);
        ArgumentNullException.ThrowIfNull(roster);
        ArgumentNullException.ThrowIfNull(enemy);

        var combatants = ImmutableDictionary.CreateBuilder<Guid, Unit>();
        var hitPoints = ImmutableDictionary.CreateBuilder<Guid, int>();

        foreach (var unit in roster)
        {
            if (!board.Contains(unit.Id)) continue; // 盤面に居る者だけが戦闘参加者
            combatants[unit.Id] = unit;
            hitPoints[unit.Id] = ResolveMaxHitPoints(unit);
        }

        return new BattleSnapshot
        {
            Board = board,
            Combatants = combatants.ToImmutable(),
            UnitHitPoints = hitPoints.ToImmutable(),
            Enemy = enemy,
            TurnNumber = 0,
            Outcome = BattleOutcome.Ongoing,
        };
    }

    // ─── 1 ターン解決（純粋オーケストレータ） ────────────────────────────

    /// <summary>
    /// 現在の静止画から 1 ターンを解決し、次の静止画とイベントログを返す。
    /// 入力 <paramref name="current"/> は一切変更しない（完全不変）。
    /// </summary>
    /// <param name="current">解決対象の現在スナップショット。</param>
    /// <param name="rotation">ターン冒頭に適用する回転作戦（無作戦なら null）。</param>
    /// <param name="rng">確率要素用に外部注入する乱数発生器（null 不可）。</param>
    /// <exception cref="ArgumentNullException">current または rng が null の場合。</exception>
    public static BattleTurnResult ResolveTurn(
        BattleSnapshot current, RotationDirection? rotation, Random rng)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(rng);

        var events = ImmutableArray.CreateBuilder<BattleEvent>();

        // 既に決着済みのターンは何も起こさず、入力スナップショットをそのまま返す。
        if (current.Outcome != BattleOutcome.Ongoing)
        {
            return new BattleTurnResult { Snapshot = current, Events = events.ToImmutable() };
        }

        // ── ① 回転（分隊単位ローテーション） ──────────────────────────
        var board = current.Board;
        if (rotation is { } direction)
        {
            board = board.Rotated(direction);
            events.Add(new RotationPerformedEvent(direction));
        }

        // 解決中の作業状態（すべてローカル。入力レコードは不変のまま）。
        var combatants = current.Combatants;
        var hitPoints = current.UnitHitPoints;
        var enemy = current.Enemy;
        var outcome = BattleOutcome.Ongoing;

        // ── ② 写像 + ③ 行動順構築 ──────────────────────────────────────
        //  盤面の占有者を「生存している Unit 実体」へ写し、敵 1 席を加えて速度降順に。
        var battalionAtTurnStart = AliveBattalion(board, combatants);
        var order = BattleManager.BuildInitiativeOrder(battalionAtTurnStart, enemy.Speed);

        // ── ④ 行動順に各席を解決 ──────────────────────────────────────
        foreach (var seat in order)
        {
            if (outcome != BattleOutcome.Ongoing) break; // 決着したら以降の席は行動しない

            if (seat.IsEnemy)
            {
                if (enemy.IsDefeated) continue;

                (combatants, hitPoints) =
                    ResolveEnemyOffense(board, combatants, hitPoints, enemy, events);

                // 敵の一撃で大隊が全滅したら敗北確定。
                if (!AliveBattalion(board, combatants).Any())
                {
                    outcome = BattleOutcome.BattalionDefeat;
                }
                continue;
            }

            // 味方席: 既に死亡していれば（このターンの敵攻撃等で）行動しない。
            if (!combatants.TryGetValue(seat.UnitId, out var attacker) || !attacker.IsAlive)
            {
                continue;
            }
            if (board.CoordinateOf(attacker.Id) is not { } seatCoordinate) continue;

            // バフ撒布は「現時点で生存している大隊」を基準に集計する。
            var liveBattalion = AliveBattalion(board, combatants);
            var damage = BattleManager.ResolveOffenseDamage(
                attacker, seatCoordinate.Row, liveBattalion, order);
            enemy = enemy.WithDamageTaken(damage);
            events.Add(new AllyOffenseEvent(attacker.Id, damage, enemy.Hp));

            if (enemy.IsDefeated)
            {
                // とどめ: ラストヒット成長を解決（rng はここで初めて意味を持つ）。
                var lastHit = BattleManager.ExecuteLastHit(attacker, rng);
                combatants = combatants.SetItem(attacker.Id, lastHit.NewUnit);
                events.Add(new LastHitResolvedEvent(
                    attacker.Id,
                    lastHit.LevelOverflow,
                    lastHit.ItemLevelUpTriggered,
                    lastHit.ItemDestroyed,
                    lastHit.GreedPointsStolen));
                outcome = BattleOutcome.BattalionVictory;
            }
        }

        // ── ⑤ ターン終了回復（決着していない場合のみ） ────────────────
        if (outcome == BattleOutcome.Ongoing)
        {
            hitPoints = ResolveTurnEndHeal(board, combatants, hitPoints, events);
        }

        // ── ⑥ 決着イベントの記録 ──────────────────────────────────────
        if (outcome != BattleOutcome.Ongoing)
        {
            events.Add(new BattleConcludedEvent(outcome));
        }

        var nextSnapshot = new BattleSnapshot
        {
            Board = board,
            Combatants = combatants,
            UnitHitPoints = hitPoints,
            Enemy = enemy,
            TurnNumber = current.TurnNumber + 1,
            Outcome = outcome,
        };

        return new BattleTurnResult { Snapshot = nextSnapshot, Events = events.ToImmutable() };
    }

    // ─── 内部ヘルパー ───────────────────────────────────────────────────

    /// <summary>
    /// 敵の攻撃（最小コアでは FRONT 行固定）を解決し、更新後の参加者・HP マップを返す。
    /// 軽減後ダメージは行内で一定（防御は行のメンバーで決まる）なので 1 度だけ算出し、
    /// FRONT 行の各生存ユニットへ適用する。HP 0 到達は ApplyLethalDamage で完全ロスト。
    /// </summary>
    private static (ImmutableDictionary<Guid, Unit>, ImmutableDictionary<Guid, int>)
        ResolveEnemyOffense(
            FormationBoard board,
            ImmutableDictionary<Guid, Unit> combatants,
            ImmutableDictionary<Guid, int> hitPoints,
            EnemyState enemy,
            ImmutableArray<BattleEvent>.Builder events)
    {
        var frontRowUnits = AliveUnitsInRow(board, combatants, EnemyTargetRow);

        // frontRowUnits が大隊防御（FRONT 配置時）と対象分隊防御の双方を担う。
        var damagePerUnit = BattleManager.ResolveIncomingDamage(
            enemy.Attack, frontRowUnits, frontRowUnits);
        events.Add(new EnemyOffenseEvent(EnemyTargetRow, damagePerUnit));

        foreach (var unit in frontRowUnits)
        {
            var currentHp = hitPoints.TryGetValue(unit.Id, out var hp) ? hp : 0;
            var nextHp = Math.Max(0, currentHp - damagePerUnit);
            hitPoints = hitPoints.SetItem(unit.Id, nextHp);

            if (nextHp == 0)
            {
                combatants = combatants.SetItem(unit.Id, BattleManager.ApplyLethalDamage(unit));
                events.Add(new UnitDefeatedEvent(unit.Id));
            }
            else
            {
                events.Add(new UnitDamagedEvent(unit.Id, damagePerUnit, nextHp));
            }
        }

        return (combatants, hitPoints);
    }

    /// <summary>
    /// 各分隊（行）のターン終了回復を解決し、更新後の HP マップを返す。
    /// 回復量は CalculateSquadTurnEndHeal、上限クランプは ApplyHealClamped に委譲する。
    /// </summary>
    private static ImmutableDictionary<Guid, int> ResolveTurnEndHeal(
        FormationBoard board,
        ImmutableDictionary<Guid, Unit> combatants,
        ImmutableDictionary<Guid, int> hitPoints,
        ImmutableArray<BattleEvent>.Builder events)
    {
        foreach (var row in FormationBoard.RowOrder)
        {
            var squadUnits = AliveUnitsInRow(board, combatants, row);
            if (squadUnits.IsDefaultOrEmpty) continue;

            var healAmount = BattleManager.CalculateSquadTurnEndHeal(squadUnits);
            if (healAmount <= 0) continue;

            foreach (var unit in squadUnits)
            {
                var maxHp = ResolveMaxHitPoints(unit);
                var currentHp = hitPoints.TryGetValue(unit.Id, out var hp) ? hp : 0;
                var nextHp = BattleManager.ApplyHealClamped(currentHp, maxHp, healAmount);
                if (nextHp == currentHp) continue; // 既に満タン等で変化なしなら据え置き

                hitPoints = hitPoints.SetItem(unit.Id, nextHp);
                events.Add(new UnitHealedEvent(unit.Id, nextHp - currentHp, nextHp));
            }
        }

        return hitPoints;
    }

    /// <summary>
    /// 盤面に配置され、かつ生存しているユニット実体を正準順（行 → 列）で返す。
    /// </summary>
    private static ImmutableArray<Unit> AliveBattalion(
        FormationBoard board, ImmutableDictionary<Guid, Unit> combatants)
    {
        var builder = ImmutableArray.CreateBuilder<Unit>();
        foreach (var unitId in board.PlacedUnitIds)
        {
            if (combatants.TryGetValue(unitId, out var unit) && unit.IsAlive)
            {
                builder.Add(unit);
            }
        }
        return builder.ToImmutable();
    }

    /// <summary>
    /// 指定行の生存ユニット実体を列順（0 → 2）で返す。
    /// </summary>
    private static ImmutableArray<Unit> AliveUnitsInRow(
        FormationBoard board, ImmutableDictionary<Guid, Unit> combatants, SquadRow row)
    {
        var builder = ImmutableArray.CreateBuilder<Unit>();
        for (int column = 0; column < FormationBoard.ColumnsPerRow; column++)
        {
            var occupant = board.OccupantAt(new SlotCoordinate(row, column));
            if (occupant is { } unitId
                && combatants.TryGetValue(unitId, out var unit)
                && unit.IsAlive)
            {
                builder.Add(unit);
            }
        }
        return builder.ToImmutable();
    }

    /// <summary>
    /// ユニットの最大 HP を所属ジョブ（JobMaster）から解決する。未知ジョブは 0。
    /// </summary>
    private static int ResolveMaxHitPoints(Unit unit)
    {
        var definition = JobMaster.Find(unit.Job);
        return definition?.Stats.MaxHp ?? 0;
    }
}
