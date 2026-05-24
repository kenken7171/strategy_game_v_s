---
description: Unit インスタンス生成時の規約（性別・継承・年齢パラメータ）
---

# Unit Generation Conventions

`new Unit({...})` を呼ぶ全ての箇所で守るべき規約。血統継承システム導入後に追加。

## 性別（gender）

- 必須属性。仕様上は **生成時に乱数で50:50 割り当て** が原則。
- `UnitProps.gender` はオプショナル。未指定時はコンストラクタが `'Male'` をデフォルトにする（既存テスト互換のため）が、**新規生成箇所では必ず明示的に乱数を渡すこと**:

  ```typescript
  gender: rand() < 0.5 ? "Male" : "Female",
  ```

- DI された RNG（mulberry32 等）を使うことで再現性を確保する（`project_conventions` の「乱数」ルールに従う）。
- 既存のバトルデモ・テスト用ユニットで性別が無意味な場合のみ省略可。シミュレーション系では必須。

## 文化圏（origin） & 命名

- `Origin` は `'Japanese' | 'European' | 'Classical'`。命名プールを選ぶキー。
- `UnitProps.origin` はオプショナル。未指定時のデフォルトは `'European'`（互換）。**シミュレーション系では必ず `pickRandomOrigin(rand)` で渡すこと**。
- 名前は **絶対に手動文字列で渡さない**。`NameGenerator.pick(origin, gender, historical)` を経由して `historical` Set に対するユニーク性を保証すること:

  ```typescript
  import { NameGenerator, pickRandomOrigin } from "packages/core/src/data/names";
  const gen = new NameGenerator(rand);
  const origin = pickRandomOrigin(rand);
  const gender = rand() < 0.5 ? "Male" : "Female";
  const name = gen.pick(origin, gender, brigade.historicalNames);
  ```

- 同じ年に複数ユニットを生成する場合、**ローカル累積 Set を渡す**（`brigade.historicalNames` に直接 add してはいけない — readonly）:

  ```typescript
  const local = new Set(brigade.historicalNames);
  const r1 = makeRecruit(local); local.add(r1.name);
  const r2 = makeRecruit(local); local.add(r2.name);
  ```

- **「Jr.」「II世」「(2)」式の記号的重複回避は厳禁**。プール枯渇時は `NameGenerator` が自動で称号（「暁の」「古の」等）を付与する。
- 子供（継承者）の名前は `Brigade.advance({ nameGenerator })` を渡すと内部で自動採番される。両親の `origin` から 50% で継承される。

## 三段階モデルの年齢パラメータ

| パラメータ | 推奨レンジ（成人新人） | 用途 |
|---|---|---|
| `age` | 14〜18（入団時）| 開始年齢 |
| `peakStartAge` | `age + 6〜15` | 全盛期開始 |
| `peakEndAge` | `peakStartAge + 3〜8` | 全盛期終了 |
| `maxAge` | `peakEndAge + 15〜25` | 引退年齢 |

- `peakStartAge < peakEndAge < maxAge` を必ず満たすこと。
- 子（継承者）は Brigade.advance の `childPeakStartAge/EndAge/MaxAge` で別途規定（デフォルト 25/32/55）。

## 継承計算（Brigade.advance 内部で自動実行）

子が生成される際の計算ルール。手で生成するときも同式に従う:

- **baseStats**: 両親 baseStats の整数平均（`Math.round((a + b) / 2)`）
- **job**: 父か母のジョブを 50:50 で継承（`rng() < 0.5 ? father.job : mother.job`）
- **gender**: 乱数 50:50
- **age**: 15 固定（仕様）
- **birthYear**: 出産予約時の旅団暦
- **parents**: `{ fatherId, motherId }` を必ず記録
- **実 stats**: `baseStats × growthFactor` で自動算出される（`growthFactor = 15 / peakStartAge`）。手動計算は不要。

## affinity / spouseId / parents の取り扱い

- イミュータビリティ厳守。書き換えは必ずヘルパー経由:
  - `unit.withIncreasedAffinity(otherId, delta)` — 新 Map を作って新 Unit を返す
  - `unit.withSpouse(spouseId)` — 配偶者ID を設定した新 Unit を返す
- 結婚は **男女ペアのみ・互いに affinity ≥ 100** が必須条件（Brigade.advance がチェック）。
- 配偶者ID は **必ず双方向に設定**（advance 内で自動）。片方向だけ書き換えると整合性が崩れる。

## ID 命名

| 種類 | プレフィックス例 |
|---|---|
| 通常入団ユニット | `u000`, `u001`, ...（連番） |
| 大隊シミュ等の用途別 | `a-f1`, `e-rl2`（[A=Ally / E=Enemy] + slot + index） |
| 継承者（advance が自動生成） | `child-<year>-<n>` |
| 検証スクリプト用主人公 | `p-<name>`（`p-arthur`, `p-elise`） |

- 同一旅団内で衝突しないよう責任を持つこと。
- 継承者は Brigade.advance が `child-<newYear>-<counter>` で自動生成するので呼び出し側は触らない。

## NG例

```typescript
// ❌ 性別を指定せずシミュレーション系で Unit を生成
new Unit({ id, name, age, peakStartAge, peakEndAge, maxAge, baseStats });

// ❌ peakStartAge >= peakEndAge
new Unit({ ..., peakStartAge: 30, peakEndAge: 28, ... });

// ❌ affinity を直接書き換える
unit.affinity.set(otherId, 100);  // ReadonlyMap だが TS が許す環境で書ける場合 NG

// ❌ 配偶者ID を片方向だけ設定
husband.withSpouse(wife.id);  // wife 側も withSpouse(husband.id) すること
```

## 推奨パターン

```typescript
// 新人騎士の生成
function makeRecruit(currentYear: number, rng: () => number): Unit {
  const age = 14 + Math.floor(rng() * 5);
  const peakStartAge = age + 6 + Math.floor(rng() * 10);
  const peakEndAge   = peakStartAge + 3 + Math.floor(rng() * 6);
  const maxAge       = peakEndAge + 15 + Math.floor(rng() * 11);
  return new Unit({
    id: nextUnitId(),
    name: pickName(rng),
    age,
    birthYear: currentYear - age,
    peakStartAge, peakEndAge, maxAge,
    baseStats: rollBaseStats(rng),
    gender: rng() < 0.5 ? "Male" : "Female",  // ← 必須
    job: pickJob(rng),
  });
}
```
