#!/usr/bin/env bun
/**
 * 大隊間戦闘シミュレーター エントリーポイント
 *
 * 使い方:
 *   bun scripts/run-sim.ts                    # 標準大隊 vs 標準大隊
 *   bun scripts/run-sim.ts --seed 123         # 乱数シードを指定
 *   bun scripts/run-sim.ts --turns 20         # 最大ターン数を変更
 *   bun scripts/run-sim.ts --quiet            # ターン詳細ログを抑制
 *   bun scripts/run-sim.ts --preset aggressive vs balanced  # プリセット選択
 */

import { Unit } from "../packages/core/src/models/Unit";
import { Squad } from "../packages/core/src/models/Squad";
import { BattleSimulator, printBattleReport } from "../packages/core/src/BattleSimulator";

// ─── CLI 引数パース ───────────────────────────────────────────────────────────

const args = process.argv.slice(2);

function argValue(flag: string): string | undefined {
  const idx = args.indexOf(flag);
  return idx >= 0 ? args[idx + 1] : undefined;
}

const SEED     = parseInt(argValue("--seed")  ?? "42",  10);
const MAX_TURN = parseInt(argValue("--turns") ?? "30",  10);
const QUIET    = args.includes("--quiet");
const PRESET_A = argValue("--preset") ?? "balanced";
const PRESET_B = (() => {
  const vsIdx = args.indexOf("vs");
  return vsIdx >= 0 ? args[vsIdx + 1] : "balanced";
})();

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

// ─── ユニット生成ヘルパー ─────────────────────────────────────────────────────

type JobType =
  | "iron_wall_knight" | "tactician" | "medic" | "sniper"
  | "sorcerer" | "standard_bearer" | "heavy_infantry" | "scout";

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

let _uid = 0;
function makeUnit(
  prefix: string,
  name: string,
  job: JobType,
  overrides: Partial<JobDefaults> = {}
): Unit {
  const d = { ...JOB_DEFAULTS[job], ...overrides };
  return new Unit({
    id: `${prefix}-${_uid++}`,
    name, job,
    age: 25, peakStartAge: 28, peakEndAge: 33, maxAge: 55,
    baseStats: { strength: 50, agility: 50, intelligence: 50, endurance: 50 },
    maxHp: d.maxHp, hp: d.maxHp,
    speed: d.speed, frontAttack: d.frontAttack, rearAttack: d.rearAttack,
    bdf: d.bdf, sdf: d.sdf, ab: d.ab, hl: d.hl,
  });
}

// ─── 大隊プリセット ───────────────────────────────────────────────────────────

/**
 * 標準大隊: 全4ジョブをバランス良く配置した汎用編成
 *   FRONT  : 鉄壁騎士×2 + 戦術官×1
 *   REAR-L : 狙撃兵×2  + 衛生兵×1
 *   REAR-R : 衛生兵×2  + 戦術官×1
 */
function makeBrigade_balanced(prefix: string): Squad[] {
  return [
    new Squad("FRONT",  [
      makeUnit(prefix, `${prefix}シールドA`, "iron_wall_knight"),
      makeUnit(prefix, `${prefix}シールドB`, "iron_wall_knight"),
      makeUnit(prefix, `${prefix}参謀長`,    "tactician"),
    ]),
    new Squad("REAR-L", [
      makeUnit(prefix, `${prefix}スナイパーA`, "sniper"),
      makeUnit(prefix, `${prefix}スナイパーB`, "sniper"),
      makeUnit(prefix, `${prefix}衛生兵α`,     "medic"),
    ]),
    new Squad("REAR-R", [
      makeUnit(prefix, `${prefix}衛生兵β`,  "medic"),
      makeUnit(prefix, `${prefix}衛生兵γ`,  "medic"),
      makeUnit(prefix, `${prefix}副参謀長`, "tactician"),
    ]),
  ];
}

/**
 * 攻撃特化大隊: 狙撃兵を大量配置し、戦術官バフで火力を極大化
 *   FRONT  : 鉄壁騎士×1 + 戦術官×2
 *   REAR-L : 狙撃兵×3
 *   REAR-R : 狙撃兵×2  + 衛生兵×1
 */
function makeBrigade_aggressive(prefix: string): Squad[] {
  return [
    new Squad("FRONT",  [
      makeUnit(prefix, `${prefix}守将`,    "iron_wall_knight"),
      makeUnit(prefix, `${prefix}戦略家A`, "tactician"),
      makeUnit(prefix, `${prefix}戦略家B`, "tactician"),
    ]),
    new Squad("REAR-L", [
      makeUnit(prefix, `${prefix}狙撃手A`, "sniper"),
      makeUnit(prefix, `${prefix}狙撃手B`, "sniper"),
      makeUnit(prefix, `${prefix}狙撃手C`, "sniper"),
    ]),
    new Squad("REAR-R", [
      makeUnit(prefix, `${prefix}狙撃手D`, "sniper"),
      makeUnit(prefix, `${prefix}狙撃手E`, "sniper"),
      makeUnit(prefix, `${prefix}衛生兵`,  "medic"),
    ]),
  ];
}

/**
 * 守備特化大隊: 鉄壁騎士と衛生兵で硬く守り抜く
 *   FRONT  : 鉄壁騎士×3
 *   REAR-L : 衛生兵×3
 *   REAR-R : 鉄壁騎士×1 + 衛生兵×1 + 戦術官×1
 */
function makeBrigade_defensive(prefix: string): Squad[] {
  return [
    new Squad("FRONT",  [
      makeUnit(prefix, `${prefix}城壁A`,  "iron_wall_knight"),
      makeUnit(prefix, `${prefix}城壁B`,  "iron_wall_knight"),
      makeUnit(prefix, `${prefix}城壁C`,  "iron_wall_knight"),
    ]),
    new Squad("REAR-L", [
      makeUnit(prefix, `${prefix}衛生兵α`, "medic"),
      makeUnit(prefix, `${prefix}衛生兵β`, "medic"),
      makeUnit(prefix, `${prefix}衛生兵γ`, "medic"),
    ]),
    new Squad("REAR-R", [
      makeUnit(prefix, `${prefix}後方騎士`, "iron_wall_knight"),
      makeUnit(prefix, `${prefix}後衛衛生兵`, "medic"),
      makeUnit(prefix, `${prefix}指揮官`,    "tactician"),
    ]),
  ];
}

/**
 * 重装+斥候 大隊: 重装歩兵で受け、斥候で先制削り、旗手で全体火力ブースト
 *   FRONT  : 重装歩兵×2 + 旗手
 *   REAR-L : 斥候×3
 *   REAR-R : 斥候×2 + 衛生兵
 */
function makeBrigade_vanguard(prefix: string): Squad[] {
  return [
    new Squad("FRONT",  [
      makeUnit(prefix, `${prefix}重甲A`,   "heavy_infantry"),
      makeUnit(prefix, `${prefix}重甲B`,   "heavy_infantry"),
      makeUnit(prefix, `${prefix}旗手`,    "standard_bearer"),
    ]),
    new Squad("REAR-L", [
      makeUnit(prefix, `${prefix}斥候A`, "scout"),
      makeUnit(prefix, `${prefix}斥候B`, "scout"),
      makeUnit(prefix, `${prefix}斥候C`, "scout"),
    ]),
    new Squad("REAR-R", [
      makeUnit(prefix, `${prefix}斥候D`, "scout"),
      makeUnit(prefix, `${prefix}斥候E`, "scout"),
      makeUnit(prefix, `${prefix}衛生兵`, "medic"),
    ]),
  ];
}

/**
 * 砲台大隊（呪術師の理想運用）: 前衛厚く・後衛で呪術師を守る
 *   FRONT  : 鉄壁騎士 + 重装歩兵 + 旗手
 *   REAR-L : 呪術師×3
 *   REAR-R : 呪術師×2 + 衛生兵
 */
function makeBrigade_arcane(prefix: string): Squad[] {
  return [
    new Squad("FRONT",  [
      makeUnit(prefix, `${prefix}盾騎士`,   "iron_wall_knight"),
      makeUnit(prefix, `${prefix}重装`,     "heavy_infantry"),
      makeUnit(prefix, `${prefix}旗手`,     "standard_bearer"),
    ]),
    new Squad("REAR-L", [
      makeUnit(prefix, `${prefix}呪術師A`, "sorcerer"),
      makeUnit(prefix, `${prefix}呪術師B`, "sorcerer"),
      makeUnit(prefix, `${prefix}呪術師C`, "sorcerer"),
    ]),
    new Squad("REAR-R", [
      makeUnit(prefix, `${prefix}呪術師D`, "sorcerer"),
      makeUnit(prefix, `${prefix}呪術師E`, "sorcerer"),
      makeUnit(prefix, `${prefix}衛生兵`,  "medic"),
    ]),
  ];
}

/**
 * 脆い砲台（呪術師の即死パターン検証用）: 呪術師を FRONT に晒す
 *   FRONT  : 呪術師×3                 ← 1ターン目で全滅する想定
 *   REAR-L : 呪術師×2 + 衛生兵
 *   REAR-R : 衛生兵×2 + 旗手
 */
function makeBrigade_glass_cannon(prefix: string): Squad[] {
  return [
    new Squad("FRONT",  [
      makeUnit(prefix, `${prefix}前衛呪術師A`, "sorcerer"),
      makeUnit(prefix, `${prefix}前衛呪術師B`, "sorcerer"),
      makeUnit(prefix, `${prefix}前衛呪術師C`, "sorcerer"),
    ]),
    new Squad("REAR-L", [
      makeUnit(prefix, `${prefix}呪術師D`, "sorcerer"),
      makeUnit(prefix, `${prefix}呪術師E`, "sorcerer"),
      makeUnit(prefix, `${prefix}衛生兵α`, "medic"),
    ]),
    new Squad("REAR-R", [
      makeUnit(prefix, `${prefix}衛生兵β`, "medic"),
      makeUnit(prefix, `${prefix}衛生兵γ`, "medic"),
      makeUnit(prefix, `${prefix}旗手`,    "standard_bearer"),
    ]),
  ];
}

const PRESETS: Record<string, (prefix: string) => Squad[]> = {
  balanced:     makeBrigade_balanced,
  aggressive:   makeBrigade_aggressive,
  defensive:    makeBrigade_defensive,
  vanguard:     makeBrigade_vanguard,
  arcane:       makeBrigade_arcane,
  glass_cannon: makeBrigade_glass_cannon,
};

function loadPreset(name: string, prefix: string): Squad[] {
  const fn = PRESETS[name];
  if (!fn) {
    console.error(`Unknown preset: "${name}". Valid: ${Object.keys(PRESETS).join(", ")}`);
    process.exit(1);
  }
  return fn(prefix);
}

// ─── ヘッダー出力 ─────────────────────────────────────────────────────────────

console.log("╔══════════════════════════════════════════════════════════╗");
console.log("║       Chronicle Knights — 大隊戦闘シミュレーター          ║");
console.log("╚══════════════════════════════════════════════════════════╝");
console.log(`  Allies  preset : ${PRESET_A}`);
console.log(`  Enemies preset : ${PRESET_B}`);
console.log(`  Max turns      : ${MAX_TURN}`);
console.log(`  RNG seed       : ${SEED}`);
console.log("");

// ─── 大隊構築 & シミュレーション実行 ─────────────────────────────────────────

const allies  = loadPreset(PRESET_A, "[A]");
const enemies = loadPreset(PRESET_B, "[E]");

const sim = new BattleSimulator(allies, enemies, {
  maxTurns: MAX_TURN,
  rng: mulberry32(SEED),
  verbose: !QUIET,
});

const result = sim.run();
printBattleReport(result);
