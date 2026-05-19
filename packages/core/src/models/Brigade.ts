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
  private _squads: Squad[];

  constructor(units: ReadonlyArray<Unit>, squads: Squad[] = []) {
    this.units = units;
    this._squads = [...squads];
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
      brigade: new Brigade([...active, ...recruits]),
      events,
    };
  }
}
