/**
 * 大隊間戦闘デモ (BattleSimulator + BattleManager ベース)
 * run: bun run apps/cli/src/simulate_brigade_battle.ts
 */
import { Unit } from "../../../packages/core/src/models/Unit";
import { Squad } from "../../../packages/core/src/models/Squad";
import { BattleSimulator, printBattleReport } from "../../../packages/core/src/BattleSimulator";

function mulberry32(seed: number) {
  let s = seed;
  return () => {
    s = (s + 0x6d2b79f5) | 0;
    let t = Math.imul(s ^ (s >>> 15), 1 | s);
    t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}

function makeUnit(
  id: string,
  name: string,
  job: "iron_wall_knight" | "tactician" | "medic" | "sniper",
  overrides: Partial<{
    maxHp: number; speed: number;
    frontAttack: number; rearAttack: number;
    bdf: number; sdf: number; ab: number; hl: number;
  }> = {}
): Unit {
  const defaults: Record<string, Record<string, number>> = {
    iron_wall_knight: { maxHp: 250, speed: 10, frontAttack: 50, rearAttack: 10, bdf: 10, sdf: 15 },
    tactician:        { maxHp: 120, speed: 35, frontAttack: 20, rearAttack: 20, ab: 20 },
    medic:            { maxHp: 100, speed: 25, frontAttack: 10, rearAttack: 10, hl: 30 },
    sniper:           { maxHp:  80, speed: 40, frontAttack: 20, rearAttack: 90 },
  };
  const d = defaults[job];
  return new Unit({
    id, name, job,
    age: 25, peakStartAge: 25, peakEndAge: 35, maxAge: 60,
    baseStats: { strength: 50, agility: 50, intelligence: 50, endurance: 50 },
    maxHp:        overrides.maxHp        ?? d.maxHp,
    hp:           overrides.maxHp        ?? d.maxHp,
    speed:        overrides.speed        ?? d.speed,
    frontAttack:  overrides.frontAttack  ?? d.frontAttack,
    rearAttack:   overrides.rearAttack   ?? d.rearAttack,
    bdf:          overrides.bdf          ?? d.bdf ?? 0,
    sdf:          overrides.sdf          ?? d.sdf ?? 0,
    ab:           overrides.ab           ?? d.ab  ?? 0,
    hl:           overrides.hl           ?? d.hl  ?? 0,
  });
}

// ─── 自軍大隊 ─────────────────────────────────────────────────────────────────
const alliedFront = new Squad("FRONT", [
  makeUnit("a-f1", "シールドマン甲", "iron_wall_knight"),
  makeUnit("a-f2", "シールドマン乙", "iron_wall_knight"),
  makeUnit("a-f3", "参謀長",         "tactician"),
]);
const alliedRearL = new Squad("REAR-L", [
  makeUnit("a-rl1", "エースガンナー",  "sniper"),
  makeUnit("a-rl2", "ロングボウマン", "sniper"),
  makeUnit("a-rl3", "野戦衛生兵α",   "medic"),
]);
const alliedRearR = new Squad("REAR-R", [
  makeUnit("a-rr1", "野戦衛生兵β",  "medic"),
  makeUnit("a-rr2", "野戦衛生兵γ",  "medic"),
  makeUnit("a-rr3", "副参謀長",      "tactician"),
]);

// ─── 敵軍大隊 ─────────────────────────────────────────────────────────────────
const enemyFront = new Squad("FRONT", [
  makeUnit("e-f1", "敵鉄壁騎士",      "iron_wall_knight"),
  makeUnit("e-f2", "敵前衛狙撃手甲",  "sniper"),
  makeUnit("e-f3", "敵前衛狙撃手乙",  "sniper"),
]);
const enemyRearL = new Squad("REAR-L", [
  makeUnit("e-rl1", "敵戦術官A", "tactician"),
  makeUnit("e-rl2", "敵戦術官B", "tactician"),
  makeUnit("e-rl3", "敵衛生兵",  "medic"),
]);
const enemyRearR = new Squad("REAR-R", [
  makeUnit("e-rr1", "敵狙撃手I",   "sniper"),
  makeUnit("e-rr2", "敵狙撃手II",  "sniper"),
  makeUnit("e-rr3", "敵狙撃手III", "sniper"),
]);

// ─── シミュレーション実行 ─────────────────────────────────────────────────────
const sim = new BattleSimulator(
  [alliedFront, alliedRearL, alliedRearR],
  [enemyFront, enemyRearL, enemyRearR],
  { maxTurns: 30, rng: mulberry32(42), verbose: true }
);

const result = sim.run();
printBattleReport(result);
