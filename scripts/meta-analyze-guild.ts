#!/usr/bin/env bun
/**
 * meta-analyze-guild.ts
 *
 * 「50人定員・超高回転スパルタモード」専用のメタ分析。
 * CHRONICLE_CONFIG_EXTREME を読み込み、10シードで100年×100戦を実行する。
 *
 * 通常モードに対する主な違い:
 *   - DECAY_RATE 10%, RECRUIT_INTERVAL 1, BATTLE_INTERVAL 1
 *   - INITIAL_MEMBER_COUNT 25, BATTALION_SIZE 12, MAX_BRIGADE_SIZE 50
 *   - AFFINITY_PER_BATTLE 35, MARRIAGE_THRESHOLD 70, MARRIAGE_PROB 0.8, BIRTH_PROB 0.6
 *
 * 出力:
 *   - reports/_meta-analysis-guild-data.json — 生データ
 */
import { Unit } from "../packages/core/src/models/Unit";
import { Brigade } from "../packages/core/src/models/Brigade";
import { Squad } from "../packages/core/src/models/Squad";
import { BattleSimulator } from "../packages/core/src/BattleSimulator";
import type { SimulationResult } from "../packages/core/src/BattleSimulator";
import type { JobType, Gender } from "../packages/core/src/models/Unit";
import { NameGenerator, pickRandomOrigin, TITLES } from "../packages/core/src/data/names";
import { CHRONICLE_CONFIG_EXTREME as CFG } from "../packages/core/src/config/ChronicleConfig.extreme";
import { rollPeakAges } from "../packages/core/src/utils/age";
import { enforceMaxBrigadeSize } from "../packages/core/src/utils/brigade";
import { writeFileSync } from "fs";
import { join } from "path";

// ─── RNG ─────────────────────────────────────────────────────────────────────

function mulberry32(seed: number): () => number {
  let s = seed;
  return () => {
    s = (s + 0x6d2b79f5) | 0;
    let t = Math.imul(s ^ (s >>> 15), 1 | s);
    t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}

// ─── ジョブ定義 ───────────────────────────────────────────────────────────────

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
const FRONT_JOBS: ReadonlyArray<JobType> = [
  "iron_wall_knight", "tactician", "heavy_infantry", "standard_bearer",
];

// ─── 1シード分シミュレーション ────────────────────────────────────────────────

interface BattleRecord {
  year: number;
  result: "Win" | "Loss" | "Draw";
  turns: number;
  avgAge: number;
  peakCount: number;
  battalionSize: number;
  mvpJob: JobType | "なし";
}

interface RunResult {
  seed: number;
  battles: BattleRecord[];
  wins: number;
  losses: number;
  draws: number;
  bestVictoryTurns: number | null;
  bestVictoryYear: number | null;
  populationByYear: number[];
  finalPopulation: number;
  maxPopulation: number;
  minPopulation: number;
  brigadeDiedAt: number | null;
  // 定員リストラ統計
  totalRetirements: number; // enforceMaxBrigadeSize で除名された累計
  totalNaturalRetirements: number; // 衰退期最高齢除名（既存ロジック）
  totalAdvanceRetirements: number; // 引退年齢到達による retire
  // 血統
  totalMarriages: number;
  totalPlannedBirths: number;
  totalActualBirths: number;
  // 命名
  titledNamesCount: number;
  totalUnitsCreated: number;
  // 世代交代
  descendantsAtY100: number;
  descendantRatioY100: number;
  // ステータス
  avgStrengthY100: number;
  // 文化圏分布
  originDistY100: Record<string, number>;
}

function runOne(seed: number): RunResult {
  const rand = mulberry32(seed);
  const battleRng = mulberry32(seed + 1);
  const ri = (min: number, max: number) => Math.floor(rand() * (max - min + 1)) + min;
  const pick = <T>(arr: ReadonlyArray<T>): T => arr[Math.floor(rand() * arr.length)];

  const nameGen = new NameGenerator(rand);
  let _uid = 0;

  function makeRecruit(job: JobType, age: number, currentYear: number, historical: ReadonlySet<string>): Unit {
    const { peakStartAge, peakEndAge } = rollPeakAges(rand);
    const maxAge = peakEndAge + ri(15, 25);
    const id = `s${seed}-u${String(_uid++).padStart(3, "0")}`;
    const gender: Gender = rand() < 0.5 ? "Male" : "Female";
    const origin = pickRandomOrigin(rand);
    const name = nameGen.pick(origin, gender, historical);
    return new Unit({
      id, name, job, age,
      birthYear: currentYear - age,
      peakStartAge, peakEndAge, maxAge,
      baseStats: { strength: ri(70, 130), agility: 0, intelligence: 0, endurance: 0 },
      gender, origin,
    });
  }

  function buildBattleUnit(u: Unit): Unit {
    if (!u.job) return u;
    const d = JOB_DEFAULTS[u.job];
    const f = u.growthFactor;
    const scale = (v: number) => Math.max(1, Math.round(v * f));
    return new Unit({
      ...u,
      maxHp: d.maxHp, hp: d.maxHp,
      speed: scale(d.speed),
      frontAttack: scale(d.frontAttack),
      rearAttack: scale(d.rearAttack),
      bdf: d.bdf, sdf: d.sdf, ab: d.ab, hl: d.hl,
    });
  }

  function formBattalion(picks: ReadonlyArray<Unit>) {
    const frontMax = CFG.BATTLE.FRONT_ROW_COUNT;
    const squadMax = CFG.BATTLE.SQUAD_SIZE;
    const front: Unit[] = [];
    const rear: Unit[] = [];
    for (const u of picks) {
      if (front.length < frontMax && u.job && FRONT_JOBS.includes(u.job)) front.push(u);
      else rear.push(u);
    }
    while (front.length < frontMax && rear.length > 0) front.push(rear.shift()!);
    const rearL = rear.slice(0, squadMax);
    const rearR = rear.slice(squadMax, squadMax * 2);
    const squads = [
      new Squad("FRONT", front.map(buildBattleUnit)),
      new Squad("REAR-L", rearL.map(buildBattleUnit)),
      new Squad("REAR-R", rearR.map(buildBattleUnit)),
    ];
    const avgAge = picks.length > 0 ? picks.reduce((s, u) => s + u.age, 0) / picks.length : 0;
    const peakCount = picks.filter((u) => u.age >= u.peakStartAge && u.age <= u.peakEndAge).length;
    return { squads, avgAge, peakCount };
  }

  function makeTrialEnemy(): Squad[] {
    const enemyUnit = (i: number): Unit => new Unit({
      id: `enemy-${i}`, name: `試練の兵${i + 1}`, job: null,
      age: 25, peakStartAge: 25, peakEndAge: 35, maxAge: 60,
      baseStats: { strength: 60, agility: 0, intelligence: 0, endurance: 0 },
      maxHp: 150, hp: 150, speed: 20, frontAttack: 30, rearAttack: 30,
    });
    const units = Array.from({ length: 10 }, (_, i) => enemyUnit(i));
    return [
      new Squad("E1", units.slice(0, 3)),
      new Squad("E2", units.slice(3, 6)),
      new Squad("E3", units.slice(6, 9)),
      new Squad("E4", units.slice(9, 10)),
    ];
  }

  function determineMvp(stats: SimulationResult["statistics"]): JobType | "なし" {
    const entries = Object.entries(stats.totalDamageDealt) as [JobType, number][];
    if (entries.length === 0) return "なし";
    return entries.sort(([, a], [, b]) => b - a)[0][0];
  }

  function removeOldestDecliningUnit(brigade: Brigade): { brigade: Brigade; removed: Unit | null } {
    const decliners = brigade.units.filter((u) => u.age > u.peakEndAge && !u.isRetired);
    if (decliners.length === 0) return { brigade, removed: null };
    const oldest = decliners.reduce((a, b) => (a.age > b.age ? a : b));
    return {
      brigade: new Brigade(
        brigade.units.filter((u) => u.id !== oldest.id),
        [...brigade.squads], brigade.currentYear, brigade.pendingBirths, brigade.historicalNames
      ),
      removed: oldest,
    };
  }

  // ── 創設メンバー（INITIAL_MEMBER_COUNT 名） ──────────────────────────────
  const founding: Unit[] = [];
  {
    const cumulative = new Set<string>();
    for (let i = 0; i < CFG.SCHEDULE.INITIAL_MEMBER_COUNT; i++) {
      const u = makeRecruit(pick(JOB_LIST), 20, 1, cumulative);
      cumulative.add(u.name);
      founding.push(u);
    }
  }
  let brigade = new Brigade(founding);

  // ── データ収集器 ──────────────────────────────────────────────────────────
  const battles: BattleRecord[] = [];
  const populationByYear: number[] = [];
  let totalMarriages = 0;
  let totalPlannedBirths = 0;
  let totalActualBirths = 0;
  let totalRetirements = 0;
  let totalNaturalRetirements = 0;
  let totalAdvanceRetirements = 0;
  let brigadeDiedAt: number | null = null;

  for (let year = 1; year <= CFG.SCHEDULE.CHRONICLE_YEARS; year++) {
    let recruits: Unit[] = [];
    if (year % CFG.SCHEDULE.RECRUIT_INTERVAL === 0) {
      const local = new Set(brigade.historicalNames);
      for (let i = 0; i < CFG.SCHEDULE.RECRUIT_COUNT; i++) {
        const r = makeRecruit(pick(JOB_LIST), 18, brigade.currentYear, local);
        local.add(r.name);
        recruits.push(r);
      }
    }

    const { brigade: advanced, events } = brigade.advance(recruits, {
      nameGenerator: nameGen,
      marriageProb: CFG.LINEAGE.MARRIAGE_PROBABILITY,
      birthProb: CFG.LINEAGE.BIRTH_PROBABILITY,
      affinityThreshold: CFG.LINEAGE.MARRIAGE_THRESHOLD,
    });
    brigade = advanced;

    for (const e of events) {
      if (e.type === "marriage") totalMarriages++;
      else if (e.type === "birth_planned") totalPlannedBirths++;
      else if (e.type === "birth") totalActualBirths++;
      else if (e.type === "retire") totalAdvanceRetirements++;
    }

    const { brigade: pruned, removed } = removeOldestDecliningUnit(brigade);
    brigade = pruned;
    if (removed) totalNaturalRetirements++;

    // 定員管理: MAX_BRIGADE_SIZE 超過時に弱者を除名
    const limit = enforceMaxBrigadeSize(brigade, CFG.LIMITS.MAX_BRIGADE_SIZE);
    brigade = limit.brigade;
    totalRetirements += limit.retired.length;

    populationByYear.push(brigade.units.length);
    if (brigade.units.length === 0 && brigadeDiedAt === null) brigadeDiedAt = year;

    if (year % CFG.SCHEDULE.BATTLE_INTERVAL === 0 && brigade.units.length > 0) {
      const picks = brigade.selectBattalion(CFG.SCHEDULE.BATTALION_SIZE);
      const { squads, avgAge, peakCount } = formBattalion(picks);
      const enemy = makeTrialEnemy();
      const sim = new BattleSimulator(squads, enemy, {
        maxTurns: CFG.BATTLE.MAX_TURNS,
        rng: battleRng, verbose: false,
      });
      const result = sim.run();
      // 戦闘後の好感度蓄積（バグ修正後の意図通りの挙動）
      brigade = brigade.applyBattleAffinity(
        result.squadmatePairs, CFG.LINEAGE.AFFINITY_PER_BATTLE
      );
      const winLossDraw: "Win" | "Loss" | "Draw" =
        result.winner === "Allies" ? "Win" : result.winner === "Enemies" ? "Loss" : "Draw";
      battles.push({
        year, result: winLossDraw, turns: result.turns,
        avgAge, peakCount, battalionSize: picks.length,
        mvpJob: determineMvp(result.statistics),
      });
    }
  }

  // ── 集計 ──────────────────────────────────────────────────────────────────
  const wins = battles.filter((b) => b.result === "Win").length;
  const losses = battles.filter((b) => b.result === "Loss").length;
  const draws = battles.filter((b) => b.result === "Draw").length;

  const winningBattles = battles.filter((b) => b.result === "Win");
  let bestVictoryTurns: number | null = null;
  let bestVictoryYear: number | null = null;
  if (winningBattles.length > 0) {
    const best = winningBattles.reduce((a, b) => (a.turns <= b.turns ? a : b));
    bestVictoryTurns = best.turns;
    bestVictoryYear = best.year;
  }

  const finalUnits = brigade.units;
  const finalPopulation = finalUnits.length;
  const maxPopulation = Math.max(...populationByYear);
  const minPopulation = Math.min(...populationByYear);

  let titledNamesCount = 0;
  for (const name of brigade.historicalNames) {
    for (const t of TITLES) {
      if (name.startsWith(t)) { titledNamesCount++; break; }
    }
  }

  const descendantsAtY100 = finalUnits.filter((u) => u.parents !== null).length;
  const descendantRatioY100 = finalPopulation > 0 ? descendantsAtY100 / finalPopulation : 0;
  const avgStrengthY100 = finalPopulation > 0
    ? finalUnits.reduce((s, u) => s + u.stats.strength, 0) / finalPopulation : 0;

  const originDistY100: Record<string, number> = { Japanese: 0, European: 0, Classical: 0 };
  for (const u of finalUnits) originDistY100[u.origin]++;

  return {
    seed,
    battles, wins, losses, draws,
    bestVictoryTurns, bestVictoryYear,
    populationByYear, finalPopulation, maxPopulation, minPopulation,
    brigadeDiedAt,
    totalRetirements, totalNaturalRetirements, totalAdvanceRetirements,
    totalMarriages, totalPlannedBirths, totalActualBirths,
    titledNamesCount,
    totalUnitsCreated: brigade.historicalNames.size,
    descendantsAtY100, descendantRatioY100,
    avgStrengthY100,
    originDistY100,
  };
}

// ─── 実行 ────────────────────────────────────────────────────────────────────

const SEEDS = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10];
console.log("─── 10シード ギルドモード メタ分析開始 ───");
console.log(`Config: CHRONICLE_CONFIG_EXTREME`);
console.log(`  DECAY_RATE=${CFG.TIME.DECAY_RATE}, INITIAL=${CFG.SCHEDULE.INITIAL_MEMBER_COUNT}名, MAX=${CFG.LIMITS.MAX_BRIGADE_SIZE}名`);
console.log(`  BATTLE_INTERVAL=${CFG.SCHEDULE.BATTLE_INTERVAL}年, BATTALION=${CFG.SCHEDULE.BATTALION_SIZE}名`);
console.log(`  AFFINITY/B=${CFG.LINEAGE.AFFINITY_PER_BATTLE}, THRESH=${CFG.LINEAGE.MARRIAGE_THRESHOLD}, M%=${CFG.LINEAGE.MARRIAGE_PROBABILITY}, B%=${CFG.LINEAGE.BIRTH_PROBABILITY}`);
console.log("");
const startTime = Date.now();

const results: RunResult[] = [];
for (const seed of SEEDS) {
  process.stdout.write(`  seed=${seed} ... `);
  const t0 = Date.now();
  const r = runOne(seed);
  results.push(r);
  console.log(
    `${Date.now() - t0}ms | ${r.wins}勝${r.losses}敗${r.draws}分 / ` +
    `最終${r.finalPopulation}名 (peak ${r.maxPopulation}) / ` +
    `結婚${r.totalMarriages} 入団${r.totalActualBirths} 称号${r.titledNamesCount} ` +
    `子孫率${(r.descendantRatioY100 * 100).toFixed(0)}%`
  );
}
console.log(`\n全体: ${Date.now() - startTime}ms\n`);

// ─── JSON 保存 ───────────────────────────────────────────────────────────────

function avg(arr: number[]): number {
  return arr.length > 0 ? arr.reduce((s, v) => s + v, 0) / arr.length : 0;
}
function median(arr: number[]): number {
  if (arr.length === 0) return 0;
  const sorted = [...arr].sort((a, b) => a - b);
  const mid = Math.floor(sorted.length / 2);
  return sorted.length % 2 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2;
}

const winRates = results.map((r) => r.wins / (r.wins + r.losses + r.draws));
const finalPops = results.map((r) => r.finalPopulation);
const maxPops = results.map((r) => r.maxPopulation);
const minPops = results.map((r) => r.minPopulation);
const marriages = results.map((r) => r.totalMarriages);
const plannedBirths = results.map((r) => r.totalPlannedBirths);
const actualBirths = results.map((r) => r.totalActualBirths);
const titled = results.map((r) => r.titledNamesCount);
const totalUnits = results.map((r) => r.totalUnitsCreated);
const descendantRatios = results.map((r) => r.descendantRatioY100);
const descendants = results.map((r) => r.descendantsAtY100);
const avgStrengths = results.map((r) => r.avgStrengthY100);
const retirements = results.map((r) => r.totalRetirements);

const dataPath = join(import.meta.dir, "..", "reports", "_meta-analysis-guild-data.json");
writeFileSync(dataPath, JSON.stringify({
  config: "extreme",
  seeds: SEEDS,
  summary: {
    avgWinRate: avg(winRates),
    avgFinalPop: avg(finalPops),
    avgMaxPop: avg(maxPops),
    avgMinPop: avg(minPops),
    avgMarriages: avg(marriages),
    avgPlannedBirths: avg(plannedBirths),
    avgActualBirths: avg(actualBirths),
    avgTitled: avg(titled),
    avgTotalUnits: avg(totalUnits),
    avgDescendants: avg(descendants),
    avgDescendantRatio: avg(descendantRatios),
    avgStrengthY100: avg(avgStrengths),
    avgRetirements: avg(retirements),
  },
  results: results.map(r => ({
    seed: r.seed,
    wins: r.wins, losses: r.losses, draws: r.draws,
    bestVictoryYear: r.bestVictoryYear,
    bestVictoryTurns: r.bestVictoryTurns,
    finalPopulation: r.finalPopulation,
    maxPopulation: r.maxPopulation,
    minPopulation: r.minPopulation,
    totalMarriages: r.totalMarriages,
    totalPlannedBirths: r.totalPlannedBirths,
    totalActualBirths: r.totalActualBirths,
    titledNamesCount: r.titledNamesCount,
    totalUnitsCreated: r.totalUnitsCreated,
    descendantsAtY100: r.descendantsAtY100,
    descendantRatioY100: r.descendantRatioY100,
    avgStrengthY100: r.avgStrengthY100,
    originDistY100: r.originDistY100,
    totalRetirements: r.totalRetirements,
    totalNaturalRetirements: r.totalNaturalRetirements,
    totalAdvanceRetirements: r.totalAdvanceRetirements,
    // 簡易: 5年ごとのバトル結果（100戦 → 20件にダウンサンプル）
    battlesSampled: r.battles.filter((b, i) => i % 5 === 0).map(b => ({
      year: b.year, result: b.result, turns: b.turns, avgAge: b.avgAge,
      peakCount: b.peakCount, mvpJob: b.mvpJob,
    })),
  })),
}, null, 2));
console.log(`✓ 生データ JSON: ${dataPath}`);

console.log("\n=== 集計 ===");
console.log(`  avgWinRate         : ${(avg(winRates) * 100).toFixed(1)}%`);
console.log(`  avgFinalPop        : ${avg(finalPops).toFixed(1)} (median ${median(finalPops)})`);
console.log(`  avgMaxPop          : ${avg(maxPops).toFixed(1)} (median ${median(maxPops)})`);
console.log(`  avgMarriages       : ${avg(marriages).toFixed(1)}`);
console.log(`  avgPlannedBirths   : ${avg(plannedBirths).toFixed(1)}`);
console.log(`  avgActualBirths    : ${avg(actualBirths).toFixed(1)}`);
console.log(`  avgTitled          : ${avg(titled).toFixed(1)}`);
console.log(`  avgDescendantRatio : ${(avg(descendantRatios) * 100).toFixed(1)}%`);
console.log(`  avgRetirements (定員): ${avg(retirements).toFixed(1)}`);
