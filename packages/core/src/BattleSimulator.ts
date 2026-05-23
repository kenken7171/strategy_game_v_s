import { BattleManager, IntegratedTurnResult } from "./BattleManager";
import { Enemy, EnemyAction } from "./models/Enemy";
import { Squad } from "./models/Squad";

// ─── DynamicEnemy ─────────────────────────────────────────────────────────────
// Wraps an enemy-brigade's Squad[] as a single Enemy object so BattleManager
// can consume it via processIntegratedTurn.  The key override is
// getActionForTurn(), which is called every turn to generate an EnemyAction
// from the *current* state of the enemy units.

interface EnemyUnitRecord {
  readonly unitId: string;
  readonly squadId: string;
  readonly name: string;
  readonly job: string | null;
  readonly maxHp: number;
  readonly speed: number;
  /** Base attack power (avg of front/rear at construction time, without buffs) */
  readonly baseAttack: number;
  /** Mutable: decremented as ally squads deal damage to the enemy pool */
  currentHp: number;
}

class DynamicEnemy extends Enemy {
  private readonly _units: EnemyUnitRecord[];

  constructor(enemySquads: Squad[]) {
    const records: EnemyUnitRecord[] = enemySquads.flatMap((squad) =>
      squad.units.map((u) => ({
        unitId: u.id,
        squadId: squad.id,
        name: u.name,
        job: u.job,
        maxHp: u.maxHp,
        speed: u.speed,
        baseAttack: Math.max(1, Math.round((u.frontAttack + u.rearAttack) / 2)),
        currentHp: u.hp,
      }))
    );

    const totalHp = records.reduce((s, u) => s + u.maxHp, 0);
    const maxSpeed = records.reduce((m, u) => Math.max(m, u.speed), 0);

    // One placeholder action — getActionForTurn is fully overridden below
    super({
      hp: totalHp,
      maxHp: totalHp,
      speed: maxSpeed,
      actions: [{ name: "_placeholder", targetSlotIds: "NONE", hitCount: 0, damage: 0 }],
    });

    this._units = records;
  }

  get unitRecords(): ReadonlyArray<EnemyUnitRecord> {
    return this._units;
  }

  /**
   * Distribute damage across enemy units sequentially (front-to-back order).
   * Called by BattleSimulator after each turn to keep unit HP in sync with
   * BattleManager's internal enemyHp pool.
   */
  applyDamageToUnits(totalDamage: number): void {
    let remaining = totalDamage;
    for (const rec of this._units) {
      if (remaining <= 0) break;
      if (rec.currentHp <= 0) continue;
      const dmg = Math.min(remaining, rec.currentHp);
      rec.currentHp -= dmg;
      remaining -= dmg;
    }
  }

  /**
   * Overrides Enemy.getActionForTurn so BattleManager uses the *live* state
   * of enemy units instead of a fixed rotation.
   * Each alive enemy unit contributes one hit; damage = their average attack.
   */
  getActionForTurn(_turn: number): EnemyAction {
    const alive = this._units.filter((u) => u.currentHp > 0);
    if (alive.length === 0) {
      return { name: "なし（全滅）", targetSlotIds: "NONE", hitCount: 0, damage: 0 };
    }
    const avgDmg = Math.max(
      1,
      Math.round(alive.reduce((s, u) => s + u.baseAttack, 0) / alive.length)
    );
    return {
      name: `旅団突撃（${alive.length}体）`,
      targetSlotIds: "RANDOM",
      hitCount: alive.length,
      damage: avgDmg,
    };
  }
}

// ─── Statistics & Result ──────────────────────────────────────────────────────

export interface BattleStatistics {
  /** HP dealt per job, across all ally attacks */
  readonly totalDamageDealt: Readonly<Record<string, number>>;
  /** HP prevented by iron_wall_knight BDF/SDF each time enemy attacked */
  readonly totalDamageMitigated: number;
  /** HP restored per job */
  readonly totalHealing: Readonly<Record<string, number>>;
  /** Kill-count (final blow) per job */
  readonly killCount: Readonly<Record<string, number>>;
}

export interface SurvivorRecord {
  readonly name: string;
  readonly job: string | null;
  readonly hp: number;
  readonly maxHp: number;
}

export interface SimulationResult {
  readonly winner: "Allies" | "Enemies" | "Draw";
  readonly turns: number;
  readonly statistics: BattleStatistics;
  readonly allySurvivors: ReadonlyArray<SurvivorRecord>;
  readonly enemySurvivors: ReadonlyArray<SurvivorRecord>;
}

// ─── BattleSimulator ─────────────────────────────────────────────────────────

export class BattleSimulator {
  private readonly allies: Squad[];
  private readonly dynamicEnemy: DynamicEnemy;
  private readonly manager: BattleManager;
  private readonly maxTurns: number;
  private readonly verbose: boolean;

  // Accumulators
  private dmgByJob = new Map<string, number>();
  private totalMitigated = 0;
  private healByJob = new Map<string, number>();
  private killByJob = new Map<string, number>();

  /** unitId → job (for ally units) */
  private readonly unitJob = new Map<string, string | null>();

  constructor(
    allies: Squad[],
    enemies: Squad[],
    options: { maxTurns?: number; rng?: () => number; verbose?: boolean } = {}
  ) {
    this.allies = allies;
    this.dynamicEnemy = new DynamicEnemy(enemies);
    this.manager = new BattleManager(allies, this.dynamicEnemy, options.rng ?? Math.random);
    this.maxTurns = options.maxTurns ?? 30;
    this.verbose = options.verbose ?? true;

    for (const sq of allies) {
      for (const u of sq.units) {
        this.unitJob.set(u.id, u.job);
      }
    }
  }

  // ── helpers ────────────────────────────────────────────────────────────────

  private allAlliesDefeated(): boolean {
    return this.allies.every((sq) => sq.isDefeated);
  }

  private addDmg(jobKey: string, amount: number): void {
    this.dmgByJob.set(jobKey, (this.dmgByJob.get(jobKey) ?? 0) + amount);
  }

  private addHeal(jobKey: string, amount: number): void {
    this.healByJob.set(jobKey, (this.healByJob.get(jobKey) ?? 0) + amount);
  }

  private addKill(jobKey: string): void {
    this.killByJob.set(jobKey, (this.killByJob.get(jobKey) ?? 0) + 1);
  }

  // ── stats collection ───────────────────────────────────────────────────────

  private collectStats(
    result: IntegratedTurnResult,
    actionUsed: EnemyAction,
    aliveCountBySquad: ReadonlyMap<string, number>
  ): void {
    // 1. Ally damage by job
    for (const offResult of result.squadOffenseResults) {
      for (const log of offResult.attackLogs) {
        const job = this.unitJob.get(log.unitId) ?? "unknown";
        this.addDmg(job, log.damageDealt);
      }
    }

    // 2. BDF/SDF mitigation (iron_wall_knight)
    //    Expected HP loss without any reduction: action.damage × hits × alive_units_in_squad
    //    Actual HP lost: slotResult.damageTaken
    //    Mitigation = max(0, expected − actual)
    if (result.enemyActionResult && actionUsed.damage > 0) {
      for (const [slotId, slotResult] of Object.entries(result.enemyActionResult.perSlot)) {
        if (slotResult.hits === 0) continue;
        const aliveUnits = aliveCountBySquad.get(slotId) ?? 1;
        const expectedRaw = actionUsed.damage * slotResult.hits * aliveUnits;
        this.totalMitigated += Math.max(0, expectedRaw - slotResult.damageTaken);
      }
    }

    // 3. Healing by job (all medic-sourced via BattleManager's applyMedicHealing)
    for (const h of result.healLogs) {
      this.addHeal("medic", h.healAmount);
    }

    // 4. Kill: last unit to attack when enemy is defeated this turn
    if (result.victory) {
      for (const offResult of result.squadOffenseResults) {
        if (offResult.enemyDefeated && offResult.attackLogs.length > 0) {
          const lastLog = offResult.attackLogs[offResult.attackLogs.length - 1];
          const job = this.unitJob.get(lastLog.unitId) ?? "unknown";
          this.addKill(job);
          break;
        }
      }
    }

    // 5. Sync enemy unit HP with BattleManager's internal pool delta
    const totalAllyDmg = result.squadOffenseResults.reduce((s, r) => s + r.totalDamage, 0);
    this.dynamicEnemy.applyDamageToUnits(totalAllyDmg);
  }

  // ── console logging ────────────────────────────────────────────────────────

  private logTurn(turnNum: number, result: IntegratedTurnResult, actionUsed: EnemyAction): void {
    if (!this.verbose) return;

    console.log(`\nTurn ${turnNum}`);

    // Initiative order
    const orderStr = result.initiativeOrder
      .map((e) => (e.type === "enemy" ? "Enemy" : `Ally[${e.id}]`) + `(spd${e.speed.toFixed(1)})`)
      .join(" → ");
    console.log(`  Order : ${orderStr}`);

    // Enemy action
    if (actionUsed.targetSlotIds === "NONE") {
      console.log(`  Enemy : (行動なし)`);
    } else if (result.enemyActionResult) {
      const hitSummary = Object.entries(result.enemyActionResult.perSlot)
        .filter(([, s]) => s.hits > 0)
        .map(([slot, s]) => `${slot} ×${s.hits} ${s.damageTaken}dmg${s.defeated ? " [壊滅]" : ""}`)
        .join(", ");
      console.log(
        `  Enemy : ${actionUsed.name}  ${actionUsed.damage}dmg/hit → ${hitSummary || "命中なし"}`
      );
    }

    // Ally squad attacks
    for (const off of result.squadOffenseResults) {
      if (off.attackLogs.length === 0) continue;
      const logStr = off.attackLogs
        .map((l) => `${l.unitName}${l.isDoubleAttack ? "×2" : ""} ${l.damageDealt}dmg`)
        .join(" | ");
      console.log(`  Ally[${off.squadId}] : ${logStr}  (計${off.totalDamage})`);
    }

    // Healing
    for (const h of result.healLogs) {
      console.log(`  Heal  : Squad[${h.squadId}] +${h.healAmount}HP`);
    }
  }

  // ── main loop ──────────────────────────────────────────────────────────────

  run(): SimulationResult {
    if (this.verbose) {
      console.log("=".repeat(50));
      console.log("  BATTLE START");
      console.log("=".repeat(50));
    }

    let winner: "Allies" | "Enemies" | "Draw" = "Draw";
    let totalTurns = 0;

    while (totalTurns < this.maxTurns) {
      if (this.allAlliesDefeated()) { winner = "Enemies"; break; }
      if (this.manager.isVictory)   { winner = "Allies";  break; }

      // Snapshot BEFORE the turn: action used + alive unit counts (for mitigation calc)
      const actionUsed = this.dynamicEnemy.getActionForTurn(this.manager.turn);
      const aliveCountBySquad = new Map(
        this.allies.map((sq) => [sq.id, sq.units.filter((u) => u.isAlive).length])
      );

      const result = this.manager.processIntegratedTurn();
      totalTurns++;

      this.logTurn(totalTurns, result, actionUsed);
      this.collectStats(result, actionUsed, aliveCountBySquad);

      if (result.victory)          { winner = "Allies";  break; }
      if (this.allAlliesDefeated()) { winner = "Enemies"; break; }
    }

    if (this.verbose) {
      console.log("\n" + "=".repeat(50));
    }

    // Build survivor lists
    const allySurvivors: SurvivorRecord[] = this.allies
      .flatMap((sq) => sq.units)
      .filter((u) => u.isAlive)
      .map((u) => ({ name: u.name, job: u.job, hp: u.hp, maxHp: u.maxHp }));

    const enemySurvivors: SurvivorRecord[] = this.dynamicEnemy.unitRecords
      .filter((r) => r.currentHp > 0)
      .map((r) => ({ name: r.name, job: r.job, hp: r.currentHp, maxHp: r.maxHp }));

    return {
      winner,
      turns: totalTurns,
      statistics: {
        totalDamageDealt: Object.fromEntries(this.dmgByJob),
        totalDamageMitigated: this.totalMitigated,
        totalHealing: Object.fromEntries(this.healByJob),
        killCount: Object.fromEntries(this.killByJob),
      },
      allySurvivors,
      enemySurvivors,
    };
  }
}

// ─── Report printer ───────────────────────────────────────────────────────────

const JOB_JP: Record<string, string> = {
  iron_wall_knight: "鉄壁騎士",
  tactician: "戦術官",
  medic: "衛生兵",
  sniper: "狙撃兵",
  unknown: "不明",
};

function jobLabel(job: string | null): string {
  if (!job) return "不明";
  return JOB_JP[job] ?? job;
}

export function printBattleReport(result: SimulationResult): void {
  const s = result.statistics;

  // ─ RESULT ─
  console.log("\n--- BATTLE RESULT ---");
  console.log(`Winner : ${result.winner}`);
  console.log(`Turns  : ${result.turns}`);

  // ─ MVP & CONTRIBUTIONS ─
  console.log("\n--- MVP & CONTRIBUTIONS ---");

  const topAttacker = Object.entries(s.totalDamageDealt).sort(([, a], [, b]) => b - a)[0];
  const topHealer = Object.entries(s.totalHealing).sort(([, a], [, b]) => b - a)[0];

  console.log(
    `- Top Attacker : ${topAttacker ? `${jobLabel(topAttacker[0])} (${topAttacker[1]} Damage)` : "N/A"}`
  );
  console.log(`- Best Defender: iron_wall_knight (${s.totalDamageMitigated} Mitigated)`);
  console.log(
    `- Best Healer  : ${topHealer ? `${jobLabel(topHealer[0])} (${topHealer[1]} Healed)` : "N/A"}`
  );

  if (Object.keys(s.killCount).length > 0) {
    const killStr = Object.entries(s.killCount)
      .map(([j, c]) => `${jobLabel(j)} ×${c}`)
      .join(", ");
    console.log(`- Kill Count   : ${killStr}`);
  }

  // ─ SURVIVORS ─
  const survivors = result.winner === "Enemies" ? result.enemySurvivors : result.allySurvivors;
  console.log("\n--- SURVIVORS ---");
  if (survivors.length === 0) {
    console.log("(none)");
  } else {
    for (const u of survivors) {
      console.log(`- ${u.name} [${jobLabel(u.job)}]: ${u.hp}/${u.maxHp}`);
    }
  }
}
