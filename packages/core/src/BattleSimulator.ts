import { BattleManager, IntegratedTurnResult } from "./BattleManager";
import { Enemy, EnemyAction } from "./models/Enemy";
import { Squad } from "./models/Squad";
import { Unit } from "./models/Unit";
import { CHRONICLE_CONFIG } from "./config/ChronicleConfig";

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
  /**
   * 次ターンに強制実行する EnemyAction。
   * BattleSimulator が予告システムで「次ターンの攻撃」を予め決めておくため、
   * これがセットされていれば getActionForTurn の戻り値を上書きする。
   */
  private _forcedNextAction: EnemyAction | null = null;

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
   * 次ターン強制実行する EnemyAction をセットする（BattleSimulator 用）。
   * セットされた値は次の getActionForTurn 呼び出しで返され、その後 null に戻る。
   */
  setNextAction(action: EnemyAction): void {
    this._forcedNextAction = action;
  }

  /**
   * BattleManager が呼ぶ。setNextAction で予約されていればそれを優先的に返す。
   * 予約が無い場合は旧来のフォールバック（生存数ベースの集団攻撃）に戻る。
   */
  getActionForTurn(_turn: number): EnemyAction {
    if (this._forcedNextAction) {
      const a = this._forcedNextAction;
      this._forcedNextAction = null;
      return a;
    }
    // フォールバック（互換動作）
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
  /** 性別（味方のみ、敵は省略） */
  readonly gender?: "Male" | "Female";
  readonly hp: number;
  readonly maxHp: number;
}

/** ローテーション戦略 */
export type RotationStrategy = "NONE" | "CW" | "CCW";

/** 配置情報（1ユニットがどのマスにいるか） */
export interface GridPlacement {
  readonly unitId: string;
  readonly unitName: string;
  readonly job: string | null;
  /** 性別（UI アイコン表示で使用） */
  readonly gender: "Male" | "Female";
  readonly row: "FRONT" | "REAR-L" | "REAR-R";
  readonly col: number;
  readonly hp: number;
  readonly maxHp: number;
}

/**
 * UI ステップ再生用に、各ターンの要点を構造化したログ
 */
export interface TurnLog {
  readonly turn: number;
  /** 表示用初期化（"Turn 3" 等） */
  readonly headerText: string;
  /** イニシアチブ順の表示文字列 */
  readonly initiativeText: string;
  /** 敵アクションの説明（不発時は "(行動なし)"） */
  readonly enemyActionText: string;
  /** ally 各分隊の攻撃ログ（行ごと） */
  readonly allyAttackLines: ReadonlyArray<string>;
  /** 回復ログ */
  readonly healLines: ReadonlyArray<string>;
  /** このターンで勝利確定したか */
  readonly victory: boolean;
  /**
   * このターン開始時にローテーションが発生した場合の通知。
   * `null` ならローテーションなし（NONE 戦略 or 初ターン）。
   */
  readonly rotationNotice: string | null;
  /** このターン開始時点の各ユニットの配置（ローテーション反映後） */
  readonly placements: ReadonlyArray<GridPlacement>;
  /** このターンに実行された攻撃の intent（前ターンの予告通りの攻撃が解決された後の記録） */
  readonly resolvedIntent: AttackIntent | null;
}

export interface SimulationResult {
  readonly winner: "Allies" | "Enemies" | "Draw";
  readonly turns: number;
  readonly statistics: BattleStatistics;
  readonly allySurvivors: ReadonlyArray<SurvivorRecord>;
  readonly enemySurvivors: ReadonlyArray<SurvivorRecord>;
  /**
   * このバトルで同じ Squad に同居していた ally ユニットの ID ペア。
   * Brigade.advance({ battlePairs }) にそのまま渡せる形式。
   * 各ペアは [a, b] の片方向のみ（[b, a] は重複しない）。
   */
  readonly squadmatePairs: ReadonlyArray<readonly [string, string]>;
  /**
   * UI ステップ再生用のターン別ログ。各ターンの要点を1配列に格納。
   */
  readonly turnLogs: ReadonlyArray<TurnLog>;
  /** 採用された戦略 */
  readonly rotationStrategy: RotationStrategy;
}

// ─── BattleSimulator ─────────────────────────────────────────────────────────

/** タイムライン（行動順予報）の各エントリ */
export interface TimelineEntry {
  readonly kind: "ally" | "enemy";
  readonly id: string;
  readonly label: string;
  readonly speed: number;
  readonly jobs?: ReadonlyArray<string>;
  readonly members?: ReadonlyArray<string>;
}

/**
 * 敵の攻撃パターン種別。
 *   SINGLE_STRIKE : 単一分隊強襲（1分隊に高ダメージ）
 *   PINCER        : 複数分隊挟撃（2分隊に中ダメージ）
 *   TOTAL_ASSAULT : 全大隊総攻撃（3分隊全てに低ダメージ）
 */
export type AttackPatternKind = "SINGLE_STRIKE" | "PINCER" | "TOTAL_ASSAULT";

/** プレイヤーへの「敵の次ターン行動予告」 */
export interface AttackIntent {
  /** パターン種別 */
  readonly kind: AttackPatternKind;
  /** プレイヤー表示用のスキル名 */
  readonly skillName: string;
  /** 対象となる分隊（FRONT / REAR-L / REAR-R のいずれか1〜3個） */
  readonly targetRows: ReadonlyArray<"FRONT" | "REAR-L" | "REAR-R">;
  /** 各対象ユニット1人あたりに与えるダメージ */
  readonly damagePerUnit: number;
}

/**
 * 敵ボスの現在 state。
 * 戦闘画面の最上部に敵ステータスカードを描画するために、毎ターン UI へ返す。
 * 本ゲームの敵は単体ボス（「試練の門の守護者」）なので、配列ではなく単一構造体。
 */
export interface EnemyState {
  readonly name: string;
  readonly job: string | null;
  readonly hp: number;
  readonly maxHp: number;
  readonly speed: number;
  readonly frontAttack: number;
  readonly rearAttack: number;
}

export class BattleSimulator {
  private readonly allies: Squad[];
  private readonly dynamicEnemy: DynamicEnemy;
  private readonly manager: BattleManager;
  private readonly maxTurns: number;
  private readonly verbose: boolean;
  private readonly rotationStrategy: RotationStrategy;

  // Accumulators
  private dmgByJob = new Map<string, number>();
  private totalMitigated = 0;
  private healByJob = new Map<string, number>();
  private killByJob = new Map<string, number>();

  // ── ターン単位 API 用の累積状態 ─────────────────────────────────
  private _totalTurns = 0;
  private _winner: "Allies" | "Enemies" | "Draw" | null = null;
  private _turnLogs: TurnLog[] = [];

  // ── 次ターン予告（敵の攻撃パターン） ──────────────────────────
  private _rng: () => number;
  /** ベース攻撃力（敵スケーリング由来）。攻撃パターン生成で参照 */
  private _enemyBaseAttack: number;
  /** 次ターンに発動する敵の攻撃予告 */
  private _nextIntent: AttackIntent;
  /** 次ターンに BattleManager へ渡す EnemyAction */
  private _nextAction: EnemyAction;

  /** unitId → job (for ally units) */
  private readonly unitJob = new Map<string, string | null>();

  constructor(
    allies: Squad[],
    enemies: Squad[],
    options: {
      maxTurns?: number;
      rng?: () => number;
      verbose?: boolean;
      rotation?: RotationStrategy;
    } = {}
  ) {
    this.allies = allies;
    this.dynamicEnemy = new DynamicEnemy(enemies);
    this._rng = options.rng ?? Math.random;
    this.manager = new BattleManager(allies, this.dynamicEnemy, this._rng);
    this.maxTurns = options.maxTurns ?? CHRONICLE_CONFIG.BATTLE.MAX_TURNS;
    this.verbose = options.verbose ?? true;
    this.rotationStrategy = options.rotation ?? "NONE";

    for (const sq of allies) {
      for (const u of sq.units) {
        this.unitJob.set(u.id, u.job);
      }
    }

    // 敵の基準攻撃力（最大 baseAttack）を初期化用に保持
    const enemyRecs = this.dynamicEnemy.unitRecords;
    this._enemyBaseAttack = enemyRecs.length > 0
      ? Math.max(...enemyRecs.map((r) => r.baseAttack))
      : 30;

    // 初手の予告を生成
    const first = this.generateAttackPattern();
    this._nextIntent = first.intent;
    this._nextAction = first.action;
  }

  // ── 攻撃パターン生成 ─────────────────────────────────────────────────

  /** ±15% のジッター（敵ステ乱数化と同様） */
  private jitter(): number {
    return 0.85 + this._rng() * 0.30;
  }

  private pickOne<T>(arr: ReadonlyArray<T>): T {
    return arr[Math.floor(this._rng() * arr.length)];
  }

  /**
   * 次ターンの敵攻撃パターン（SINGLE_STRIKE / PINCER / TOTAL_ASSAULT）を
   * 抽選し、intent と EnemyAction をペアで返す。
   *
   * ダメージ倍率:
   *   SINGLE_STRIKE : 2.0  （1分隊集中 = 高ダメージ）
   *   PINCER        : 1.4  （2分隊挟撃 = 中ダメージ）
   *   TOTAL_ASSAULT : 0.9  （3分隊全員 = 低ダメージ）
   */
  private generateAttackPattern(): { intent: AttackIntent; action: EnemyAction } {
    const ROWS: ReadonlyArray<"FRONT" | "REAR-L" | "REAR-R"> =
      ["FRONT", "REAR-L", "REAR-R"];
    const r = this._rng();
    const base = this._enemyBaseAttack;

    if (r < 1 / 3) {
      // SINGLE_STRIKE
      const target = this.pickOne(ROWS);
      const damage = Math.max(1, Math.round(base * 2.0 * this.jitter()));
      return {
        intent: {
          kind: "SINGLE_STRIKE",
          skillName: "単一分隊強襲",
          targetRows: [target],
          damagePerUnit: damage,
        },
        action: {
          name: `単一分隊強襲【${target}】`,
          targetSlotIds: [target],
          damage,
          hitCount: 1,
          multiTargetMode: "spread",
        },
      };
    }
    if (r < 2 / 3) {
      // PINCER（3C2=3パターン）
      const pairs: Array<["FRONT" | "REAR-L" | "REAR-R", "FRONT" | "REAR-L" | "REAR-R"]> = [
        ["FRONT", "REAR-L"],
        ["FRONT", "REAR-R"],
        ["REAR-L", "REAR-R"],
      ];
      const pair = this.pickOne(pairs);
      const damage = Math.max(1, Math.round(base * 1.4 * this.jitter()));
      return {
        intent: {
          kind: "PINCER",
          skillName: "複数分隊挟撃",
          targetRows: pair,
          damagePerUnit: damage,
        },
        action: {
          name: `複数分隊挟撃【${pair.join(" + ")}】`,
          targetSlotIds: [pair[0], pair[1]],
          damage,
          hitCount: 1,
          multiTargetMode: "spread",
        },
      };
    }
    // TOTAL_ASSAULT
    const damage = Math.max(1, Math.round(base * 0.9 * this.jitter()));
    return {
      intent: {
        kind: "TOTAL_ASSAULT",
        skillName: "全大隊総攻撃",
        targetRows: [...ROWS],
        damagePerUnit: damage,
      },
      action: {
        name: "全大隊総攻撃",
        targetSlotIds: "ALL",
        damage,
        hitCount: 1,
        multiTargetMode: "spread",
      },
    };
  }

  /** 現在予告されている次ターン攻撃 intent */
  getNextActionIntent(): AttackIntent {
    return this._nextIntent;
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

  // ── ローテーション ─────────────────────────────────────────────────────

  /**
   * 分隊単位のローテーション（squad-level rotation）。
   *
   * V 字陣形（FRONT が中央上、REAR-L が左下、REAR-R が右下）の三角配置に対し、
   * プレイヤーの直感に沿った「ガシャコンと旅団全体が回転する」挙動を実装する。
   *
   * - **時計回り (CW)**: 旅団全体を時計回りに回す
   *     REAR-L → FRONT （左後ろの分隊が右にスライドして前へせり出す）
   *     FRONT  → REAR-R
   *     REAR-R → REAR-L
   *
   * - **反時計回り (CCW)**: 旅団全体を反時計回りに回す
   *     REAR-R → FRONT （右後ろの分隊が左にスライドして前へせり出す）
   *     FRONT  → REAR-L
   *     REAR-L → REAR-R
   *
   * 各分隊内のスロット順（col 0/1/2）は完全に保持される。
   * 敵の分隊単位ダメージは squad ID（FRONT/REAR-L/REAR-R）にバインドされるため、
   * 内部 units 配列を入れ替えるだけで攻撃ターゲットも正しく追従する。
   */
  private rotateGrid(strategy: "CW" | "CCW"): void {
    const front  = this.allies.find((s) => s.id === "FRONT");
    const rearL  = this.allies.find((s) => s.id === "REAR-L");
    const rearR  = this.allies.find((s) => s.id === "REAR-R");
    if (!front || !rearL || !rearR) return;

    // スナップショット
    const frontUnits = [...front.units];
    const rearLUnits = [...rearL.units];
    const rearRUnits = [...rearR.units];

    if (strategy === "CW") {
      // 時計回り: REAR-L → FRONT, FRONT → REAR-R, REAR-R → REAR-L
      front.replaceUnits(rearLUnits);
      rearR.replaceUnits(frontUnits);
      rearL.replaceUnits(rearRUnits);
    } else {
      // 反時計回り: REAR-R → FRONT, FRONT → REAR-L, REAR-L → REAR-R
      front.replaceUnits(rearRUnits);
      rearL.replaceUnits(frontUnits);
      rearR.replaceUnits(rearLUnits);
    }
  }

  /** 現在配置を GridPlacement 配列に変換（UI 表示用） */
  private collectPlacements(): GridPlacement[] {
    const ROWS = ["FRONT", "REAR-L", "REAR-R"] as const;
    const out: GridPlacement[] = [];
    for (const rowId of ROWS) {
      const sq = this.allies.find((s) => s.id === rowId);
      if (!sq) continue;
      sq.units.forEach((u, c) => {
        out.push({
          unitId: u.id,
          unitName: u.name,
          job: u.job,
          gender: u.gender,
          row: rowId,
          col: c,
          hp: u.hp,
          maxHp: u.maxHp,
        });
      });
    }
    return out;
  }

  // ── ターンログ収集（UI ステップ再生用） ─────────────────────────────────

  private buildTurnLog(
    turnNum: number,
    result: IntegratedTurnResult,
    actionUsed: EnemyAction,
    rotationNotice: string | null,
    placements: ReadonlyArray<GridPlacement>,
    resolvedIntent: AttackIntent | null
  ): TurnLog {
    const initiativeText = result.initiativeOrder
      .map((e) => (e.type === "enemy" ? "Enemy" : `Ally[${e.id}]`) + `(spd${e.speed.toFixed(1)})`)
      .join(" → ");

    let enemyActionText = "(行動なし)";
    if (actionUsed.targetSlotIds === "NONE") {
      enemyActionText = "(行動なし)";
    } else if (result.enemyActionResult) {
      const hitSummary = Object.entries(result.enemyActionResult.perSlot)
        .filter(([, s]) => s.hits > 0)
        .map(([slot, s]) => `${slot}×${s.hits} ${s.damageTaken}dmg${s.defeated ? " [壊滅]" : ""}`)
        .join(", ");
      enemyActionText = `${actionUsed.name} ${actionUsed.damage}dmg/hit → ${hitSummary || "命中なし"}`;
    }

    const allyAttackLines: string[] = [];
    for (const off of result.squadOffenseResults) {
      if (off.attackLogs.length === 0) continue;
      const logStr = off.attackLogs
        .map((l) => `${l.unitName}${l.isDoubleAttack ? "×2" : ""} ${l.damageDealt}dmg`)
        .join(" | ");
      allyAttackLines.push(`Ally[${off.squadId}]: ${logStr} (計${off.totalDamage})`);
    }

    const healLines = result.healLogs.map(
      (h) => `Heal: Squad[${h.squadId}] +${h.healAmount}HP`
    );

    return {
      turn: turnNum,
      headerText: `Turn ${turnNum}`,
      initiativeText,
      enemyActionText,
      allyAttackLines,
      healLines,
      victory: result.victory,
      rotationNotice,
      placements,
      resolvedIntent,
    };
  }

  // ── ターン単位 API（リアルタイム指揮システム用） ───────────────────────

  /** 現在の戦闘状態。完了していれば true、未完了なら false */
  get isFinished(): boolean {
    return this._winner !== null || this._totalTurns >= this.maxTurns;
  }

  /** 現在の勝敗。未完了なら null */
  get currentWinner(): "Allies" | "Enemies" | "Draw" | null {
    return this._winner;
  }

  /** 経過ターン数 */
  get currentTurn(): number {
    return this._totalTurns;
  }

  /** これまでのターンログ（読み取り専用） */
  get accumulatedTurnLogs(): ReadonlyArray<TurnLog> {
    return this._turnLogs;
  }

  /** 現在の placements を公開 */
  getCurrentPlacements(): GridPlacement[] {
    return this.collectPlacements();
  }

  /**
   * 敵ボスの現在 state（戦闘画面の最上部 EnemyStatusCard 用）。
   *
   * 本ゲームの敵は単体ボス（試練の門の守護者）として makeTrialEnemy が生成する。
   * DynamicEnemy.unitRecords[0] が唯一のレコードなので、そこから直接抽出する。
   * 0 体（生成失敗）の場合は安全な空オブジェクトを返す。
   *
   * 攻撃力は core 内では baseAttack（= front/rear の平均、生成時固定）のみ保持。
   * 試練の敵生成側で frontAttack === rearAttack === atk としているので、
   * 両方に同じ値を返す。
   */
  getEnemyState(): EnemyState {
    const r = this.dynamicEnemy.unitRecords[0];
    if (!r) {
      return {
        name: "(unknown)", job: null,
        hp: 0, maxHp: 0,
        speed: 0, frontAttack: 0, rearAttack: 0,
      };
    }
    return {
      name: r.name,
      job: r.job,
      hp: r.currentHp,
      maxHp: r.maxHp,
      speed: r.speed,
      frontAttack: r.baseAttack,
      rearAttack: r.baseAttack,
    };
  }

  /**
   * 次ターンの行動順予報を返す。
   *
   * 各 ally Squad の平均 speed + tactician/standard_bearer の AB バフ総和、
   * 敵集団の最大 speed を SPD 降順で並べる。BattleManager のイニシアチブ
   * 計算と近い結果になるが、UI 予報用なので近似値である点に注意。
   */
  getInitiativeForecast(): TimelineEntry[] {
    const timeline: TimelineEntry[] = [];

    // ally 全体の AB バフ総和（戦術官 20 / 旗手 40 を含む）
    let totalAbBuff = 0;
    for (const sq of this.allies) {
      for (const u of sq.units) {
        if (u.isAlive) totalAbBuff += u.ab;
      }
    }

    for (const sq of this.allies) {
      const alive = sq.units.filter((u) => u.isAlive);
      if (alive.length === 0) continue;
      const avgSpeed = alive.reduce((s, u) => s + u.speed, 0) / alive.length;
      const label =
        sq.id === "FRONT" ? "前衛"
        : sq.id === "REAR-L" ? "後衛-左"
        : sq.id === "REAR-R" ? "後衛-右"
        : sq.id;
      timeline.push({
        kind: "ally",
        id: sq.id,
        label,
        speed: avgSpeed + totalAbBuff,
        jobs: alive.map((u) => u.job ?? "?"),
        members: alive.map((u) => u.name),
      });
    }

    // 敵集団（最大 speed）
    const aliveEnemies = this.dynamicEnemy.unitRecords.filter((r) => r.currentHp > 0);
    if (aliveEnemies.length > 0) {
      const enemyMaxSpeed = Math.max(...aliveEnemies.map((r) => r.speed));
      timeline.push({
        kind: "enemy",
        id: "enemy-group",
        label: `敵軍（${aliveEnemies.length}体）`,
        speed: enemyMaxSpeed,
      });
    }

    timeline.sort((a, b) => b.speed - a.speed);
    return timeline;
  }

  /**
   * 指定戦略で 1 ターンだけ進める。
   * 初ターン（_totalTurns === 0）はローテーションを適用せず、純粋に1ターン実行。
   *
   * @throws 既に戦闘が終了している場合
   */
  runOneTurn(strategy: RotationStrategy): TurnLog {
    if (this.isFinished) {
      throw new Error("battle is already finished");
    }

    // ローテーション（初ターンは不要、勝利後も不要）
    let rotationNotice: string | null = null;
    if (this._totalTurns > 0 && strategy !== "NONE") {
      this.rotateGrid(strategy);
      rotationNotice =
        strategy === "CW"
          ? `↻ 右回り: 後衛-左 が前衛へ進軍`
          : `↺ 左回り: 後衛-右 が前衛へ進軍`;
    }
    const placements = this.collectPlacements();

    // ★ 前ターンに予告した攻撃を強制実行する
    const resolvedIntent = this._nextIntent;
    this.dynamicEnemy.setNextAction(this._nextAction);
    const actionUsed = this._nextAction;

    const aliveCountBySquad = new Map(
      this.allies.map((sq) => [sq.id, sq.units.filter((u) => u.isAlive).length])
    );
    const result = this.manager.processIntegratedTurn();
    this._totalTurns++;

    this.logTurn(this._totalTurns, result, actionUsed);
    this.collectStats(result, actionUsed, aliveCountBySquad);
    const turnLog = this.buildTurnLog(
      this._totalTurns, result, actionUsed, rotationNotice, placements, resolvedIntent
    );
    this._turnLogs.push(turnLog);

    // 終了判定
    if (result.victory) {
      this._winner = "Allies";
    } else if (this.allAlliesDefeated()) {
      this._winner = "Enemies";
    } else if (this._totalTurns >= this.maxTurns) {
      this._winner = "Draw";
    }

    // ★ 次ターン用の予告を生成（戦闘継続中の場合のみ）
    if (this._winner === null) {
      const next = this.generateAttackPattern();
      this._nextIntent = next.intent;
      this._nextAction = next.action;
    }

    return turnLog;
  }

  // ── 一括実行 API（バッチ用、ターン単位 API のラッパ） ─────────────────

  run(): SimulationResult {
    if (this.verbose) {
      console.log("=".repeat(50));
      console.log("  BATTLE START");
      console.log("=".repeat(50));
    }

    while (!this.isFinished) {
      this.runOneTurn(this.rotationStrategy);
    }
    const winner = this._winner ?? "Draw";
    const totalTurns = this._totalTurns;
    const turnLogs = this._turnLogs;

    if (this.verbose) {
      console.log("\n" + "=".repeat(50));
    }

    // Build survivor lists
    const allySurvivors: SurvivorRecord[] = this.allies
      .flatMap((sq) => sq.units)
      .filter((u) => u.isAlive)
      .map((u) => ({ name: u.name, job: u.job, gender: u.gender, hp: u.hp, maxHp: u.maxHp }));

    const enemySurvivors: SurvivorRecord[] = this.dynamicEnemy.unitRecords
      .filter((r) => r.currentHp > 0)
      .map((r) => ({ name: r.name, job: r.job, hp: r.currentHp, maxHp: r.maxHp }));

    // 同分隊ペア抽出（血統継承システム用）
    const squadmatePairs: [string, string][] = [];
    for (const sq of this.allies) {
      const ids = sq.units.map((u) => u.id);
      for (let i = 0; i < ids.length; i++) {
        for (let j = i + 1; j < ids.length; j++) {
          squadmatePairs.push([ids[i], ids[j]]);
        }
      }
    }

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
      squadmatePairs,
      turnLogs,
      rotationStrategy: this.rotationStrategy,
    };
  }
}

// ─── Report printer ───────────────────────────────────────────────────────────

const JOB_JP: Record<string, string> = {
  iron_wall_knight: "鉄壁騎士",
  tactician: "戦術官",
  medic: "衛生兵",
  sniper: "狙撃兵",
  sorcerer: "呪術師",
  standard_bearer: "旗手",
  heavy_infantry: "重装歩兵",
  scout: "斥候",
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
