#!/usr/bin/env bun
/**
 * verify-naming.ts — 多文化命名システムの End-to-End 検証
 *
 * 検証項目:
 *   1. 300名の連続生成で全員ユニーク
 *   2. 3文化圏（Japanese/European/Classical）と男女のバランス分布
 *   3. プール枯渇時の称号付与（fallback）動作確認
 *      - 1つの origin/gender 組み合わせを連続で 200回 抽出（プールは150）
 *      - 1〜150 名は素のプール名、151 以降は称号付き名
 *   4. Brigade.historicalNames が外部操作なしで自動蓄積されること
 *
 * 使い方:
 *   bun scripts/verify-naming.ts            # seed=42
 *   bun scripts/verify-naming.ts --seed 7
 */
import { Unit } from "../packages/core/src/models/Unit";
import { Brigade } from "../packages/core/src/models/Brigade";
import {
  NameGenerator,
  pickRandomOrigin,
  ALL_ORIGINS,
  NAMES,
} from "../packages/core/src/data/names";
import type { Origin } from "../packages/core/src/data/names";
import type { Gender } from "../packages/core/src/models/Unit";

// ─── CLI ─────────────────────────────────────────────────────────────────────

const args = process.argv.slice(2);
const SEED = parseInt(
  args[args.indexOf("--seed") + 1] ?? args[0] ?? "42",
  10
);

function mulberry32(seed: number): () => number {
  let s = seed;
  return () => {
    s = (s + 0x6d2b79f5) | 0;
    let t = Math.imul(s ^ (s >>> 15), 1 | s);
    t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}

console.log("╔══════════════════════════════════════════════════════════╗");
console.log("║       Naming System Verification — 命名重複回避の検証      ║");
console.log("╚══════════════════════════════════════════════════════════╝\n");
console.log(`RNG seed: ${SEED}\n`);

// ─── テスト 1: 300名連続生成でユニーク・分布確認 ─────────────────────────────

console.log("─── Test 1: 300名連続生成 ──────────────────────────────────────────");

const rng1 = mulberry32(SEED);
const gen1 = new NameGenerator(rng1);
const historical = new Set<string>();
const generated: { name: string; origin: Origin; gender: Gender }[] = [];

for (let i = 0; i < 300; i++) {
  const origin = pickRandomOrigin(rng1);
  const gender: Gender = rng1() < 0.5 ? "Male" : "Female";
  const name = gen1.pick(origin, gender, historical);
  historical.add(name);
  generated.push({ name, origin, gender });
}

const uniqueCount = new Set(generated.map((g) => g.name)).size;
console.log(`  生成数 : ${generated.length}`);
console.log(`  ユニーク: ${uniqueCount}`);
const test1Pass = uniqueCount === 300;
console.log(`  → ${test1Pass ? "✓" : "❌"} 全員ユニーク`);

// 文化圏分布
const byOrigin: Record<Origin, number> = { Japanese: 0, European: 0, Classical: 0 };
const byGender: Record<Gender, number> = { Male: 0, Female: 0 };
const byOriginGender: Record<string, number> = {};
for (const g of generated) {
  byOrigin[g.origin]++;
  byGender[g.gender]++;
  const key = `${g.origin}/${g.gender}`;
  byOriginGender[key] = (byOriginGender[key] ?? 0) + 1;
}

console.log(`\n  文化圏分布:`);
for (const o of ALL_ORIGINS) {
  const pct = ((byOrigin[o] / 300) * 100).toFixed(1);
  console.log(`    ${o.padEnd(10)}: ${byOrigin[o]} (${pct}%)`);
}
console.log(`\n  性別分布:`);
for (const g of ["Male", "Female"] as Gender[]) {
  const pct = ((byGender[g] / 300) * 100).toFixed(1);
  console.log(`    ${g.padEnd(6)}: ${byGender[g]} (${pct}%)`);
}
console.log(`\n  文化圏×性別 内訳:`);
for (const k of Object.keys(byOriginGender).sort()) {
  console.log(`    ${k.padEnd(20)}: ${byOriginGender[k]}`);
}

// バランス: 各文化圏が 100±40 (60〜140) に収まっていることを「バランスよく混在」とみなす
const balanceOk = ALL_ORIGINS.every((o) => byOrigin[o] >= 60 && byOrigin[o] <= 140);
console.log(`\n  → ${balanceOk ? "✓" : "❌"} 文化圏バランス（各60〜140の範囲内）`);

// サンプル20名
console.log(`\n  サンプル20名（順）:`);
for (let i = 0; i < 20; i++) {
  const g = generated[i];
  console.log(`    ${String(i + 1).padStart(3)}. ${g.name.padEnd(15)} [${g.origin}/${g.gender}]`);
}

// ─── テスト 2: プール枯渇 → 称号付与の動作 ────────────────────────────────────

console.log(`\n─── Test 2: プール枯渇時の称号付与（European/Male を200回連続） ─────`);

const rng2 = mulberry32(SEED + 1);
const gen2 = new NameGenerator(rng2);
const exhaustHistorical = new Set<string>();
const exhaustResults: { name: string; titled: boolean; retries: number }[] = [];

for (let i = 0; i < 200; i++) {
  const r = gen2.pickDetailed("European", "Male", exhaustHistorical);
  exhaustHistorical.add(r.name);
  exhaustResults.push({ name: r.name, titled: r.titled, retries: r.retries });
}

const titledCount = exhaustResults.filter((r) => r.titled).length;
const poolSize = NAMES.European.Male.length;
const expectedTitledStart = poolSize + 1; // 151番目から称号

console.log(`  生成数        : ${exhaustResults.length}`);
console.log(`  プールサイズ  : ${poolSize}`);
console.log(`  称号付き数    : ${titledCount}`);
console.log(`  期待される称号付き数: ${200 - poolSize} （プール150消費後）`);
const test2aPass = titledCount === 200 - poolSize;
console.log(`  → ${test2aPass ? "✓" : "❌"} 期待数と一致`);

// 1〜150 は素のプール名、151〜200 は称号付き
let firstTitledIdx = -1;
for (let i = 0; i < exhaustResults.length; i++) {
  if (exhaustResults[i].titled) {
    firstTitledIdx = i + 1;
    break;
  }
}
console.log(`  最初の称号付き出現位置: ${firstTitledIdx}（期待: ${expectedTitledStart}）`);
const test2bPass = firstTitledIdx === expectedTitledStart;
console.log(`  → ${test2bPass ? "✓" : "❌"} 枯渇直後に称号付与に切替`);

console.log(`\n  称号付き名サンプル（151〜160番目）:`);
for (let i = 150; i < Math.min(160, exhaustResults.length); i++) {
  const r = exhaustResults[i];
  console.log(`    ${i + 1}. ${r.name}  [titled=${r.titled}]`);
}

// 全員ユニーク（200名）
const exhaustUnique = new Set(exhaustResults.map((r) => r.name)).size;
console.log(`\n  ユニーク数: ${exhaustUnique}/${exhaustResults.length}`);
const test2cPass = exhaustUnique === 200;
console.log(`  → ${test2cPass ? "✓" : "❌"} 称号付きも含めて全員ユニーク`);

// ─── テスト 3: Brigade.historicalNames の自動蓄積 ─────────────────────────────

console.log(`\n─── Test 3: Brigade.historicalNames の自動蓄積 ──────────────────────`);

const rng3 = mulberry32(SEED + 2);
const gen3 = new NameGenerator(rng3);

function makeTestUnit(historical: ReadonlySet<string>, idx: number): Unit {
  const origin = pickRandomOrigin(rng3);
  const gender: Gender = rng3() < 0.5 ? "Male" : "Female";
  const name = gen3.pick(origin, gender, historical);
  return new Unit({
    id: `t${idx}`,
    name,
    age: 20,
    peakStartAge: 25, peakEndAge: 32, maxAge: 55,
    baseStats: { strength: 80, agility: 0, intelligence: 0, endurance: 0 },
    gender, origin,
  });
}

// 初期5名
const local0 = new Set<string>();
const initial: Unit[] = [];
for (let i = 0; i < 5; i++) {
  const u = makeTestUnit(local0, i);
  local0.add(u.name);
  initial.push(u);
}
let brigade = new Brigade(initial);
console.log(`  初期 historicalNames.size = ${brigade.historicalNames.size} （初期5名分・期待: 5）`);

// 50年分 recruits を進める（毎年1〜3名）
let yearJoins = 0;
for (let y = 1; y <= 50; y++) {
  const count = 1 + Math.floor(rng3() * 3); // 1〜3名
  const localY = new Set(brigade.historicalNames);
  const recruits: Unit[] = [];
  for (let i = 0; i < count; i++) {
    const u = makeTestUnit(localY, 100 + yearJoins);
    localY.add(u.name);
    recruits.push(u);
    yearJoins++;
  }
  brigade = brigade.advance(recruits).brigade;
}

console.log(`  50年経過後 historicalNames.size = ${brigade.historicalNames.size}`);
console.log(`    (初期5 + 投入${yearJoins} = ${5 + yearJoins}、引退者も残るので等しい)`);
const test3aPass = brigade.historicalNames.size === 5 + yearJoins;
console.log(`  → ${test3aPass ? "✓" : "❌"} 全投入名が永続記録されている`);

// 重複0
const allUnits = [...brigade.historicalNames];
const test3bPass = new Set(allUnits).size === allUnits.length;
console.log(`  → ${test3bPass ? "✓" : "❌"} historicalNames 内に重複なし`);

// ─── 総合判定 ────────────────────────────────────────────────────────────────

console.log("\n" + "=".repeat(64));
const allPass = test1Pass && balanceOk && test2aPass && test2bPass && test2cPass && test3aPass && test3bPass;
if (allPass) {
  console.log("[OK] 命名システム検証 全項目 PASS");
} else {
  console.log("[NG] 命名システムに不整合あり");
  process.exit(1);
}
console.log("=".repeat(64));
