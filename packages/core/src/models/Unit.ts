export interface Stats {
  readonly strength: number;
  readonly agility: number;
  readonly intelligence: number;
  readonly endurance: number;
}

export type JobType = "iron_wall_knight" | "tactician" | "medic" | "sniper";

export type Gender = "Male" | "Female";

export interface Parents {
  readonly fatherId: string;
  readonly motherId: string;
}

export interface UnitProps {
  readonly id: string;
  readonly name: string;
  readonly age: number;
  readonly birthYear?: number;
  readonly peakStartAge: number;
  readonly peakEndAge: number;
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
  // ── 血統継承システム ──
  readonly gender?: Gender;
  readonly affinity?: ReadonlyMap<string, number>;
  readonly parents?: Parents | null;
  readonly spouseId?: string | null;
}

export class Unit {
  readonly id: string;
  readonly name: string;
  readonly age: number;
  readonly birthYear: number | null;
  readonly peakStartAge: number;
  readonly peakEndAge: number;
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
  // ── 血統継承システム ──
  readonly gender: Gender;
  readonly affinity: ReadonlyMap<string, number>;
  readonly parents: Parents | null;
  readonly spouseId: string | null;

  constructor(props: UnitProps) {
    this.id = props.id;
    this.name = props.name;
    this.age = props.age;
    this.birthYear = props.birthYear ?? null;
    this.peakStartAge = props.peakStartAge;
    this.peakEndAge = props.peakEndAge;
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
    // 性別未指定時のデフォルトは "Male"。既存コード互換のため。
    // 新規生成箇所では呼び出し側で乱数50:50を設定すること（unit_generation skill 参照）
    this.gender = props.gender ?? "Male";
    this.affinity = props.affinity ?? new Map();
    this.parents = props.parents ?? null;
    this.spouseId = props.spouseId ?? null;
  }

  /**
   * 年齢による能力補正係数（0.0〜1.0）。三段階モデル:
   *   修業期 (age < peakStartAge): 線形上昇 [0 → 1]
   *   全盛期 (peakStartAge <= age <= peakEndAge): 1.0 固定
   *   衰退期 (age > peakEndAge): 3%/年で複利減衰
   */
  get growthFactor(): number {
    if (this.age >= this.maxAge) return 0;
    if (this.age <= this.peakEndAge && this.age >= this.peakStartAge) return 1;
    if (this.age < this.peakStartAge) {
      return this.age / this.peakStartAge;
    }
    const yearsDeclined = this.age - this.peakEndAge;
    return Math.pow(1 - 0.03, yearsDeclined);
  }

  get stats(): Stats {
    const f = this.growthFactor;
    return {
      strength:     Math.max(1, Math.round(this.baseStats.strength     * f)),
      agility:      Math.max(1, Math.round(this.baseStats.agility      * f)),
      intelligence: Math.max(1, Math.round(this.baseStats.intelligence * f)),
      endurance:    Math.max(1, Math.round(this.baseStats.endurance    * f)),
    };
  }

  get isRetired(): boolean {
    return this.age >= this.maxAge;
  }

  get isAlive(): boolean {
    return this.hp > 0;
  }

  get isMarried(): boolean {
    return this.spouseId !== null;
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

  /** 特定相手との好感度（未記録なら 0） */
  getAffinity(otherId: string): number {
    return this.affinity.get(otherId) ?? 0;
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

  /** 指定相手との好感度を delta だけ増やした新インスタンスを返す */
  withIncreasedAffinity(otherId: string, delta: number): Unit {
    const next = new Map(this.affinity);
    next.set(otherId, this.getAffinity(otherId) + delta);
    return new Unit({ ...this, affinity: next });
  }

  /** 配偶者ID を設定した新インスタンスを返す */
  withSpouse(spouseId: string): Unit {
    return new Unit({ ...this, spouseId });
  }
}
