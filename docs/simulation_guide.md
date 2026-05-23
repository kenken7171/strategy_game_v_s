# Simulation Guide

## 前提

ランタイムは **Bun** を使用する。すべてのスクリプトは `bun` コマンドで実行する。

---

## scripts/run-sim.ts — 大隊間バトルシミュレーター

大隊プリセット2つを対戦させ、詳細ターンログと統計レポートを出力する。

### 基本実行

```bash
bun scripts/run-sim.ts
```

### オプション

| フラグ | デフォルト | 説明 |
|---|---|---|
| `--seed <n>` | `42` | 乱数シード（再現性確保） |
| `--turns <n>` | `30` | 最大ターン数 |
| `--quiet` | なし | ターン詳細ログを抑制 |
| `--preset <name>` | `balanced` | 味方プリセット |
| `vs <name>` | `balanced` | 敵プリセット |

### 利用可能なプリセット

| プリセット名 | 特徴 |
|---|---|
| `balanced` | 全4ジョブをバランス配置（鉄壁×2, 戦術官×1, 狙撃×2, 衛生×3） |
| `aggressive` | 狙撃×5 + 戦術官×2 の火力特化 |
| `defensive` | 鉄壁×4 + 衛生×4 の耐久特化 |

### 使用例

```bash
# aggressive vs defensive を100ターン最大で
bun scripts/run-sim.ts --preset aggressive vs defensive --turns 100

# シードを変えて複数回試行
bun scripts/run-sim.ts --seed 1 --quiet
bun scripts/run-sim.ts --seed 2 --quiet
```

---

## scripts/age_progression_test.ts — 経年変化確認

1体のユニットが修業期→全盛期→衰退期をたどる様子を5年刻みで出力する。

```bash
bun scripts/age_progression_test.ts
```

出力例:

```
======================================================================
  経年変化テスト  (peakStart=25, peakEnd=30, maxAge=55)
======================================================================
年齢    フェーズ  係数    STR   AGI   INT   END
----------------------------------------------------------------------
20      修業期    0.800   80    64    48    56
25      全盛期    1.000   100   80    60    70
30      全盛期    1.000   100   80    60    70
35      衰退期    0.859   86    69    52    60
```

---

## apps/cli/src/simulate_history.ts — 100年旅団シミュレーション

旅団が100年にわたって兵士の加入・引退を繰り返す様子を JSON で出力する。

```bash
bun apps/cli/src/simulate_history.ts
```

出力先: `apps/cli/output/<timestamp>/history.json`

最新の実行結果は `apps/cli/output/.latest` に記録される。

### JSON 構造

```json
{
  "generatedAt": "ISO timestamp",
  "totalYears": 100,
  "totalRecruits": 150,
  "years": [
    {
      "year": 1,
      "averageStrength": 72.5,
      "unitCount": 7,
      "units": [{ "id": "u000", "name": "Leon", "age": 16, "strength": 58 }],
      "events": [{ "type": "join", "unitId": "u001", "unitName": "Arthur", "age": 15 }]
    }
  ],
  "roster": {
    "u000": {
      "joinYear": 1, "retireYear": 45,
      "joinAge": 16, "peakStartAge": 24, "peakEndAge": 28, "maxAge": 48,
      "baseStrength": 95, "peakStrength": 95
    }
  }
}
```

---

## apps/cli/src/simulate_battle_*.ts — 個別バトルシナリオ

各ファイルがそれぞれ異なる編成・ロジックの確認用シナリオを実装している。

```bash
bun apps/cli/src/simulate_battle_offense.ts
bun apps/cli/src/simulate_battle_rotation.ts
```

---

## packages/core/test/ — ユニットテスト

```bash
bun test packages/core/test/
```

テストファイル:

| ファイル | カバー範囲 |
|---|---|
| `enemy.test.ts` | Enemy アクションループ |
| `squad.test.ts` | Squad の編成・HP管理 |
| `passive_ability.test.ts` | SDF/BDF/AB/HL のパッシブ発動 |
