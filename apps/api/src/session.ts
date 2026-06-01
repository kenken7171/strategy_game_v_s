/**
 * GameSession — 単一プレイヤーの 100年クロニクル状態を保持
 *
 * インメモリ管理。複数セッション・永続化は将来課題（M3 以降）。
 * 現状は API サーバ1プロセス = 1セッションで運用する。
 */
import {
  Brigade,
  Unit,
  NameGenerator,
  pickRandomOrigin,
  rollPeakAges,
  CHRONICLE_CONFIG_EXTREME as CHRONICLE_CONFIG,
  acceptRecruit as serviceAcceptRecruit,
  dismissUnit as serviceDismissUnit,
  type Gender,
  type JobType,
  type YearEvent,
} from "../../../packages/core/src/index";
import type { BattleSimulator } from "../../../packages/core/src/BattleSimulator";

// ゲーム本編は extreme モード（50人定員・1年1戦・25名創設・SPD+0.6/年）で運用

// ─── PRNG ────────────────────────────────────────────────────────────────────

function mulberry32(seed: number): () => number {
  let s = seed;
  return () => {
    s = (s + 0x6d2b79f5) | 0;
    let t = Math.imul(s ^ (s >>> 15), 1 | s);
    t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}

const JOB_LIST: ReadonlyArray<JobType> = [
  "iron_wall_knight", "tactician", "medic", "sniper",
  "sorcerer", "standard_bearer", "heavy_infantry", "scout",
];

// ─── イベント履歴（年代記表示用） ─────────────────────────────────────────

export interface ChronicleEntry {
  readonly year: number;
  readonly type: "join" | "retire" | "marriage" | "birth_planned" | "birth" | "battle";
  readonly text: string;
}

// ─── GameSession ──────────────────────────────────────────────────────────────

export class GameSession {
  private readonly rand: () => number;
  private readonly nameGen: NameGenerator;
  private _brigade: Brigade;
  private _year: number = 1;
  private _seed: number;
  private _uid: number = 0;
  /** 採用候補プール（GUILD_MANAGEMENT フェーズで提示） */
  private _candidatePool: Unit[] = [];
  /** 年代記ログ */
  private _chronicle: ChronicleEntry[] = [];
  /** 戦闘結果ログ（最新のみ保持） */
  private _lastBattleResult: unknown = null;
  /** アクティブな戦闘インスタンス（ターン単位 API 用） */
  private _activeBattle: BattleSimulator | null = null;

  constructor(seed: number = 42) {
    this._seed = seed;
    this.rand = mulberry32(seed);
    this.nameGen = new NameGenerator(this.rand);
    this._brigade = this.bootstrapBrigade();
    this.refreshCandidatePool();
  }

  get year(): number { return this._year; }
  get seed(): number { return this._seed; }
  get brigade(): Brigade { return this._brigade; }
  get candidatePool(): ReadonlyArray<Unit> { return this._candidatePool; }
  get chronicle(): ReadonlyArray<ChronicleEntry> { return this._chronicle; }
  get lastBattleResult(): unknown { return this._lastBattleResult; }

  /** 採用候補プールを直接書き換える（applyDecisions 後の更新用） */
  setCandidatePool(pool: Unit[]): void { this._candidatePool = pool; }
  setBrigade(b: Brigade): void { this._brigade = b; }
  setLastBattleResult(r: unknown): void { this._lastBattleResult = r; }
  setActiveBattle(b: BattleSimulator | null): void { this._activeBattle = b; }
  get activeBattle(): BattleSimulator | null { return this._activeBattle; }

  /** 戦闘ログを年代記に追加 */
  pushChronicle(entry: ChronicleEntry): void {
    this._chronicle.push(entry);
  }

  /** ユニーク ID を払い出す */
  nextUnitId(prefix: string = "u"): string {
    return `${prefix}-${String(this._uid++).padStart(4, "0")}`;
  }

  /** 新規ユニットを生成（命名は historicalNames で重複回避） */
  makeRecruit(job: JobType, age: number, historical: ReadonlySet<string>): Unit {
    const { peakStartAge, peakEndAge } = rollPeakAges(this.rand);
    const maxAge = peakEndAge + 15 + Math.floor(this.rand() * 11); // 15〜25
    const id = this.nextUnitId();
    const gender: Gender = this.rand() < 0.5 ? "Male" : "Female";
    const origin = pickRandomOrigin(this.rand);
    const name = this.nameGen.pick(origin, gender, historical);
    return new Unit({
      id, name, job, age,
      birthYear: this._year - age,
      peakStartAge, peakEndAge, maxAge,
      baseStats: {
        strength: 70 + Math.floor(this.rand() * 61), // 70〜130
        agility: 0, intelligence: 0, endurance: 0,
      },
      gender, origin,
    });
  }

  /** ランダムジョブから新人を選出 */
  makeRandomRecruit(historical: ReadonlySet<string>, age: number = 18): Unit {
    const job = JOB_LIST[Math.floor(this.rand() * JOB_LIST.length)];
    return this.makeRecruit(job, age, historical);
  }

  /** 初期旅団（INITIAL_MEMBER_COUNT 名） */
  private bootstrapBrigade(): Brigade {
    const initial: Unit[] = [];
    const cumulative = new Set<string>();
    const founding: JobType[] = [
      "iron_wall_knight", "tactician", "medic", "sniper", "iron_wall_knight",
    ];
    // 5名は固定構成、残りはランダム
    for (let i = 0; i < CHRONICLE_CONFIG.SCHEDULE.INITIAL_MEMBER_COUNT; i++) {
      const job = i < founding.length ? founding[i] : JOB_LIST[Math.floor(this.rand() * JOB_LIST.length)];
      const u = this.makeRecruit(job, 20, cumulative);
      cumulative.add(u.name);
      initial.push(u);
    }
    return new Brigade(initial);
  }

  /** 候補プール生成（毎年 RECRUIT_COUNT 名 + 子供は brigade.advance が自動投入） */
  refreshCandidatePool(): void {
    const local = new Set(this._brigade.historicalNames);
    const pool: Unit[] = [];
    const count = CHRONICLE_CONFIG.SCHEDULE.RECRUIT_COUNT;
    for (let i = 0; i < count; i++) {
      const r = this.makeRandomRecruit(local);
      local.add(r.name);
      pool.push(r);
    }
    this._candidatePool = pool;
  }

  /** 採用: 候補プールから1名を旅団へ */
  applyAccept(unitId: string): { brigade: Brigade; accepted: Unit | null } {
    const target = this._candidatePool.find((u) => u.id === unitId);
    if (!target) return { brigade: this._brigade, accepted: null };
    this._brigade = serviceAcceptRecruit(this._brigade, target);
    this._candidatePool = this._candidatePool.filter((u) => u.id !== unitId);
    this.pushChronicle({
      year: this._year,
      type: "join",
      text: `✨ ${target.name}（${target.job ?? "-"}）が入団した`,
    });
    return { brigade: this._brigade, accepted: target };
  }

  /** 解雇: 旅団から1名を除名 */
  applyDismiss(unitId: string): { brigade: Brigade; dismissed: Unit | null } {
    const res = serviceDismissUnit(this._brigade, unitId);
    this._brigade = res.brigade;
    if (res.dismissed) {
      this.pushChronicle({
        year: this._year,
        type: "retire",
        text: `🕊️ ${res.dismissed.name}（${res.dismissed.job ?? "-"}, ${res.dismissed.age}歳）が引退した`,
      });
    }
    return res;
  }

  /** 年送り（CHRONICLE 移行時に呼ぶ）。advance + イベント記録 + 候補プール再生成 */
  advanceYear(rng: () => number): ReadonlyArray<YearEvent> {
    const { brigade: next, events } = this._brigade.advance([], {
      nameGenerator: this.nameGen,
      rng,
    });
    this._brigade = next;
    this._year += 1;

    for (const e of events) {
      if (e.type === "marriage") {
        this.pushChronicle({ year: this._year, type: "marriage", text: `💍 ${e.husband.name} × ${e.wife.name} が結婚した` });
      } else if (e.type === "birth_planned") {
        this.pushChronicle({ year: this._year, type: "birth_planned", text: `👶 ${e.registry.fatherId} と ${e.registry.motherId} の間に子が宿った` });
      } else if (e.type === "birth") {
        this.pushChronicle({ year: this._year, type: "birth", text: `🎉 ${e.unit.name} が15歳で旅団に加わった` });
      }
    }

    this.refreshCandidatePool();
    return events;
  }
}

// ─── シングルトン（M3 で複数セッション対応予定） ───────────────────────────

let _current: GameSession | null = null;

export function getOrCreateSession(seed?: number): GameSession {
  if (!_current) _current = new GameSession(seed ?? 42);
  return _current;
}

export function resetSession(seed?: number): GameSession {
  _current = new GameSession(seed ?? 42);
  return _current;
}
