#!/usr/bin/env bun
/**
 * verify-individuality.ts — 全盛期年齢の個体差検証
 *
 * 仕様:
 *   - 新人 : BASE_PEAK_START_AGE (24) ±3, BASE_PEAK_END_AGE (28) ±3
 *           （独立ロール、必ず peakStartAge < peakEndAge）
 *   - 子供 : 両親の peakStartAge / peakEndAge の平均 ±1
 *           「成長タイプの遺伝性」を表現する狭めレンジ
 *
 * 検証項目:
 *   1. 100名の新人を生成して peakStartAge / peakEndAge のヒストグラムを表示
 *      （個体ごとに「修業期の終わるタイミング」がバラついている）
 *   2. 仕様レンジ内に全員収まっている（21〜27 / 25〜31）
 *   3. peakStartAge < peakEndAge が常に成立
 *   4. 親（24/28 と 30/34 = 平均 27/31）→ 子（26〜28 / 30〜32 範囲）の遺伝性確認
 *
 * 使い方:
 *   bun scripts/verify-individuality.ts            # seed=42
 *   bun scripts/verify-individuality.ts --seed 7
 */
import { CHRONICLE_CONFIG } from "../packages/core/src/config/ChronicleConfig";
import { rollPeakAges, rollChildPeakAges } from "../packages/core/src/utils/age";

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

const rng = mulberry32(SEED);

console.log("╔══════════════════════════════════════════════════════════╗");
console.log("║       Individuality Verification — 個体差の検証          ║");
console.log("╚══════════════════════════════════════════════════════════╝\n");
console.log(`RNG seed: ${SEED}`);
console.log(`CHRONICLE_CONFIG.TIME.BASE_PEAK_START_AGE = ${CHRONICLE_CONFIG.TIME.BASE_PEAK_START_AGE}`);
console.log(`CHRONICLE_CONFIG.TIME.BASE_PEAK_END_AGE   = ${CHRONICLE_CONFIG.TIME.BASE_PEAK_END_AGE}\n`);

// ─── Test 1: 新人100名の peakStart / peakEnd ヒストグラム ────────────────────

console.log("─── Test 1: 新人100名の peakStartAge / peakEndAge 分布 ──────────────");

const N = 100;
const samples: { peakStartAge: number; peakEndAge: number }[] = [];
for (let i = 0; i < N; i++) samples.push(rollPeakAges(rng));

const startHist = new Map<number, number>();
const endHist   = new Map<number, number>();
const trainingEndHist = new Map<number, number>(); // = peakStartAge - 1 で「修業期最終年齢」
for (const s of samples) {
  startHist.set(s.peakStartAge, (startHist.get(s.peakStartAge) ?? 0) + 1);
  endHist.set(s.peakEndAge,     (endHist.get(s.peakEndAge)     ?? 0) + 1);
  trainingEndHist.set(s.peakStartAge - 1, (trainingEndHist.get(s.peakStartAge - 1) ?? 0) + 1);
}

const baseStart = CHRONICLE_CONFIG.TIME.BASE_PEAK_START_AGE;
const baseEnd   = CHRONICLE_CONFIG.TIME.BASE_PEAK_END_AGE;

function printHist(label: string, hist: Map<number, number>, expectMin: number, expectMax: number) {
  console.log(`\n  ${label}:`);
  const keys = [...new Set([
    ...Array.from({ length: expectMax - expectMin + 1 }, (_, i) => expectMin + i),
    ...hist.keys(),
  ])].sort((a, b) => a - b);
  for (const k of keys) {
    const count = hist.get(k) ?? 0;
    const bar = "█".repeat(count);
    const marker = (k < expectMin || k > expectMax) ? " ⚠" : "";
    console.log(`    ${String(k).padStart(3)} : ${String(count).padStart(3)}${marker} ${bar}`);
  }
}

printHist("peakStartAge 分布 (期待: 21〜27)", startHist, 21, 27);
printHist("peakEndAge 分布 (期待: 25〜31)",   endHist,   25, 31);
printHist("修業期の終わる年齢 (peakStartAge - 1)", trainingEndHist, 20, 26);

// 仕様レンジチェック
const outOfRangeStart = samples.filter((s) => s.peakStartAge < baseStart - 3 || s.peakStartAge > baseStart + 3);
const outOfRangeEnd   = samples.filter((s) => s.peakEndAge   < baseEnd   - 3 || s.peakEndAge   > baseEnd   + 3);

console.log(`\n  peakStartAge 仕様レンジ外: ${outOfRangeStart.length}/${N}`);
console.log(`  peakEndAge   仕様レンジ外: ${outOfRangeEnd.length}/${N}`);
const test1aPass = outOfRangeStart.length === 0 && outOfRangeEnd.length === 0;
console.log(`  → ${test1aPass ? "✓" : "❌"} 全員が仕様レンジに収まっている`);

// peakStart < peakEnd ガード
const guardViolations = samples.filter((s) => s.peakStartAge >= s.peakEndAge);
console.log(`  peakStart >= peakEnd 違反: ${guardViolations.length}/${N}`);
const test1bPass = guardViolations.length === 0;
console.log(`  → ${test1bPass ? "✓" : "❌"} ガード成立`);

// バラつき（ユニーク値数）
const uniqueStart = new Set(samples.map((s) => s.peakStartAge)).size;
const uniqueEnd   = new Set(samples.map((s) => s.peakEndAge)).size;
console.log(`\n  ユニーク peakStartAge 値: ${uniqueStart} / 7想定値中`);
console.log(`  ユニーク peakEndAge 値:   ${uniqueEnd} / 7想定値中`);
const test1cPass = uniqueStart >= 5 && uniqueEnd >= 5;
console.log(`  → ${test1cPass ? "✓" : "❌"} バラつき十分（個体ごとに修業期終了タイミング異なる）`);

// 修業期長の分布（peakStartAge - 15 が「入団から全盛期入りまでの年数」）
const inductionAge = CHRONICLE_CONFIG.TIME.INDUCTION_AGE;
console.log(`\n  「入団(${inductionAge}歳)から全盛期入りまでの年数」分布:`);
const trainingLenHist = new Map<number, number>();
for (const s of samples) {
  const len = s.peakStartAge - inductionAge;
  trainingLenHist.set(len, (trainingLenHist.get(len) ?? 0) + 1);
}
for (const k of [...trainingLenHist.keys()].sort((a, b) => a - b)) {
  const count = trainingLenHist.get(k)!;
  const bar = "█".repeat(count);
  console.log(`    ${String(k).padStart(2)}年 : ${String(count).padStart(3)} ${bar}`);
}

// ─── Test 2: 子供への遺伝（両親平均±1） ─────────────────────────────────────

console.log("\n─── Test 2: 子供の peak年齢遺伝（両親平均±1） ──────────────────────");

// 父: peakStart=24, peakEnd=28（標準型）
// 母: peakStart=30, peakEnd=34（晩成型）
// → 子の平均は 27, 31。±1 ロールで 26〜28 / 30〜32 の範囲に収まるはず
const FATHER = { peakStartAge: 24, peakEndAge: 28 };
const MOTHER = { peakStartAge: 30, peakEndAge: 34 };
const expectedAvgStart = (FATHER.peakStartAge + MOTHER.peakStartAge) / 2; // 27
const expectedAvgEnd   = (FATHER.peakEndAge   + MOTHER.peakEndAge)   / 2; // 31

console.log(`  父 : peakStartAge=${FATHER.peakStartAge}, peakEndAge=${FATHER.peakEndAge}（標準型）`);
console.log(`  母 : peakStartAge=${MOTHER.peakStartAge}, peakEndAge=${MOTHER.peakEndAge}（晩成型）`);
console.log(`  期待される平均: peakStart=${expectedAvgStart}, peakEnd=${expectedAvgEnd}`);
console.log(`  期待レンジ    : peakStart [${expectedAvgStart - 1}〜${expectedAvgStart + 1}], peakEnd [${expectedAvgEnd - 1}〜${expectedAvgEnd + 1}]`);

const childRng = mulberry32(SEED + 100);
const CHILD_N = 50;
const children: { peakStartAge: number; peakEndAge: number }[] = [];
for (let i = 0; i < CHILD_N; i++) {
  children.push(rollChildPeakAges(
    FATHER.peakStartAge, FATHER.peakEndAge,
    MOTHER.peakStartAge, MOTHER.peakEndAge,
    childRng
  ));
}

const childStartHist = new Map<number, number>();
const childEndHist   = new Map<number, number>();
for (const c of children) {
  childStartHist.set(c.peakStartAge, (childStartHist.get(c.peakStartAge) ?? 0) + 1);
  childEndHist.set(c.peakEndAge,     (childEndHist.get(c.peakEndAge)     ?? 0) + 1);
}

console.log(`\n  子50名の peakStartAge 分布:`);
for (const k of [...childStartHist.keys()].sort((a, b) => a - b)) {
  console.log(`    ${k} : ${childStartHist.get(k)!} ${"█".repeat(childStartHist.get(k)!)}`);
}
console.log(`\n  子50名の peakEndAge 分布:`);
for (const k of [...childEndHist.keys()].sort((a, b) => a - b)) {
  console.log(`    ${k} : ${childEndHist.get(k)!} ${"█".repeat(childEndHist.get(k)!)}`);
}

const childOutOfRangeStart = children.filter((c) =>
  c.peakStartAge < expectedAvgStart - 1 || c.peakStartAge > expectedAvgStart + 1
);
const childOutOfRangeEnd = children.filter((c) =>
  c.peakEndAge < expectedAvgEnd - 1 || c.peakEndAge > expectedAvgEnd + 1
);
console.log(`\n  peakStartAge 仕様レンジ外: ${childOutOfRangeStart.length}/${CHILD_N}`);
console.log(`  peakEndAge   仕様レンジ外: ${childOutOfRangeEnd.length}/${CHILD_N}`);
const test2aPass = childOutOfRangeStart.length === 0 && childOutOfRangeEnd.length === 0;
console.log(`  → ${test2aPass ? "✓" : "❌"} 子も両親平均±1 のレンジに収まる`);

const childGuardViolations = children.filter((c) => c.peakStartAge >= c.peakEndAge);
const test2bPass = childGuardViolations.length === 0;
console.log(`  → ${test2bPass ? "✓" : "❌"} 子の peakStart < peakEnd ガード成立`);

// ─── 総合判定 ────────────────────────────────────────────────────────────────

console.log("\n" + "=".repeat(64));
const allPass = test1aPass && test1bPass && test1cPass && test2aPass && test2bPass;
if (allPass) {
  console.log("[OK] 個体差システム検証 全項目 PASS");
} else {
  console.log("[NG] 個体差システムに不整合あり");
  process.exit(1);
}
console.log("=".repeat(64));
