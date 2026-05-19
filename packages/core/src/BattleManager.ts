import { Squad } from "./models/Squad";
import { Enemy, EnemyAction } from "./models/Enemy";

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

  private computeEffectiveDamage(baseDamage: number, targetSquadId: string): number {
    let reduction = 0;

    // BDF: 鉄壁騎士がFRONTスロットにいる場合、大隊全体のダメージを軽減（多重適用あり）
    const frontSquad = this.squads.get("FRONT");
    if (frontSquad) {
      for (const u of frontSquad.units) {
        if (u.isAlive && u.job === "iron_wall_knight") {
          reduction += u.bdf;
        }
      }
    }

    // SDF: ターゲット分隊内の鉄壁騎士が自分隊のダメージを軽減
    const targetSquad = this.squads.get(targetSquadId);
    if (targetSquad) {
      for (const u of targetSquad.units) {
        if (u.isAlive && u.job === "iron_wall_knight") {
          reduction += u.sdf;
        }
      }
    }

    return Math.max(1, baseDamage - reduction);
  }

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

  get turn(): number {
    return this.currentTurn;
  }

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

  private applyTacticianBuffs(): void {
    for (const [, squad] of this.squads) {
      for (const unit of squad.units) {
        if (unit.isAlive && unit.job === "tactician") {
          for (const [, otherSquad] of this.squads) {
            otherSquad.applyBuff(unit.speed, unit.ab, unit.id);
          }
        }
      }
    }
  }

  private applyMedicHealing(): HealLog[] {
    const logs: HealLog[] = [];
    for (const [squadId, squad] of this.squads) {
      const totalHeal = squad.units
        .filter((u) => u.isAlive && u.job === "medic")
        .reduce((sum, u) => sum + u.hl, 0);
      if (totalHeal > 0) {
        squad.applyHeal(totalHeal);
        logs.push({ squadId, healAmount: totalHeal });
      }
    }
    return logs;
  }

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
      const isFirstUnit = unitIdx === 0;
      const isSniper1st =
        unit.job === "sniper" && isFirstInInitiative && isFirstUnit;
      const attackPower =
        squadId === "FRONT" ? unit.finalFrontAttack : unit.finalRearAttack;
      const attackCount = isSniper1st ? 2 : 1;

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
          isDoubleAttack: isSniper1st && hit === 1,
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

  processIntegratedTurn(): IntegratedTurnResult {
    if (this.isVictory) {
      throw new Error("Battle is already over (enemy defeated)");
    }

    // 1. 全バフをリセット
    for (const [, squad] of this.squads) {
      squad.resetBuffs();
    }

    // 2. 戦術官のパッシブバフを適用
    this.applyTacticianBuffs();

    // 3. イニシアチブキューを構築（finalSpeedが反映されたaverageSpeedを使用）
    const initiativeOrder = this.buildInitiativeQueue();

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

    // 4. ターン終了：衛生兵の回復を適用
    const healLogs = this.applyMedicHealing();

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
