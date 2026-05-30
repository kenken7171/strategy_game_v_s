#!/usr/bin/env bun
/**
 * GrandChronicle — 旅団100年変遷シミュレーター
 *
 * 概要:
 *   各ジョブ1名ずつ＋鉄壁騎士1名の計5名（全員20歳）で開始し、
 *   100年にわたって以下を繰り返す:
 *     - 毎年       : brigade.advance() で1年進める
 *     - 2年ごと    : ランダムジョブの新人2名（18歳）を加入
 *     - 毎年       : 衰退期（age > peakEndAge）のユニットがいれば最高齢を除名
 *     - 5年ごと    : 上位9名で大隊編成 → 強力な敵と1戦
 *
 * 使い方:
 *   bun scripts/run-grand-chronicle.ts
 *   bun scripts/run-grand-chronicle.ts --seed 7
 */
import { Unit } from "../packages/core/src/models/Unit";
import type { JobType, Gender } from "../packages/core/src/models/Unit";
import { Brigade } from "../packages/core/src/models/Brigade";
import { Squad } from "../packages/core/src/models/Squad";
import { BattleSimulator } from "../packages/core/src/BattleSimulator";
import type { SimulationResult } from "../packages/core/src/BattleSimulator";
import { NameGenerator, pickRandomOrigin } from "../packages/core/src/data/names";
import { CHRONICLE_CONFIG } from "../packages/core/src/config/ChronicleConfig";
import { rollPeakAges } from "../packages/core/src/utils/age";

// ─── CLI 引数 ────────────────────────────────────────────────────────────────

const args = process.argv.slice(2);
const argValue = (flag: string) => {
  const i = args.indexOf(flag);
  return i >= 0 ? args[i + 1] : undefined;
};
const SEED = parseInt(argValue("--seed") ?? "42", 10);

// ─── 乱数 ────────────────────────────────────────────────────────────────────

function mulberry32(seed: number): () => number {
  let s = seed;
  return () => {
    s = (s + 0x6d2b79f5) | 0;
    let t = Math.imul(s ^ (s >>> 15), 1 | s);
    t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}
const rand = mulberry32(SEED);
const ri = (min: number, max: number) => Math.floor(rand() * (max - min + 1)) + min;
const pick = <T>(arr: ReadonlyArray<T>): T => arr[Math.floor(rand() * arr.length)];

// ─── ジョブ定義 ──────────────────────────────────────────────────────────────

interface JobDefaults {
  maxHp: number; speed: number;
  frontAttack: number; rearAttack: number;
  bdf: number; sdf: number; ab: number; hl: number;
}
const JOB_DEFAULTS: Record<JobType, JobDefaults> = {
  iron_wall_knight: { maxHp: 250, speed: 10, frontAttack: 50, rearAttack:  10, bdf: 10, sdf: 15, ab:  0, hl:  0 },
  tactician:        { maxHp: 120, speed: 35, frontAttack: 20, rearAttack:  20, bdf:  0, sdf:  0, ab: 20, hl:  0 },
  medic:            { maxHp: 100, speed: 25, frontAttack: 10, rearAttack:  10, bdf:  0, sdf:  0, ab:  0, hl: 30 },
  sniper:           { maxHp:  80, speed: 40, frontAttack: 20, rearAttack:  90, bdf:  0, sdf:  0, ab:  0, hl:  0 },
  sorcerer:         { maxHp:  40, speed: 15, frontAttack: 10, rearAttack: 120, bdf:  0, sdf:  0, ab:  0, hl:  0 },
  standard_bearer:  { maxHp: 150, speed: 20, frontAttack: 30, rearAttack:  30, bdf:  0, sdf:  5, ab: 40, hl:  0 },
  heavy_infantry:   { maxHp: 300, speed: 15, frontAttack: 70, rearAttack:  20, bdf:  0, sdf: 10, ab:  0, hl:  0 },
  scout:            { maxHp:  90, speed: 60, frontAttack: 40, rearAttack:  40, bdf:  0, sdf:  0, ab:  0, hl:  0 },
};
const JOB_LIST: ReadonlyArray<JobType> = [
  "iron_wall_knight", "tactician", "medic", "sniper",
  "sorcerer", "standard_bearer", "heavy_infantry", "scout",
];
const JOB_JP: Record<JobType, string> = {
  iron_wall_knight: "鉄壁騎士",
  tactician: "戦術官",
  medic: "衛生兵",
  sniper: "狙撃兵",
  sorcerer: "呪術師",
  standard_bearer: "旗手",
  heavy_infantry: "重装歩兵",
  scout: "斥候",
};

// ─── ユニット生成（NameGenerator + historicalNames で命名重複回避） ──────────

const nameGen = new NameGenerator(rand);

let _uid = 0;
function makeRecruit(
  job: JobType,
  age: number,
  currentYear: number,
  historical: ReadonlySet<string>
): Unit {
  // peakStart/End は CHRONICLE_CONFIG.TIME 基準で ±3 ロール（個体差）
  const { peakStartAge, peakEndAge } = rollPeakAges(rand);
  const maxAge       = peakEndAge + ri(15, 25);
  const id           = `u${String(_uid++).padStart(3, "0")}`;
  const gender: Gender = rand() < 0.5 ? "Male" : "Female";
  const origin       = pickRandomOrigin(rand);
  const name         = nameGen.pick(origin, gender, historical);
  return new Unit({
    id, name, job,
    age,
    birthYear: currentYear - age,
    peakStartAge,
    peakEndAge,
    maxAge,
    baseStats: { strength: ri(70, 130), agility: 0, intelligence: 0, endurance: 0 },
    gender,
    origin,
  });
}

/**
 * 大隊編成時に combat stats を年齢でスケールした Unit を返す。
 * baseStats は全盛期最大値、growthFactor は 0〜1。
 * 速度・攻撃力をスケールし、HP / 特殊能力（BDF/SDF/AB/HL）は素のまま。
 */
function buildBattleUnit(u: Unit): Unit {
  if (!u.job) return u;
  const d = JOB_DEFAULTS[u.job];
  const f = u.growthFactor;
  const scale = (v: number) => Math.max(1, Math.round(v * f));
  return new Unit({
    ...u,
    maxHp: d.maxHp,
    hp: d.maxHp,
    speed: scale(d.speed),
    frontAttack: scale(d.frontAttack),
    rearAttack: scale(d.rearAttack),
    bdf: d.bdf, sdf: d.sdf, ab: d.ab, hl: d.hl,
  });
}

// ─── 初期旅団 ────────────────────────────────────────────────────────────────

const FOUNDING_JOBS: ReadonlyArray<JobType> = [
  "iron_wall_knight",
  "tactician",
  "medic",
  "sniper",
  "iron_wall_knight", // 鉄壁を1名増やし前衛厚めの5名構成
];

function makeFoundingMembers(): Unit[] {
  // 創設時は historical 空集合。各メンバーを生成しながら累積して重複回避
  const cumulative = new Set<string>();
  const founders: Unit[] = [];
  for (const j of FOUNDING_JOBS) {
    const u = makeRecruit(j, 20, 1, cumulative);
    cumulative.add(u.name);
    founders.push(u);
  }
  return founders;
}

// ─── 大隊編成（上位9名を slot に配置） ────────────────────────────────────────

const FRONT_JOBS: ReadonlyArray<JobType> = ["iron_wall_knight", "tactician"];

function formBattalion(picks: ReadonlyArray<Unit>): {
  squads: Squad[];
  averageAge: number;
  peakCount: number;
} {
  const front: Unit[] = [];
  const rear: Unit[] = [];
  for (const u of picks) {
    if (front.length < 3 && u.job && FRONT_JOBS.includes(u.job)) {
      front.push(u);
    } else {
      rear.push(u);
    }
  }
  // FRONT が3枠埋まらなければ後衛から繰り上げ
  while (front.length < 3 && rear.length > 0) front.push(rear.shift()!);

  const rearL = rear.slice(0, 3);
  const rearR = rear.slice(3, 6);

  const squads = [
    new Squad("FRONT",  front.map(buildBattleUnit)),
    new Squad("REAR-L", rearL.map(buildBattleUnit)),
    new Squad("REAR-R", rearR.map(buildBattleUnit)),
  ];

  const totalAge = picks.reduce((s, u) => s + u.age, 0);
  const averageAge = picks.length > 0 ? totalAge / picks.length : 0;
  const peakCount = picks.filter(
    (u) => u.age >= u.peakStartAge && u.age <= u.peakEndAge
  ).length;

  return { squads, averageAge, peakCount };
}

// ─── 強敵生成（攻撃力30 / ヒット数10） ────────────────────────────────────────

function makeTrialEnemy(): Squad[] {
  // DynamicEnemy は enemy 全ユニットの平均攻撃をダメージ、
  // 生存数を hitCount として action を生成する。
  // → 攻撃力30・ヒット10 を満たすため frontAttack=rearAttack=30 のユニット10体を用意。
  const enemyUnit = (i: number): Unit =>
    new Unit({
      id: `enemy-${i}`,
      name: `試練の兵${i + 1}`,
      job: null,
      age: 25, peakStartAge: 25, peakEndAge: 35, maxAge: 60,
      baseStats: { strength: 60, agility: 0, intelligence: 0, endurance: 0 },
      maxHp: 150, hp: 150,
      speed: 20,
      frontAttack: 30, rearAttack: 30,
    });

  const units = Array.from({ length: 10 }, (_, i) => enemyUnit(i));
  return [
    new Squad("E1", units.slice(0, 3)),
    new Squad("E2", units.slice(3, 6)),
    new Squad("E3", units.slice(6, 9)),
    new Squad("E4", units.slice(9, 10)),
  ];
}

// ─── 戦闘実行 + サマリー ──────────────────────────────────────────────────────

interface BattleSummary {
  year: number;
  result: "Win" | "Loss";
  turns: number;
  averageAge: number;
  peakCount: number;
  battalionSize: number;
  mvpJob: JobType | "なし";
  killCount: Readonly<Record<string, number>>;
  joinsInWindow: number;
  retiresInWindow: number;
}

function determineMvp(stats: SimulationResult["statistics"]): JobType | "なし" {
  const entries = Object.entries(stats.totalDamageDealt) as [JobType, number][];
  if (entries.length === 0) return "なし";
  return entries.sort(([, a], [, b]) => b - a)[0][0];
}

function runTrialBattle(
  brigade: Brigade,
  year: number,
  joinsInWindow: number,
  retiresInWindow: number,
  rng: () => number
): { summary: BattleSummary; squadmatePairs: ReadonlyArray<readonly [string, string]> } {
  const picks = brigade.selectBattalion(CHRONICLE_CONFIG.SCHEDULE.BATTALION_SIZE);
  const { squads, averageAge, peakCount } = formBattalion(picks);
  const enemy = makeTrialEnemy();

  const sim = new BattleSimulator(squads, enemy, {
    maxTurns: CHRONICLE_CONFIG.BATTLE.MAX_TURNS,
    rng,
    verbose: false,
  });
  const result = sim.run();

  return {
    summary: {
      year,
      result: result.winner === "Allies" ? "Win" : "Loss",
      turns: result.turns,
      averageAge,
      peakCount,
      battalionSize: picks.length,
      mvpJob: determineMvp(result.statistics),
      killCount: result.statistics.killCount,
      joinsInWindow,
      retiresInWindow,
    },
    // ★ バグ修正: 戦闘後に同分隊好感度を蓄積するため、
    // 呼び出し側で brigade.applyBattleAffinity(squadmatePairs) を呼ぶ必要がある
    squadmatePairs: result.squadmatePairs,
  };
}

function printBattleSummary(s: BattleSummary): void {
  const mvpLabel = s.mvpJob === "なし" ? "なし" : JOB_JP[s.mvpJob];
  console.log(`\n[Year ${s.year}] --- 試練の門 ---`);
  console.log(`  Result            : ${s.result} (Turns: ${s.turns})`);
  console.log(`  Battalion Avg Age : ${s.averageAge.toFixed(1)}`);
  console.log(`  Peak Ratio        : ${s.peakCount}/${s.battalionSize} Units`);
  console.log(`  MVP               : ${mvpLabel}`);
  console.log(`  Member Change     : +${s.joinsInWindow} / -${s.retiresInWindow}`);
}

// ─── 衰退期ユニットの最高齢を除名 ─────────────────────────────────────────────

function removeOldestDecliningUnit(brigade: Brigade): { brigade: Brigade; removed: Unit | null } {
  const decliners = brigade.units.filter((u) => u.age > u.peakEndAge && !u.isRetired);
  if (decliners.length === 0) return { brigade, removed: null };
  const oldest = decliners.reduce((a, b) => (a.age > b.age ? a : b));
  const next = new Brigade(
    brigade.units.filter((u) => u.id !== oldest.id),
    [...brigade.squads],
    brigade.currentYear,
    brigade.pendingBirths,
    brigade.historicalNames // 引退者の名前も永続記録（コンストラクタで自動保持）
  );
  return { brigade: next, removed: oldest };
}

// ─── メインループ ────────────────────────────────────────────────────────────

const battleRng = mulberry32(SEED + 1); // 戦闘用 RNG（別シード）

console.log("╔══════════════════════════════════════════════════════════╗");
console.log("║       GrandChronicle — 旅団100年変遷シミュレーター       ║");
console.log("╚══════════════════════════════════════════════════════════╝");
console.log(`  RNG seed     : ${SEED}`);
console.log(`  Founding     : ${FOUNDING_JOBS.length}名（20歳・各ジョブ＋鉄壁追加）`);
console.log("");

let brigade = new Brigade(makeFoundingMembers());
const summaries: BattleSummary[] = [];

let joinsInWindow = 0;
let retiresInWindow = 0;

const TOTAL_YEARS      = CHRONICLE_CONFIG.SCHEDULE.CHRONICLE_YEARS;
const RECRUIT_INTERVAL = CHRONICLE_CONFIG.SCHEDULE.RECRUIT_INTERVAL;
const RECRUIT_COUNT    = CHRONICLE_CONFIG.SCHEDULE.RECRUIT_COUNT;
const BATTLE_INTERVAL  = CHRONICLE_CONFIG.SCHEDULE.BATTLE_INTERVAL;

for (let year = 1; year <= TOTAL_YEARS; year++) {
  // 1) RECRUIT_INTERVAL 年ごとに RECRUIT_COUNT 名（18歳・ランダムジョブ）
  let recruits: Unit[] = [];
  if (year % RECRUIT_INTERVAL === 0) {
    const local = new Set(brigade.historicalNames);
    for (let i = 0; i < RECRUIT_COUNT; i++) {
      const r = makeRecruit(pick(JOB_LIST), 18, brigade.currentYear, local);
      local.add(r.name);
      recruits.push(r);
    }
  }

  // 2) advance: 加齢 → 引退判定 → recruits 追加（子供の命名にも NameGen を使用）
  const { brigade: advanced, events } = brigade.advance(recruits, { nameGenerator: nameGen });
  brigade = advanced;
  joinsInWindow   += events.filter((e) => e.type === "join").length;
  retiresInWindow += events.filter((e) => e.type === "retire").length;

  // 3) 衰退期ユニットがいれば最高齢を除名
  const { brigade: pruned, removed } = removeOldestDecliningUnit(brigade);
  brigade = pruned;
  if (removed) retiresInWindow++;

  // 4) BATTLE_INTERVAL 年ごとに試練戦
  if (year % BATTLE_INTERVAL === 0) {
    const { summary, squadmatePairs } = runTrialBattle(
      brigade, year, joinsInWindow, retiresInWindow, battleRng
    );
    summaries.push(summary);
    printBattleSummary(summary);
    // ★ バグ修正: 戦闘後に同分隊好感度を蓄積する。
    // これを呼ばないと血統サイクル（結婚→出産→継承）が100年で一度も発火しない。
    brigade = brigade.applyBattleAffinity(
      squadmatePairs,
      CHRONICLE_CONFIG.LINEAGE.AFFINITY_PER_BATTLE
    );
    joinsInWindow = 0;
    retiresInWindow = 0;
  }
}

// ─── 最終レポート ────────────────────────────────────────────────────────────

console.log("\n" + "═".repeat(60));
console.log("  FINAL CHRONICLE REPORT (100 Years)");
console.log("═".repeat(60));

const wins   = summaries.filter((s) => s.result === "Win");
const losses = summaries.filter((s) => s.result === "Loss");

console.log(`\n● 通算戦績 : ${wins.length}勝 ${losses.length}敗 (全${summaries.length}戦)`);

// 最強期: 最短ターン勝利
const strongest = wins.length > 0
  ? wins.reduce((a, b) => (a.turns <= b.turns ? a : b))
  : null;
if (strongest) {
  console.log(`● 最強期   : Year ${strongest.year} （${strongest.turns}ターンで勝利・平均年齢${strongest.averageAge.toFixed(1)}）`);
} else {
  console.log(`● 最強期   : 勝利なし`);
}

// 歴代最多キルジョブ
const totalKills = new Map<string, number>();
for (const s of summaries) {
  for (const [job, n] of Object.entries(s.killCount)) {
    totalKills.set(job, (totalKills.get(job) ?? 0) + n);
  }
}
if (totalKills.size > 0) {
  const sorted = [...totalKills.entries()].sort(([, a], [, b]) => b - a);
  const [topJob, topKills] = sorted[0];
  const label = JOB_JP[topJob as JobType] ?? topJob;
  const breakdown = sorted
    .map(([j, n]) => `${JOB_JP[j as JobType] ?? j}:${n}`)
    .join(", ");
  console.log(`● 最多キル : ${label} (${topKills} kills)`);
  console.log(`  内訳     : ${breakdown}`);
} else {
  console.log(`● 最多キル : 戦闘で討伐なし`);
}

console.log("");
