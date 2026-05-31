# Chronicle Knights — 開発の絶対ルール（Instructions）

> このドキュメントはプロジェクトの**永続的な指示書**であり、フロントエンド・バックエンド双方の実装で必ず遵守する。
> ここに記された方針はメタ分析の結果と過去の意思決定の集約であり、**個別タスクで本指針を無断で変えてはならない**。
> 仕様変更が必要な場合は本ドキュメントを先に更新し、その差分と理由を PR/コミットメッセージに明記すること。

---

## 0. ゲームの最終目標

**Chronicle Knights は「世代交代型ローグライクRPG」である。**

- プレイヤーは騎士団長として、100年間の旅団史を紡ぐ
- 個々の騎士は生まれ・育ち・全盛期を迎え・衰退し・引退する
- 結婚・出産・継承によって家系が紡がれ、子は親を超え、また衰退する
- **戦闘の勝敗は重要だが、それ以上に「誰を残し、誰を切り、誰を継ぐか」というプレイヤーの選択が物語を作る**

参考: Venus & Braves（時の流れと人の一生を中心に据えたゲームデザイン）。

---

## 1. バトル仕様の絶対ルール

### 1-1. 大隊の規模: **9名（3人 × 3分隊）**

| 項目 | 値 |
|---|---|
| `BATTLE.SQUAD_SIZE` | **3** |
| `BATTLE.FRONT_ROW_COUNT` | **3** |
| `SCHEDULE.BATTALION_SIZE` | **9** |

#### 理由

- 前回ギルドモードで採用した **12名（4×3）** は「全員入る」のジレンマが薄かった
- 9名に絞ることで「誰を出して誰を残すか」の編成緊張感が極大化する
- 3×3 マスのグリッド UI とも自然に合致

#### 禁止事項

- BATTALION_SIZE を 9 以外に**勝手に変更してはならない**（ギルドモードでも 9）
- 旧 extreme モードの 12 名は本ルールにより**廃止**

### 1-2. 敵スピード成長率: **+0.5 〜 +0.8 / 年**（緩和）

| 項目 | 旧値 | **新値** |
|---|---:|---:|
| `ENEMY_SCALING.SPEED_GAIN_PER_YEAR` | 1.5 | **0.6（既定推奨）** |

#### 理由

- 前回検証で +1.5 は過剰デフレ（Y51 以降勝率 0%、勝率平均 21%）
- 「努力すれば 100年目でも先制を取れる」**熱い速度調整**にしたい
- 想定: Y100 で敵 SPD = 100 + 100×0.6 = **160**
- 味方最速 `scout(60) × 全盛期 × 旗手+40 × 戦術官+20` = 60 + 60 = **120**（基本）
- 血統素体値ボーナス・装備・編成シナジー等で +40 → **160 ≧ 敵** に届く設計余地
- プレイヤーが工夫すれば「老兵の最終決戦」も勝てる可能性を残す

#### 禁止事項

- `SPEED_GAIN_PER_YEAR` を 1.5 に戻してはならない
- 0.5 未満（緩すぎ）・0.8 超（強すぎ）も同様

### 1-3. 敵ステータスの**乱数化**: ±15% の振れ幅

#### 仕様

`makeTrialEnemy(year, rng)` を以下のように改める:

```typescript
// 旧: 固定値
const hp = BASE_HP + year * HP_GAIN_PER_YEAR;

// 新: ランダムバリエーション
const baseHp = BASE_HP + year * HP_GAIN_PER_YEAR;
const hp = Math.round(baseHp * (0.85 + rng() * 0.30)); // ±15%
```

- 各個体ごと（10体すべてに対して独立に rng を適用）
- HP / ATTACK / SPEED の全てに適用
- **±15% の振れ幅**: BASE の 85%〜115% でランダム

#### 理由

- 固定値だと「Y100の敵 = 必ず HP650/ATK90/SPD250」と予定調和になる
- 乱数化で「弱めの個体ばかりの幸運な戦闘」「平均より強い不運な戦闘」が生まれ、戦況に揺らぎが出る
- ローグライクの根幹「一度きりの賽の目」を戦闘にも導入する

#### 禁止事項

- 固定値計算（`BASE + year × GAIN`）のままの実装で commit してはならない
- 振れ幅 0%（決定的）も同様

---

## 2. 人事権の絶対ルール（最重要）

### 2-1. 自動リストラの**完全廃止**

`utils/brigade.ts` の `enforceMaxBrigadeSize` を**自動運用してはならない**。

#### 旧仕様（禁止）

```typescript
// ❌ 自動で弱者・老兵を機械的に除名する
brigade = enforceMaxBrigadeSize(brigade, MAX_BRIGADE_SIZE).brigade;
```

#### 新仕様

- `enforceMaxBrigadeSize` 関数自体は残してよい（API として）
- **本番の年次ループから直接呼ぶことを禁止**
- 代わりに、定員超過時は**プレイヤー選択フェーズ**を発火させる

### 2-2. プレイヤー選択フェーズ（人事フェーズ）

以下の3つは**すべてプレイヤーの選択**として実装する:

1. **新人の入団承認** — 志願者リストから採用する/しない
2. **子供の雇用判断** — 15歳継承者を旅団に加える/独立させる
3. **老兵の引退勧告** — 衰退期ユニットを残す/クビにする

#### バックエンドの責務

- 「次のターンに必要な判断リスト」を返す API/関数を提供する
- 例: `getPendingDecisions(brigade)`: 入団希望者・引退候補・継承予定者を構造化して返す
- プレイヤーが選択した結果を受けて `brigade.applyHumanDecisions(...)` のような関数で状態遷移

#### ゲーム性

- **能力が低くても血統DNAを持つ者**を残すか、**目先の戦力**を取るかの苦渋の決断
- 「育てた子供を泣く泣くクビにする」「年老いた英雄を最後まで戦わせる」というロールプレイ
- これがゲームの **核心的な楽しさ** であり、自動化してはならない

### 2-3. 移行戦略

- 既存のメタ分析スクリプト（`meta-analyze-grand-chronicle.ts` / `meta-analyze-guild.ts`）は引き続き自動運用してよい（バランス測定用）
- ただし本番ゲームループ（`run-grand-chronicle.ts` の将来形）では **完全手動** に切替
- 過渡期は `--auto` フラグで「自動リストラ（テスト用）」「手動（本番想定）」を切替可能にする

---

## F. フロントエンド実装の絶対ルール（M2 以降必須）

### F-1. 厳格な状態遷移（4フェーズステートマシン）

ゲームループは以下の **4フェーズを不可逆かつ一方通行** で遷移する。

```
CHRONICLE（年初）
   ↓
GUILD_MANAGEMENT（人事）
   ↓
BATTALION_FORMATION（編成）
   ↓
BATTLE_SIMULATION（戦闘）
   ↓
（戦闘完了後、次年の CHRONICLE へ戻る）
```

#### 必須要件

- **前のフェーズへの後退は禁止**（後悔はゲームの一部）
- 各フェーズには **完了条件（`canProceed: boolean`）** を必ず定義
- 完了していない場合、**次へ進むボタンは必ず `disabled`** にする
  - 例1: GUILD_MANAGEMENT で定員超過のまま → 次へボタン disabled
  - 例2: BATTALION_FORMATION で 9名未満 / 空スロット残あり → 次へボタン disabled
  - 例3: API 通信中（pending） → disabled
  - 例4: BATTLE_SIMULATION の戦闘実行中 → disabled
- 不正な遷移を **コードレベルでガード**（`nextPhase()` 関数が条件不成立時に no-op）

#### 禁止事項

- `setPhase(任意)` のような自由遷移 API を**作ってはならない**
- 「戻る」ボタンを設置してはならない（ロールバック禁止）
- フェーズ順序を変更してはならない（4フェーズ固定）

### F-2. 全コンポーネントへの `data-testid` 強制付与

将来の CI/CD・Playwright 等の自動テスト導入を見据え、**全てのコンポーネントに `data-testid` を付与**する。

#### 付与対象（例外なし）

- **全ての React コンポーネントのルート要素**
- **全てのボタン**（`<button>`, クリッカブル `<div>` 等）
- **全ての入力フォーム**（`<input>`, `<select>`, `<textarea>`）
- **全てのカード**（ユニット表示、敵情報等）
- **全ての主要データ表示セル**（テーブル行・グリッドセル）

#### 命名規則

```
data-testid="[フェーズ名または共通]-[要素の種類]-[固有名詞やID]"
```

| 種別 | 例 |
|---|---|
| ページのルート | `chronicle-page-root`, `guild-management-page-root` |
| ボタン（個別ID付き） | `guild-accept-button-${unitId}`, `guild-dismiss-button-${unitId}` |
| グリッドセル | `formation-grid-cell-${row}-${col}` |
| データカード | `unit-card-${unitId}`, `enemy-preview-card-${index}` |
| フェーズ進行 | `next-phase-button`, `phase-indicator-${phase}` |
| 共通 UI | `shared-modal-${name}`, `shared-tooltip-${target}` |

#### 禁止事項

- `data-testid` の付け忘れ（PR レビューで必ず指摘）
- 命名規則違反（キャメルケース禁止、`-` 区切り必須）
- 動的 ID を含む要素で `${id}` を省略すること（同名複数要素は不可）

### F-3. 状態管理の純粋性

- `packages/core` の純粋関数 API（`HumanDecisionService` 等）を**そのまま呼び出す**
- フロント独自のビジネスロジック実装は禁止（コアに集約）
- 状態の変更は必ず `reducer` または `service` 経由（直接 setState で複雑な分岐を書かない）

### F-4. テスト容易性のための DOM 構造

- リスト表示は `<ul>` / `<table>` 等の意味的タグを使う
- 動的な要素には必ず `key` と `data-testid` 両方を付ける
- インラインスタイルよりクラス名（`className`）優先（Playwright のセレクタが安定する）

---

## 3. 既に固定されているルール（変更禁止）

以下は本ドキュメント以前に確立済みのルール。今回の指示書では再確認のみ。

### 3-1. コーディング規約（[.claude/skills/project_conventions.md](.claude/skills/project_conventions.md)）

- イミュータビリティ厳守（Unit/Brigade は新インスタンス返却）
- `Math.max(MIN_STAT_VALUE, ...)` 必須
- 乱数は DI（`rng: () => number`）

### 3-2. Config 単一 SoT（[.claude/skills/chronicle_config.md](.claude/skills/chronicle_config.md)）

- ハードコード禁止、`CHRONICLE_CONFIG.<SECTION>.<KEY>` 参照必須
- 個体差は `utils/age.ts` の `rollPeakAges` ヘルパー経由

### 3-3. ユニット生成（[.claude/skills/unit_generation.md](.claude/skills/unit_generation.md)）

- 性別ランダム必須
- 名前は `NameGenerator.pick(origin, gender, historical)` 経由
- 「Jr./II世/(2)」式の記号的重複回避は厳禁

### 3-4. シミュレーションレポート（[.claude/skills/simulation_reporting.md](.claude/skills/simulation_reporting.md)）

- シミュ実行時は必ず `reports/<sim_name>_<YYYY-MM-DD>_<HH-MM-SS>.md` 形式で保存

### 3-5. コミットメッセージ（[.claude/skills/commit_conventions.md](.claude/skills/commit_conventions.md)）

- 日本語、type プレフィックス（feat/fix/docs 等）のみ英語

---

## 4. 適用先のチェックリスト

仕様変更時、以下すべてを確認してから commit すること:

- [ ] `packages/core/src/config/ChronicleConfig.ts` （デフォルト）
- [ ] `packages/core/src/config/ChronicleConfig.extreme.ts` （ギルドモード）
- [ ] `scripts/run-grand-chronicle.ts` （本番想定）
- [ ] `scripts/meta-analyze-grand-chronicle.ts` （メタ分析）
- [ ] `scripts/meta-analyze-guild.ts` （ギルドメタ分析）
- [ ] `docs/system_architecture.md` （アーキテクチャ説明）
- [ ] `instructions.md` （本ファイル）
- [ ] `TODO.md` （タスクリスト）

---

## 5. ドキュメント更新履歴

| 日付 | 変更内容 |
|---|---|
| 2026-05-30 | 初版作成。大隊9名・敵スピード緩和・乱数化・人事権委譲を固定 |
| 2026-05-30 | フロントエンド絶対ルール（F-1〜F-4）を追加。4フェーズ厳格遷移と data-testid 強制付与 |
