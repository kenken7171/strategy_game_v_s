#!/usr/bin/env bun
/**
 * meta-analyze-grand-chronicle.ts
 *
 * 10シードで run-grand-chronicle と同等の100年シミュレーションを実行し、
 * 結果をメタ分析して reports/grand-chronicle-meta-analysis.md を出力する。
 *
 * 収集データ:
 *   - 各年の勝/敗/ドロー、最短ターン勝利、バトル人口比率
 *   - 100年間の旅団人数（最大/最小/最終）、旅団死亡（人数0）の有無
 *   - 結婚数、出産予約数、15歳入団した子孫の数
 *   - 称号フォールバック発生数
 *   - 100年目の旅団における子孫（parents != null）の割合
 */
import { Unit } from "../packages/core/src/models/Unit";
import { Brigade } from "../packages/core/src/models/Brigade";
import { Squad } from "../packages/core/src/models/Squad";
import { BattleSimulator } from "../packages/core/src/BattleSimulator";
import type { SimulationResult } from "../packages/core/src/BattleSimulator";
import type { JobType, Gender } from "../packages/core/src/models/Unit";
import { NameGenerator, pickRandomOrigin, TITLES } from "../packages/core/src/data/names";
import { CHRONICLE_CONFIG } from "../packages/core/src/config/ChronicleConfig";
import { rollPeakAges } from "../packages/core/src/utils/age";
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

// ─── ジョブ定義（run-grand-chronicle と同じ） ─────────────────────────────────

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
const FOUNDING_JOBS: ReadonlyArray<JobType> = [
  "iron_wall_knight", "tactician", "medic", "sniper", "iron_wall_knight",
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
  totalAllyDamage: number;
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
  // 旅団暦
  totalMarriages: number;
  totalPlannedBirths: number;
  totalActualBirths: number; // 15歳入団した子供
  // 命名
  titledNamesCount: number;
  totalUnitsCreated: number;
  // 世代交代
  descendantsAtY100: number;
  descendantRatioY100: number;
  // 平均能力指標（インフレ確認）
  avgStrengthY100: number;
  // 文化圏分布（100年目）
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
    const front: Unit[] = [];
    const rear: Unit[] = [];
    for (const u of picks) {
      if (front.length < CHRONICLE_CONFIG.BATTLE.FRONT_ROW_COUNT && u.job && FRONT_JOBS.includes(u.job)) {
        front.push(u);
      } else {
        rear.push(u);
      }
    }
    while (front.length < CHRONICLE_CONFIG.BATTLE.FRONT_ROW_COUNT && rear.length > 0) front.push(rear.shift()!);
    const rearL = rear.slice(0, CHRONICLE_CONFIG.BATTLE.SQUAD_SIZE);
    const rearR = rear.slice(CHRONICLE_CONFIG.BATTLE.SQUAD_SIZE, CHRONICLE_CONFIG.BATTLE.SQUAD_SIZE * 2);
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

  function removeOldestDecliningUnit(brigade: Brigade): Brigade {
    const decliners = brigade.units.filter((u) => u.age > u.peakEndAge && !u.isRetired);
    if (decliners.length === 0) return brigade;
    const oldest = decliners.reduce((a, b) => (a.age > b.age ? a : b));
    return new Brigade(
      brigade.units.filter((u) => u.id !== oldest.id),
      [...brigade.squads], brigade.currentYear, brigade.pendingBirths, brigade.historicalNames
    );
  }

  // ── 創設メンバー ───────────────────────────────────────────────────────────
  const founding: Unit[] = [];
  {
    const cumulative = new Set<string>();
    for (const j of FOUNDING_JOBS) {
      const u = makeRecruit(j, 20, 1, cumulative);
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
  let brigadeDiedAt: number | null = null;

  const TOTAL_YEARS = CHRONICLE_CONFIG.SCHEDULE.CHRONICLE_YEARS;
  const RECRUIT_INTERVAL = CHRONICLE_CONFIG.SCHEDULE.RECRUIT_INTERVAL;
  const RECRUIT_COUNT = CHRONICLE_CONFIG.SCHEDULE.RECRUIT_COUNT;
  const BATTLE_INTERVAL = CHRONICLE_CONFIG.SCHEDULE.BATTLE_INTERVAL;

  for (let year = 1; year <= TOTAL_YEARS; year++) {
    let recruits: Unit[] = [];
    if (year % RECRUIT_INTERVAL === 0) {
      const local = new Set(brigade.historicalNames);
      for (let i = 0; i < RECRUIT_COUNT; i++) {
        const r = makeRecruit(pick(JOB_LIST), 18, brigade.currentYear, local);
        local.add(r.name);
        recruits.push(r);
      }
    }

    const { brigade: advanced, events } = brigade.advance(recruits, { nameGenerator: nameGen });
    brigade = advanced;

    // イベント集計
    for (const e of events) {
      if (e.type === "marriage") totalMarriages++;
      else if (e.type === "birth_planned") totalPlannedBirths++;
      else if (e.type === "birth") totalActualBirths++;
    }

    brigade = removeOldestDecliningUnit(brigade);

    populationByYear.push(brigade.units.length);
    if (brigade.units.length === 0 && brigadeDiedAt === null) brigadeDiedAt = year;

    if (year % BATTLE_INTERVAL === 0 && brigade.units.length > 0) {
      const picks = brigade.selectBattalion(CHRONICLE_CONFIG.SCHEDULE.BATTALION_SIZE);
      const { squads, avgAge, peakCount } = formBattalion(picks);
      const enemy = makeTrialEnemy();
      const sim = new BattleSimulator(squads, enemy, {
        maxTurns: CHRONICLE_CONFIG.BATTLE.MAX_TURNS,
        rng: battleRng, verbose: false,
      });
      const result = sim.run();
      // ★ 重要: バトル後に同分隊好感度を加算する（run-grand-chronicle 本体はこれを忘れている）
      // これを呼ばないと結婚→出産→継承の血統サイクルが100年で全く回らない
      brigade = brigade.applyBattleAffinity(
        result.squadmatePairs,
        CHRONICLE_CONFIG.LINEAGE.AFFINITY_PER_BATTLE
      );
      const totalDmg = Object.values(result.statistics.totalDamageDealt).reduce((s, v) => s + v, 0);
      const winLossDraw: "Win" | "Loss" | "Draw" =
        result.winner === "Allies" ? "Win" : result.winner === "Enemies" ? "Loss" : "Draw";
      battles.push({
        year, result: winLossDraw, turns: result.turns,
        avgAge, peakCount, battalionSize: picks.length,
        mvpJob: determineMvp(result.statistics),
        totalAllyDamage: totalDmg,
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

  // 称号付きユニット数: historicalNames を走査して TITLES のいずれかの prefix で始まる名前
  let titledNamesCount = 0;
  for (const name of brigade.historicalNames) {
    for (const t of TITLES) {
      if (name.startsWith(t)) { titledNamesCount++; break; }
    }
  }
  const totalUnitsCreated = brigade.historicalNames.size;

  // 100年目の子孫数（parents != null = 旅団内で生まれた継承者）
  const descendantsAtY100 = finalUnits.filter((u) => u.parents !== null).length;
  const descendantRatioY100 = finalPopulation > 0 ? descendantsAtY100 / finalPopulation : 0;

  // 平均ステータス
  const avgStrengthY100 = finalPopulation > 0
    ? finalUnits.reduce((s, u) => s + u.stats.strength, 0) / finalPopulation : 0;

  // 文化圏分布
  const originDistY100: Record<string, number> = { Japanese: 0, European: 0, Classical: 0 };
  for (const u of finalUnits) originDistY100[u.origin]++;

  return {
    seed,
    battles, wins, losses, draws,
    bestVictoryTurns, bestVictoryYear,
    populationByYear, finalPopulation, maxPopulation, minPopulation,
    brigadeDiedAt,
    totalMarriages, totalPlannedBirths, totalActualBirths,
    titledNamesCount, totalUnitsCreated,
    descendantsAtY100, descendantRatioY100,
    avgStrengthY100,
    originDistY100,
  };
}

// ─── 集計ヘルパー ────────────────────────────────────────────────────────────

function avg(arr: number[]): number {
  return arr.length > 0 ? arr.reduce((s, v) => s + v, 0) / arr.length : 0;
}
function median(arr: number[]): number {
  if (arr.length === 0) return 0;
  const sorted = [...arr].sort((a, b) => a - b);
  const mid = Math.floor(sorted.length / 2);
  return sorted.length % 2 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2;
}

// ─── 実行 ────────────────────────────────────────────────────────────────────

const SEEDS = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10];
console.log("─── 10シード メタ分析開始 ───");
const startTime = Date.now();

const results: RunResult[] = [];
for (const seed of SEEDS) {
  process.stdout.write(`  seed=${seed} ... `);
  const t0 = Date.now();
  const r = runOne(seed);
  results.push(r);
  console.log(
    `${Date.now() - t0}ms | ${r.wins}勝${r.losses}敗${r.draws}分 / ` +
    `最終${r.finalPopulation}名 / 結婚${r.totalMarriages} 入団${r.totalActualBirths} 称号${r.titledNamesCount}`
  );
}
console.log(`\n全体: ${Date.now() - startTime}ms\n`);

// ─── 集計 ────────────────────────────────────────────────────────────────────

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
const avgStrengths = results.map((r) => r.avgStrengthY100);
const drawCount = results.reduce((s, r) => s + r.draws, 0);
const brigadeDeaths = results.filter((r) => r.brigadeDiedAt !== null).length;
const titledRates = results.map((r) => r.totalUnitsCreated > 0 ? r.titledNamesCount / r.totalUnitsCreated : 0);

// ─── レポート生成 ────────────────────────────────────────────────────────────

function fmt(n: number, digits = 1): string { return n.toFixed(digits); }
function pct(n: number): string { return (n * 100).toFixed(1) + "%"; }

// ─── JSON 生データを別ファイルに保存（手書きレポートの参考用） ────────────

const dataPath = join(import.meta.dir, "..", "reports", "_meta-analysis-data.json");
writeFileSync(dataPath, JSON.stringify({
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
    avgDescendantRatio: avg(descendantRatios),
    avgStrengthY100: avg(avgStrengths),
    totalDraws: drawCount,
    brigadeDeaths,
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
    battles: r.battles,
  })),
}, null, 2));
console.log(`✓ 生データ JSON: ${dataPath}`);

// ─── 自動生成 Markdown は廃止（深掘り考察は手書きでメンテする方針）──────
// 生データ JSON のみを書き出し、reports/grand-chronicle-meta-analysis.md は
// 手書きで生データを参照しながら執筆する。詳細は同レポート参照。
process.exit(0);

// 以下は将来テンプレート再生成が必要になったとき用に残置（実行されない）
const md = `# Chronicle Knights — 100年シミュレーション統合考察レポート

> 生成日時: ${new Date().toISOString().replace("T", " ").slice(0, 19)}
> 実行コマンド: \`bun scripts/meta-analyze-grand-chronicle.ts\`
> シード: ${SEEDS.join(", ")}（計${SEEDS.length}ラン）

---

## 1. 実行サマリー（10ラン分）

| Seed | 勝 | 敗 | 分 | 勝率 | 最強期 | 最終人数 | 最大人数 | 結婚 | 出生予約 | 入団 | 称号 | 子孫率(Y100) |
|---:|---:|---:|---:|---:|:---:|---:|---:|---:|---:|---:|---:|---:|
${results.map((r) => `| ${r.seed} | ${r.wins} | ${r.losses} | ${r.draws} | ${pct(r.wins / (r.wins + r.losses + r.draws))} | ${r.bestVictoryYear !== null ? `Y${r.bestVictoryYear}/${r.bestVictoryTurns}T` : "—"} | ${r.finalPopulation} | ${r.maxPopulation} | ${r.totalMarriages} | ${r.totalPlannedBirths} | ${r.totalActualBirths} | ${r.titledNamesCount} | ${pct(r.descendantRatioY100)} |`).join("\n")}

## 2. 集計指標

| 指標 | 平均 | 最小 | 最大 | 中央値 |
|---|---:|---:|---:|---:|
| 勝率 | ${pct(avg(winRates))} | ${pct(Math.min(...winRates))} | ${pct(Math.max(...winRates))} | ${pct(median(winRates))} |
| 100年目人数 | ${fmt(avg(finalPops))} | ${Math.min(...finalPops)} | ${Math.max(...finalPops)} | ${median(finalPops)} |
| 最大人数 (peak) | ${fmt(avg(maxPops))} | ${Math.min(...maxPops)} | ${Math.max(...maxPops)} | ${median(maxPops)} |
| 最小人数 (trough) | ${fmt(avg(minPops))} | ${Math.min(...minPops)} | ${Math.max(...minPops)} | ${median(minPops)} |
| 総結婚数 | ${fmt(avg(marriages))} | ${Math.min(...marriages)} | ${Math.max(...marriages)} | ${median(marriages)} |
| 出生予約数 | ${fmt(avg(plannedBirths))} | ${Math.min(...plannedBirths)} | ${Math.max(...plannedBirths)} | ${median(plannedBirths)} |
| 実入団者数 | ${fmt(avg(actualBirths))} | ${Math.min(...actualBirths)} | ${Math.max(...actualBirths)} | ${median(actualBirths)} |
| 累計生成ユニット | ${fmt(avg(totalUnits))} | ${Math.min(...totalUnits)} | ${Math.max(...totalUnits)} | ${median(totalUnits)} |
| 称号付きユニット | ${fmt(avg(titled))} | ${Math.min(...titled)} | ${Math.max(...titled)} | ${median(titled)} |
| 称号率（累計ユニット中） | ${pct(avg(titledRates))} | ${pct(Math.min(...titledRates))} | ${pct(Math.max(...titledRates))} | ${pct(median(titledRates))} |
| Y100 子孫率 | ${pct(avg(descendantRatios))} | ${pct(Math.min(...descendantRatios))} | ${pct(Math.max(...descendantRatios))} | ${pct(median(descendantRatios))} |
| Y100 平均 strength | ${fmt(avg(avgStrengths))} | ${fmt(Math.min(...avgStrengths))} | ${fmt(Math.max(...avgStrengths))} | ${fmt(median(avgStrengths))} |

**特記事項:**
- 旅団死亡（人数0）発生ラン: **${brigadeDeaths}/${SEEDS.length}**
- 全ラン累計ドロー: **${drawCount}**
- 全ランで最短勝利: **${Math.min(...results.flatMap((r) => r.battles.filter(b => b.result === "Win").map(b => b.turns)))}ターン**
- 全ランで最長勝利: **${Math.max(...results.flatMap((r) => r.battles.filter(b => b.result === "Win").map(b => b.turns)))}ターン**

---

## 3. 評価軸別考察

### ① 旅団のサバイバル性

**観察:**
- 平均最終人数: **${fmt(avg(finalPops))}名**（最小${Math.min(...finalPops)} / 最大${Math.max(...finalPops)}）
- 平均最大人数: **${fmt(avg(maxPops))}名** に達するラン平均
- 平均最小人数: **${fmt(avg(minPops))}名**（最小${Math.min(...minPops)}）
- 旅団死亡: **${brigadeDeaths}/${SEEDS.length}ラン**

**考察:**
${(() => {
  const m = avg(finalPops);
  const max = avg(maxPops);
  if (brigadeDeaths > 0) {
    return `旅団死亡が${brigadeDeaths}ラン発生しており、純粋なサバイバルゲームとしては危機感が出ている。ただし他の${SEEDS.length - brigadeDeaths}ランは存続している点で、運（シード）依存の振れ幅が大きい。`;
  }
  if (m > 30) {
    return `100年目時点の生存人数が平均${fmt(m)}名と多く、新人投入間隔（${CHRONICLE_CONFIG.SCHEDULE.RECRUIT_INTERVAL}年ごと${CHRONICLE_CONFIG.SCHEDULE.RECRUIT_COUNT}名）と現在の引退/除名圧力に対して**人口爆発気味**。最大人数平均${fmt(max)}名は大隊定員9名の${fmt(max / 9)}倍に達する。`;
  } else if (m < 8) {
    return `100年目時点の生存人数が平均${fmt(m)}名と少なく、大隊編成（9名）を割っているラン（${results.filter(r => r.finalPopulation < 9).length}/${SEEDS.length}）が存在する。少子高齢化の兆候。`;
  } else {
    return `100年目時点の生存人数が平均${fmt(m)}名で、大隊定員9名に対して${fmt(m / 9)}倍と適切な余裕がある。サバイバル性として健全。`;
  }
})()}

### ② 血統システムの機能度

**観察:**
- 平均総結婚数: **${fmt(avg(marriages))}件**
- 平均出生予約数: **${fmt(avg(plannedBirths))}件**
- 平均実入団者数（15歳到達）: **${fmt(avg(actualBirths))}名**
- 平均Y100子孫率: **${pct(avg(descendantRatios))}**

**考察:**
${(() => {
  const dr = avg(descendantRatios);
  const am = avg(marriages);
  const ab = avg(actualBirths);
  if (dr < 0.1) {
    return `Y100子孫率が平均${pct(dr)}と非常に低く、血統システムが**ほぼ機能していない**状態。結婚イベントは平均${fmt(am)}件発生しているが、出産予約から15歳入団までの15年間で親が引退・除名され、世代継承が成立しないケースが目立つ。`;
  } else if (dr < 0.3) {
    return `Y100子孫率が平均${pct(dr)}と、血統システムは一定機能しているが**新人に埋もれがち**。結婚${fmt(am)}件 → 実入団${fmt(ab)}名のうちY100まで生存しているのはわずか${fmt(avg(results.map(r => r.descendantsAtY100)))}名平均。15年の入団タイムラグと8ジョブからの新人ランダム加入が血統を希薄化している。`;
  } else if (dr > 0.6) {
    return `Y100子孫率が平均${pct(dr)}と高く、**子孫が主力を張れている**。血統サイクルが健全に回転している。`;
  } else {
    return `Y100子孫率が平均${pct(dr)}と、新人と子孫が拮抗するバランス。血統システムは機能しているが支配的ではない。`;
  }
})()}

### ③ ステータスのインフレ/デフレ

**観察:**
- 平均勝率: **${pct(avg(winRates))}**（最小${pct(Math.min(...winRates))} / 最大${pct(Math.max(...winRates))}）
- 平均Y100 strength: **${fmt(avg(avgStrengths))}**（最小${fmt(Math.min(...avgStrengths))} / 最大${fmt(Math.max(...avgStrengths))}）
- 全ラン累計ドロー数: **${drawCount}**

**考察:**
${(() => {
  const wr = avg(winRates);
  const ws = winRates.filter(w => w === 1.0).length;
  if (wr > 0.95) {
    return `平均勝率${pct(wr)}は**100%張り付き気味**。${ws}/${SEEDS.length}ランで全勝。試練の門（敵10体・攻撃力30）は若い旅団でも余裕で勝てるレベルに弱体化している。`;
  } else if (wr > 0.85 && wr <= 0.95) {
    return `平均勝率${pct(wr)}と高めだが、${results.filter(r => r.losses > 0).length}/${SEEDS.length}ランで敗北を経験。試練の門が「100年中の壁」として機能している瞬間がある。`;
  } else if (wr < 0.5) {
    return `平均勝率${pct(wr)}と低く、後半ジリ貧の傾向。衰退期ユニットの累積か、子孫が主力に育つ前に旅団が崩壊している可能性。`;
  } else {
    return `平均勝率${pct(wr)}とバランスのとれた範囲。試練の門が「越えるべき壁」として機能している。`;
  }
})()}

### ④ 命名プールの寿命

**観察:**
- 平均累計生成ユニット数: **${fmt(avg(totalUnits))}名**（プール総数 910名）
- 平均称号付きユニット数: **${fmt(avg(titled))}名**
- 平均称号率: **${pct(avg(titledRates))}**
- プール使用率（累計÷910）: **${pct(avg(totalUnits) / 910)}**

**考察:**
${(() => {
  const tr = avg(titledRates);
  const usage = avg(totalUnits) / 910;
  if (usage < 0.3) {
    return `100年で平均${fmt(avg(totalUnits))}名 = プール使用率${pct(usage)}と、910名プールは十分余裕。称号フォールバック発生率${pct(tr)}は適切に低い。ただし6プール（Origin×Gender）に分散するため、特定プール（例: Classical/Male 150）への偏りで局所的に枯渇する可能性は残る。`;
  } else if (usage < 0.5) {
    return `プール使用率${pct(usage)}で半分手前。称号率${pct(tr)}は適度に出現し、世代交代の証として機能している。`;
  } else {
    return `プール使用率${pct(usage)}と高く、称号率${pct(tr)}が無視できない水準。「暁の」「古の」を冠した二つ名ユニットが旅団内に常駐するようになり、伝説の世界観が強まる。プール拡張も検討余地。`;
  }
})()}

文化圏分布（10ラン合計のY100時点）:
${(() => {
  const total = { Japanese: 0, European: 0, Classical: 0 };
  for (const r of results) {
    total.Japanese += r.originDistY100.Japanese;
    total.European += r.originDistY100.European;
    total.Classical += r.originDistY100.Classical;
  }
  const sum = total.Japanese + total.European + total.Classical;
  if (sum === 0) return "- データなし";
  return `- Japanese  : ${total.Japanese} (${pct(total.Japanese / sum)})\n- European  : ${total.European} (${pct(total.European / sum)})\n- Classical : ${total.Classical} (${pct(total.Classical / sum)})`;
})()}

---

## 4. CHRONICLE_CONFIG 変更提案

10ラン分析の結果と4評価軸の所見をもとに、より「面白く・シビアに」する具体的調整を提案する。

${(() => {
  const wr = avg(winRates);
  const dr = avg(descendantRatios);
  const m = avg(finalPops);
  const tr = avg(titledRates);
  const proposals: string[] = [];

  // 勝率調整
  if (wr > 0.9) {
    proposals.push(`### 4-1. 試練の門の強化（勝率インフレ抑制）
**現状**: 平均勝率 ${pct(wr)} と高すぎ、緊張感が薄い。
**提案**: 試練の敵を強化する（敵パラメータ自体は CHRONICLE_CONFIG にないが、scripts 内 \`makeTrialEnemy\` で攻撃力30→40、HP150→200 程度に上げる、もしくは年経過で段階的にスケール）。
**意図**: 「試練の門」が真に旅団の歴史的試練として機能するよう、若い旅団では苦戦し、円熟期に勝ち、衰退期で再び苦戦する波を作る。`);
  }

  // 結婚・出産確率調整
  if (dr < 0.2) {
    proposals.push(`### 4-2. 血統サイクルの加速
**現状**: Y100子孫率 ${pct(dr)} と低く、新人で旅団が回ってしまっている。
**提案**:
- \`LINEAGE.MARRIAGE_PROBABILITY\`: 0.3 → **0.5**（条件達成ペアの半数を毎年結婚させる）
- \`LINEAGE.BIRTH_PROBABILITY\`: 0.2 → **0.35**（結婚カップル毎年35%）
- \`LINEAGE.AFFINITY_PER_BATTLE\`: 10 → **15**（結婚条件の閾値到達を早める）
**意図**: 子孫が主力になる「家系の時代」を100年内に必ず観測できるようにする。`);
  } else if (dr > 0.5) {
    proposals.push(`### 4-2. 血統サイクルの抑制
**現状**: Y100子孫率 ${pct(dr)} と高く、新人加入が血統に飲まれている。
**提案**:
- \`LINEAGE.BIRTH_PROBABILITY\`: 0.2 → **0.12**
**意図**: 8ジョブの多様性を保つため、子孫支配を抑制。`);
  }

  // 人口調整
  if (m > 25) {
    proposals.push(`### 4-3. 人口爆発の抑制
**現状**: Y100平均人数 ${fmt(m)} 名は大隊定員9名の${fmt(m / 9)}倍で過剰。
**提案**:
- \`SCHEDULE.RECRUIT_INTERVAL\`: 2 → **3**（3年に1回の新人加入）
- \`TIME.DECAY_RATE\`: 0.03 → **0.05**（5%/年減衰で衰退を早める）
**意図**: 引退圧力を強め、定員に対する人数バランスを引き締める。シビアな入れ替わりを作る。`);
  } else if (m < 10) {
    proposals.push(`### 4-3. 少子高齢化の解消
**現状**: Y100平均人数 ${fmt(m)} 名は大隊定員9名を割っており旅団存続が危うい。
**提案**:
- \`SCHEDULE.RECRUIT_COUNT\`: 2 → **3**（1回あたり3名）
- \`LINEAGE.BIRTH_PROBABILITY\`: 0.2 → **0.30**
**意図**: 人口安定化。100年完走を保証する。`);
  }

  // 衰退率
  proposals.push(`### 4-4. 衰退期のシビア化（推奨）
**現状**: \`TIME.DECAY_RATE = 0.03\` は穏やかすぎ、衰退期ユニットが10年以上現役を続けてしまう。
**提案**: \`TIME.DECAY_RATE\`: 0.03 → **0.05**（5%/年複利）
**効果**: 衰退期5年でgrowthFactor ≈ 0.77、10年で ≈ 0.60。世代交代の必然性が強まり、「全盛期に出産・継承」の戦略性が出る。`);

  // 結婚閾値
  if (avg(marriages) < 8) {
    proposals.push(`### 4-5. 結婚閾値の緩和
**現状**: 平均結婚 ${fmt(avg(marriages))} 件は100年で稀。
**提案**: \`LINEAGE.MARRIAGE_THRESHOLD\`: 100 → **80**（同分隊8戦で達成）
**意図**: 100年でより多くのカップルが結ばれ、家系が複数立ち上がるドラマ性。`);
  }

  // 称号
  if (tr > 0.05) {
    proposals.push(`### 4-6. 命名プール拡張（任意）
**現状**: 称号率 ${pct(tr)}。プールは現状で足りているが、長期シミュレーションでは将来枯渇しうる。
**提案**: 各プール 150 → **200名**に拡張（特に Classical/Male）。`);
  }

  return proposals.join("\n\n");
})()}

---

## 5. 総括

10シード分析から得られた主要な知見:

${(() => {
  const wr = avg(winRates);
  const dr = avg(descendantRatios);
  const m = avg(finalPops);
  return `1. **勝率 ${pct(wr)}**: ${wr > 0.9 ? "試練の門が易化しすぎ、敵の段階強化が望まれる" : wr < 0.5 ? "厳しすぎる、旅団のジリ貧が顕著" : "概ねバランスは取れている"}
2. **Y100子孫率 ${pct(dr)}**: ${dr < 0.2 ? "血統システムが新人投入に埋もれている、結婚/出産確率の引き上げが必要" : dr > 0.5 ? "子孫が支配的、新ジョブ多様性の維持が必要" : "新人と子孫の混在は健全"}
3. **人口 平均${fmt(m)}名**: ${m > 25 ? "人口爆発、引退圧力強化が必要" : m < 10 ? "少子高齢化、入団数の増加が必要" : "概ね健全"}

**最重要の調整候補**:
- 衰退率 0.03 → 0.05（世代交代の必然性）
- 結婚確率 0.3 → 0.5、出産確率 0.2 → 0.35（血統機能の活性化）
- 試練の門の段階強化または年経過スケーリング（戦闘の波）

これらにより、100年クロニクルが「平和に新人を回して終わる100年」ではなく、「結婚と継承の波、衰退による緊張、試練の門による戦慄」を持つ歴史叙事詩になることを期待する。`;
})()}
`;

const outPath = join(import.meta.dir, "..", "reports", "grand-chronicle-meta-analysis.md");
writeFileSync(outPath, md);
console.log(`✓ レポート出力: ${outPath}`);
console.log(`  サイズ: ${md.length} 文字`);
