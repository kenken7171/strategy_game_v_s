import { writeFileSync } from "fs";
import { join } from "path";
import { Unit } from "../../../packages/core/src/models/Unit";
import { Squad } from "../../../packages/core/src/models/Squad";
import { Enemy } from "../../../packages/core/src/models/Enemy";
import { BattleManager, IntegratedTurnResult } from "../../../packages/core/src/BattleManager";

// ---- ユニット定義 ----

const HEAVY_KNIGHT = {
  id: "heavy_knight",
  name: "重騎士",
  age: 25,
  peakAge: 30,
  maxAge: 60,
  baseStats: { strength: 80, agility: 20, intelligence: 0, endurance: 100 },
  maxHp: 200,
  hp: 200,
  speed: 20,
  frontAttack: 60,
  rearAttack: 10,
};

const ARCHER = {
  id: "archer",
  name: "弓使い",
  age: 22,
  peakAge: 28,
  maxAge: 55,
  baseStats: { strength: 30, agility: 70, intelligence: 50, endurance: 40 },
  maxHp: 120,
  hp: 120,
  speed: 50,
  frontAttack: 10,
  rearAttack: 80,
};

// ---- 敵定義 ----
// speed=30: 弓使い(50) > 敵(30) > 重騎士(20) の順でアクション

const ENEMY = new Enemy({
  hp: 500,
  maxHp: 500,
  speed: 30,
  actions: [
    { name: "斬撃", targetSlotIds: ["FRONT"], damage: 40, hitCount: 1 },
  ],
});

// ---- シナリオ実行 ----

interface ScenarioResult {
  label: string;
  frontUnitName: string;
  rearUnitName: string;
  result: IntegratedTurnResult;
  logs: string[];
}

function runScenario(
  label: string,
  frontUnitProps: typeof HEAVY_KNIGHT,
  rearUnitProps: typeof ARCHER
): ScenarioResult {
  const frontUnit = new Unit(frontUnitProps);
  const rearUnit = new Unit(rearUnitProps);

  const frontSquad = new Squad("FRONT", [frontUnit]);
  const rearSquad = new Squad("REAR-L", [rearUnit]);

  const manager = new BattleManager([frontSquad, rearSquad], ENEMY);
  const result = manager.processIntegratedTurn();

  const logs: string[] = [];
  for (const offenseResult of result.squadOffenseResults) {
    for (const log of offenseResult.attackLogs) {
      logs.push(
        `${log.unitName}の攻撃！ [${log.slotId}]から${log.damageDealt}ダメージ`
      );
    }
  }

  return { label, frontUnitName: frontUnitProps.name, rearUnitName: rearUnitProps.name, result, logs };
}

const scenarioA = runScenario("シナリオA：重騎士FRONT・弓使いREAR-L", HEAVY_KNIGHT, ARCHER);
const scenarioB = runScenario("シナリオB：弓使いFRONT・重騎士REAR-L", ARCHER, HEAVY_KNIGHT);

// ---- コンソール出力 ----

function printScenario(s: ScenarioResult): void {
  console.log(`\n【${s.label}】`);
  console.log(`  配置: FRONT=${s.frontUnitName}, REAR-L=${s.rearUnitName}`);
  console.log(`  行動順:`);
  for (const entry of s.result.initiativeOrder) {
    const label = entry.type === "enemy" ? "  敵" : `  ${entry.id}スロット`;
    console.log(`    ${label} (speed: ${entry.speed})`);
  }
  console.log(`  攻撃ログ:`);
  for (const log of s.logs) {
    console.log(`    ${log}`);
  }
  const totalDmg = s.result.squadOffenseResults.reduce((sum, r) => sum + r.totalDamage, 0);
  console.log(`  → 1ターン敵への総ダメージ: ${totalDmg}`);
  console.log(`  → 勝利: ${s.result.victory ? "はい" : "いいえ"}`);
}

console.log("╔════════════════════════════════════════════════════════╗");
console.log("║   攻撃フェーズ・配置依存火力システム 検証シミュレーション ║");
console.log("╚════════════════════════════════════════════════════════╝");
printScenario(scenarioA);
printScenario(scenarioB);

const totalA = scenarioA.result.squadOffenseResults.reduce((s, r) => s + r.totalDamage, 0);
const totalB = scenarioB.result.squadOffenseResults.reduce((s, r) => s + r.totalDamage, 0);
console.log(`\n▶ ダメージ比較: シナリオA=${totalA} / シナリオB=${totalB} （差: ${totalA - totalB}）`);

// ---- レポート生成 ----

function formatInitiative(result: IntegratedTurnResult, frontName: string, rearName: string): string {
  return result.initiativeOrder
    .map((e) => {
      if (e.type === "enemy") return `敵（speed:${e.speed}）`;
      const unitName = e.id === "FRONT" ? frontName : rearName;
      return `${unitName}・${e.id}スロット（speed:${e.speed}）`;
    })
    .join(" → ");
}

function buildAttackTable(s: ScenarioResult): string[] {
  const rows: string[] = [];
  rows.push("| ユニット名 | スロット | 攻撃力 | 敵への実ダメージ |");
  rows.push("|-----------|---------|-------|--------------|");
  for (const offenseResult of s.result.squadOffenseResults) {
    for (const log of offenseResult.attackLogs) {
      rows.push(
        `| **${log.unitName}** | ${log.slotId} | ${log.attackPower} | ${log.damageDealt} |`
      );
    }
  }
  return rows;
}

const L: string[] = [];
const now = new Date();

L.push("# 攻撃フェーズ統合検証レポート");
L.push("");
L.push(`> 生成日時: ${now.toLocaleString("ja-JP")}`);
L.push("");
L.push("---");
L.push("");

// ユニットスペック
L.push("## ユニットスペック");
L.push("");
L.push("| ユニット | frontAttack | rearAttack | speed | HP |");
L.push("|---------|------------|-----------|-------|-----|");
L.push(`| **重騎士** | ${HEAVY_KNIGHT.frontAttack} | ${HEAVY_KNIGHT.rearAttack} | ${HEAVY_KNIGHT.speed} | ${HEAVY_KNIGHT.maxHp} |`);
L.push(`| **弓使い** | ${ARCHER.frontAttack} | ${ARCHER.rearAttack} | ${ARCHER.speed} | ${ARCHER.maxHp} |`);
L.push("");

L.push("### 敵スペック");
L.push("");
L.push(`| HP | speed | アクション |`);
L.push(`|-----|-------|-----------|`);
L.push(`| ${ENEMY.hp} | ${ENEMY.speed} | 斬撃（FRONTへ40ダメージ） |`);
L.push("");
L.push("---");
L.push("");

// シナリオ説明
L.push("## シナリオ設定");
L.push("");
L.push("| シナリオ | FRONTスロット | REAR-Lスロット |");
L.push("|---------|------------|-------------|");
L.push(`| **シナリオA** | 重騎士（FA:${HEAVY_KNIGHT.frontAttack}, speed:${HEAVY_KNIGHT.speed}） | 弓使い（RA:${ARCHER.rearAttack}, speed:${ARCHER.speed}） |`);
L.push(`| **シナリオB** | 弓使い（FA:${ARCHER.frontAttack}, speed:${ARCHER.speed}） | 重騎士（RA:${HEAVY_KNIGHT.rearAttack}, speed:${HEAVY_KNIGHT.speed}） |`);
L.push("");
L.push("---");
L.push("");

// シナリオA詳細
L.push("## シナリオA：重騎士FRONT・弓使いREAR-L（最適配置）");
L.push("");
L.push("### 行動順（イニシアチブ）");
L.push("");
L.push(formatInitiative(scenarioA.result, scenarioA.frontUnitName, scenarioA.rearUnitName));
L.push("");
L.push("**解説**: 弓使い(speed:50) > 敵(speed:30) > 重騎士(speed:20)");
L.push("弓使いが敵より先行動。重騎士は敵の攻撃後に行動。");
L.push("");
L.push("### 攻撃ログ");
L.push("");
buildAttackTable(scenarioA).forEach((row) => L.push(row));
L.push("");
L.push(`**1ターン敵への総ダメージ: ${totalA}**`);
L.push("");
L.push("- 弓使い（REAR-L）: `rearAttack = 80` を使用");
L.push("- 重騎士（FRONT）: `frontAttack = 60` を使用");
L.push(`- 合計: 80 + 60 = **${totalA}**`);
L.push("");
L.push("---");
L.push("");

// シナリオB詳細
L.push("## シナリオB：弓使いFRONT・重騎士REAR-L（逆転配置）");
L.push("");
L.push("### 行動順（イニシアチブ）");
L.push("");
L.push(formatInitiative(scenarioB.result, scenarioB.frontUnitName, scenarioB.rearUnitName));
L.push("");
L.push("**解説**: 弓使い(speed:50) > 敵(speed:30) > 重騎士(speed:20)");
L.push("イニシアチブ順序はシナリオAと同じだが、スロット割り当てが逆になっている。");
L.push("");
L.push("### 攻撃ログ");
L.push("");
buildAttackTable(scenarioB).forEach((row) => L.push(row));
L.push("");
L.push(`**1ターン敵への総ダメージ: ${totalB}**`);
L.push("");
L.push("- 弓使い（FRONT）: `frontAttack = 10` を使用（本来の強みを発揮できない）");
L.push("- 重騎士（REAR-L）: `rearAttack = 10` を使用（本来の強みを発揮できない）");
L.push(`- 合計: 10 + 10 = **${totalB}**`);
L.push("");
L.push("---");
L.push("");

// 比較表
L.push("## 比較レポート");
L.push("");
L.push("### 1ターン総ダメージ比較");
L.push("");
L.push("| 指標 | シナリオA（最適） | シナリオB（逆転） | 差分 |");
L.push("|------|--------------|--------------|------|");
L.push(`| 敵への総ダメージ | **${totalA}** | **${totalB}** | +${totalA - totalB}（${((totalA / totalB) * 100).toFixed(0)}%） |`);
L.push(`| 弓使いの火力 | 80（REAR rearAttack） | 10（FRONT frontAttack） | −70 |`);
L.push(`| 重騎士の火力 | 60（FRONT frontAttack） | 10（REAR rearAttack） | −50 |`);
L.push("");

L.push("### 行動順（イニシアチブ）比較");
L.push("");
L.push("| 行動順 | シナリオA | シナリオB |");
L.push("|-------|---------|---------|");
L.push(`| 1番手 | 弓使い・REAR-Lスロット（speed:${ARCHER.speed}） | 弓使い・FRONTスロット（speed:${ARCHER.speed}） |`);
L.push(`| 2番手 | 敵（speed:${ENEMY.speed}） | 敵（speed:${ENEMY.speed}） |`);
L.push(`| 3番手 | 重騎士・FRONTスロット（speed:${HEAVY_KNIGHT.speed}） | 重騎士・REAR-Lスロット（speed:${HEAVY_KNIGHT.speed}） |`);
L.push("");
L.push("イニシアチブ順序は**配置に依らず同一**。速度はユニット個人の `speed` パラメータによって決まるため、");
L.push("どちらのシナリオでも弓使い→敵→重騎士の順で行動する。");
L.push("");
L.push("---");
L.push("");

// 考察
L.push("## 考察");
L.push("");
L.push("### 配置依存火力システムの影響");
L.push("");
L.push("配置を逆転させると1ターンの総ダメージが **140 → 20** に低下する（**86%減**）。");
L.push("");
L.push("| ユニット | 最適配置での火力 | 逆転配置での火力 | 差 |");
L.push("|---------|--------------|--------------|-----|");
L.push(`| 重騎士 | frontAttack=**60**（FRONT） | rearAttack=**10**（REAR-L） | −50 |`);
L.push(`| 弓使い | rearAttack=**80**（REAR-L） | frontAttack=**10**（FRONT） | −70 |`);
L.push("");
L.push("重騎士は前線で盾を構えながら近接斬撃を叩き込む「前衛特化型」、");
L.push("弓使いは後方から安全に狙撃する「後衛特化型」であり、");
L.push("それぞれを適切なスロットに配置することで**最大火力を引き出せる**。");
L.push("");
L.push("### イニシアチブと配置の独立性");
L.push("");
L.push("行動順（素早さベース）は **ユニット自体の `speed`** で決まり、配置スロットには影響されない。");
L.push("そのため「どの配置でも弓使いが先行動」という事実は変わらないが、");
L.push("その行動で**どのAttackパラメータを使うか**がスロットによって切り替わる。");
L.push("");
L.push("### 戦術的示唆");
L.push("");
L.push("- 高 `speed` ユニットを後衛に置いても先行動のアドバンテージは失われない");
L.push("- 配置はイニシアチブではなく **火力の質**（frontAttack vs rearAttack）に直結する");
L.push("- 「弓使いをFRONTに置いて盾代わりにする」戦術は耐久面ではあり得るが、DPSが1/8に激減するコストを伴う");
L.push("");
L.push("---");
L.push("");
L.push("## まとめ");
L.push("");
L.push(`最適配置（重騎士FRONT・弓使いREAR-L）は逆転配置と比べて **${totalA - totalB}ダメージ（${((totalA / totalB) * 100 - 100).toFixed(0)}%増）** の火力差を生む。`);
L.push("行動順は配置に依存せず速度のみで決まるため、イニシアチブを確保しつつ最大火力を発揮するには、");
L.push("**各ユニットのfrontAttack/rearAttack特性に合わせたスロット選択**が不可欠である。");
L.push("");

const reportPath = join(import.meta.dir, "../../../reports/battle_offense_integrated.md");
writeFileSync(reportPath, L.join("\n"));
console.log(`\nレポート: ${reportPath}`);
