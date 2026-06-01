import { Unit } from "./Unit";
import { MAX_UNITS_PER_SQUAD } from "../config";

export class Squad {
  readonly id: string;
  private _units: Unit[];

  constructor(id: string, units: Unit[] = []) {
    if (units.length > MAX_UNITS_PER_SQUAD) {
      throw new Error(
        `Squad cannot exceed ${MAX_UNITS_PER_SQUAD} units (got ${units.length})`
      );
    }
    this.id = id;
    this._units = [...units];
  }

  get units(): ReadonlyArray<Unit> {
    return this._units;
  }

  addUnit(unit: Unit): void {
    if (this._units.length >= MAX_UNITS_PER_SQUAD) {
      throw new Error(`Squad is full (max ${MAX_UNITS_PER_SQUAD} units)`);
    }
    this._units.push(unit);
  }

  /**
   * units 配列を一括差し替える（ローテーション用）。
   * BattleSimulator が毎ターンの陣形回転で使う。
   * 死亡ユニット（HP=0）もそのまま位置情報を保持して回転に参加できる。
   */
  replaceUnits(units: ReadonlyArray<Unit>): void {
    if (units.length > MAX_UNITS_PER_SQUAD) {
      throw new Error(
        `Squad cannot exceed ${MAX_UNITS_PER_SQUAD} units (got ${units.length})`
      );
    }
    this._units = [...units];
  }

  get averageSpeed(): number {
    const alive = this._units.filter((u) => u.isAlive);
    if (alive.length === 0) return 0;
    const total = alive.reduce((sum, u) => sum + u.finalSpeed, 0);
    return total / alive.length;
  }

  get isDefeated(): boolean {
    if (this._units.length === 0) return false;
    return this._units.every((u) => u.hp <= 0);
  }

  applyDamage(damage: number): void {
    this._units = this._units.map((u) =>
      u.isAlive ? u.takeDamage(damage) : u
    );
  }

  applyBuff(speedBuff: number, attackBuff: number, excludeUnitId?: string): void {
    this._units = this._units.map((u) =>
      u.id === excludeUnitId ? u : u.withBuffs(speedBuff, attackBuff)
    );
  }

  resetBuffs(): void {
    this._units = this._units.map((u) => u.resetBuffs());
  }

  applyHeal(amount: number): void {
    this._units = this._units.map((u) => (u.isAlive ? u.withHeal(amount) : u));
  }
}
