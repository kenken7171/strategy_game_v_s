---
description: 新ジョブを追加するときのチェックリスト
---

# 新ジョブ追加手順

## 1. config/jobs.json に定義を追加

```json
{
  "id": "your_job_id",
  "name": "日本語名",
  "description": "能力の一文説明",
  "defaults": {
    "frontAttack": 30, "rearAttack": 30,
    "speed": 20, "maxHp": 120,
    "sdf": 0, "bdf": 0, "ab": 0, "hl": 0
  }
}
```

## 2. Unit.ts の JobType を更新

```typescript
// packages/core/src/models/Unit.ts
export type JobType = "iron_wall_knight" | "tactician" | "medic" | "sniper" | "your_job_id";
```

## 3. UnitProps / Unit に専用フィールドが必要な場合

新しいパラメータ（例: `stunChance`）が必要なら:
1. `UnitProps` インターフェースに `readonly stunChance?: number` を追加
2. `Unit` クラスに `readonly stunChance: number` を追加
3. `constructor` で `this.stunChance = props.stunChance ?? 0` を追加

## 4. BattleManager に能力ロジックを実装

既存のパターンを参考に:

```typescript
// 既存: applyTacticianBuffs, applyMedicHealing, computeEffectiveDamage
// 新規: 適切なメソッドに能力処理を追加し、processIntegratedTurn() から呼ぶ
```

呼び出しタイミングの選択:
- ターン開始時バフ → `applyTacticianBuffs` の後に追加
- ターン末処理（回復等）→ `applyMedicHealing` の後に追加
- ダメージ軽減 → `computeEffectiveDamage` に追加

## 5. scripts/run-sim.ts の JOB_DEFAULTS を更新

```typescript
const JOB_DEFAULTS: Record<JobType, JobDefaults> = {
  // 既存...
  your_job_id: { maxHp: 120, speed: 20, frontAttack: 30, rearAttack: 30, bdf: 0, sdf: 0, ab: 0, hl: 0 },
};
```

## 6. ドキュメント更新

- `docs/job_definitions.md` に新ジョブのセクションを追加
- 能力の発動条件・スタック可否・推奨配置を明記

## 7. テスト追加

`packages/core/test/passive_ability.test.ts` に発動確認テストを追加。
