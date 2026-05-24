#!/usr/bin/env bun
/**
 * verify-bloodline.ts — 血統継承システムの End-to-End 検証
 *
 * シナリオ:
 *   1. 男女2名の若手騎士を生成し、同じ FRONT 分隊に配置
 *   2. 10戦実施。各戦闘後に Brigade.applyBattleAffinity で同分隊好感度 +10
 *      → 累計 +100 で結婚条件を満たす
 *   3. advance() を 1 年実行 → 結婚成立（marriageProb = 1.0）
 *   4. advance() を 1 年実行 → 出産予約（birthProb = 1.0）
 *   5. advance() を 15 年回し、出産年+15 で子供が Unit 化されることを確認
 *   6. 子供のステータスが両親平均 × (15 / peakStartAge) と一致するか検証
 *
 * すべて確率 1.0 + 固定シードで決定的に再現可能。
 */
import { Unit } from "../packages/core/src/models/Unit";
import { Squad } from "../packages/core/src/models/Squad";
import { Brigade } from "../packages/core/src/models/Brigade";
import { BattleSimulator } from "../packages/core/src/BattleSimulator";
import type { JobType } from "../packages/core/src/models/Unit";

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

const SEED = parseInt(process.argv[2] ?? "7", 10);
const battleRng = mulberry32(SEED);
const advanceRng = mulberry32(SEED + 99);

// ─── ユニット生成（戦闘用ステータス込み） ─────────────────────────────────────

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

function makeKnight(
  id: string,
  name: string,
  job: JobType,
  gender: "Male" | "Female",
  baseStrength: number
): Unit {
  const d = JOB_DEFAULTS[job];
  return new Unit({
    id, name, job, gender,
    age: 22,
    peakStartAge: 25, peakEndAge: 32, maxAge: 60,
    baseStats: { strength: baseStrength, agility: 60, intelligence: 60, endurance: 60 },
    maxHp: d.maxHp, hp: d.maxHp,
    speed: d.speed, frontAttack: d.frontAttack, rearAttack: d.rearAttack,
    bdf: d.bdf, sdf: d.sdf, ab: d.ab, hl: d.hl,
  });
}

// ─── 検証フロー ───────────────────────────────────────────────────────────────

console.log("╔══════════════════════════════════════════════════════════╗");
console.log("║       Bloodline Verification — 結婚・出産・継承の検証     ║");
console.log("╚══════════════════════════════════════════════════════════╝\n");

// 主人公2人
const arthur = makeKnight("p-arthur",  "Arthur",  "iron_wall_knight", "Male",   120);
const elise  = makeKnight("p-elise",   "Elise",   "medic",            "Female", 100);

// 戦闘用に同分隊を形成するための補助ユニット（HP受け要員）
const filler1 = makeKnight("p-fil1", "Tank補助",  "iron_wall_knight", "Male", 80);
const filler2 = makeKnight("p-fil2", "Atk補助",   "sniper",           "Male", 90);
const filler3 = makeKnight("p-fil3", "Buf補助",   "tactician",        "Male", 70);

// 旅団立ち上げ（currentYear=1）
let brigade = new Brigade([arthur, elise, filler1, filler2, filler3]);

console.log(`Year ${brigade.currentYear}: 初期ユニット ${brigade.units.length}名`);
console.log(`  - ${arthur.name} (${arthur.gender}, ${arthur.job})`);
console.log(`  - ${elise.name}  (${elise.gender}, ${elise.job})`);

// ─── Step 1: 10戦して好感度を 100 まで積み上げる ─────────────────────────────

console.log(`\n─── Step 1: 同分隊で10戦 ─────────────────────────────────────────`);

const FATHER_BASE = { ...arthur.baseStats };
const MOTHER_BASE = { ...elise.baseStats };

for (let battleN = 1; battleN <= 10; battleN++) {
  // 毎戦、現在の brigade.units から FRONT 分隊を作る（HP満タンで再生成）
  const lookup = new Map(brigade.units.map((u) => [u.id, u]));
  const front = new Squad("FRONT", [
    refreshHp(lookup.get(arthur.id)!),
    refreshHp(lookup.get(elise.id)!),
    refreshHp(lookup.get(filler1.id)!),
  ]);
  const rear = new Squad("REAR-L", [
    refreshHp(lookup.get(filler2.id)!),
    refreshHp(lookup.get(filler3.id)!),
  ]);

  // 弱めの敵: 1ユニット・前衛攻撃のみで好感度稼ぎ重視
  const dummyEnemy = new Unit({
    id: `e-${battleN}`,
    name: `練習相手${battleN}`,
    age: 25, peakStartAge: 25, peakEndAge: 35, maxAge: 60,
    baseStats: { strength: 30, agility: 0, intelligence: 0, endurance: 0 },
    maxHp: 100, hp: 100,
    speed: 15, frontAttack: 5, rearAttack: 5,
  });
  const enemy = [new Squad("E1", [dummyEnemy])];

  const sim = new BattleSimulator([front, rear], enemy, {
    maxTurns: 5,
    rng: battleRng,
    verbose: false,
  });
  const result = sim.run();

  // 同分隊ペア（arthur / elise を含む組のみ抽出して見やすく）
  const focusPairs = result.squadmatePairs.filter(
    ([a, b]) =>
      (a === arthur.id && b === elise.id) || (a === elise.id && b === arthur.id)
  );

  brigade = brigade.applyBattleAffinity(result.squadmatePairs, 10);

  // 進捗ログ（毎戦は冗長なので3戦目以降は要点のみ）
  const arthurNow = brigade.units.find((u) => u.id === arthur.id)!;
  const aff = arthurNow.getAffinity(elise.id);
  console.log(
    `  Battle ${String(battleN).padStart(2)} → ${result.winner.padEnd(7)} ` +
      `(${result.turns}T) | Arthur→Elise 好感度 ${aff}` +
      (focusPairs.length > 0 ? " ★同分隊" : "")
  );
}

const arthurAfterBattles = brigade.units.find((u) => u.id === arthur.id)!;
const eliseAfterBattles  = brigade.units.find((u) => u.id === elise.id)!;
console.log(
  `\n  → 好感度確認: Arthur→Elise=${arthurAfterBattles.getAffinity(
    elise.id
  )}, Elise→Arthur=${eliseAfterBattles.getAffinity(arthur.id)}`
);

// ─── Step 2: advance() で結婚成立 ─────────────────────────────────────────────

console.log(`\n─── Step 2: 1年経過 → 結婚判定 (marriageProb=1.0) ─────────────────`);

const advOpt = {
  rng: advanceRng,
  marriageProb: 1.0,
  birthProb: 0.0, // この年は結婚のみ
  affinityPerBattle: 0,
};

const r1 = brigade.advance([], advOpt);
brigade = r1.brigade;

console.log(`  Year ${brigade.currentYear}: イベント数 ${r1.events.length}`);
for (const ev of r1.events) {
  if (ev.type === "marriage") {
    console.log(
      `   💍 結婚: ${ev.husband.name} (${ev.husband.id}) × ${ev.wife.name} (${ev.wife.id})`
    );
  }
}

const arthurMarried = brigade.units.find((u) => u.id === arthur.id)!;
const eliseMarried  = brigade.units.find((u) => u.id === elise.id)!;
if (arthurMarried.spouseId !== elise.id || eliseMarried.spouseId !== arthur.id) {
  console.error("❌ 結婚が成立していません！");
  process.exit(1);
}
console.log(
  `  ✓ Arthur.spouseId = ${arthurMarried.spouseId}, Elise.spouseId = ${eliseMarried.spouseId}`
);

// ─── Step 3: 翌年に出産予約 ───────────────────────────────────────────────────

console.log(`\n─── Step 3: 1年経過 → 出産予約 (birthProb=1.0) ────────────────────`);

const r2 = brigade.advance([], { ...advOpt, marriageProb: 0.0, birthProb: 1.0 });
brigade = r2.brigade;

let plannedBirth = null;
for (const ev of r2.events) {
  if (ev.type === "birth_planned") {
    plannedBirth = ev.registry;
    console.log(`  Year ${brigade.currentYear}: 👶 出産予約`);
    console.log(`    fatherId: ${ev.registry.fatherId}`);
    console.log(`    motherId: ${ev.registry.motherId}`);
    console.log(`    birthYear: ${ev.registry.birthYear}`);
    console.log(`    plannedJoinYear: ${ev.registry.plannedJoinYear} (15年後)`);
    console.log(`    potentialStats: ${JSON.stringify(ev.registry.potentialStats)}`);
    console.log(`    job (継承): ${ev.registry.job}`);
  }
}
if (!plannedBirth) {
  console.error("❌ 出産予約が発生しませんでした！");
  process.exit(1);
}
console.log(`  pendingBirths.length = ${brigade.pendingBirths.length}`);

// ─── Step 4: 15年経過 → 15歳入団 ──────────────────────────────────────────────

console.log(`\n─── Step 4: 15年経過 → 継承者の入団を待つ ─────────────────────────`);

const expectedJoinYear = plannedBirth.plannedJoinYear;
let bornChild: Unit | null = null;

while (brigade.currentYear < expectedJoinYear) {
  const r = brigade.advance([], {
    rng: advanceRng,
    marriageProb: 0.0,
    birthProb: 0.0, // 追加出産は抑制
    affinityPerBattle: 0,
  });
  brigade = r.brigade;
  for (const ev of r.events) {
    if (ev.type === "birth") {
      bornChild = ev.unit;
      console.log(`  Year ${brigade.currentYear}: 🎉 継承者誕生`);
      console.log(`    name: ${ev.unit.name}`);
      console.log(`    id: ${ev.unit.id}`);
      console.log(`    age: ${ev.unit.age}`);
      console.log(`    gender: ${ev.unit.gender}`);
      console.log(`    job: ${ev.unit.job}`);
      console.log(`    parents: father=${ev.unit.parents?.fatherId}, mother=${ev.unit.parents?.motherId}`);
      console.log(`    baseStats (= potentialStats): ${JSON.stringify(ev.unit.baseStats)}`);
      console.log(`    実stats (age15・growthFactor=${ev.unit.growthFactor.toFixed(3)}): ${JSON.stringify(ev.unit.stats)}`);
    }
  }
}

if (!bornChild) {
  console.error("❌ 継承者が誕生しませんでした！");
  process.exit(1);
}

// ─── Step 5: 検証 ─────────────────────────────────────────────────────────────

console.log(`\n─── Step 5: 継承計算の検証 ────────────────────────────────────────`);

const expectedPotential = {
  strength:     Math.round((FATHER_BASE.strength     + MOTHER_BASE.strength)     / 2),
  agility:      Math.round((FATHER_BASE.agility      + MOTHER_BASE.agility)      / 2),
  intelligence: Math.round((FATHER_BASE.intelligence + MOTHER_BASE.intelligence) / 2),
  endurance:    Math.round((FATHER_BASE.endurance    + MOTHER_BASE.endurance)    / 2),
};

console.log(`  父 (Arthur) baseStats : ${JSON.stringify(FATHER_BASE)}`);
console.log(`  母 (Elise)  baseStats : ${JSON.stringify(MOTHER_BASE)}`);
console.log(`  期待 potentialStats   : ${JSON.stringify(expectedPotential)}`);
console.log(`  実 baseStats          : ${JSON.stringify(bornChild.baseStats)}`);

const okPotential = JSON.stringify(bornChild.baseStats) === JSON.stringify(expectedPotential);
console.log(`  → ${okPotential ? "✓" : "❌"} 全盛期予想値（potentialStats）一致`);

// 実ステータス = potentialStats × (15 / peakStartAge)
const factor = 15 / bornChild.peakStartAge;
const expectedStats = {
  strength:     Math.max(1, Math.round(expectedPotential.strength     * factor)),
  agility:      Math.max(1, Math.round(expectedPotential.agility      * factor)),
  intelligence: Math.max(1, Math.round(expectedPotential.intelligence * factor)),
  endurance:    Math.max(1, Math.round(expectedPotential.endurance    * factor)),
};
console.log(`  期待 実stats (×${factor.toFixed(3)}) : ${JSON.stringify(expectedStats)}`);
console.log(`  実 stats                : ${JSON.stringify(bornChild.stats)}`);
const okStats = JSON.stringify(bornChild.stats) === JSON.stringify(expectedStats);
console.log(`  → ${okStats ? "✓" : "❌"} 修業期実ステータス一致`);

const okJob = bornChild.job === arthur.job || bornChild.job === elise.job;
console.log(
  `  → ${okJob ? "✓" : "❌"} job 継承（父=${arthur.job} / 母=${elise.job} → 子=${bornChild.job}）`
);

const okParents =
  bornChild.parents?.fatherId === arthur.id &&
  bornChild.parents?.motherId === elise.id;
console.log(`  → ${okParents ? "✓" : "❌"} parents 記録`);

console.log("\n" + "=".repeat(62));
if (okPotential && okStats && okJob && okParents) {
  console.log("✅ 血統継承システム検証 全項目 PASS");
} else {
  console.log("❌ 血統継承システムに不整合あり");
  process.exit(1);
}
console.log("=".repeat(62));

// ─── HP満タンに戻すユーティリティ ────────────────────────────────────────────

function refreshHp(u: Unit): Unit {
  return new Unit({ ...u, hp: u.maxHp });
}
