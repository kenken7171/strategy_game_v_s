# CLAUDE.md — Chronicle Knights プロジェクト現状ドキュメント

> このドキュメントは **本リポジトリの「現在のありのままの設計図」** を記録するものである。
> 提案・将来構想は含まない。コードを読み取って実態のみを写す。
>
> 一次仕様書（絶対ルール）は別途 `instructions.md` に存在する。本書は「コードの実態」、
> instructions.md は「守るべきルール」と役割が分かれている。
>
> 最終更新の根拠コミット: `3673cca`（ローテーション直感統一 + 次鋒予告 UI）

---

## A. システムアーキテクチャ概要

### A-1. リポジトリ構成（モノレポ）

```
strategy_game_v_s/
├── apps/
│   ├── api/          @chronicle-knights/api      Hono 4 + Bun の HTTP API
│   └── cli/          @chronicle-knights/cli      検証・シミュレーション CLI 群
├── packages/
│   ├── core/         @chronicle-knights/core     ゲームロジックの純粋 TS ライブラリ
│   └── frontend/     @chronicle-knights/frontend React 18 + Vite 5 の SPA
├── config/           設定 JSON（jobs.json 等）
├── docs/             設計ドキュメント
├── scripts/          スクリプト（run-grand-chronicle 等）
├── tools/            補助ツール
├── reports/          シミュレーション出力レポート
├── image/            16-bit ドット絵原本（複製先: packages/frontend/public/image）
├── instructions.md   絶対ルール集（プロジェクト永続指示書）
└── TODO.md           タスクトラッキング
```

### A-2. 技術スタック

| レイヤ | 技術 | バージョン |
|---|---|---|
| ランタイム | **Bun** | 1.3.13 |
| 言語 | **TypeScript** | 5.7.2（strict mode） |
| API サーバ | **Hono** | 4.6.14 |
| API HTTP アダプタ | `@hono/node-server` | 1.13.7 |
| フロント UI | **React** | 18.3.1 |
| フロントビルド | **Vite** | 5.4.11 |
| Vite React プラグイン | `@vitejs/plugin-react` | 4.3.4 |
| スタイル | **Plain CSS**（Tailwind は未導入） | 単一ファイル `styles/global.css` |

### A-3. 依存関係（モノレポ内 import）

- `apps/api` → `packages/core/src/index.ts` 経由で全 core エクスポートを参照
  - import 例: `import { Unit, Squad, BattleSimulator, ... } from "../../../packages/core/src/index"`
- `apps/api/src/session.ts` → 同上（GameSession 用）
- `packages/frontend` → core への直接依存 **なし**（API JSON 経由で疎結合）
- `packages/frontend/src/api/types.ts` が「API レスポンスと一致する型」を独自に定義する責務を持つ
- `apps/cli` → `packages/core` を参照（シミュレーションスクリプト群）

### A-4. パッケージ間プロキシ（開発時）

- Vite dev server: `:5173`（host: true、外部 IP からもアクセス可）
- API サーバ: `:8787`（`bun --hot run apps/api/src/server.ts`）
- フロントの `/api/*` は Vite proxy で `http://localhost:8787` に転送される（`packages/frontend/vite.config.ts`）

### A-5. コマンド実行規約

| コマンド | 場所 | 内容 |
|---|---|---|
| `bun test` | リポジトリルート | 全テスト実行（後述 D-1） |
| `bun run type-check` | `apps/api/` | `tsc --noEmit` |
| `bun run type-check` | `packages/frontend/` | `tsc --noEmit` |
| `bun run build` | `packages/frontend/` | `tsc --noEmit && vite build` |
| `bun run dev` | `apps/api/` | `bun --hot run src/server.ts`（API 起動） |
| `bun run dev` | `packages/frontend/` | `vite`（フロント開発サーバ） |
| `bun run sim` | `apps/cli/` | `bun run scripts/run-sim.ts` |
| `bun run simulate` | `apps/cli/` | `simulate_history.ts` |
| `bun run simulate:unit` | `apps/cli/` | `simulate_unit.ts` |
| `bun run simulate:brigade` | `apps/cli/` | `simulate_brigade_battle.ts` |

ルート `package.json` は **存在しない**（各サブパッケージが独立した `package.json` を持つ）。

---

## B. Unit クラス / 型定義の現在の設計

実体: `packages/core/src/models/Unit.ts`

### B-1. プロパティ一覧（`Unit` クラスのインスタンスが保持するもの）

| プロパティ | 型 | デフォルト | 説明 |
|---|---|---|---|
| `id` | `string` | 必須 | 一意 ID |
| `name` | `string` | 必須 | 表示名（NameGenerator 由来） |
| `age` | `number` | 必須 | 現在年齢 |
| `birthYear` | `number \| null` | `null` | 出生年。手動入団者は null |
| `peakStartAge` | `number` | 必須 | 全盛期開始年齢（`rollPeakAges`） |
| `peakEndAge` | `number` | 必須 | 全盛期終了年齢 |
| `maxAge` | `number` | 必須 | 引退（=死亡）年齢 |
| `baseStats` | `Stats` | 必須 | 全盛期最大値（strength/agility/intelligence/endurance） |
| `maxHp` | `number` | `100` | 最大 HP |
| `hp` | `number` | `maxHp` | 現在 HP |
| `speed` | `number` | `0` | 素早さ（バフ前） |
| `frontAttack` | `number` | `0` | FRONT 配置時の攻撃力 |
| `rearAttack` | `number` | `0` | REAR 配置時の攻撃力 |
| `job` | `JobType \| null` | `null` | ジョブ（B-2 参照） |
| `sdf` | `number` | `0` | 自分隊被ダメ軽減（Squad Defense） |
| `bdf` | `number` | `0` | 大隊全員被ダメ軽減（Brigade Defense, FRONT 配置時のみ発動） |
| `ab` | `number` | `0` | 自分以外への速度+攻撃バフ（Ally Buff） |
| `hl` | `number` | `0` | 自分隊回復量（Heal） |
| `speedBuff` | `number` | `0` | バフで加算された速度 |
| `attackBuff` | `number` | `0` | バフで加算された攻撃 |
| `gender` | `Gender` | `"Male"` | `"Male" \| "Female"` |
| `affinity` | `ReadonlyMap<string, number>` | `new Map()` | 他ユニット ID → 好感度 |
| `parents` | `Parents \| null` | `null` | `{ fatherId, motherId }`、継承者ならセット |
| `spouseId` | `string \| null` | `null` | 配偶者の ID（既婚なら非 null） |
| `origin` | `Origin` | `"European"` | `"Japanese" \| "European" \| "Classical"`（命名文化圏） |

`Unit` は **完全イミュータブル**：`grow()` / `takeDamage()` / `withBuffs()` / `withHeal()` / `resetBuffs()` / `withIncreasedAffinity()` / `withSpouse()` はすべて新インスタンスを返す。

### B-2. `job` の型・保持形式

```ts
export type JobType =
  | "iron_wall_knight"
  | "tactician"
  | "medic"
  | "sniper"
  | "sorcerer"
  | "standard_bearer"
  | "heavy_infantry"
  | "scout";
```

- `Unit.job` は **`JobType | null` の文字列 union**。オブジェクト型ではない
- `null` 許容：敵ボス（`makeTrialEnemy` の「試練の門の守護者」）は `job: null`
- ジョブの **能力値の SoT** は `packages/core/src/data/jobs.ts` の `JOB_DEFAULTS: Record<JobType, JobDefaults>`
- ジョブの **能力解説 SoT** は同ファイルの `JOB_ABILITY: Record<JobType, JobAbility>`
- ジョブの **日本語ラベル SoT** は同ファイルの `JOB_JP`
- API ルート `apps/api/src/routes/battle.ts` 内に `buildBattleUnit(u)` があり、`Unit` を渡すと該当 `JOB_DEFAULTS[u.job]` の値を `growthFactor` でスケールして新 `Unit` を返す（実戦投入時の最終ステータス確定処理）

### B-3. 派生ゲッタ

| ゲッタ | 戻り値 | 内容 |
|---|---|---|
| `growthFactor` | `number 0.0–1.0` | 年齢補正係数（三段階モデル: 修業期 / 全盛期 / 衰退期） |
| `stats` | `Stats` | `baseStats × growthFactor`（最小値 `CHRONICLE_CONFIG.TIME.MIN_STAT_VALUE`） |
| `isRetired` | `boolean` | `age >= maxAge` |
| `isAlive` | `boolean` | `hp > 0` |
| `isMarried` | `boolean` | `spouseId !== null` |
| `finalSpeed` | `number` | `speed + speedBuff` |
| `finalFrontAttack` | `number` | `frontAttack + attackBuff` |
| `finalRearAttack` | `number` | `rearAttack + attackBuff` |
| `finalAttack` | `number` | `frontAttack + attackBuff`（互換用、`finalFrontAttack` と同値） |

### B-4. 補助型（同ファイル内 export）

```ts
export interface Stats {
  readonly strength: number;
  readonly agility: number;
  readonly intelligence: number;
  readonly endurance: number;
}
export interface Parents {
  readonly fatherId: string;
  readonly motherId: string;
}
```

`Gender`, `Origin`, `JobType`, `UnitProps`, `Unit` はすべて `packages/core/src/index.ts` から再エクスポートされている。

---

## C. バトル・編成システムとの現在の連携状況

### C-1. `Squad` クラス（`packages/core/src/models/Squad.ts`）

- `id: string`（実用上は `"FRONT" | "REAR-L" | "REAR-R"`）
- `private _units: Unit[]`、getter `units: ReadonlyArray<Unit>`
- 最大保持数 `MAX_UNITS_PER_SQUAD = 3`（`packages/core/src/config.ts`）
- 主要メソッド:
  - `addUnit(unit)`
  - `replaceUnits(units)` — **units 配列を一括差し替える**（ローテーション・編成変更の基本操作）
  - `averageSpeed` — 生存ユニットの `finalSpeed` 平均
  - `isDefeated` — 全員 HP ≤ 0
  - `applyDamage(damage)` / `applyBuff(...)` / `resetBuffs()` / `applyHeal(...)` — 一括更新（内部で `Unit.takeDamage` 等を呼ぶ）

### C-2. 配置データ（`GridPlacement`）

`packages/core/src/BattleSimulator.ts` でエクスポート。UI 表示用に毎ターン生成される：

```ts
export interface GridPlacement {
  readonly unitId: string;
  readonly unitName: string;
  readonly job: string | null;          // ジョブ ID（フロントでアイコンパスに使用）
  readonly gender: "Male" | "Female";   // フロントでアイコン path /image/{job}/{gender}.png
  readonly row: "FRONT" | "REAR-L" | "REAR-R";
  readonly col: number;                  // 0 / 1 / 2
  readonly hp: number;
  readonly maxHp: number;
}
```

`BattleSimulator.collectPlacements()` が、各 Squad の `units` 配列インデックスを `col` として `GridPlacement[]` を組み立てる。

### C-3. 分隊単位ローテーション（squad swap）

`BattleSimulator.rotateGrid(strategy: "CW" | "CCW")` の現行ロジック：

```
CW (時計回り):
  REAR-L → FRONT
  FRONT  → REAR-R
  REAR-R → REAR-L

CCW (反時計回り):
  REAR-R → FRONT
  FRONT  → REAR-L
  REAR-L → REAR-R
```

- 実装は **`Squad.replaceUnits` で全 units 配列を 3 squad 間で swap** するだけ
- squad 内のスロット順（col 0/1/2）は完全保持
- 敵の分隊単位ダメージは squad ID にバインドされるため、内部 units 配列を入れ替えるだけで「正しい分隊」にダメージが落ちる整合性を保つ
- ローテーション後に `collectPlacements()` を呼んで `turnLog.placements` を生成するため、UI 反映も自動で同期

### C-4. 敵ボスの単体化と `EnemyState`

- `apps/api/src/routes/battle.ts` の `makeTrialEnemy(year, rng)` で **敵を単体ボス「試練の門の守護者」として生成**
- `Unit` として表現される（`job: null`, `id: "enemy-1"`）
- HP は旧 10 体合算と同等のスケール（`baseHp × 10`）、攻撃力・速度は `±15%` 乱数（`0.85 + rng() * 0.30`）
- `BattleSimulator` 内では `DynamicEnemy`（`Enemy` のサブクラス）でラップされ、`unitRecords[0]` に唯一のレコードを保持
- `BattleSimulator.getEnemyState()` がフロントへの公開メソッド：

```ts
export interface EnemyState {
  readonly name: string;
  readonly job: string | null;
  readonly hp: number;
  readonly maxHp: number;
  readonly speed: number;
  readonly frontAttack: number;     // baseAttack（生成時の攻撃力固定値）
  readonly rearAttack: number;      // 同上（front と同値）
}
```

- `/api/battle/init` と `/api/battle/turn` のレスポンスに毎回 `enemy: EnemyState` が含まれる（HP は毎ターン減算）

### C-5. 攻撃予告システム

```ts
export type AttackPatternKind = "SINGLE_STRIKE" | "PINCER" | "TOTAL_ASSAULT";
export interface AttackIntent {
  readonly kind: AttackPatternKind;
  readonly skillName: string;
  readonly targetRows: ReadonlyArray<"FRONT" | "REAR-L" | "REAR-R">;
  readonly damagePerUnit: number;
}
```

- `BattleSimulator.getNextActionIntent()` が次ターンの行動を予告
- `DynamicEnemy.setNextAction(action)` で予告された行動を次の `runOneTurn` 実行時に強制的に使用
- フロントでは `BattleSimulationPage` の `EnemyIntentBanner` と「ライブ陣形の赤枠脈動」（`data-targeted` 属性）でこれを可視化

### C-6. 戦闘フロー全体（API シーケンス）

```
[フロント] BattalionFormationPage で 9 名配置 → sessionStorage に保存
   ↓
[フロント] BattleSimulationPage マウント
   ↓ POST /api/battle/init { placements }
[API] makeTrialEnemy() で敵生成、BattleSimulator を session.activeBattle に格納
   ← { placements, timeline, currentTurn:0, nextActionIntent, enemy: EnemyState }
   ↓
[フロント] 表示: 敵カード（最上部）→ 攻撃予告 → V字陣形（targetRows に赤枠）
   ↓ ユーザーがコマンドボタン押下
   ↓ POST /api/battle/turn { strategy: "NONE" | "CW" | "CCW" }
[API] sim.runOneTurn(strategy) → 戦闘 1 ターン処理
   ← { turnLog, timeline, finished, winner, currentTurn, enemy: EnemyState,
        nextActionIntent (or null if finished), allySurvivors, enemySurvivors }
   ↓
[フロント] ターンログ追加 → placements 更新 → 次ターンへ
   ↓ 戦闘終了後 unmount で
   ↓ POST /api/battle/finish
[API] session.advanceYear(rng) → 次年へ
   ← { nextYear, eventsCount, brigadeSize }
```

### C-7. セッション

- `apps/api/src/session.ts` の `GameSession` が **インメモリで単一プレイヤー状態を保持**
- 現状は API サーバ 1 プロセス = 1 セッション
- `session.activeBattle` フィールドに `BattleSimulator` インスタンスを格納して、ターン間で状態を持続させる
- `getOrCreateSession()` が `apps/api/src/routes/*.ts` から呼ばれる

### C-8. 編成フロー（V字配置）

- `packages/frontend/src/phases/BattalionFormation/BattalionFormationPage.tsx`
- 分隊ごとに 3 スロット × 3 squad = 9 マスの **V 字レイアウト**
  - FRONT は中央上にせり出し
  - REAR-L / REAR-R は左下 / 右下
- ベンチクリック → `UnitDetailModal` 表示 → モーダル内 V 字ミニグリッドからマス指定
- 配置完了で `sessionStorage["formation:placements"]` に `BattlePlacement[]` を保存

### C-9. API 経由で公開される Unit 情報

`packages/frontend/src/api/types.ts` で定義される行型：

| 型 | 表示用フィールド（抜粋） |
|---|---|
| `RosterUnit` | id, name, job, gender, origin, age, **strength**, baseStrength, growthFactor, isMarried, spouseId, parents, isAlive, isRetired, descendantCount + `BattleStatsFields` |
| `RecruitRow` | id, name, job, gender, origin, age, baseStrength, source ("application"\|"heir"), hasLineage, relatedFamilyIds + `BattleStatsFields` |
| `RetireeRow` | id, name, job, gender, origin, age, strength, **strengthRank**, reasons, hasLineage, descendantCount, isMarried + `BattleStatsFields` |
| `BattleStatsFields` | maxHp, attack (= max(frontAttack,rearAttack)), frontAttack, rearAttack, speed, totalRating |
| `SurvivorRow` | name, job, gender?, hp, maxHp |
| `GridPlacement` | unitId, unitName, job, gender, row, col, hp, maxHp |

`Unit` クラス本体はフロントには露出せず、これら **API 行型に正規化されて送出される**。

---

## D. テスト規約と data-testid 規約

### D-1. テストファイル一覧（41 pass / 0 fail）

| ファイル | 主な describe | 対象 |
|---|---|---|
| `packages/core/test/squad.test.ts` | Config, Squad, Brigade.assignUnitToSquad | Squad / Brigade の基本動作 |
| `packages/core/test/enemy.test.ts` | Enemy.getActionForTurn, Enemy バリデーション, BattleManager ダメージ処理, BattleManager 攻撃予報, BattleManager hitCount と ActionResult | 敵行動・BattleManager |
| `packages/core/test/passive_ability.test.ts` | 狙撃兵×戦術官 SPD バフ, 鉄壁騎士ダメージ軽減, 衛生兵回復, Unit バフ適用 | ジョブパッシブ・バフ仕様 |
| `scripts/age_progression_test.ts` | 経年変化テスト（修業期/全盛期/衰退期） | `growthFactor` 三段階モデル |

合計: **4 ファイル / 41 tests / 0 fail**（最新 `bun test` 出力）。

`apps/api` および `packages/frontend` には現時点でユニットテスト無し（型チェックと `bun run build` で代替）。

### D-2. data-testid 命名規則

instructions.md の「全コンポーネントに data-testid を例外なく付与」ルールに基づき、フロント全コンポーネントが testid を持つ。命名パターンは以下の通り：

#### 静的 testid（コンポーネント単位）

`{phase-or-section}-{element-type}` の kebab-case。

例:
- `app-root`, `game-manager-root`, `game-manager-header`, `game-manager-main`, `game-manager-footer`
- `phase-indicator-root`, `next-phase-button`
- `chronicle-page-root`, `chronicle-page-title`, `chronicle-summary-card`, `chronicle-enemy-preview-card`, `chronicle-history-section`
- `guild-management-page-root`, `guild-candidates-section`, `guild-retirees-section`, `guild-overflow-summary`
- `battalion-formation-page-root`, `formation-v-shape-root`, `formation-instruction-banner`, `formation-roster-section`
- `battle-simulation-page-root`, `battle-enemy-status-card`, `battle-enemy-intent-banner`, `battle-live-grid-section`, `battle-preview-timeline-section`, `battle-turn-command-root`, `battle-log-section`, `battle-result-section`
- `unit-detail-modal-root`, `unit-detail-modal-backdrop`, `unit-detail-modal-close-button`, `unit-detail-header`, `unit-detail-stats-section`, `unit-detail-assign-section`, `unit-detail-mini-grid`
- `roster-controls-root`, `roster-sort-select`, `roster-filter-job-select`
- `common-loading-spinner`, `common-error-banner`

#### 動的 testid（ID / row / col / idx を埋め込む）

`{phase}-{element-type}-{key}` の形：

| パターン | 例 |
|---|---|
| **マス系（row/col）** | `formation-target-slot-${row}-${col}`, `formation-cell-unit-${row}-${col}`, `formation-cell-empty-${row}-${col}`, `formation-cell-icon-slot-${row}-${col}`, `formation-cell-unit-name/job/stats/total-${row}-${col}`, `formation-cell-heart-${row}-${col}` |
| **配置指定モーダル** | `formation-assign-btn-${row}-${col}`, `formation-assign-btn-self-mark-${row}-${col}`, `formation-assign-btn-occupant-${row}-${col}`, `formation-assign-btn-occupant-icon-slot-${row}-${col}` |
| **V 字 squad ラッパー** | `formation-v-squad-${row}`, `formation-v-squad-label-${row}`, `formation-v-slot-row-${row}`, `unit-detail-mini-grid-slot-row-${row}`, `unit-detail-mini-grid-row-${row}`, `unit-detail-mini-grid-row-label-${row}` |
| **戦闘ライブ陣形** | `battle-live-grid-cell-${row}-${col}`, `battle-live-grid-unit-${row}-${col}`, `battle-live-grid-icon-slot-${row}-${col}`, `battle-live-grid-name/job/hp-${row}-${col}`, `battle-live-grid-row-label-${row}`, `battle-live-v-squad-${row}`, `battle-live-v-slot-row-${row}`, `battle-live-v-squad-targeted-mark-${row}` |
| **コマンドボタン** | `battle-turn-command-${NONE\|CW\|CCW}`, `battle-turn-command-label-${s}`, `battle-turn-command-desc-${s}`, `battle-turn-command-preview-${s}`, `battle-turn-command-preview-icon-${s}`, `battle-turn-command-preview-members-${s}` |
| **ユニットカード（id ベース）** | `guild-candidate-card-${id}`, `guild-candidate-source/icon-slot/job/name/gender/age/stats/hp/atk/spd/total-${id}`, `guild-accept-button-${id}` |
| 同上（引退候補） | `guild-retiree-card-${id}`, `guild-retiree-rank/icon-slot/job/name/gender/age/stats/hp/atk/spd/total/reasons/lineage-badge/descendant-badge-${id}`, `guild-dismiss-button-${id}` |
| **ベンチカード** | `formation-bench-card-${id}`, `formation-bench-icon-slot/job/name/gender/age/stats/hp/atk/spd/total/married/heir/descendants/placed-badge-${id}` |
| **ユニット詳細モーダル** | `unit-detail-icon-slot-${unit.id}`, `unit-detail-ability-${job}`, `unit-detail-stat-hp/attack/speed/base-strength/growth`, `unit-detail-married-badge`, `unit-detail-heir-badge`, `unit-detail-descendants-badge` |
| **戦闘予報タイムライン** | `battle-preview-timeline-item-${i}`, `battle-preview-order/icon/label/speed-${i}` |
| **戦闘ログ** | `battle-log-row-${idx}`, `battle-log-header/initiative/enemy-action/rotation-notice/victory-mark-${idx}`, `battle-log-ally-attack-${idx}-${i}`, `battle-log-heal-${idx}-${i}` |
| **戦闘後生存者** | `battle-survivor-row-${i}`, `battle-survivor-icon-slot/name/job/hp-${i}` |
| **敵ステータスカード** | `battle-enemy-status-header`, `battle-enemy-status-icon`, `battle-enemy-status-name`, `battle-enemy-status-turn-label`, `battle-enemy-hp-row`, `battle-enemy-hp-label`, `battle-enemy-hp-bar-wrap`, `battle-enemy-hp-bar-fill`, `battle-enemy-hp-text`, `battle-enemy-stats-row`, `battle-enemy-stat-front-attack`, `battle-enemy-stat-rear-attack`, `battle-enemy-stat-speed` |
| **敵攻撃予告バナー** | `battle-enemy-intent-banner`, `battle-enemy-intent-header/icon/warning-text/body/skill-name/arrow/target-range/damage/affected-list/affected-units/affected-empty`, `battle-enemy-intent-affected-unit-${id}`, `battle-enemy-intent-affected-icon-slot/name-${id}` |
| **年代記** | `chronicle-history-item-${i}`, `chronicle-history-year-${i}`, `chronicle-history-text-${i}`, `chronicle-history-empty` |
| **ソート/フィルタ** | `roster-sort-option-${key}`, `roster-filter-job-option-${jobId}`, `roster-filter-job-option-all`, `roster-controls-count` |
| **UnitIcon フォールバック** | `unit-icon-${suffix}-fallback` |
| **フェーズインジケータ** | `phase-indicator-${phase}` |

### D-3. data-testid 凍結ポリシー（実態）

過去のリファクタ・刷新（V 字レイアウト化、ローテーション直感化、UnitIcon 親フィット化等）では一貫して以下が守られている：

- **既存 testid は同名のまま class や HTML 要素のみ変更**（例: `battle-live-grid-table` の `<table>` → `<div>` 変換時も testid 保持）
- **新規追加 testid は既存命名規約と一致させる**（kebab-case、phase/element/key の順）
- **削除や名称変更は基本行わない**

---

## E. ゲームフェーズ状態機械

`packages/frontend/src/game/GamePhase.ts`：

```ts
export type GamePhase =
  | "CHRONICLE"
  | "GUILD_MANAGEMENT"
  | "BATTALION_FORMATION"
  | "BATTLE_SIMULATION";

export const PHASE_ORDER = [
  "CHRONICLE", "GUILD_MANAGEMENT", "BATTALION_FORMATION", "BATTLE_SIMULATION"
];
```

- 一方通行・不可逆遷移（自由遷移 API なし）
- `nextPhase(current)` のみが正規の遷移手段
- BATTLE_SIMULATION → CHRONICLE 遷移時は内部で年送り（`session.advanceYear`）

---

## F. API エンドポイント一覧

`apps/api/src/server.ts` で 5 ルート群を `app.route()`：

### Game
- `POST /api/game/new` — 新規ゲーム開始（seed 指定可）
- `GET  /api/game/state` — 現在年・旅団規模等を返す

### Chronicle
- `GET  /api/chronicle` — 100 年史サマリー + 履歴
- `GET  /api/chronicle/preview` — 今年の敵プレビュー（±15% 予測レンジ）

### Guild
- `GET  /api/guild/decisions` — 採用候補・引退候補（HumanDecisionService 経由）
- `POST /api/guild/accept` — 志願者を採用 `{ unitId }`
- `POST /api/guild/dismiss` — 旅団員を解雇 `{ unitId }`

### Formation
- `GET  /api/formation/roster` — 全旅団員 + affinityMap

### Battle
- `POST /api/battle/preview` — 配置から行動順予報を返す `{ placements }`
- `POST /api/battle/init` — 戦闘初期化 `{ placements }` → enemy + nextIntent 含む
- `POST /api/battle/turn` — 1 ターン実行 `{ strategy: "NONE"|"CW"|"CCW" }`
- `POST /api/battle/run` — 一括実行（互換用）`{ placements, rotation }`
- `POST /api/battle/finish` — 戦闘後の年送り

---

## G. 主要ファイルマップ

### core
- `packages/core/src/models/Unit.ts` — Unit クラス本体
- `packages/core/src/models/Squad.ts` — Squad クラス
- `packages/core/src/models/Brigade.ts` — Brigade（旅団全体・血統 BirthRegistry 含む）
- `packages/core/src/models/Enemy.ts` — Enemy 基底クラス
- `packages/core/src/BattleManager.ts` — 1 ターンの戦闘解決ロジック
- `packages/core/src/BattleSimulator.ts` — 戦闘全体の orchestrator（DynamicEnemy・ローテーション・予告・ターン単位 API を提供）
- `packages/core/src/data/jobs.ts` — JOB_DEFAULTS / JOB_JP / JOB_ABILITY / totalRating の SoT
- `packages/core/src/data/names.ts` — NameGenerator・3 文化圏（Japanese/European/Classical）
- `packages/core/src/services/HumanDecisionService.ts` — 手動人事 API（純粋関数）
- `packages/core/src/utils/age.ts` — `rollPeakAges`, `rollChildPeakAges`
- `packages/core/src/utils/brigade.ts` — `enforceMaxBrigadeSize`
- `packages/core/src/config/ChronicleConfig.ts` — 既定設定
- `packages/core/src/config/ChronicleConfig.extreme.ts` — 本番ゲーム用設定（API が使用）
- `packages/core/src/index.ts` — re-export 集約

### apps/api
- `apps/api/src/server.ts` — Hono アプリ立ち上げ
- `apps/api/src/session.ts` — `GameSession`（インメモリ単一セッション）
- `apps/api/src/routes/{game,chronicle,guild,formation,battle}.ts` — 5 ルート

### packages/frontend
- `packages/frontend/src/main.tsx` / `App.tsx` — エントリ
- `packages/frontend/src/game/GameManager.tsx` — 4 フェーズの遷移制御
- `packages/frontend/src/game/GamePhase.ts` — フェーズ型
- `packages/frontend/src/phases/Chronicle/ChroniclePage.tsx`
- `packages/frontend/src/phases/GuildManagement/GuildManagementPage.tsx`
- `packages/frontend/src/phases/BattalionFormation/BattalionFormationPage.tsx`
- `packages/frontend/src/phases/BattleSimulation/BattleSimulationPage.tsx`
- `packages/frontend/src/components/UnitIcon.tsx` — 親要素フィット型のアイコン
- `packages/frontend/src/components/UnitDetailModal.tsx` — ユニット詳細 + V 字ミニ配置 grid
- `packages/frontend/src/components/RosterControls.tsx` — ソート・フィルタ
- `packages/frontend/src/api/client.ts` — fetch ラッパー
- `packages/frontend/src/api/types.ts` — API レスポンス型定義
- `packages/frontend/src/utils/job.ts` — フロント側のジョブヘルパー
- `packages/frontend/src/styles/global.css` — 全 CSS（V 字レイアウト・敵カード・unit-icon-slot 等を含む）

### config
- `config/jobs.json` — ジョブ能力値（コード側 `JOB_DEFAULTS` と一致を保つ）

### 静的アセット
- `image/{jobId}/{gender}.png` — 原本
- `packages/frontend/public/image/{jobId}/{gender}.png` — 配信先
- パス規約: `/image/{jobId}/{gender}.png`（`UnitIcon.getJobIconPath` が組み立てる）
- CSS で `image-rendering: pixelated` を強制適用（ドット絵のシャープさを担保）

---

## H. 主要な絶対ルール（要約・instructions.md より）

詳細は `instructions.md` を一次情報源とする。本書ではコードと照合可能な要点だけ抜粋：

- 大隊規模: **9 名（3×3）**、`SCHEDULE.BATTALION_SIZE = 9`、`BATTLE.SQUAD_SIZE = 3`
- 敵スピード成長率: `ENEMY_SCALING.SPEED_GAIN_PER_YEAR = 0.6`
- 敵ステータス: HP / ATTACK / SPEED に `±15%` の乱数（`0.85 + rng() * 0.30`）
- 新人入団・子供雇用・老兵引退は **すべて手動選択**（自動リストラ凍結）
- ハードコード禁止、`CHRONICLE_CONFIG.<SECTION>.<KEY>` 参照必須
- 全コンポーネントに `data-testid` 例外なく付与
- コミットメッセージは日本語、英語の type prefix（`feat:` / `refactor:` / `fix:` 等）を付ける
- フェーズ遷移は不可逆・一方通行
