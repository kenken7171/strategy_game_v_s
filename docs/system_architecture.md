# System Architecture

## Overview

Chronicle Knights はターン制ストラテジーゲームのコアロジックライブラリ (`packages/core`) と CLI アプリ (`apps/cli`) から構成される。

---

## Battle Logic

### 構成要素

| クラス | 役割 |
|---|---|
| `Unit` | 兵士1人。イミュータブル。全ステータスは `readonly`。 |
| `Squad` | 最大3体 (`MAX_UNITS_PER_SQUAD`) のユニットグループ。スロットID（`FRONT`, `REAR-L`, `REAR-R` 等）で識別。 |
| `Enemy` | 固定アクションローテーションを持つ敵。 |
| `BattleManager` | 1回の戦闘を管理。ターン処理・ダメージ計算の中枢。 |
| `BattleSimulator` | `BattleManager` を wrap し、統計収集・ログ出力を行う高レベル API。 |
| `Brigade` | ユニット集団（旅団）。年次進行 (`advance`) と大隊選出 (`selectBattalion`) を管理。 |

### ターン処理順序（`processIntegratedTurn`）

1. **全バフをリセット** — 前ターンの `speedBuff`/`attackBuff` をゼロに戻す
2. **戦術官のバフ適用** — 生存中の `tactician` が大隊全体の SPD と FA/RA を加算
3. **イニシアチブ決定** — `finalSpeed` 降順でアクション順を確定
4. **アクション実行** — 速い側から交互に敵攻撃 / 味方攻撃
5. **衛生兵の回復** — ターン末に生存中の `medic` が分隊全員を `hl` 分回復

### ダメージ軽減（SDF / BDF）

- **BDF** (`bdf`): `FRONT` スロットにいる `iron_wall_knight` が大隊全体への被ダメを軽減。複数いれば加算。
- **SDF** (`sdf`): ターゲット分隊内の `iron_wall_knight` が自分隊への被ダメを軽減。
- 計算式: `effectiveDamage = Math.max(1, baseDamage - totalReduction)`

### 狙撃兵の2連撃条件

`sniper` がイニシアチブ1番手かつ分隊内で最速の場合、同ターン2回攻撃。

---

## 経年変化システム

ユニットのステータスは年齢によって三段階で変化する。`baseStats` は全盛期の最大値を示す。

### フェーズ定義

| フェーズ | 条件 | growthFactor |
|---|---|---|
| 修業期 | `age < peakStartAge` | `age / peakStartAge` （線形上昇 0→1） |
| 全盛期 | `peakStartAge <= age <= peakEndAge` | `1.0`（固定） |
| 衰退期 | `age > peakEndAge` | `(0.97)^(age - peakEndAge)`（複利 3%/年減衰） |
| 引退 | `age >= maxAge` | `0`（`isRetired = true`） |

実効ステータス:

```
stats[key] = Math.max(1, Math.round(baseStats[key] * growthFactor))
```

### Unit フィールド一覧

| フィールド | 型 | 説明 |
|---|---|---|
| `birthYear` | `number \| null` | 生まれ年（Brigade.currentYear と組み合わせて年齢確認に使用） |
| `peakStartAge` | `number` | 全盛期開始年齢 |
| `peakEndAge` | `number` | 全盛期終了年齢 |
| `maxAge` | `number` | この年齢以上で引退（`isRetired = true`） |

### Brigade.currentYear

`Brigade` は `currentYear: number` を保持し、`advance()` を呼ぶたびにインクリメントされる。  
大隊編成には `selectBattalion(n)` を使うことで、その時点のステータスで上位 n 体を選出できる。

---

## ファイル構成

```
packages/core/src/
  models/
    Unit.ts          ← ユニット定義・経年変化ロジック
    Brigade.ts       ← 旅団・年次進行
    Squad.ts         ← 分隊
    Enemy.ts         ← 敵
  BattleManager.ts   ← ターン処理・ダメージ計算
  BattleSimulator.ts ← 高レベルシミュレーター
  config.ts          ← MAX_UNITS_PER_SQUAD 等

apps/cli/src/
  simulate_history.ts  ← 100年旅団シミュレーション
  simulate_battle_*.ts ← 各種バトルシナリオ
  generate_report.ts   ← JSON → コンソールレポート

scripts/
  run-sim.ts               ← 大隊間バトルCLI
  age_progression_test.ts  ← 経年変化の動作確認

config/
  jobs.json           ← ジョブデフォルト値
  game_settings.json  ← MAX_UNITS_PER_SQUAD 等
```
