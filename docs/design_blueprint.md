# Chronicle Knights — 設計書 (Design Blueprint)

## コンセプト

100年間の騎士団の歴史を、世代交代を繰り返しながら紡ぐRPG。
Venus & Braves (V&B) にインスパイアされ、時間の流れと人の一生を中心に据えたゲームデザイン。

プレイヤーは騎士団長として、生まれ・老い・逝く騎士たちを束ねながら100年を生き抜く。

---

## アーキテクチャ方針

### 1. Immutable Data Structure

ユニットの状態は不変（immutable）とする。

- `Unit` インスタンスは作成後に内部状態を変更しない
- `grow()` / `takeDamage()` / `withBuffs()` / `withHeal()` などの状態遷移メソッドは、変化後の新しいインスタンスを返す
- 過去の全年齢データを履歴として保持可能（スナップショット設計）
- これによりタイムトラベル・リプレイ・デバッグが容易になる

### 2. API-First / Headless Logic

コアロジックはUI・外部APIから完全に分離する。

- `packages/core` はピュアなTypeScriptロジックのみ
- 加齢・成長・戦闘計算はすべて副作用のない純粋関数または不変クラスとして実装
- CLIやWebフロントエンドはコアロジックを呼び出すだけのアダプター層として機能する

### 3. Scalability

最初はローカル環境で動かし、将来的にクラウドへ移行できる構成。

- 初期: **Bun + SQLite** でローカル動作
- 将来: **Cloudflare Workers (Hono)** 等のエッジ環境へ移行可能
- `packages/core` がランタイム非依存であることがこれを実現する鍵

---

## ディレクトリ構成

```
chronicle-knights/
├── packages/
│   └── core/                     # ゲームエンジン（ランタイム非依存）
│       ├── src/
│       │   ├── models/
│       │   │   ├── Unit.ts       # 騎士の不変データモデル・経年変化
│       │   │   ├── Brigade.ts    # 旅団・年次進行・大隊選出
│       │   │   ├── Squad.ts      # 分隊（最大3体）
│       │   │   └── Enemy.ts      # 敵
│       │   ├── BattleManager.ts  # ターン処理・ダメージ計算
│       │   ├── BattleSimulator.ts# 高レベル戦闘シミュレーター
│       │   ├── config.ts         # 定数
│       │   └── index.ts          # パブリックAPI
│       └── test/                 # ユニットテスト
├── apps/
│   └── cli/                      # CLI シミュレーション群
│       └── src/
│           ├── simulate_history.ts        # 100年旅団シミュレーション
│           ├── simulate_brigade_battle.ts # 大隊間戦闘デモ
│           └── simulate_battle_*.ts       # 各種バトルシナリオ
├── scripts/
│   ├── run-sim.ts                # プリセット対戦CLI
│   └── age_progression_test.ts   # 経年変化動作確認
├── config/
│   ├── jobs.json                 # ジョブデフォルト値（正規ソース）
│   └── game_settings.json
└── docs/
    ├── design_blueprint.md       # このファイル
    ├── system_architecture.md    # 実装アーキテクチャ
    ├── job_definitions.md        # ジョブ仕様
    └── simulation_guide.md       # CLI 操作ガイド
```

---

## コアモデル設計

### Unit（騎士）

騎士1人を表す不変オブジェクト。年齢に応じて `stats` が動的に算出される。

| フィールド       | 型              | 説明                                |
|----------------|-----------------|-------------------------------------|
| `id`           | string          | ユニーク識別子                       |
| `name`         | string          | 騎士名                              |
| `age`          | number          | 現在年齢                            |
| `birthYear`    | number \| null  | 生まれ年（Brigade.currentYear と組み合わせ） |
| `peakStartAge` | number          | 全盛期開始年齢                       |
| `peakEndAge`   | number          | 全盛期終了年齢                       |
| `maxAge`       | number          | 引退年齢（これ以上で `isRetired = true`）|
| `baseStats`    | Stats           | 全盛期の最大能力値                   |
| `stats`        | Stats (derived) | 年齢補正後の現在能力値（getter）       |
| `job`          | JobType \| null | ジョブ（iron_wall_knight 等）        |

### 経年変化アルゴリズム（三段階モデル）

`baseStats` を全盛期の最大値とし、年齢 `a` に対して係数 `growthFactor` を算出する。

| フェーズ | 条件                                | growthFactor                       |
|---------|------------------------------------|------------------------------------|
| 修業期   | `a < peakStartAge`                  | `a / peakStartAge`（線形 0→1）      |
| 全盛期   | `peakStartAge <= a <= peakEndAge`   | `1.0` 固定                          |
| 衰退期   | `a > peakEndAge`                    | `0.97^(a - peakEndAge)`（複利 3%/年）|
| 引退     | `a >= maxAge`                       | `0`（`isRetired = true`）           |

実効ステータス:

```
stats[key] = Math.max(1, Math.round(baseStats[key] * growthFactor))
```

### Brigade（旅団）

複数の Unit を束ねる集団。年次進行と大隊選出を担う。

- `currentYear: number` を保持し、`advance(recruits)` で1年進める
- `advance` は加齢・引退判定・新兵追加を行い、新しい `Brigade` と `events`（join/retire）を返す
- `selectBattalion(n)` は `stats.strength` 上位 n 体（未引退）を返す → 戦闘前の編成に使う

### Squad / BattleManager / BattleSimulator

- **Squad**: 最大3体（`MAX_UNITS_PER_SQUAD`）。スロットID（`FRONT` / `REAR-L` / `REAR-R`）で識別
- **BattleManager**: 1戦闘のターン処理。バフリセット → tactician バフ → イニシアチブ → アクション → medic 回復
- **BattleSimulator**: BattleManager を wrap し、旅団 vs 旅団の戦闘を統計付きで実行（DynamicEnemy 経由）

詳細は [system_architecture.md](system_architecture.md) を参照。

---

## ジョブシステム

現在4ジョブが実装済み。`config/jobs.json` が正規ソース、仕様詳細は [job_definitions.md](job_definitions.md) を参照。

| Job ID              | 役割              | 主な能力               |
|---------------------|------------------|------------------------|
| `iron_wall_knight`  | 前衛防御・大隊防護  | SDF / BDF（FRONT 配置時）|
| `tactician`         | 全体バフ支援      | AB（速度・攻撃力バフ）   |
| `medic`             | 回復後方支援      | HL（ターン末分隊回復）   |
| `sniper`            | 後衛高火力        | 2連撃（条件付き）        |

---

## 今後の設計課題

- [ ] **100年タイムラインとバトルの統合** — 現状 `simulate_history`（年次進行）と `BattleSimulator`（戦闘）が分離。毎年 or 任意年に大隊選出 → 戦闘 → 損耗持ち越し のループが未実装
- [ ] **世代交代トリガー** — 戦死・師弟継承・指名後継のイベント機構
- [ ] **パーティ編成とシナジー** — ジョブ間の追加効果・分隊間の連携
- [ ] **セーブデータ構造（SQLite スキーマ）** — 旅団・ユニット履歴・戦闘ログの永続化
- [ ] **Web フロントエンド** — API-First 方針に沿ったアダプター層
