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

## 血統継承システム

ユニットに性別・好感度・配偶者・親情報を持たせ、戦闘経験から結婚 → 出産予約 → 15年後に「継承者」が旅団に加入する世代交代モデル。

### Unit 拡張フィールド

| フィールド | 型 | 説明 |
|---|---|---|
| `gender` | `'Male' \| 'Female'` | 性別。コンストラクタ未指定時のデフォルトは `'Male'`（既存コード互換）。新規生成箇所では呼び出し側で乱数 50:50 を渡すこと |
| `affinity` | `ReadonlyMap<string, number>` | 他ユニットIDをキーとする好感度マップ。`getAffinity(otherId)` で取得（未記録は 0） |
| `parents` | `{ fatherId, motherId } \| null` | 親の ID 記録（継承者にのみ設定される） |
| `spouseId` | `string \| null` | 配偶者ID。`isMarried` ゲッターで未婚判定 |

### Brigade.pendingBirths

`BirthRegistry[]` を旅団に保持。これは「結婚カップルが子を授かったが、Unit インスタンスはまだ生成されていない」予約状態。

```ts
interface BirthRegistry {
  fatherId: string;
  motherId: string;
  birthYear: number;          // 予約された年（旅団暦）
  potentialStats: Stats;      // 父母の baseStats 平均（子の全盛期予想値）
  job: JobType | null;        // 両親のいずれかから 50:50 で継承
  plannedJoinYear: number;    // = birthYear + 15
}
```

### advance() の年次処理順序

```
1) 好感度更新     — battlePairs に渡された ally 同分隊ペアに +affinityPerBattle
2) 加齢 → 引退判定 — 全ユニット grow()、age >= maxAge を retire イベント
3) 結婚判定        — 未婚男女・互いに >= affinityThreshold・marriageProb で成立
                    各ユニットは1年で最大1組まで成立。spouseId を双方向に記録
4) 出産予約        — 結婚済みカップル毎年 birthProb で BirthRegistry を作成
                    pendingBirths に push（カップル単位、双方向重複を排除）
5) 15歳入団        — pendingBirths のうち plannedJoinYear = newYear のものを Unit 化
                    baseStats に potentialStats を入れ、age=15 とすることで
                    stats ゲッターが自動的に growthFactor = 15/peakStartAge を適用
6) recruits 追加  — 既存の入団ロジック（外部から渡されたユニット）
```

### 継承者ステータス計算

子の baseStats（全盛期最大値）は両親の baseStats を整数平均:

```
potentialStats[k] = round((father.baseStats[k] + mother.baseStats[k]) / 2)
```

15歳入団時の実効ステータスは三段階モデルの修業期式そのもの:

```
stats[k] = max(1, round(potentialStats[k] * (15 / peakStartAge)))
```

例: peakStartAge=25 なら growthFactor = 0.6、potentialStats.strength=110 → stats.strength=66。

### AdvanceOptions

| オプション | 既定 | 用途 |
|---|---|---|
| `battlePairs` | `[]` | バトル直後に渡す `[allyId, allyId]` 配列。`BattleSimulator.run()` の `squadmatePairs` をそのまま渡せる |
| `rng` | `Math.random` | DI された乱数生成器（再現性のため） |
| `marriageProb` | `0.3` | 条件成立ペアの結婚成立確率 |
| `birthProb` | `0.2` | 結婚済みカップル毎年の出産予約確率 |
| `affinityPerBattle` | `10` | バトル1回で同分隊ペアに加算される好感度 |
| `affinityThreshold` | `100` | 結婚条件の好感度閾値 |
| `childPeakStartAge` | `25` | 子の全盛期開始年齢（実ステータス算出にも使用） |
| `childPeakEndAge` | `32` | 子の全盛期終了年齢 |
| `childMaxAge` | `55` | 子の引退年齢 |

### YearEvent 拡張

```ts
type YearEvent =
  | { type: 'join'; unit }
  | { type: 'retire'; unit }
  | { type: 'marriage'; husband; wife }
  | { type: 'birth_planned'; registry }
  | { type: 'birth'; unit };          // 15歳入団した継承者
```

### BattleSimulator 連携

`SimulationResult.squadmatePairs: ReadonlyArray<[string, string]>` に、そのバトルで同一 Squad に同居していた ally ユニットの ID 組が片方向で格納される。これを `brigade.advance({ battlePairs: result.squadmatePairs })` または `brigade.applyBattleAffinity(result.squadmatePairs)` に渡すと好感度が更新される。

### 検証スクリプト

`scripts/verify-bloodline.ts` で「同分隊10戦 → 結婚 → 出産 → 15年後継承者加入」の一連流れを決定的シード + 確率100%設定で検証できる。

---

## 命名システム

旅団 100年史で全ユニットが固有の存在感を持てるよう、多文化ネーミングデータと「歴史的に重複しない」ユニーク制約を実装している。

### 文化圏 (Origin)

| Origin | 雰囲気 | 含まれる名前の系統 |
|---|---|---|
| `Japanese` | 古風・力強い和風 | 戦国武将・古典文学・神話・公家武家の名乗り・幕末志士・自然/神獣 |
| `European` | 叙事詩的・中世風 | ニーベルンゲン・アーサー王・シャルルマーニュ・神聖ローマ・北欧ヴァイキング |
| `Classical` | 神話・星・幻想 | ギリシャ/ローマ・メソポタミア・エジプト・ケルト・天体・カバラ・ヒンドゥー |

### データ規模

`packages/core/src/data/names.ts` に各 Origin × Gender = 6 プール、各 150 名以上、合計 **910名** を収録。プール定義時に内部重複と件数下限（150）をモジュール読み込み時にアサートしており、欠落は即座に throw する。

### 重複回避の仕様

1. **Brigade.historicalNames: ReadonlySet<string>** — 過去に旅団に所属した全ユニット（新人・子供・引退者含む）の名前を永続記録。
   - Brigade コンストラクタが現 `units` の名前を自動登録する。
   - `advance()` が新規追加（recruits + 子供）の名前も追記して新 Brigade に引き継ぐ。
   - `applyBattleAffinity()` など他のメソッドも `historicalNames` を引き継いで返す。

2. **NameGenerator.pick(origin, gender, historical)** — `historical` Set を参照し、未登場の候補名を返す。
   - プール内の使用済みを除外して残候補からランダム抽出
   - プール枯渇時は称号（`TITLES`: 「暁の」「古の」「不屈の」など32種）を接頭辞として付与
   - 称号付き名も `historical` と照合してユニーク性を保証

3. **「Jr.」「II世」「(2)」式の記号的重複回避は厳禁**（仕様）。回避手段は称号付与のみ。

### 命名の継承ロジック

- **新人の命名**: ランダムな `Origin` + `Gender` から未登場名を選択。
- **子供の命名**（出産予約時）: 両親のいずれかの `Origin` を **50% で継承**。出産予約 `BirthRegistry.origin` に記録され、15歳入団時に `NameGenerator.pick(reg.origin, gender, historical)` で名前を決定する。
- `Brigade.advance({ nameGenerator })` に NameGenerator を渡さない場合は旧挙動（`継承者child-<year>-<n>`）にフォールバックする（後方互換）。

### Unit.origin

`Origin` を Unit のフィールドとして保持し、子供の文化圏継承に使う。`UnitProps.origin` はオプショナル、未指定時のデフォルトは `"European"`（既存テスト互換）。新規生成箇所では NameGenerator と合わせて明示指定すること（[`unit_generation` skill](../.claude/skills/unit_generation.md) 参照）。

### 検証

`scripts/verify-naming.ts` で以下を確認できる:

1. 300名連続生成で全員ユニーク + 3文化圏バランス分布（各 60〜140 範囲）
2. プール枯渇テスト（European/Male を 200連続 → 151番目から称号付与に切替）
3. Brigade.historicalNames の自動蓄積（50年経過 → 投入名全件保持）

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
  run-grand-chronicle.ts   ← 100年旅団変遷シミュレーター
  age_progression_test.ts  ← 経年変化の動作確認
  verify-bloodline.ts      ← 血統継承システムのE2E検証
  verify-naming.ts         ← 命名重複回避システムのE2E検証

packages/core/src/data/
  names.ts                 ← 多文化名前データ(910名) + NameGenerator

config/
  jobs.json           ← ジョブデフォルト値
  game_settings.json  ← MAX_UNITS_PER_SQUAD 等
```
