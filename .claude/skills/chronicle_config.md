---
description: チューニングパラメータは必ず CHRONICLE_CONFIG を参照する規約
---

# Chronicle Config 規約

`packages/core/src/config/ChronicleConfig.ts` の `CHRONICLE_CONFIG` がシステム全体のチューニングパラメータの **単一 SoT（Source of Truth）**。コード内に数値リテラルをハードコードしてはならない。

## 禁止例 (NG)

```typescript
// ❌ ハードコードされた30ターン
new BattleSimulator(allies, enemies, { maxTurns: 30, ... });

// ❌ ハードコードされた100年
for (let year = 1; year <= 100; year++) { ... }

// ❌ ハードコードされた 0.97 (1 - DECAY_RATE)
return Math.pow(0.97, yearsDeclined);

// ❌ ハードコードされた15歳入団
plannedJoinYear: newYear + 15

// ❌ ハードコードされた150名プールサイズ
if (pool.length < 150) throw ...
```

## 推奨例 (OK)

```typescript
import { CHRONICLE_CONFIG } from "packages/core/src/config/ChronicleConfig";

// ✓ Config 参照
new BattleSimulator(allies, enemies, {
  maxTurns: CHRONICLE_CONFIG.BATTLE.MAX_TURNS,
});

for (let year = 1; year <= CHRONICLE_CONFIG.SCHEDULE.CHRONICLE_YEARS; year++) { ... }

return Math.pow(1 - CHRONICLE_CONFIG.TIME.DECAY_RATE, yearsDeclined);

plannedJoinYear: newYear + CHRONICLE_CONFIG.TIME.INDUCTION_AGE
```

## CHRONICLE_CONFIG セクション早見表

| Section | 用途 | 代表キー |
|---|---|---|
| `TIME` | 経年変化・年齢基準 | BASE_PEAK_START_AGE, BASE_PEAK_END_AGE, INDUCTION_AGE, DECAY_RATE, MIN_STAT_VALUE |
| `SCHEDULE` | 旅団運営・イベント周期 | CHRONICLE_YEARS, RECRUIT_INTERVAL, RECRUIT_COUNT, BATTLE_INTERVAL, INITIAL_MEMBER_COUNT, BATTALION_SIZE |
| `LINEAGE` | 好感度・結婚・血統 | AFFINITY_PER_BATTLE, MARRIAGE_THRESHOLD, MARRIAGE_PROBABILITY, BIRTH_PROBABILITY, CULTURE_INHERIT_PROB |
| `BATTLE` | バトル係数 | MAX_TURNS, SQUAD_SIZE, FRONT_ROW_COUNT |
| `NAMING` | 名前プール | POOL_MIN_SIZE |

## 個体差を入れるべき値は別ヘルパーで

`peakStartAge` / `peakEndAge` のような「基準値はあるが個体差を入れたい」値は、`CHRONICLE_CONFIG.TIME.BASE_*` を **基準**とし、`utils/age.ts` の `rollPeakAges(rng)` / `rollChildPeakAges(...)` で実際の個体値を生成する:

```typescript
import { rollPeakAges } from "packages/core/src/utils/age";
const { peakStartAge, peakEndAge } = rollPeakAges(rand);
```

直接 `BASE_PEAK_START_AGE` を Unit に渡してしまうと全員同じ年齢に揃ってしまうので注意。

## 例外: テストデータ・ダミー値

ユニットテストで「PRNG を使わず固定値で確認したい」場合や、検証スクリプトの「中央値の確認」など、明確に意図がある場合のみリテラル使用可。ただしマジックナンバーには必ず短いコメントを付ける:

```typescript
// テストで全盛期固定の挙動を確認するため、CONFIG基準値をベタ書き
new Unit({ ..., peakStartAge: 24, peakEndAge: 28, ... });
```

## CHRONICLE_CONFIG への追加手順

1. `ChronicleConfig.ts` の該当セクションに `KEY: value` を追加（JSDoc コメント必須）
2. 旧ハードコードの参照箇所を `CHRONICLE_CONFIG.<SECTION>.<KEY>` に置換
3. `system_architecture.md` の早見表に追記
4. 既存テスト・検証スクリプトが全 PASS であることを確認

## NG: 既存値の上書き

`CHRONICLE_CONFIG` は `as const` のため TypeScript レベルで書き換え不可。**ランタイムでも書き換えてはならない**。バランス調整は本ファイル編集 → 再ビルドで行う。テスト用に値を差し替えたい場合は、関数の引数経由（例: `AdvanceOptions`）でオーバーライドする設計にすること。
