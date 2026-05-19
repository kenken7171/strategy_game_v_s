export interface Stats {
  readonly strength: number;
  readonly agility: number;
  readonly intelligence: number;
  readonly endurance: number;
}

export type JobType = "iron_wall_knight" | "tactician" | "medic" | "sniper";

export interface UnitProps {
  readonly id: string;
  readonly name: string;
  readonly age: number;
  readonly peakAge: number;
  readonly maxAge: number;
  readonly baseStats: Stats;
  readonly maxHp?: number;
  readonly hp?: number;
  readonly speed?: number;
  readonly frontAttack?: number;
  readonly rearAttack?: number;
  readonly job?: JobType | null;
  readonly sdf?: number;
  readonly bdf?: number;
  readonly ab?: number;
  readonly hl?: number;
  readonly speedBuff?: number;
  readonly attackBuff?: number;
}

export class Unit {
  readonly id: string;
  readonly name: string;
  readonly age: number;
  readonly peakAge: number;
  readonly maxAge: number;
  readonly baseStats: Stats;
  readonly maxHp: number;
  readonly hp: number;
  readonly speed: number;
  readonly frontAttack: number;
  readonly rearAttack: number;
  readonly job: JobType | null;
  readonly sdf: number;
  readonly bdf: number;
  readonly ab: number;
  readonly hl: number;
  readonly speedBuff: number;
  readonly attackBuff: number;

  constructor(props: UnitProps) {
    this.id = props.id;
    this.name = props.name;
    this.age = props.age;
    this.peakAge = props.peakAge;
    this.maxAge = props.maxAge;
    this.baseStats = props.baseStats;
    this.maxHp = props.maxHp ?? 100;
    this.hp = props.hp ?? this.maxHp;
    this.speed = props.speed ?? 0;
    this.frontAttack = props.frontAttack ?? 0;
    this.rearAttack = props.rearAttack ?? 0;
    this.job = props.job ?? null;
    this.sdf = props.sdf ?? 0;
    this.bdf = props.bdf ?? 0;
    this.ab = props.ab ?? 0;
    this.hl = props.hl ?? 0;
    this.speedBuff = props.speedBuff ?? 0;
    this.attackBuff = props.attackBuff ?? 0;
  }

  /**
   * 年齢による能力補正係数（0.0〜1.0）。
   * f(a) = 1 - k*(a - peakAge)^2, k = 1/(maxAge - peakAge)^2
   * peakAgeで1.0、maxAgeで0.0になる二次関数モデル。
   */
  get growthFactor(): number {
    if (this.age >= this.maxAge) return 0;
    const k = 1 / Math.pow(this.maxAge - this.peakAge, 2);
    return Math.max(0, 1 - k * Math.pow(this.age - this.peakAge, 2));
  }

  get stats(): Stats {
    const f = this.growthFactor;
    return {
      strength: Math.round(this.baseStats.strength * f),
      agility: Math.round(this.baseStats.agility * f),
      intelligence: Math.round(this.baseStats.intelligence * f),
      endurance: Math.round(this.baseStats.endurance * f),
    };
  }

  get isRetired(): boolean {
    return this.age >= this.maxAge;
  }

  get isAlive(): boolean {
    return this.hp > 0;
  }

  get finalSpeed(): number {
    return this.speed + this.speedBuff;
  }

  get finalFrontAttack(): number {
    return this.frontAttack + this.attackBuff;
  }

  get finalRearAttack(): number {
    return this.rearAttack + this.attackBuff;
  }

  get finalAttack(): number {
    return this.frontAttack + this.attackBuff;
  }

  grow(): Unit {
    return new Unit({ ...this, age: this.age + 1 });
  }

  takeDamage(amount: number): Unit {
    return new Unit({ ...this, hp: Math.max(0, this.hp - amount) });
  }

  withBuffs(speedBuff: number, attackBuff: number): Unit {
    return new Unit({
      ...this,
      speedBuff: this.speedBuff + speedBuff,
      attackBuff: this.attackBuff + attackBuff,
    });
  }

  withHeal(amount: number): Unit {
    return new Unit({ ...this, hp: Math.min(this.maxHp, this.hp + amount) });
  }

  resetBuffs(): Unit {
    return new Unit({ ...this, speedBuff: 0, attackBuff: 0 });
  }
}
