---
description: Chronicle Knights のゲーム性・絶対ルール（コード生成時に必須参照）
---

# Game Constitution（ゲーム憲法）

プロジェクトの**永続的な仕様憲法**。詳細は [instructions.md](../../instructions.md) と [TODO.md](../../TODO.md) を参照すること。本スキルは要点のみのクイックリファレンス。

## 0. 最重要原則

**Chronicle Knights は「世代交代型ローグライクRPG」である**。
プレイヤーの「人事の選択」が物語を作る。自動化と数値最適化に逃げてはならない。

## 1. バトル絶対値

| 項目 | 値 | 備考 |
|---|---:|---|
| 大隊サイズ | **9名（3×3）** | 12名は廃止。BATTALION_SIZE=9 / SQUAD_SIZE=3 / FRONT_ROW_COUNT=3 |
| 敵スピード成長 | **+0.6/年** | 旧+1.5は過剰。0.5〜0.8 の範囲で運用 |
| 敵ステータス乱数 | **±15% 振れ幅** | `BASE × (0.85 + rng() × 0.30)`。固定値計算は禁止 |

これらを変更したい場合は **必ず instructions.md を先に更新** し、その理由を commit メッセージに明記する。

## 2. 人事権の絶対ルール

### 2-1. 自動リストラ厳禁

```typescript
// ❌ 本番ループでこれをやってはいけない
brigade = enforceMaxBrigadeSize(brigade, MAX).brigade;
```

`enforceMaxBrigadeSize` 自体は残してよいが、**run-grand-chronicle 系の本番ゲームループから自動呼び出ししてはならない**。

### 2-2. 手動選択フェーズ

新人入団 / 子供雇用 / 老兵引退 はすべて**プレイヤーの手動選択**:

```typescript
// ✅ 推奨
const decisions = await getPendingDecisions(brigade);
const chosen = await presentToPlayer(decisions); // UI で選択
brigade = applyDecisions(brigade, chosen);
```

メタ分析スクリプトは測定用なので `--auto-cull` フラグで自動化を許す（フラグなしは手動想定）。

## 3. 「血統 vs 戦力」の苦渋を残せ

ゲームの**核心的な楽しさ**は次の選択にある:
- **能力が低くても血統DNAを持つ者**を残すか
- **目先の戦力**を取るか

このトレードオフを消すような自動化（例: 「自動で最強の9名を選出して戦闘」）を実装してはならない。
血統情報（parents / spouseId）は人事画面で **必ず表示** する。

## 4. 既存スキルとの関係

| 関連スキル | 内容 |
|---|---|
| [project_conventions](project_conventions.md) | イミュータビリティ・最小値1・乱数DI |
| [chronicle_config](chronicle_config.md) | ハードコード禁止・CHRONICLE_CONFIG 経由 |
| [unit_generation](unit_generation.md) | 性別ランダム・NameGenerator 経由 |
| [commit_conventions](commit_conventions.md) | 日本語コミット |
| [simulation_reporting](simulation_reporting.md) | reports/ に時刻付き保存 |
| **本スキル** | ゲーム憲法（最上位の方針） |

## 5. 矛盾発生時の優先順位

複数スキル間で矛盾があった場合の優先度:

1. **本スキル（game_constitution）** ← 最上位
2. instructions.md（プロジェクト指示書）
3. その他の skill 文書
4. 個別ファイル内コメント

下位ドキュメントに反するコードを書く場合、まず本スキルおよび instructions.md を更新してから着手する。

## 6. NG例

```typescript
// ❌ 大隊12名のまま実装
const picks = brigade.selectBattalion(12);

// ❌ ハードコード
new BattleSimulator(..., { maxTurns: 30 });

// ❌ 自動リストラを本番ループから呼ぶ
for (let y = 1; y <= 100; y++) {
  brigade = enforceMaxBrigadeSize(brigade, 50).brigade;
}

// ❌ 敵ステータスを固定値で計算
const hp = 150 + year * 5;

// ❌ プレイヤー選択を skip して自動で「最強9名」を選出
brigade.selectBattalion(9); // メタ分析用ならOK、本番ループでは要 UI 確認
```

## 7. OK例

```typescript
// ✅ Config 参照、9名、乱数化、手動選択
const picks = brigade.selectBattalion(CHRONICLE_CONFIG.SCHEDULE.BATTALION_SIZE);
const enemy = makeTrialEnemy(year, rng); // 乱数化済み
const decisions = getPendingDecisions(brigade);
// UI でプレイヤーに提示 → 選択結果を受け取る
brigade = applyDecisions(brigade, playerChoice);
```
