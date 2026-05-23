---
description: Chronicle Knights のコーディング規約・実装ルール
---

# Project Conventions

## イミュータビリティの維持

- `Unit` はイミュータブル。状態変更は必ず新インスタンスを返す。
  ```typescript
  // Good
  takeDamage(amount: number): Unit {
    return new Unit({ ...this, hp: Math.max(0, this.hp - amount) });
  }
  // Bad — this.hp を直接書き換えない
  ```
- `Brigade` の `units` は `ReadonlyArray<Unit>`。`Squad._units` は内部ミュータブルだが、`get units()` で `ReadonlyArray` として公開する。

## Math.max(1, x) の徹底

- 全ての **ステータス値**（stats）と **有効ダメージ**（effectiveDamage）には最低値 1 を保証すること。
  ```typescript
  // stats ゲッター
  strength: Math.max(1, Math.round(baseStats.strength * f))
  // ダメージ計算
  return Math.max(1, baseDamage - reduction);
  ```
- HP は 0 まで許容する: `Math.max(0, hp - damage)`

## 型安全

- `JobType` のユニオン型を常に最新に保つ（`Unit.ts` の `JobType`）。
- `UnitProps` インターフェースに追加フィールドを足したら `Unit` クラスの `constructor` も更新する。

## 乱数

- 再現性が必要なシミュレーションには Mulberry32 シード付き PRNG を使う（`scripts/run-sim.ts` 参照）。
- `BattleManager` は `rng: () => number` を DI で受け取る設計。テストではシード固定の PRNG を渡すこと。

## ジョブ能力の実装場所

| 能力 | 実装場所 |
|---|---|
| SDF / BDF | `BattleManager.computeEffectiveDamage()` |
| AB（攻撃・速度バフ） | `BattleManager.applyTacticianBuffs()` |
| HL（回復） | `BattleManager.applyMedicHealing()` |
| 2連撃 | `BattleManager.processSquadOffense()` |

新ジョブの能力は既存メソッドのパターンに合わせて追加する。

## ファイル分割方針

- モデルクラス（データ + 純粋な計算）は `packages/core/src/models/` に置く。
- バトルロジック（副作用ありの状態機械）は `BattleManager.ts` に置く。
- 高レベルの実行フローと統計は `BattleSimulator.ts` に置く。
