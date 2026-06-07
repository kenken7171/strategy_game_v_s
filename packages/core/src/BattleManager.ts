import { Squad } from "./models/Squad";
import { Unit } from "./models/Unit";
import { Enemy, EnemyAction } from "./models/Enemy";
import { JOB_PASSIVES } from "./data/jobs";

// ─── 公開型 ─────────────────────────────────────────────────────────────────

export interface SlotResult {
  readonly hits: number;
  readonly damageTaken: number;
  readonly defeated: boolean;
}

export interface ActionResult {
  readonly perSlot: Readonly<Record<string, SlotResult>>;
}

export interface AttackForecast {
  readonly actionName: string;
  readonly targetSlotIds: "RANDOM" | "ALL" | "NONE" | string[];
  readonly multiTargetMode?: "spread" | "random";
}

export interface UnitAttackLog {
  readonly unitId: string;
  readonly unitName: string;
  readonly slotId: string;
  readonly attackPower: number;
  readonly damageDealt: number;
  readonly isDoubleAttack: boolean;
}

export interface SquadOffenseResult {
  readonly squadId: string;
  readonly attackLogs: ReadonlyArray<UnitAttackLog>;
  readonly totalDamage: number;
  readonly enemyDefeated: boolean;
}

export interface InitiativeEntry {
  readonly type: "enemy" | "squad";
  readonly id: string;
  readonly speed: number;
}

export interface HealLog {
  readonly squadId: string;
  readonly healAmount: number;
}

export interface IntegratedTurnResult {
  readonly turn: number;
  readonly initiativeOrder: ReadonlyArray<InitiativeEntry>;
  readonly enemyActionResult?: ActionResult;
  readonly squadOffenseResults: ReadonlyArray<SquadOffenseResult>;
  readonly victory: boolean;
  readonly healLogs: ReadonlyArray<HealLog>;
}

// ─── BattleManager ─────────────────────────────────────────────────────────
//
// 1 ターンの戦闘解決を担うクラス。
//
// 設計方針:
//   - 「ジョブ固有のロジック」は全て data/jobs.ts の `JOB_PASSIVES` 述語に
//     集約され、本クラスからは文字列マッチ（`unit.job === "..."`）を排除
//   - `processIntegratedTurn()` 本体はライフサイクルだけを宣言的に並べる
//     オーケストレータとして書く（個々のジョブを意識しない）
//
// ライフサイクル分類:
//   Phase A : 常駐・大隊バフの計算（Brigade Scope）   = `evaluateBrigadePassiveBuffs`
//   Phase B : ターン時限バフのクリアと適用（Turn Scope）= `clearTurnScopedBuffs`
//                                                       / `applyTurnEndHeals`
//   Phase C : 行動解決と局所スキル（Action/Damage Scope）= `applyEnemyAction`
//                                                       / `processSquadOffense`
//                                                         内のサブヘルパー

export class BattleManager {
  private squads: Map<string, Squad>;
  private enemy: Enemy;
  private currentTurn: number;
  private rng: () => number;
  private currentEnemyHp: number;

  constructor(squads: Squad[], enemy: Enemy, rng: () => number = Math.random) {
    this.squads = new Map(squads.map((s) => [s.id, s]));
    this.enemy = enemy;
    this.currentTurn = 0;
    this.rng = rng;
    this.currentEnemyHp = enemy.hp;
  }

  get enemyHp(): number {
    return this.currentEnemyHp;
  }

  get isVictory(): boolean {
    return this.currentEnemyHp <= 0;
  }

  get turn(): number {
    return this.currentTurn;
  }

  // ─── Phase A: 常駐パッシブ（Brigade Scope） ─────────────────────────────

  /**
   * 大隊全体に常駐する AB バフ（速度+攻撃）を撒く全ユニットを評価する。
   *
   * 対象は `JOB_PASSIVES.broadcastsAllyBuff` が true を返す全 unit。
   * 現時点では tactician / standard_bearer が該当（旗手の AB=40 も
   * 本リファクタで初めて発動するようになる）。
   *
   * 各 broadcaster は自分以外の全 squad メンバーに `applyBuff` を撒く。
   * 自分自身には適用されない（`excludeUnitId` で除外）。
   */
  private evaluateBrigadePassiveBuffs(): void {
    for (const [, squad] of this.squads) {
      for (const unit of squad.units) {
        if (!JOB_PASSIVES.broadcastsAllyBuff(unit)) continue;
        for (const [, otherSquad] of this.squads) {
          otherSquad.applyBuff(unit.speed, unit.ab, unit.id);
        }
      }
    }
  }

  // ─── Phase B: 時限バフのクリアと適用（Turn Scope） ──────────────────────

  /**
   * 前ターンに撒かれた時限バフを全 squad で一括クリアする。
   * 必ず Phase A の前に呼ぶ（バフは毎ターン新規に計算される設計）。
   */
  private clearTurnScopedBuffs(): void {
    for (const [, squad] of this.squads) {
      squad.resetBuffs();
    }
  }

  /**
   * ターン末に発動する自分隊回復を適用する。
   *
   * 対象は `JOB_PASSIVES.healsSquadAtTurnEnd` が true を返す unit
   * （現時点では medic のみ）。同分隊内で複数いれば HL 値が加算される。
   * 回復は `Squad.applyHeal` 経由で maxHp 上限キャップ付き。
   */
  private applyTurnEndHeals(): HealLog[] {
    const logs: HealLog[] = [];
    for (const [squadId, squad] of this.squads) {
      const totalHeal = squad.units
        .filter((u) => JOB_PASSIVES.healsSquadAtTurnEnd(u))
        .reduce((sum, u) => sum + u.hl, 0);
      if (totalHeal > 0) {
        squad.applyHeal(totalHeal);
        logs.push({ squadId, healAmount: totalHeal });
      }
    }
    return logs;
  }

  // ─── Phase C: 局所スキル（Action/Damage Scope） ─────────────────────────

  /**
   * 被弾ダメージから「軽減」を引いた実効ダメージを計算する。
   *
   * 軽減源:
   *   - 大隊全体軽減 (BDF): FRONT スロットにいて
   *     `JOB_PASSIVES.reducesBrigadeDamageWhenFront` を満たす unit の bdf 合計
   *   - 自分隊軽減 (SDF): ターゲット分隊内で
   *     `JOB_PASSIVES.reducesSquadDamage` を満たす unit の sdf 合計
   *
   * 軽減後の最小値は 1（必ず痛みは残す）。
   */
  private computeEffectiveDamage(baseDamage: number, targetSquadId: string): number {
    let reduction = 0;

    const front = this.squads.get("FRONT");
    if (front) {
      for (const u of front.units) {
        if (JOB_PASSIVES.reducesBrigadeDamageWhenFront(u)) reduction += u.bdf;
      }
    }

    const target = this.squads.get(targetSquadId);
    if (target) {
      for (const u of target.units) {
        if (JOB_PASSIVES.reducesSquadDamage(u)) reduction += u.sdf;
      }
    }

    return Math.max(1, baseDamage - reduction);
  }

  /**
   * 当該ユニットの今ターンの攻撃回数を返す。
   *
   * 通常は 1。`JOB_PASSIVES.doubleStrikesOnFirstInitiative` を満たし、
   * かつ「行動順 1 番手の分隊」かつ「分隊内の先頭ユニット」のときに 2。
   */
  private offenseHitCount(
    unit: Unit,
    isFirstInInitiative: boolean,
    unitIdx: number
  ): number {
    const isFirstUnit = unitIdx === 0;
    const isFirstStrike =
      isFirstInInitiative &&
      isFirstUnit &&
      JOB_PASSIVES.doubleStrikesOnFirstInitiative(unit);
    return isFirstStrike ? 2 : 1;
  }

  // ─── 敵アクション解決 ───────────────────────────────────────────────────

  applyEnemyAction(action: EnemyAction): ActionResult {
    const { targetSlotIds, hitCount, damage, multiTargetMode } = action;

    if (targetSlotIds === "NONE") {
      return { perSlot: {} };
    }

    const allIds = Array.from(this.squads.keys());
    const candidates: string[] =
      targetSlotIds === "RANDOM" || targetSlotIds === "ALL"
        ? allIds
        : (targetSlotIds as string[]).filter((id) => this.squads.has(id));

    if (candidates.length === 0) {
      return { perSlot: {} };
    }

    const isSpread =
      multiTargetMode === "spread" ||
      (multiTargetMode === undefined && targetSlotIds === "ALL");

    const initialHp = new Map<string, number>();
    for (const id of candidates) {
      const squad = this.squads.get(id)!;
      initialHp.set(id, squad.units.reduce((sum, u) => sum + u.hp, 0));
    }

    const hitCounts = new Map<string, number>(candidates.map((id) => [id, 0]));

    if (isSpread) {
      for (const targetId of candidates) {
        const squad = this.squads.get(targetId)!;
        const effective = this.computeEffectiveDamage(damage, targetId);
        for (let i = 0; i < hitCount; i++) {
          squad.applyDamage(effective);
        }
        hitCounts.set(targetId, hitCount);
      }
    } else {
      for (let i = 0; i < hitCount; i++) {
        const targetId = candidates[Math.floor(this.rng() * candidates.length)];
        const squad = this.squads.get(targetId)!;
        const effective = this.computeEffectiveDamage(damage, targetId);
        squad.applyDamage(effective);
        hitCounts.set(targetId, hitCounts.get(targetId)! + 1);
      }
    }

    const perSlot: Record<string, SlotResult> = {};
    for (const id of candidates) {
      const squad = this.squads.get(id)!;
      const finalHp = squad.units.reduce((sum, u) => sum + u.hp, 0);
      perSlot[id] = {
        hits: hitCounts.get(id)!,
        damageTaken: initialHp.get(id)! - finalHp,
        defeated: squad.isDefeated,
      };
    }

    return { perSlot };
  }

  processTurn(): ActionResult {
    const action = this.enemy.getActionForTurn(this.currentTurn);
    const result = this.applyEnemyAction(action);
    this.currentTurn++;
    return result;
  }

  getAttackForecast(nextTurn?: number): AttackForecast {
    const turn = nextTurn ?? this.currentTurn;
    const action = this.enemy.getActionForTurn(turn);
    return {
      actionName: action.name,
      targetSlotIds:
        typeof action.targetSlotIds === "string"
          ? action.targetSlotIds
          : [...action.targetSlotIds],
      multiTargetMode: action.multiTargetMode,
    };
  }

  // ─── イニシアチブ構築 ───────────────────────────────────────────────────

  private buildInitiativeQueue(): InitiativeEntry[] {
    const queue: InitiativeEntry[] = [
      { type: "enemy", id: "enemy", speed: this.enemy.speed },
    ];
    for (const [id, squad] of this.squads) {
      if (!squad.isDefeated) {
        queue.push({ type: "squad", id, speed: squad.averageSpeed });
      }
    }
    return queue.sort((a, b) => b.speed - a.speed);
  }

  // ─── 分隊オフェンス（Phase C — Action Scope） ───────────────────────────

  processSquadOffense(squadId: string, isFirstInInitiative = false): SquadOffenseResult {
    const squad = this.squads.get(squadId);
    if (!squad) throw new Error(`Squad "${squadId}" not found`);

    const aliveUnits = [...squad.units]
      .filter((u) => u.isAlive)
      .sort((a, b) => b.finalSpeed - a.finalSpeed);

    const attackLogs: UnitAttackLog[] = [];
    let totalDamage = 0;

    for (let unitIdx = 0; unitIdx < aliveUnits.length; unitIdx++) {
      if (this.currentEnemyHp <= 0) break;

      const unit = aliveUnits[unitIdx];
      const attackPower =
        squadId === "FRONT" ? unit.finalFrontAttack : unit.finalRearAttack;
      const attackCount = this.offenseHitCount(unit, isFirstInInitiative, unitIdx);

      for (let hit = 0; hit < attackCount; hit++) {
        if (this.currentEnemyHp <= 0) break;

        const damageDealt = Math.min(attackPower, this.currentEnemyHp);
        this.currentEnemyHp = Math.max(0, this.currentEnemyHp - attackPower);

        attackLogs.push({
          unitId: unit.id,
          unitName: unit.name,
          slotId: squadId,
          attackPower,
          damageDealt,
          // 2 回攻撃の 2 発目だけ isDoubleAttack=true（ログ表示用）
          isDoubleAttack: attackCount === 2 && hit === 1,
        });
        totalDamage += damageDealt;
      }
    }

    return {
      squadId,
      attackLogs,
      totalDamage,
      enemyDefeated: this.currentEnemyHp <= 0,
    };
  }

  // ─── イニシアチブループ（Phase C をディスパッチするだけ） ──────────────

  /**
   * イニシアチブ順に enemy / squad の行動を解決していく純粋なディスパッチャ。
   * ジョブ固有の知識は持たず、各エントリの type を見て対応メソッドを呼ぶだけ。
   */
  private runInitiativeLoop(initiativeOrder: ReadonlyArray<InitiativeEntry>): {
    enemyActionResult?: ActionResult;
    squadOffenseResults: SquadOffenseResult[];
    victory: boolean;
  } {
    let enemyActionResult: ActionResult | undefined;
    const squadOffenseResults: SquadOffenseResult[] = [];
    let victory = false;

    for (let i = 0; i < initiativeOrder.length; i++) {
      const entry = initiativeOrder[i];

      if (entry.type === "enemy") {
        const action = this.enemy.getActionForTurn(this.currentTurn);
        enemyActionResult = this.applyEnemyAction(action);
      } else {
        const result = this.processSquadOffense(entry.id, i === 0);
        squadOffenseResults.push(result);
        if (result.enemyDefeated) {
          victory = true;
          break;
        }
      }
    }

    return { enemyActionResult, squadOffenseResults, victory };
  }

  // ─── オーケストレータ ──────────────────────────────────────────────────

  /**
   * 1 ターン全体を進行する。本メソッドはライフサイクルを宣言的に並べるだけで、
   * 個々のジョブ仕様には触れない（全て JOB_PASSIVES と Phase メソッドに委譲）。
   */
  processIntegratedTurn(): IntegratedTurnResult {
    if (this.isVictory) {
      throw new Error("Battle is already over (enemy defeated)");
    }

    // Phase B (start): 前ターンの時限バフをクリア
    this.clearTurnScopedBuffs();

    // Phase A: 常駐パッシブを評価し AB バフを撒く
    this.evaluateBrigadePassiveBuffs();

    // 行動順を確定（finalSpeed が Phase A の結果を反映している）
    const initiativeOrder = this.buildInitiativeQueue();

    // Phase C をディスパッチ
    const { enemyActionResult, squadOffenseResults, victory } =
      this.runInitiativeLoop(initiativeOrder);

    // Phase B (end): ターン末の回復
    const healLogs = this.applyTurnEndHeals();

    const turn = this.currentTurn;
    this.currentTurn++;
    return {
      turn,
      initiativeOrder,
      enemyActionResult,
      squadOffenseResults,
      victory,
      healLogs,
    };
  }
}
