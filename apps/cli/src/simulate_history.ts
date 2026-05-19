import { Unit, Brigade } from "../../../packages/core/src/index";
import { mkdirSync, writeFileSync } from "fs";
import { join } from "path";

// 再現性のためのシード付きPRNG (Mulberry32)
function mulberry32(seed: number) {
  let s = seed;
  return function () {
    s = (s + 0x6d2b79f5) | 0;
    let t = Math.imul(s ^ (s >>> 15), 1 | s);
    t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}

const rand = mulberry32(2024);
const ri = (min: number, max: number) => Math.floor(rand() * (max - min + 1)) + min;

const NAMES = [
  "Leon",      "Arthur",    "Roland",    "Siegfried", "Lancelot",
  "Percival",  "Tristan",   "Galahad",   "Gawain",    "Bedivere",
  "Kay",       "Geraint",   "Gareth",    "Agravain",  "Lamorak",
  "Erec",      "Yvain",     "Bors",      "Lionel",    "Lucan",
  "Aldric",    "Bruno",     "Conrad",    "Dietrich",  "Ernst",
  "Friedrich", "Gustav",    "Heinrich",  "Ivan",      "Johann",
  "Karl",      "Ludwig",    "Matthias",  "Nikolaus",  "Otto",
  "Philipp",   "Rudolf",    "Stefan",    "Thomas",    "Ulrich",
  "Viktor",    "Werner",    "Xavier",    "Yorick",    "Zacharias",
  "Aldous",    "Bertram",   "Crispin",   "Dorian",    "Edmund",
  "Fabian",    "Gilbert",   "Harold",    "Ignatius",  "Julian",
  "Magnus",    "Nathan",    "Pascal",    "Quentin",   "Reginald",
];

let unitCounter = 0;

function makeRecruit(): Unit {
  const joinAge  = ri(14, 18);
  const peakAge  = joinAge + ri(10, 18);
  const maxAge   = peakAge + ri(15, 25);
  const name     = NAMES[unitCounter % NAMES.length];
  const id       = `u${String(unitCounter).padStart(3, "0")}`;
  unitCounter++;
  return new Unit({
    id,
    name,
    age: joinAge,
    peakAge,
    maxAge,
    baseStats: { strength: ri(70, 130), agility: 0, intelligence: 0, endurance: 0 },
  });
}

// ---- 型定義 ----

interface UnitRecord {
  id: string;
  name: string;
  joinYear: number;
  retireYear: number | null;
  joinAge: number;
  peakAge: number;
  maxAge: number;
  baseStrength: number;
  peakStrength: number;
}

interface EventRecord {
  type: "join" | "retire";
  unitId: string;
  unitName: string;
  age: number;
}

interface UnitSnapshot {
  id: string;
  name: string;
  age: number;
  strength: number;
}

interface YearRecord {
  year: number;
  averageStrength: number;
  unitCount: number;
  units: UnitSnapshot[];
  events: EventRecord[];
}

// ---- ユニット台帳 ----

const unitRecords = new Map<string, UnitRecord>();

function registerUnit(u: Unit, joinYear: number) {
  unitRecords.set(u.id, {
    id: u.id,
    name: u.name,
    joinYear,
    retireYear: null,
    joinAge: u.age,
    peakAge: u.peakAge,
    maxAge: u.maxAge,
    baseStrength: u.baseStats.strength,
    peakStrength: u.stats.strength,
  });
}

function updatePeak(u: Unit) {
  const r = unitRecords.get(u.id);
  if (r && u.stats.strength > r.peakStrength) r.peakStrength = u.stats.strength;
}

// ---- 初期ロスター ----

const startingUnits: Unit[] = [];
for (let i = 0; i < ri(5, 8); i++) {
  const u = makeRecruit();
  startingUnits.push(u);
  registerUnit(u, 1);
}

let brigade = new Brigade(startingUnits);
const yearRecords: YearRecord[] = [];
const TOTAL_YEARS = 100;

// ---- 100年シミュレーション ----

for (let y = 1; y <= TOTAL_YEARS; y++) {
  // 年初の戦力ピーク更新
  for (const u of brigade.units) updatePeak(u);

  // 新規入団
  const recruits: Unit[] = [];
  for (let i = 0; i < ri(1, 3); i++) {
    const r = makeRecruit();
    recruits.push(r);
    registerUnit(r, y);
  }

  // 年を進める（既存ユニット加齢・引退処理・新兵追加）
  const { brigade: next, events } = brigade.advance(recruits);

  // 引退年を記録
  for (const e of events) {
    if (e.type === "retire") {
      const rec = unitRecords.get(e.unit.id);
      if (rec) rec.retireYear = y;
    }
  }

  // 年末の戦力ピーク更新
  for (const u of next.units) updatePeak(u);

  yearRecords.push({
    year: y,
    averageStrength: next.averageStrength,
    unitCount: next.units.length,
    units: next.units.map((u) => ({
      id: u.id,
      name: u.name,
      age: u.age,
      strength: u.stats.strength,
    })),
    events: events.map((e) => ({
      type: e.type,
      unitId: e.unit.id,
      unitName: e.unit.name,
      age: e.unit.age,
    })),
  });

  brigade = next;
}

// ---- JSON出力 ----

const now = new Date();
const pad = (n: number, len = 2) => String(n).padStart(len, "0");
const dateStr = `${now.getFullYear()}-${pad(now.getMonth() + 1)}-${pad(now.getDate())}_${pad(now.getHours())}-${pad(now.getMinutes())}-${pad(now.getSeconds())}-${pad(now.getMilliseconds(), 3)}`;

const output = {
  generatedAt: now.toISOString(),
  totalYears: TOTAL_YEARS,
  totalRecruits: unitRecords.size,
  years: yearRecords,
  roster: Object.fromEntries(unitRecords),
};

const outputDir = join(import.meta.dir, "../output", dateStr);
mkdirSync(outputDir, { recursive: true });

const jsonPath = join(outputDir, "history.json");
writeFileSync(jsonPath, JSON.stringify(output, null, 2));

// generate_report.ts が参照できるよう最新ディレクトリ名を記録
writeFileSync(join(import.meta.dir, "../output/.latest"), dateStr);

console.log(`✓ history.json → ${jsonPath}`);
console.log(`  総騎士数: ${unitRecords.size}名`);
console.log(`  シミュレーション期間: ${TOTAL_YEARS}年`);
