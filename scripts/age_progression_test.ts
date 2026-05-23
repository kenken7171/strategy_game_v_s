#!/usr/bin/env bun
/**
 * 経年変化の動作確認スクリプト
 * 1人のユニットが20歳（修業期）から35歳（衰退期）まで
 * 5年刻みでステータスがどう変化するかを出力する。
 */

import { Unit } from "../packages/core/src/models/Unit";

const BASE = { strength: 100, agility: 80, intelligence: 60, endurance: 70 };

// peakStartAge=25, peakEndAge=30 で三段階が明確に確認できる設定
const template = {
  id: "test-unit",
  name: "Arthur",
  birthYear: 0,
  peakStartAge: 25,
  peakEndAge: 30,
  maxAge: 55,
  baseStats: BASE,
};

const AGES = [20, 25, 30, 35];

function phase(age: number, peakStart: number, peakEnd: number, maxAge: number): string {
  if (age >= maxAge)   return "引退";
  if (age < peakStart) return "修業期";
  if (age <= peakEnd)  return "全盛期";
  return "衰退期";
}

console.log("=".repeat(70));
console.log("  経年変化テスト  (peakStart=25, peakEnd=30, maxAge=55)");
console.log("=".repeat(70));
console.log(
  `${"年齢".padEnd(6)}${"フェーズ".padEnd(8)}${"係数".padEnd(8)}` +
  `${"STR".padEnd(6)}${"AGI".padEnd(6)}${"INT".padEnd(6)}${"END".padEnd(6)}`
);
console.log("-".repeat(70));

for (const age of AGES) {
  const unit = new Unit({ ...template, age });
  const s = unit.stats;
  const f = unit.growthFactor;
  const p = phase(age, template.peakStartAge, template.peakEndAge, template.maxAge);
  console.log(
    `${String(age).padEnd(6)}${p.padEnd(8)}${f.toFixed(3).padEnd(8)}` +
    `${String(s.strength).padEnd(6)}${String(s.agility).padEnd(6)}` +
    `${String(s.intelligence).padEnd(6)}${String(s.endurance).padEnd(6)}`
  );
}

console.log("=".repeat(70));
console.log("\n[補足] baseStats は全盛期の最大値:");
console.log(`  STR=${BASE.strength}  AGI=${BASE.agility}  INT=${BASE.intelligence}  END=${BASE.endurance}`);
