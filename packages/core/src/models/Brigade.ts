import { Unit, Stats, JobType, Gender, Origin } from "./Unit";
import { Squad } from "./Squad";
import { NameGenerator, ALL_ORIGINS } from "../data/names";
import { CHRONICLE_CONFIG } from "../config/ChronicleConfig";
import { rollChildPeakAges } from "../utils/age";

// ─── 出生予約 ────────────────────────────────────────────────────────────────

export interface BirthRegistry {
  readonly fatherId: string;
  readonly motherId: string;
  /** 子が予約された旅団暦（仕様: 「誕生した年の旅団暦」） */
  readonly birthYear: number;
  /** 両親の baseStats を平均した「全盛期予想値」 */
  readonly potentialStats: Stats;
  /** 親のいずれかから 50:50 で継承 */
  readonly job: JobType | null;
  /** birthYear + INDUCTION_AGE。この年に達したら Unit を実体化する */
  readonly plannedJoinYear: number;
  /** 親のいずれかから CULTURE_INHERIT_PROB の確率で継承される文化圏 */
  readonly origin: Origin;
  /**
   * 出産予約時にロールされた子の peakStartAge / peakEndAge。
   * rollChildPeakAges で「両親平均±1」の遺伝性を持たせている。
   * 15年後の入団時にこの値が使われるので、親が老いたり死んだりしても
   * 出産時の親の能力ピークが子に反映される。
   */
  readonly childPeakStartAge: number;
  readonly childPeakEndAge: number;
}

// ─── 年次イベント ─────────────────────────────────────────────────────────────

export type YearEvent =
  | { readonly type: "join"; readonly unit: Unit }
  | { readonly type: "retire"; readonly unit: Unit }
  | { readonly type: "marriage"; readonly husband: Unit; readonly wife: Unit }
  | { readonly type: "birth_planned"; readonly registry: BirthRegistry }
  | { readonly type: "birth"; readonly unit: Unit };

export interface AdvanceResult {
  readonly brigade: Brigade;
  readonly events: ReadonlyArray<YearEvent>;
}

// ─── advance のオプション ─────────────────────────────────────────────────────

export interface AdvanceOptions {
  /** 直前のバトルで同一分隊だったユニットID組（双方向の重複は不要、片方向1組でよい） */
  readonly battlePairs?: ReadonlyArray<readonly [string, string]>;
  /** 確率判定に使う RNG（テスト・再現性のためDI） */
  readonly rng?: () => number;
  /** 結婚成立確率（条件成立ペアに対して）。デフォルト 0.3 */
  readonly marriageProb?: number;
  /** 出産予約確率（結婚済みカップル毎年）。デフォルト 0.2 */
  readonly birthProb?: number;
  /** バトル1回あたり同分隊ペアに加算される好感度。デフォルト 10 */
  readonly affinityPerBattle?: number;
  /** 結婚条件の好感度閾値。デフォルト 100 */
  readonly affinityThreshold?: number;
  /**
   * 子の maxAge（既定 55）。
   * 子の peakStart/EndAge は親から「両親平均±1」で自動継承されるため
   * オプションで上書きできない（CHRONICLE_CONFIG.TIME と utils/age 参照）。
   */
  readonly childMaxAge?: number;
  /**
   * 子供の命名に使う Generator。指定がない場合は機械的命名
   * （`継承者child-<year>-<n>`）にフォールバックする（旧挙動）。
   * historicalNames を参照して重複回避する。
   */
  readonly nameGenerator?: NameGenerator;
}

// ─── Brigade ──────────────────────────────────────────────────────────────────

export class Brigade {
  readonly units: ReadonlyArray<Unit>;
  readonly currentYear: number;
  readonly pendingBirths: ReadonlyArray<BirthRegistry>;
  /**
   * 過去に旅団に所属した全ユニット（新人・子供・引退者含む）の名前 Set。
   * 命名重複回避のために永続記録される。
   * コンストラクタで現 units の名前を自動登録、advance() で新規追加ユニットも追記する。
   */
  readonly historicalNames: ReadonlySet<string>;
  private _squads: Squad[];

  constructor(
    units: ReadonlyArray<Unit>,
    squads: Squad[] = [],
    currentYear = 1,
    pendingBirths: ReadonlyArray<BirthRegistry> = [],
    historicalNames: ReadonlySet<string> = new Set()
  ) {
    this.units = units;
    this._squads = [...squads];
    this.currentYear = currentYear;
    this.pendingBirths = pendingBirths;
    // 渡された名前 + 現 units の名前を全て登録（外部の Set を変更しない）
    const history = new Set(historicalNames);
    for (const u of units) history.add(u.name);
    this.historicalNames = history;
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

  /**
   * バトル後に同分隊だったペアの好感度を加算した新 Brigade を返す。
   * 通常は advance() に battlePairs を渡せばまとめて処理される。
   * 検証用に単独でも呼べるようにしてある。
   */
  applyBattleAffinity(
    pairs: ReadonlyArray<readonly [string, string]>,
    delta: number = 10
  ): Brigade {
    const map = new Map(this.units.map((u) => [u.id, u]));
    for (const [aId, bId] of pairs) {
      const a = map.get(aId);
      const b = map.get(bId);
      if (!a || !b) continue;
      map.set(aId, a.withIncreasedAffinity(bId, delta));
      map.set(bId, b.withIncreasedAffinity(aId, delta));
    }
    return new Brigade(
      [...map.values()],
      [...this._squads],
      this.currentYear,
      this.pendingBirths,
      this.historicalNames
    );
  }

  /**
   * 1年進める。年次処理順序:
   *   1) 好感度更新（直前バトルで同分隊だったペアに +delta）
   *   2) 加齢 → 引退判定
   *   3) 結婚判定（未婚男女・互いに閾値以上・確率で成立）
   *   4) 出産予約判定（結婚カップル毎年・確率で予約）
   *   5) 15歳入団（pendingBirths のうち plannedJoinYear = newYear のものを Unit 化）
   *   6) recruits 追加
   */
  advance(
    recruits: ReadonlyArray<Unit> = [],
    options: AdvanceOptions = {}
  ): AdvanceResult {
    const rng               = options.rng               ?? Math.random;
    const marriageProb      = options.marriageProb      ?? CHRONICLE_CONFIG.LINEAGE.MARRIAGE_PROBABILITY;
    const birthProb         = options.birthProb         ?? CHRONICLE_CONFIG.LINEAGE.BIRTH_PROBABILITY;
    const affinityPerBattle = options.affinityPerBattle ?? CHRONICLE_CONFIG.LINEAGE.AFFINITY_PER_BATTLE;
    const affinityThreshold = options.affinityThreshold ?? CHRONICLE_CONFIG.LINEAGE.MARRIAGE_THRESHOLD;
    // childMaxAge は CHRONICLE_CONFIG に直接対応がないため独自既定値
    const childMaxAge       = options.childMaxAge       ?? 55;
    const cultureInheritProb = CHRONICLE_CONFIG.LINEAGE.CULTURE_INHERIT_PROB;
    const inductionAge      = CHRONICLE_CONFIG.TIME.INDUCTION_AGE;
    const battlePairs       = options.battlePairs      ?? [];

    const events: YearEvent[] = [];
    const newYear = this.currentYear + 1;

    // ── 1) 好感度更新 ────────────────────────────────────────────────────────
    const unitMap = new Map(this.units.map((u) => [u.id, u]));
    for (const [aId, bId] of battlePairs) {
      const a = unitMap.get(aId);
      const b = unitMap.get(bId);
      if (!a || !b) continue;
      unitMap.set(aId, a.withIncreasedAffinity(bId, affinityPerBattle));
      unitMap.set(bId, b.withIncreasedAffinity(aId, affinityPerBattle));
    }

    // ── 2) 加齢 → 引退判定 ──────────────────────────────────────────────────
    let working: Unit[] = [...unitMap.values()].map((u) => u.grow());
    const survivors: Unit[] = [];
    for (const u of working) {
      if (u.isRetired) {
        events.push({ type: "retire", unit: u });
      } else {
        survivors.push(u);
      }
    }
    working = survivors;

    // ── 3) 結婚判定 ──────────────────────────────────────────────────────────
    // 各人は1年で最大1ペアまで成立。idで添字を管理して、書き換える。
    const idxOf = new Map<string, number>();
    working.forEach((u, i) => idxOf.set(u.id, i));
    const consumed = new Set<string>();
    for (let i = 0; i < working.length; i++) {
      const a = working[i];
      if (consumed.has(a.id) || a.isMarried) continue;
      for (let j = i + 1; j < working.length; j++) {
        const b = working[j];
        if (consumed.has(b.id) || b.isMarried) continue;
        if (a.gender === b.gender) continue;
        if (a.getAffinity(b.id) < affinityThreshold) continue;
        if (b.getAffinity(a.id) < affinityThreshold) continue;
        if (rng() >= marriageProb) continue;

        const husband = a.gender === "Male" ? a : b;
        const wife    = a.gender === "Female" ? a : b;
        const newHusband = husband.withSpouse(wife.id);
        const newWife    = wife.withSpouse(husband.id);
        working[idxOf.get(husband.id)!] = newHusband;
        working[idxOf.get(wife.id)!]    = newWife;
        consumed.add(a.id);
        consumed.add(b.id);
        events.push({ type: "marriage", husband: newHusband, wife: newWife });
        break;
      }
    }

    // ── 4) 出産予約 ──────────────────────────────────────────────────────────
    const newPending: BirthRegistry[] = [...this.pendingBirths];
    const handledCouples = new Set<string>();
    for (const u of working) {
      if (!u.isMarried || !u.spouseId) continue;
      const coupleKey = [u.id, u.spouseId].sort().join("|");
      if (handledCouples.has(coupleKey)) continue;
      handledCouples.add(coupleKey);
      const spouse = working.find((x) => x.id === u.spouseId);
      if (!spouse) continue;
      if (rng() >= birthProb) continue;

      const father = u.gender === "Male" ? u : spouse;
      const mother = u.gender === "Female" ? u : spouse;
      const potentialStats: Stats = {
        strength:     Math.round((father.baseStats.strength     + mother.baseStats.strength)     / 2),
        agility:      Math.round((father.baseStats.agility      + mother.baseStats.agility)      / 2),
        intelligence: Math.round((father.baseStats.intelligence + mother.baseStats.intelligence) / 2),
        endurance:    Math.round((father.baseStats.endurance    + mother.baseStats.endurance)    / 2),
      };
      const job: JobType | null = rng() < 0.5 ? father.job : mother.job;
      // 仕様: 子は両親のいずれかの文化圏を CULTURE_INHERIT_PROB で継承
      const origin: Origin = rng() < cultureInheritProb ? father.origin : mother.origin;
      // 子の peakStart/End は「両親の平均±1」でロール（成長タイプの遺伝性）
      const childAges = rollChildPeakAges(
        father.peakStartAge, father.peakEndAge,
        mother.peakStartAge, mother.peakEndAge,
        rng
      );
      const registry: BirthRegistry = {
        fatherId: father.id,
        motherId: mother.id,
        birthYear: newYear,
        potentialStats,
        job,
        plannedJoinYear: newYear + inductionAge,
        origin,
        childPeakStartAge: childAges.peakStartAge,
        childPeakEndAge:   childAges.peakEndAge,
      };
      newPending.push(registry);
      events.push({ type: "birth_planned", registry });
    }

    // ── 5) 15歳入団 ──────────────────────────────────────────────────────────
    // baseStats = potentialStats を渡すことで、stats ゲッターが自動的に
    // growthFactor = 15 / peakStartAge を掛ける（仕様通り）
    // 名前は NameGenerator を使い、historicalNames を見て重複回避する。
    // 重複回避のため、生成順に「ローカル累積 Set」へ追加していく
    // （this.historicalNames は readonly なので、その上に重ねた一時 Set を渡す）
    const cumulative = new Set(this.historicalNames);
    const remainingPending: BirthRegistry[] = [];
    let childCounter = 0;
    for (const reg of newPending) {
      if (reg.plannedJoinYear !== newYear) {
        remainingPending.push(reg);
        continue;
      }
      const childId = `child-${newYear}-${childCounter++}`;
      const gender: Gender = rng() < 0.5 ? "Male" : "Female";
      const childName = options.nameGenerator
        ? options.nameGenerator.pick(reg.origin, gender, cumulative)
        : `継承者${childId}`;
      cumulative.add(childName);
      const child = new Unit({
        id: childId,
        name: childName,
        age: inductionAge,
        birthYear: reg.birthYear,
        // 出産予約時にロール済みの「両親平均±1」を使う（遺伝性）
        peakStartAge: reg.childPeakStartAge,
        peakEndAge:   reg.childPeakEndAge,
        maxAge: childMaxAge,
        baseStats: reg.potentialStats,
        gender,
        origin: reg.origin,
        job: reg.job,
        parents: { fatherId: reg.fatherId, motherId: reg.motherId },
      });
      working.push(child);
      events.push({ type: "birth", unit: child });
    }

    // ── 6) recruits 追加 ────────────────────────────────────────────────────
    for (const r of recruits) {
      working.push(r);
      cumulative.add(r.name);
      events.push({ type: "join", unit: r });
    }

    return {
      brigade: new Brigade(working, [], newYear, remainingPending, cumulative),
      events,
    };
  }
}
