import { Unit } from "../../../packages/core/src/index";

const leon = new Unit({
  id: "unit-001",
  name: "Leon",
  age: 15,
  peakAge: 30,
  maxAge: 60,
  baseStats: {
    strength: 100,
    agility: 0,
    intelligence: 0,
    endurance: 0,
  },
});

// 15〜60歳の全スナップショットを収集
const history: Unit[] = [];
let current = leon;
while (!current.isRetired) {
  history.push(current);
  current = current.grow();
}

// --- console.table 用データ ---
const tableData = history.map((u) => ({
  age: u.age,
  strength: u.stats.strength,
  growthFactor: u.growthFactor.toFixed(4),
}));
console.log("\n=== Leon's Life Stats ===\n");
console.table(tableData);

// --- 横棒グラフ ---
const maxStrength = Math.max(...history.map((u) => u.stats.strength));
const BAR_WIDTH = 40;

console.log("\n=== Strength Chart ===\n");
for (const u of history) {
  const barLen = Math.round((u.stats.strength / maxStrength) * BAR_WIDTH);
  const bar = "*".repeat(barLen);
  const age = String(u.age).padStart(2);
  const str = String(u.stats.strength).padStart(3);
  console.log(`Age ${age} [${str}] ${bar}`);
}
console.log();
