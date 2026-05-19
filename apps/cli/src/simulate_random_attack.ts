import { writeFileSync } from "fs";
import { join } from "path";
import { Enemy, EnemyAction } from "../../../packages/core/src/models/Enemy";
import { BattleManager } from "../../../packages/core/src/BattleManager";
import { Squad } from "../../../packages/core/src/models/Squad";
import { Unit } from "../../../packages/core/src/models/Unit";

// ---- 再現性のある PRNG (Mulberry32) ----

function mulberry32(seed: number) {
  let s = seed;
  return function () {
    s = (s + 0x6d2b79f5) | 0;
    let t = Math.imul(s ^ (s >>> 15), 1 | s);
    t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}

const rand = mulberry32(42);

// ---- シミュレーション設定 ----

const SLOT_IDS = ["FRONT", "REAR-L", "REAR-R"];
const UNIT_HP = 80;
const HIT_COUNT = 10;
const DAMAGE_PER_HIT = 15;
const TRIAL_COUNT = 5;

const actionDef: EnemyAction = {
  name: "乱れ撃ち",
  targetSlotIds: "RANDOM",
  hitCount: HIT_COUNT,
  damage: DAMAGE_PER_HIT,
};

const enemy = new Enemy({
  hp: 1000,
  maxHp: 1000,
  speed: 3,
  actions: [actionDef],
});

function makeSquads(): Squad[] {
  return SLOT_IDS.map((slotId) => {
    const unit = new Unit({
      id: `${slotId}-u`,
      name: `${slotId} Guard`,
      age: 25,
      peakAge: 30,
      maxAge: 60,
      baseStats: { strength: 50, agility: 10, intelligence: 0, endurance: 0 },
      maxHp: UNIT_HP,
      hp: UNIT_HP,
      speed: 10,
    });
    return new Squad(slotId, [unit]);
  });
}

// ---- 確率計算ヘルパー ----

function binomCoeff(n: number, k: number): number {
  if (k === 0 || k === n) return 1;
  let r = 1;
  for (let i = 0; i < k; i++) r = (r * (n - i)) / (i + 1);
  return r;
}

function binomialCdf(n: number, minK: number, p: number): number {
  let prob = 0;
  for (let k = minK; k <= n; k++) {
    prob += binomCoeff(n, k) * Math.pow(p, k) * Math.pow(1 - p, n - k);
  }
  return prob;
}

// ---- シミュレーション実行 ----

interface SlotData {
  hits: number;
  damageTaken: number;
  defeated: boolean;
}

interface TrialSummary {
  trial: number;
  slots: Record<string, SlotData>;
}

const trials: TrialSummary[] = [];

for (let t = 0; t < TRIAL_COUNT; t++) {
  const squads = makeSquads();
  const manager = new BattleManager(squads, enemy, rand);
  const result = manager.applyEnemyAction(actionDef);

  trials.push({
    trial: t + 1,
    slots: Object.fromEntries(
      SLOT_IDS.map((id) => [
        id,
        result.perSlot[id] ?? { hits: 0, damageTaken: 0, defeated: false },
      ])
    ),
  });
}

// ---- 集計 ----

const totalHits: Record<string, number> = Object.fromEntries(SLOT_IDS.map((id) => [id, 0]));
const totalDamage: Record<string, number> = Object.fromEntries(SLOT_IDS.map((id) => [id, 0]));
const defeatCount: Record<string, number> = Object.fromEntries(SLOT_IDS.map((id) => [id, 0]));

for (const trial of trials) {
  for (const id of SLOT_IDS) {
    totalHits[id] += trial.slots[id].hits;
    totalDamage[id] += trial.slots[id].damageTaken;
    if (trial.slots[id].defeated) defeatCount[id]++;
  }
}

const totalDefeats = SLOT_IDS.reduce((s, id) => s + defeatCount[id], 0);
const mostTargeted = SLOT_IDS.reduce((a, b) =>
  totalHits[a] >= totalHits[b] ? a : b
);

// 壊滅に必要な最小ヒット数
const hitsToDefeat = Math.ceil(UNIT_HP / DAMAGE_PER_HIT);
const pSlotDefeat = binomialCdf(HIT_COUNT, hitsToDefeat, 1 / SLOT_IDS.length);
const pAnyDefeatPerTrial = 1 - Math.pow(1 - pSlotDefeat, SLOT_IDS.length);
const expectedDefeatsTotal = TRIAL_COUNT * SLOT_IDS.length * pSlotDefeat;

// ---- レポート生成 ----

const L: string[] = [];
const now = new Date();

L.push("# ランダム複数回攻撃 分析レポート");
L.push("");
L.push(`> 生成日時: ${now.toLocaleString("ja-JP")}`);
L.push(`> 乱数シード: 42（Mulberry32）`);
L.push("");
L.push("---");
L.push("");

// ---- 攻撃定義 ----

L.push("## 攻撃定義");
L.push("");
L.push("| 項目 | 値 |");
L.push("|------|----|");
L.push(`| スキル名 | **${actionDef.name}** |`);
L.push(`| ターゲット指定 | RANDOM（全スロット均等抽選） |`);
L.push(`| ヒット数 | **${HIT_COUNT}回** |`);
L.push(`| 1ヒットあたりダメージ | **${DAMAGE_PER_HIT}** |`);
L.push(`| 総ダメージ量（上限） | **${HIT_COUNT * DAMAGE_PER_HIT}**（全弾同一スロット集中時） |`);
L.push(`| 試行回数 | ${TRIAL_COUNT}回 |`);
L.push(`| 分隊数 | ${SLOT_IDS.length}（${SLOT_IDS.join("、")}） |`);
L.push(`| 各分隊 HP | 1体 × ${UNIT_HP} HP |`);
L.push(`| 壊滅閾値 | ${hitsToDefeat}ヒット（${hitsToDefeat * DAMAGE_PER_HIT}ダメージ ＞ ${UNIT_HP} HP） |`);
L.push("");

// ---- 試行結果テーブル ----

L.push("## 試行結果テーブル");
L.push("");
L.push("各試行における各スロットへの命中数・ダメージ（左: ヒット数 / 右: ダメージ）");
L.push("");

const header = ["試行", ...SLOT_IDS.map((id) => `${id}`), "合計ヒット"].join(" | ");
const separator = ["---", ...SLOT_IDS.map(() => "---"), "---"].join(" | ");
L.push(`| ${header} |`);
L.push(`| ${separator} |`);

for (const trial of trials) {
  const totalHitsInTrial = SLOT_IDS.reduce((s, id) => s + trial.slots[id].hits, 0);
  const cells = SLOT_IDS.map((id) => {
    const s = trial.slots[id];
    return `**${s.hits}**ヒット / ${s.damageTaken}dmg`;
  });
  L.push(`| ${trial.trial}回目 | ${cells.join(" | ")} | ${totalHitsInTrial} |`);
}

L.push("");
const totalCells = SLOT_IDS.map(
  (id) => `**${totalHits[id]}**ヒット / ${totalDamage[id]}dmg`
);
L.push(`| **合計** | ${totalCells.join(" | ")} | **${HIT_COUNT * TRIAL_COUNT}** |`);
L.push("");

// ---- 生存分析 ----

L.push("## 生存分析");
L.push("");
L.push("壊滅: 分隊内の全ユニットの HP が 0 に達した状態");
L.push("");

const survivorHeader = ["試行", ...SLOT_IDS, "壊滅数"].join(" | ");
const survivorSep = ["---", ...SLOT_IDS.map(() => "---"), "---"].join(" | ");
L.push(`| ${survivorHeader} |`);
L.push(`| ${survivorSep} |`);

let totalTrialDefeats = 0;
for (const trial of trials) {
  const defeatedInTrial = SLOT_IDS.filter((id) => trial.slots[id].defeated).length;
  totalTrialDefeats += defeatedInTrial;
  const cells = SLOT_IDS.map((id) => {
    const s = trial.slots[id];
    if (s.defeated) return "**壊滅** ⚠️";
    const remaining = UNIT_HP - s.damageTaken;
    return `生存（${remaining} HP）`;
  });
  L.push(`| ${trial.trial}回目 | ${cells.join(" | ")} | ${defeatedInTrial} |`);
}

L.push(`| **合計** | ${SLOT_IDS.map((id) => `${defeatCount[id]}回壊滅`).join(" | ")} | **${totalDefeats}** |`);
L.push("");

if (totalDefeats === 0) {
  L.push("> 今回の5試行では壊滅は発生しなかった。ただし理論上は各試行で約 " +
    `${(pAnyDefeatPerTrial * 100).toFixed(1)}% の確率で壊滅が起こりうる（後述）。`);
} else {
  L.push(`> **${totalDefeats}件**の壊滅が発生した。理論上の期待値は **${expectedDefeatsTotal.toFixed(2)}件**。`);
}
L.push("");

// ---- 統計的考察 ----

L.push("## 統計的考察");
L.push("");

L.push("### ヒット分布の集計");
L.push("");
L.push("| スロット | 総ヒット（5試行） | 平均ヒット／試行 | 理論平均 | 壊滅回数 |");
L.push("|---------|----------------|----------------|--------|--------|");
const theoreticalAvg = (HIT_COUNT / SLOT_IDS.length).toFixed(2);
for (const id of SLOT_IDS) {
  const avg = (totalHits[id] / TRIAL_COUNT).toFixed(2);
  L.push(`| ${id} | ${totalHits[id]} | **${avg}** | ${theoreticalAvg} | ${defeatCount[id]} |`);
}
L.push("");

L.push("### 確率的分析");
L.push("");
L.push("均等ランダム（各スロット選択確率 1/3）に基づく理論値：");
L.push("");
L.push(`- **壊滅必要ヒット数**: ${hitsToDefeat}ヒット（${hitsToDefeat}×${DAMAGE_PER_HIT}=${hitsToDefeat * DAMAGE_PER_HIT} ＞ ${UNIT_HP} HP）`);
L.push(`- **1スロットが壊滅する確率（1試行）**: P(X ≥ ${hitsToDefeat}) = **${(pSlotDefeat * 100).toFixed(2)}%** ※X〜Bin(${HIT_COUNT}, 1/${SLOT_IDS.length})`);
L.push(`- **いずれかのスロットが壊滅する確率（1試行）**: 1 − (1−P)³ = **${(pAnyDefeatPerTrial * 100).toFixed(1)}%**`);
L.push(`- **5試行での壊滅期待件数**: ${TRIAL_COUNT} × ${SLOT_IDS.length} × P = **${expectedDefeatsTotal.toFixed(2)}件**`);
L.push(`- **実際の壊滅件数**: **${totalDefeats}件**`);
L.push("");

L.push("### エンジニア的コメント");
L.push("");

const hitVariances = SLOT_IDS.map((id) => {
  const avg = totalHits[id] / TRIAL_COUNT;
  return { id, avg, diff: Math.abs(avg - HIT_COUNT / SLOT_IDS.length) };
});
const mostBiased = hitVariances.reduce((a, b) => (a.diff > b.diff ? a : b));

L.push(`**偏りの観察**: 最も集中したスロットは **${mostTargeted}**（総 ${totalHits[mostTargeted]} ヒット、平均 ${(totalHits[mostTargeted] / TRIAL_COUNT).toFixed(2)}/試行）。`);
L.push(`理論平均（${theoreticalAvg}）から最大 ${mostBiased.diff.toFixed(2)} ヒット乖離しており、`);
L.push("少数試行では偏りが顕在化しやすいことが確認できる。");
L.push("");
L.push(`**「乱数事故」の発生頻度**: 各試行において約 **${(pAnyDefeatPerTrial * 100).toFixed(1)}%** の確率で壊滅が発生しうる。`);
L.push(`10戦程度のプレイセッションを仮定すると、少なくとも1回は壊滅が起きる確率は`);
const pAtLeastOneIn10 = 1 - Math.pow(1 - pAnyDefeatPerTrial, 10);
L.push(`**${(pAtLeastOneIn10 * 100).toFixed(1)}%** に達する。プレイヤーが「理不尽」と感じる閾値（≥30%）を`);
L.push(pAtLeastOneIn10 >= 0.3
  ? "**超えており**、バランス調整が検討に値する。"
  : "下回っているが、ゲームの難易度設計に応じて再評価すべきである。");
L.push("");
L.push("**緩和策の候補**:");
L.push(`- ヒット数を減らす（例: ${HIT_COUNT}→8）: 壊滅閾値を高める`);
L.push(`- HP を増やす（例: ${UNIT_HP}→100）: ${Math.ceil(100 / DAMAGE_PER_HIT)}ヒット必要になり壊滅確率が約 ${(binomialCdf(HIT_COUNT, Math.ceil(100 / DAMAGE_PER_HIT), 1 / SLOT_IDS.length) * 100).toFixed(2)}% に低下`);
L.push(`- ダメージを下げる（例: ${DAMAGE_PER_HIT}→10）: ${Math.ceil(UNIT_HP / 10)}ヒット必要になり壊滅確率が約 ${(binomialCdf(HIT_COUNT, Math.ceil(UNIT_HP / 10), 1 / SLOT_IDS.length) * 100).toFixed(2)}% に低下`);
L.push("");

// ---- 出力 ----

const reportPath = join(import.meta.dir, "../../../reports/random_attack_analysis.md");
writeFileSync(reportPath, L.join("\n"));

// ---- コンソールサマリー ----

console.log("╔══════════════════════════════════════════════════╗");
console.log("║     ランダム攻撃分析レポート 生成完了           ║");
console.log("╚══════════════════════════════════════════════════╝");
console.log("");
console.log(`  スキル: ${actionDef.name}  |  ヒット数: ${HIT_COUNT}  |  ダメージ/発: ${DAMAGE_PER_HIT}`);
console.log(`  対象: RANDOM（${SLOT_IDS.join(" / ")}）`);
console.log(`  試行回数: ${TRIAL_COUNT}  |  各スロット HP: ${UNIT_HP}  |  乱数シード: 42`);
console.log("");
console.log("┌─────────────────────────────────────────────────────────────┐");
console.log("│ 試行  │  FRONT              │  REAR-L             │  REAR-R             │");
console.log("├─────────────────────────────────────────────────────────────┤");

for (const trial of trials) {
  const cells = SLOT_IDS.map((id) => {
    const s = trial.slots[id];
    const label = s.defeated ? "⚠️  壊滅       " : `${s.hits}hit / ${(UNIT_HP - s.damageTaken).toString().padStart(2)}HP残`;
    return label.padEnd(20);
  });
  console.log(`│  ${trial.trial}回目 │ ${cells.join(" │ ")} │`);
}

console.log("├─────────────────────────────────────────────────────────────┤");
const totalRow = SLOT_IDS.map((id) => {
  const avg = (totalHits[id] / TRIAL_COUNT).toFixed(1);
  return `avg ${avg}hit (${defeatCount[id]}壊滅)`.padEnd(20);
});
console.log(`│  合計 │ ${totalRow.join(" │ ")} │`);
console.log("└─────────────────────────────────────────────────────────────┘");
console.log("");
console.log(`  壊滅件数: ${totalDefeats}件 / 期待値: ${expectedDefeatsTotal.toFixed(2)}件`);
console.log(`  最多被弾スロット: ${mostTargeted}（総${totalHits[mostTargeted]}ヒット）`);
console.log(`  1試行で壊滅が起きる確率: ${(pAnyDefeatPerTrial * 100).toFixed(1)}%`);
console.log("");
console.log(`  レポート出力先: ${reportPath}`);
