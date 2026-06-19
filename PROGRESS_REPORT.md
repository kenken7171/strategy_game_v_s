# 📜 Chronicle Knights — 開発進捗レポート（C# / Godot 版）

> **更新日時**: 2026-06-18
> **対象コードベース**: `generated_csharp/`（Godot 4.3 mono / .NET 8 / C# 12）
> **規模**: 約 123 C# ファイル（うち Core 48 / Tests 52）／ 約 32,000 行
> **検収**: `dotnet test` → **653 pass / 0 fail**。`godot 4.3.stable.mono` ＋ `dotnet 10.0.301` で実機起動可能。
> **基準コミット**: `abc249b`
>
> ※ 旧 TypeScript 版（`apps/` `packages/` `scripts/`）は凍結された参照専用で本レポートの対象外。
> ※ 2026-06-10 版レポートは「Godot 土台も戦闘も無い」と記録していたが、それらは**すべて実装・通電済み**になった
>   （本版はその全面改訂）。

---

## 1. このゲームが目指すもの（コア思想）

『ヴィーナス＆ブレイブス』にインスパイアされた **世代交代型シミュレーション RPG** を、
**ハクスラ／ローグライト** と融合させた作品。プレイヤーは英雄個人ではなく **「旅団」と血脈** を導く。

**コア思想「1 世代 ＝ 時間軸 1 周」**: 年代記で選んだ予言の `SkipYears`（2〜4 年）ぶんの未来を 1 周のループ
（年代記→拠点→編成→戦闘）で戦い抜き、ループ幕引きで一気に時が流れる。全員が加齢し、寿命/戦死者は完全ロストし、
収入が入り、次の予言が提示される。30 周回せばそれが旅団の 30 年史になる。

---

## 2. 開発の四柱（設計憲法）

| 憲法 | 要旨 |
|---|---|
| ① 厳格 ASCII | ロジックは日本語を知らない。表示文字列は `Config/localization_ja.json` に完全分離。略称（BDF/SDF/AB/HL）廃止 |
| ② 不変性 | ドメインは `record` + `with`。コレクションは Immutable。破壊的変更禁止 |
| ③ 単一 SoT ＋ 単方向 | 状態は `ChronicleGlobal` のみ。UI は API を呼びシグナルで読み直す。`lock` 内変更・解放後 `SafeEmit` |
| ④ 完全決定論 | 1 シードで同一の歴史。Random は引数注入・非保存 |

アーキテクチャは「純粋な脳（`Core/`）／唯一の真実（`Autoload/ChronicleGlobal`）／ Godot の身体（`UI/`）」の三層分離。
詳細は `docs/system_architecture.md`、実態の俯瞰は `CLAUDE.md`。

---

## 3. 各システム仕様のディテール

### 3-1. 💰 経済（`PointsEconomy`）

単一通貨「ポイント」を全消費（スカウト・婚姻・装備）が共有。状態は 3 値（残高/累計獲得/累計消費）。
収入: `EarnFromTimeSkip(years)=years×1` ／ `EarnFromKill(lvl)=floor(lvl×1.5)` ／ `EarnDirect(delta)`。
`SpendPoints` は残高不足で `InvalidOperationException`（マイナス残高は構造的に発生しない）。

### 3-2. 🔮 予言タイムライン（`TimelineEngine` / `Prophecy`）

毎ターン必ず 3 つ提示し 1 つ選ぶ。`ProphecyKind`: `RewardPoints` / `Battle` / `ScoutReward` / `EquipmentDrop` / `Rest`。
各予言は `SkipYears`（時間スキップ）＋ Kind/Value（報酬）を持つ。選択時は消費せず保留し、年末の決算で確定する:
SkipYears は加齢・収入・暦の前進へ（ボス年スナップ込み）、報酬（Kind/Value）は `RestService.Resolve` が現金化する
（RewardPoints→ポイント / ScoutReward→無償新人加入 / EquipmentDrop→装備ドロップ / Rest→固定 +2 / Battle→0）。
**リロール不可を型で保証**: 次ターン予言を保持するフィールドが存在せず、`AdvanceToNextTurn` でしか生成できない。
※ 生成器 `DefaultGenerator` は暫定（均等巡回）。`ProphecyMaster`（バランス調整版）への置換が今後の課題。

### 3-3. 👥 ロスター ＆ 世代交代（`Unit` / `RosterLifecycle`）

C# 版 `Unit` は **HP・ステータスを持たない経歴書**（数値は `JobMaster` で動的解決）。保持は
`Id/Job/Age/MaxAge/Level(1〜3)/Origin/Gender/名前キー/装備/好感度/IsDead/血統/配偶者`。
`RosterLifecycle.AdvanceGeneration`: 生存者を加齢 → `IsRemovedFromRoster`（戦闘死 or 寿命到達）で現役/離脱に仕分け。
**完全ロスト**: 離脱者は不可逆（装備も同時喪失）。順序保持・エッジケースはテストで固定。

### 3-4. ⚔️ 戦闘 ＆ ジョブパッシブ（`BattleResolver` / `BattleManager` / `JobMaster`）

8 ジョブの能力値 SoT は `JobMaster.All`（数値表は `docs/job_definitions.md`）。パッシブ解決:
1. 行動順構築（実効速度降順・同速は味方優先）
2. 号令ブロードキャスト（InitiativeBuff 持ちが自分以外へ「速度＝Speed / 攻撃＝InitiativeBuff」加算）
3. 連続攻撃（先頭かつ `ConsecutiveStrike` 保持時のみ 2 回）
4. ダメージ軽減（`max(1, baseDamage − 大隊守護 − 分隊守護)`）
5. 継続回復（ターン末に衛生兵が自分隊を上限クランプ回復）

戦闘ライフサイクルは `ChronicleGlobal` が常駐統合: `StartBattle` → `ResolveBattleTurn` → `EndBattle`。
とどめ `ExecuteLastHit`（成長/装備強化/Lv5 装備の運命/強欲の古銭）後に `FinalizeBattleSpoils` が統合台帳を確定。
敵は `EnemyScaler`（Base HP150/ATK30/SPD100、年率 5.0/0.6/0.6、±15% 個体差）＋ 章ボス（25/50/75/100 年）。

### 3-5. 💍 婚姻 ＆ スカウト ＆ 兵器廠

- 婚姻（`MarriageService`）: コスト `ceil((父Rating×倍率 + 母Rating×倍率)/20)`。双方向好感度 ≥150 なら自然婚姻 0pt。
  子は Lv1・ジョブ/Origin 50:50 継承・名前自動払い出し。男女ペア限定。
- スカウト（`ScoutService`）: 年齢 16〜28・寿命 55〜75・乱数ジョブの外様を有償採用。残高不足は null。
- 兵器廠（`ShopService`）: 購入 `BuyCost=5`、強化 `2×現Lv`。装備は 5 種・Lv1〜5・レベル倍率 `{1.2,1.3,1.4,1.5}`。

### 3-6. 📛 命名 ＆ ローカライズ

ASCII キー → `localization_ja.json`。`NameGenerator`（3 文化圏 × 性別、枯渇時は称号 `@` 複合キー）、
`NameResolver` / `PhaseNameResolver` / `MasterDataNameResolver` が解決。未知キーは例外を投げず生キーを返す。

### 3-7. 🔁 フェーズ ＆ 行動分岐

`GamePhaseFlow`（Chronicle→Guild→Formation→Battle の一方通行）＋ `ActionPhaseRouter`（March→編成/戦闘・Rest→年代記）。
構造的ゲート: `DeploymentGate`（無人出撃封鎖）/ `BattleProgressGate`（戦闘スキップ封鎖）/ `MayGenerateEnemy`（敵生成隔離）。

### 3-8. 💾 永続化

`user://save_data.json`（未暗号化整形 JSON・アトミック書き込み・DTO・enum 文字列・Version 1）。
保存: 経済/タイムライン/ロスタ/旅団史。非保存: Random/盤面/戦闘/英霊アーカイブ。ロードは常に Chronicle 再開。

---

## 4. 実装状況マトリクス

凡例: 🟢 完了 ／ 🟡 簡易・要調整 ／ 🔴 未着手

### 4-1. Core 層

| システム | 状態 |
|---|---|
| ポイント経済 / 世代交代 / ジョブ SoT / 戦闘解決 / パッシブ | 🟢 |
| 婚姻 / スカウト / 兵器廠・装備 / 命名・ローカライズ / フェーズ状態機械 / 永続化 | 🟢 |
| 戦闘シミュレータ（`BattleResolver`・盤面・敵・攻撃予告・スケーリング） | 🟢（2026-06-10 版の「🔴 皆無」から実装完了） |
| 予言の報酬適用（RewardPoints/ScoutReward/EquipmentDrop/Rest を年末決算で現金化） | 🟢 `RestService.Resolve` |
| 予言生成マスター（`ProphecyMaster`） | 🟡 `DefaultGenerator` 暫定（均等巡回） |
| Affix（接尾効果）システム | 🔴 |

### 4-2. Autoload 層

| 機能 | 状態 |
|---|---|
| `ChronicleGlobal` 全 API（初期化/LH/予言/婚姻/スカウト/兵器廠/装備/フェーズ/戦闘/年送り/休息/ローカライズ/セーブ） | 🟢 |
| スレッド安全・ヌル安全（lock 規律・SafeEmit・例外握り潰し） | 🟢 |
| 新規ゲームのブートストラップ（`NewGameFactory` → `Initialize`、タイトルゲート） | 🟢（旧版の「🔴 誰も呼ばない」を解消） |

### 4-3. UI 層（現役 `UI/`・コードで動的構築）

| 画面 | 状態 |
|---|---|
| `GameDirector`（動的 B 型・現在フェーズ画面のみ new/QueueFree） | 🟢 |
| `TimelineUI`（年代記）/ `MarriageUI`（拠点）/ `FormationUI`（▲ウェッジ編成）/ `BattleUI`（ターン戦闘→とどめ→決算） | 🟢 |
| オーバーレイ（ジョブマニュアル/ユニット詳細/家系図/運命の帯/休息報酬/とどめ演出/戦果決算）・JuiceDirector | 🟢 |
| ローカライズ結線（job/item/prophecy 名は辞書解決・コードに日本語なし） | 🟢（旧版のハードコード課題を解消） |

### 4-4. ⚠ 既知の問題

- ✅ **並走 UI `UserInterface/` のデッドコードは粛清済**: 死蔵 6 View（`UserInterfaceRoot`/`TitleView`/`HubView`/
  `BattleView`/`SettlementView` ＋ 死蔵 `Hub/ProphecyTimelineOverlay`）を削除し `ProphecyTimelineOverlay` の二重定義を解消。
  残るのは現役共有のみ（`JobTextureLibrary.cs` ＋ `Hub/` の D&D 部品 3 種）。詳細 `CLAUDE.md` G-3 / `TODO.md` T-1。
- ドキュメント微差: 一部ドキュメントのテスト数表記が古い場合あり（正は `dotnet test` 実測値）。
- `Tests/Core/Battle/BattleSeatingContractTests.cs:82` の xUnit2013 警告（`Assert.Single` 推奨。WarningsAsErrors 対象外で無害）。

### 4-5. アセット

ジョブ立ち絵 16 枚は配置済み。背景 4 枚 / 敵 5 枚は要追加（専用ローダも未実装）。詳細 `docs/ASSET_MANIFEST.md`。

---

## 5. 総括

| 観点 | 評価 |
|---|---|
| コアロジック（脳） | 🟢 不変設計・ロック規律・単方向フロー・年送り・パッシブ厳密検証まで堅牢（653 テスト緑） |
| Godot 外殻（身体） | 🟢 土台一式・起動スイッチ・実機起動・動的 B 型 UI が通電済み（旧版の最大ボトルネックは解消） |
| 本物の戦闘 | 🟢 盤面・敵・スケーリング・攻撃予告・ターン進行・とどめ・戦果決算まで実装 |
| 残課題 | ▶ `ProphecyMaster` 本実装 / Affix・ドロップ / 背景・敵アセット / 年送り反映後のバランス再検証 |

「壊れない脳」と「それを宿す箱」は揃い、世代交代の循環を端から端まで手で回せる段階に到達した。
並走 UI の死蔵コードは粛清済。次は中毒性の中核（ドロップ・配合・見栄え）の作り込みに進む。

---

*— 本レポートは `generated_csharp/` のスキャンとグリーンビルド（653 pass）に基づき、コードの実態のみを記録した。
将来構想は `docs/MIGRATION_GODOT_HACK_AND_SLASH.md` / `docs/VISUAL_AND_JUICE_ROADMAP.md` を参照。*
