import { readFileSync, writeFileSync } from "fs";
import { join } from "path";

// ---- 型定義（history.jsonの構造に対応） ----

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

interface HistoryData {
  generatedAt: string;
  totalYears: number;
  totalRecruits: number;
  years: YearRecord[];
  roster: Record<string, UnitRecord>;
}

// ---- データ読み込み ----

const latestPath = join(import.meta.dir, "../output/.latest");
const dateStr = readFileSync(latestPath, "utf-8").trim();
const outputDir = join(import.meta.dir, "../output", dateStr);
const jsonPath = join(outputDir, "history.json");
const data: HistoryData = JSON.parse(readFileSync(jsonPath, "utf-8"));
const { years, roster, totalYears, totalRecruits } = data;
const units = Object.values(roster);

// ---- ヘルパー ----

function avg(nums: number[]): number {
  if (nums.length === 0) return 0;
  return Math.round((nums.reduce((a, b) => a + b, 0) / nums.length) * 10) / 10;
}

function bar(value: number, max: number, width = 32): string {
  const filled = Math.round((value / max) * width);
  return "█".repeat(filled) + "░".repeat(width - filled);
}

function serviceYears(u: UnitRecord): number {
  return (u.retireYear ?? totalYears) - u.joinYear + 1;
}

function retireAge(u: UnitRecord): number {
  return u.joinAge + ((u.retireYear ?? totalYears) - u.joinYear);
}

// ---- レポート生成 ----

const L: string[] = [];

// ==============================
// ヘッダー
// ==============================
L.push("# Chronicle Knights — 騎士団100年史");
L.push("");
L.push(`> 生成日時: ${data.generatedAt}`);
L.push("");
L.push("---");
L.push("");

// ==============================
// 序章：概要
// ==============================
L.push("## 序章：騎士団の概要");
L.push("");

const allAvgStr   = years.map((y) => y.averageStrength);
const maxAvgStr   = Math.max(...allAvgStr);
const minAvgStr   = Math.min(...allAvgStr);
const peakYear    = years.find((y) => y.averageStrength === maxAvgStr)!.year;
const valleyYear  = years.find((y) => y.averageStrength === minAvgStr)!.year;
const overallAvg  = avg(allAvgStr);
const retiredCount = units.filter((u) => u.retireYear !== null).length;
const activeCount  = units.filter((u) => u.retireYear === null).length;

L.push("| 項目 | 値 |");
L.push("|------|-----|");
L.push(`| 総在籍騎士数 | **${totalRecruits}名** |`);
L.push(`| シミュレーション期間 | ${totalYears}年 |`);
L.push(`| 100年通算平均戦力 | **${overallAvg}** |`);
L.push(`| 戦力最高値（平均） | **${maxAvgStr}**（第${peakYear}年） |`);
L.push(`| 戦力最低値（平均） | ${minAvgStr}（第${valleyYear}年） |`);
L.push(`| 引退騎士数 | ${retiredCount}名 |`);
L.push(`| 100年後も現役 | ${activeCount}名 |`);
L.push("");

// ==============================
// 第一章：戦力の歴史的推移
// ==============================
L.push("## 第一章：戦力の歴史的推移");
L.push("");
L.push("5年ごとの平均戦力の推移：");
L.push("");
L.push("| 年 | 平均戦力 | 騎士数 | チャート |");
L.push("|---:|---------:|------:|---------|");

for (let y = 5; y <= 100; y += 5) {
  const rec = years.find((r) => r.year === y)!;
  L.push(
    `| ${String(y).padStart(3)} | ${rec.averageStrength.toFixed(1)} | ${rec.unitCount}名 | \`${bar(rec.averageStrength, maxAvgStr)}\` |`
  );
}
L.push("");

// ==============================
// 第二章：年代記（10年ごと）
// ==============================
L.push("## 第二章：年代記（10年ごと）");
L.push("");

for (let decade = 0; decade < 10; decade++) {
  const startY = decade * 10 + 1;
  const endY   = startY + 9;
  const dYears = years.filter((y) => y.year >= startY && y.year <= endY);

  const dAvg     = avg(dYears.map((y) => y.averageStrength));
  const joins    = dYears.flatMap((y) => y.events.filter((e) => e.type === "join"));
  const retires  = dYears.flatMap((y) => y.events.filter((e) => e.type === "retire"));

  // その年代で最も高い瞬間戦力を持ったユニット
  let heroSnap: { name: string; strength: number; year: number } | null = null;
  for (const yr of dYears) {
    for (const u of yr.units) {
      if (!heroSnap || u.strength > heroSnap.strength) {
        heroSnap = { name: u.name, strength: u.strength, year: yr.year };
      }
    }
  }

  // 最多引退年
  const retireByYear = new Map<number, number>();
  for (const yr of dYears) {
    const cnt = yr.events.filter((e) => e.type === "retire").length;
    if (cnt > 0) retireByYear.set(yr.year, cnt);
  }
  const peakRetireEntry = [...retireByYear.entries()].sort((a, b) => b[1] - a[1])[0];

  L.push(`### 第${decade + 1}年代（${startY}〜${endY}年）`);
  L.push("");
  L.push(`- **期間平均戦力**: ${dAvg}`);
  L.push(`- **入団**: ${joins.length}名　**引退**: ${retires.length}名`);
  if (heroSnap) {
    L.push(`- **最強記録**: ${heroSnap.name}（第${heroSnap.year}年 / 戦力 ${heroSnap.strength}）`);
  }
  if (peakRetireEntry) {
    L.push(`- **最多引退**: 第${peakRetireEntry[0]}年に${peakRetireEntry[1]}名が同時引退`);
  }
  L.push("");
}

// ==============================
// 第三章：殿堂入り騎士
// ==============================
L.push("## 第三章：殿堂入り騎士");
L.push("");

// Top 5 最高戦力
L.push("### 最強の騎士 Top 5（歴代最高戦力）");
L.push("");
L.push("| 順位 | 名前 | 最高戦力 | 在籍期間 | ピーク年齢 |");
L.push("|-----:|------|--------:|---------|----------:|");
[...units]
  .sort((a, b) => b.peakStrength - a.peakStrength)
  .slice(0, 5)
  .forEach((u, i) => {
    const period = `第${u.joinYear}〜${u.retireYear ?? "現役"}年`;
    L.push(`| ${i + 1} | **${u.name}** | ${u.peakStrength} | ${period} | ${u.peakAge}歳 |`);
  });
L.push("");

// Top 5 最長在籍
L.push("### 最長在籍 Top 5（騎士団への貢献）");
L.push("");
L.push("| 順位 | 名前 | 在籍年数 | 入団年 | 引退年 | 入団時年齢 |");
L.push("|-----:|------|--------:|------:|------:|---------:|");
[...units]
  .sort((a, b) => serviceYears(b) - serviceYears(a))
  .slice(0, 5)
  .forEach((u, i) => {
    const retire = u.retireYear ? `第${u.retireYear}年` : "現役";
    L.push(
      `| ${i + 1} | **${u.name}** | ${serviceYears(u)}年 | 第${u.joinYear}年 | ${retire} | ${u.joinAge}歳 |`
    );
  });
L.push("");

// Top 5 最長命（引退時年齢）
L.push("### 最長命 Top 5（引退時の年齢）");
L.push("");
L.push("| 順位 | 名前 | 引退時年齢 | 在籍期間 | 最高戦力 |");
L.push("|-----:|------|----------:|---------|--------:|");
[...units]
  .sort((a, b) => retireAge(b) - retireAge(a))
  .slice(0, 5)
  .forEach((u, i) => {
    const period = `第${u.joinYear}〜${u.retireYear ?? "現役"}年`;
    L.push(`| ${i + 1} | **${u.name}** | ${retireAge(u)}歳 | ${period} | ${u.peakStrength} |`);
  });
L.push("");

// ==============================
// 第四章：統計
// ==============================
L.push("## 第四章：統計");
L.push("");

const avgSvc      = avg(units.map(serviceYears));
const avgRetAge   = avg(units.filter((u) => u.retireYear !== null).map(retireAge));
const avgJoinAge  = avg(units.map((u) => u.joinAge));
const highStrCount = units.filter((u) => u.peakStrength >= 100).length;
const maxPeakStr  = Math.max(...units.map((u) => u.peakStrength));

L.push("| 統計項目 | 値 |");
L.push("|---------|-----|");
L.push(`| 合計入団者数 | **${totalRecruits}名** |`);
L.push(`| 引退者数 | ${retiredCount}名 |`);
L.push(`| 100年後も現役 | ${activeCount}名 |`);
L.push(`| 平均在籍年数 | ${avgSvc}年 |`);
L.push(`| 平均引退年齢 | ${avgRetAge}歳 |`);
L.push(`| 平均入団年齢 | ${avgJoinAge}歳 |`);
L.push(`| 戦力100超えを記録した騎士 | ${highStrCount}名 |`);
L.push(`| 歴代最高瞬間戦力 | ${maxPeakStr} |`);
L.push(`| 戦死者数 | 0名（将来実装予定） |`);
L.push("");

// ==============================
// 出力
// ==============================

const reportPath = join(outputDir, "report.md");
writeFileSync(reportPath, L.join("\n"));
console.log(`✓ report.md → ${reportPath}`);
