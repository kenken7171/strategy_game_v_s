import { Unit } from "./Unit";
import { Squad } from "./Squad";

export interface YearEvent {
  readonly type: "join" | "retire";
  readonly unit: Unit;
}

export interface AdvanceResult {
  readonly brigade: Brigade;
  readonly events: ReadonlyArray<YearEvent>;
}

export class Brigade {
  readonly units: ReadonlyArray<Unit>;
  readonly currentYear: number;
  private _squads: Squad[];

  constructor(units: ReadonlyArray<Unit>, squads: Squad[] = [], currentYear = 1) {
    this.units = units;
    this._squads = [...squads];
    this.currentYear = currentYear;
  }

  get squads(): ReadonlyArray<Squad> {
    return this._squads;
  }

  addSquad(squad: Squad): void {
    this._squads.push(squad);
  }

  assignUnitToSquad(unitId: string, squadId: string): void {
    const unit = this.units.find((u) => u.id === unitId);
    if (!unit) throw new Error(`Unit "${unitId}" not found in brigade`);

    const squad = this._squads.find((s) => s.id === squadId);
    if (!squad) throw new Error(`Squad "${squadId}" not found in brigade`);

    squad.addUnit(unit);
  }

  get averageStrength(): number {
    if (this.units.length === 0) return 0;
    const total = this.units.reduce((sum, u) => sum + u.stats.strength, 0);
    return Math.round((total / this.units.length) * 10) / 10;
  }

  /**
   * 大隊選出: 現在のステータスで上位 n 体を返す（大隊編成時に呼ぶ）。
   * ここで stats が確定するため、選出後のバフ等は別途 Squad に適用する。
   */
  selectBattalion(size: number): ReadonlyArray<Unit> {
    return [...this.units]
      .filter((u) => !u.isRetired)
      .sort((a, b) => b.stats.strength - a.stats.strength)
      .slice(0, size);
  }

  advance(recruits: ReadonlyArray<Unit> = []): AdvanceResult {
    const events: YearEvent[] = [];

    const aged = this.units.map((u) => u.grow());
    const active: Unit[] = [];
    for (const u of aged) {
      if (u.isRetired) {
        events.push({ type: "retire", unit: u });
      } else {
        active.push(u);
      }
    }
    for (const r of recruits) {
      events.push({ type: "join", unit: r });
    }

    return {
      brigade: new Brigade([...active, ...recruits], [], this.currentYear + 1),
      events,
    };
  }
}
