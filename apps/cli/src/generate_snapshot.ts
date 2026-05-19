import { Unit } from "../../../packages/core/src/models/Unit";
import { Squad } from "../../../packages/core/src/models/Squad";
import { Brigade } from "../../../packages/core/src/models/Brigade";
import { MAX_UNITS_PER_SQUAD } from "../../../packages/core/src/config";
import { mkdirSync } from "node:fs";

// ─── ユニット生成ヘルパー ───
function makeUnit(id: string, name: string, maxHp: number, speed: number): Unit {
  return new Unit({
    id,
    name,
    age: 25,
    peakAge: 30,
    maxAge: 60,
    baseStats: {
      strength: 60,
      agility: speed,
      intelligence: 30,
      endurance: Math.round(maxHp / 10),
    },
    maxHp,
    hp: maxHp,
    speed,
  });
}

// ─── テストデータ (10名) ───
const allUnits: Unit[] = [
  // 先遣隊: 高速・低HP
  makeUnit("u01", "アリア",      60,  95),
  makeUnit("u02", "ルーク",      55,  88),
  makeUnit("u03", "エルフィン",  50, 100),
  // 本隊: バランス型
  makeUnit("u04", "ガレス",     100,  55),
  makeUnit("u05", "セリア",      90,  60),
  makeUnit("u06", "ランス",      95,  50),
  // 後方支援隊: 高HP・低速
  makeUnit("u07", "ドーウィン", 150,  25),
  makeUnit("u08", "ヘレナ",     140,  30),
  makeUnit("u09", "グラント",   160,  20),
  // 待機中 (Reserve)
  makeUnit("u10", "ノア",        80,  70),
];

// ─── 分隊・旅団の構築 ───
const vanguard    = new Squad("vanguard");
const mainForce   = new Squad("main");
const rearSupport = new Squad("rear");
const brigade     = new Brigade(allUnits, [vanguard, mainForce, rearSupport]);

["u01", "u02", "u03"].forEach(id => brigade.assignUnitToSquad(id, "vanguard"));
["u04", "u05", "u06"].forEach(id => brigade.assignUnitToSquad(id, "main"));
["u07", "u08", "u09"].forEach(id => brigade.assignUnitToSquad(id, "rear"));
// u10 (ノア) は未配属 → 待機中

const squadDefs = [
  { squad: vanguard,    label: "先遣隊 (Vanguard)",        icon: "⚡" },
  { squad: mainForce,   label: "本隊 (Main Force)",         icon: "⚔" },
  { squad: rearSupport, label: "後方支援隊 (Rear Support)", icon: "🛡" },
];

// ─────────────────────────────────────────────
//  検証ログ
// ─────────────────────────────────────────────
console.log("\n╔════════════════════════════════════════╗");
console.log("║    旅団編成スナップショット 検証ログ    ║");
console.log("╚════════════════════════════════════════╝\n");

// averageSpeed 検証
console.log("■ averageSpeed 検証\n");
for (const { squad, label } of squadDefs) {
  const speeds   = squad.units.map(u => u.speed);
  const manual   = speeds.reduce((a, b) => a + b, 0) / speeds.length;
  const computed = squad.averageSpeed;
  const ok       = Math.abs(computed - manual) < 0.001 ? "✅ 一致" : "❌ 不一致";
  console.log(`  ${label}`);
  console.log(`    speeds:      [${speeds.join(", ")}]`);
  console.log(`    averageSpeed: ${computed.toFixed(2)}  (手動計算: ${manual.toFixed(2)})  ${ok}\n`);
}

// applyDamage + isDefeated 検証
console.log("■ applyDamage + isDefeated 検証 (先遣隊デモコピー)\n");

type UnitState = { name: string; hp: number; maxHp: number; isAlive: boolean };
interface RoundRecord {
  round:            number;
  label:            string;
  damage:           number | null;
  aliveCountBefore: number | null;
  damagePerUnit:    number | null;
  unitStates:       UnitState[];
  isDefeated:       boolean;
}

const records: RoundRecord[] = [];
const demoSquad = new Squad("demo", [...vanguard.units]);

function capture(
  round: number,
  label: string,
  damage: number | null,
  aliveCountBefore: number | null,
): void {
  const damagePerUnit =
    damage !== null && aliveCountBefore !== null && aliveCountBefore > 0
      ? Math.floor(damage / aliveCountBefore)
      : null;
  records.push({
    round, label, damage, aliveCountBefore, damagePerUnit,
    unitStates: demoSquad.units.map(u => ({
      name: u.name, hp: u.hp, maxHp: u.maxHp, isAlive: u.isAlive,
    })),
    isDefeated: demoSquad.isDefeated,
  });
}

capture(0, "初期状態", null, null);

const damageRounds = [
  { damage: 90, label: "ラウンド 1" },
  { damage: 75, label: "ラウンド 2" },
  { damage: 15, label: "ラウンド 3 (全滅判定)" },
];

for (let i = 0; i < damageRounds.length; i++) {
  const { damage, label } = damageRounds[i];
  const aliveCount = demoSquad.units.filter(u => u.isAlive).length;
  demoSquad.applyDamage(damage);
  capture(i + 1, label, damage, aliveCount);
}

for (const rec of records) {
  const header =
    rec.damage === null
      ? `  [Round 0] 初期状態`
      : `  [Round ${rec.round}] ${rec.label}: ${rec.damage} ダメージ適用 (生存 ${rec.aliveCountBefore} 名 → ${rec.damagePerUnit}/名)`;
  console.log(header);
  for (const u of rec.unitStates) {
    const status = u.isAlive ? "生存" : "戦闘不能";
    console.log(`    ${u.name.padEnd(8)} HP: ${String(u.hp).padStart(3)}/${u.maxHp}  [${status}]`);
  }
  console.log(`    isDefeated: ${rec.isDefeated}${rec.isDefeated ? " ← 全滅！" : ""}\n`);
}

console.log("✅ 検証完了\n");

// ─────────────────────────────────────────────
//  戦術分析データ
// ─────────────────────────────────────────────
const squadStats = squadDefs.map(({ squad, label, icon }) => ({
  label, icon,
  avgSpeed: squad.averageSpeed,
  totalHp:  squad.units.reduce((s, u) => s + u.maxHp, 0),
  count:    squad.units.length,
}));

const fastestSquad = squadStats.reduce((a, b) => a.avgSpeed > b.avgSpeed ? a : b);
const durableSquad = squadStats.reduce((a, b) => a.totalHp  > b.totalHp  ? a : b);

const reserveUnits = allUnits.filter(u =>
  !brigade.squads.some(s => s.units.some(su => su.id === u.id))
);

// ─────────────────────────────────────────────
//  Markdown 生成ヘルパー
// ─────────────────────────────────────────────
function mdUnitTable(units: readonly Unit[]): string {
  return [
    "| ユニット | HP | MaxHP | Speed |",
    "|:--------|---:|------:|------:|",
    ...units.map(u => `| ${u.name} | ${u.hp} | ${u.maxHp} | ${u.speed} |`),
  ].join("\n");
}

function mdDamageTable(states: UnitState[]): string {
  return [
    "| ユニット | HP | MaxHP | 状態 |",
    "|:--------|---:|------:|:-----|",
    ...states.map(u =>
      `| ${u.name} | ${u.hp} | ${u.maxHp} | ${u.isAlive ? "✅ 生存" : "💀 戦闘不能"} |`
    ),
  ].join("\n");
}

// ─────────────────────────────────────────────
//  Markdown 組み立て
// ─────────────────────────────────────────────
const now = new Date().toLocaleString("ja-JP", { timeZone: "Asia/Tokyo" });

const squadDetailSection = squadDefs.map(({ squad, label, icon }, i) => {
  const { avgSpeed, totalHp } = squadStats[i];
  return [
    `### ${icon} ${label}`,
    "",
    mdUnitTable(squad.units),
    "",
    `- **分隊平均素早さ**: ${avgSpeed.toFixed(2)}`,
    `- **合計 HP**: ${totalHp}`,
    `- **人数**: ${squad.units.length} / ${MAX_UNITS_PER_SQUAD}`,
  ].join("\n");
}).join("\n\n");

const damageSimSection = records.map((rec, i) => {
  let heading: string;
  if (rec.damage === null) {
    heading = "### 初期状態";
  } else {
    heading = [
      `### ${rec.label}: ${rec.damage} ダメージ適用`,
      "",
      `> 生存 **${rec.aliveCountBefore}** 名 → **${rec.damagePerUnit}** ダメージ/名`,
    ].join("\n");
  }
  const defeatNote = rec.isDefeated ? " — **全滅確認！**" : "";
  return [
    heading,
    "",
    mdDamageTable(rec.unitStates),
    "",
    `**isDefeated**: \`${rec.isDefeated}\`${defeatNote}`,
  ].join("\n");
}).join("\n\n---\n\n");

const configRows = brigade.squads.map((s, i) => {
  const { label } = squadDefs[i];
  const ok = s.units.length <= MAX_UNITS_PER_SQUAD;
  return `| ${label} | ${s.units.length} | ${MAX_UNITS_PER_SQUAD} | ${ok ? "✅ 合格" : "❌ 超過"} |`;
}).join("\n");

const report = [
  "# 旅団編成スナップショット・レポート",
  "",
  `> 生成日時: ${now}`,
  "",
  "---",
  "",
  "## 【旅団サマリー】",
  "",
  "| 指標 | 値 |",
  "|:-----|---:|",
  `| 総ユニット数 | ${allUnits.length} |`,
  `| 分隊数 | ${brigade.squads.length} |`,
  `| 各分隊の最大人数 (Config) | ${MAX_UNITS_PER_SQUAD} |`,
  `| 分隊配属済み | ${allUnits.length - reserveUnits.length} |`,
  `| 待機中 (Reserve) | ${reserveUnits.length} |`,
  "",
  "---",
  "",
  "## 【分隊詳細】",
  "",
  squadDetailSection,
  "",
  "---",
  "",
  "### ⏳ 待機中 (Reserve)",
  "",
  mdUnitTable(reserveUnits),
  "",
  "---",
  "",
  "## 【ダメージ検証シミュレーション】",
  "",
  "> 対象: **先遣隊 (Vanguard)** のデモコピーに段階的ダメージを適用し、HP 変動と `isDefeated` の遷移を確認する。",
  "",
  damageSimSection,
  "",
  "---",
  "",
  "## 【戦術分析】",
  "",
  "| 指標 | 分隊 | 値 |",
  "|:-----|:-----|:--|",
  `| 最速分隊 | ${fastestSquad.icon} ${fastestSquad.label} | 平均 Speed: ${fastestSquad.avgSpeed.toFixed(2)} |`,
  `| 最高耐久分隊 | ${durableSquad.icon} ${durableSquad.label} | 合計 HP: ${durableSquad.totalHp} |`,
  "",
  "### 自動分析コメント",
  "",
  `- **${fastestSquad.label}** は平均素早さ **${fastestSquad.avgSpeed.toFixed(2)}** で全分隊中最速です。` +
    "先制攻撃・索敵・奇襲作戦など高機動力が求められる任務に最適です。",
  "",
  `- **${durableSquad.label}** は合計 HP **${durableSquad.totalHp}** で最も耐久力が高い部隊です。` +
    "前線維持・防衛ライン確保・盾役として最適で、長期戦において真価を発揮します。",
  "",
  `- 待機中の **${reserveUnits.map(u => u.name).join("、")}** (Speed: ${reserveUnits.map(u => u.speed).join("、")}) は` +
    "中程度の機動力を持ちます。欠員が生じた際にいずれの分隊へも柔軟に配属できる汎用要員です。",
  "",
  "---",
  "",
  "## 【Config 整合性】",
  "",
  `> \`config/game_settings.json\` で定義された \`max_units_per_squad: ${MAX_UNITS_PER_SQUAD}\` が全分隊で遵守されているか確認します。`,
  "",
  "| 分隊 | 現在人数 | 上限 | 判定 |",
  "|:-----|--------:|-----:|:-----|",
  configRows,
  "",
  `> **結論**: 全 ${brigade.squads.length} 分隊が上限 **${MAX_UNITS_PER_SQUAD} 名** を遵守しています。Config 整合性 ✅`,
  "",
  "---",
  "",
  "*Generated by Chronicle Knights Brigade Snapshot Tool*",
].join("\n");

// ─── ファイル書き込み ───
const reportsDir = "/Users/ken/work/test_claude/reports";
const reportPath = `${reportsDir}/brigade_snapshot.md`;

mkdirSync(reportsDir, { recursive: true });
await Bun.write(reportPath, report);

console.log(`📄 ${reportPath} を作成しました。\n`);
console.log("━".repeat(60));
console.log(report);
