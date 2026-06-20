# Chronicle Knights — 開発タスクリスト（C# / Godot 版）

> 対象は現役本体 `generated_csharp/`。仕様の根拠は [instructions.md](instructions.md)、実態は [CLAUDE.md](CLAUDE.md)、
> 将来戦略は [docs/MIGRATION_GODOT_HACK_AND_SLASH.md](docs/MIGRATION_GODOT_HACK_AND_SLASH.md)。
> 旧 TypeScript 版のタスク（旧 M1/M2/M3）はすべて役目を終えたため破棄した。
> 完了時は PR/コミットで本ファイルも更新すること。検収基準: `dotnet test` 653 pass を維持。

---

## ✅ 完了済み（移行マイルストーン）

ハクスラ・ローグライト移行（`docs/MIGRATION_GODOT_HACK_AND_SLASH.md` フェーズ 1〜3）の主要部は実装・通電済み。

- [x] Godot 土台一式（`project.godot` / `ChronicleKnights.csproj` / `.sln` / `Main.tscn` / autoload 登録）
- [x] 起動スイッチ（`GameDirector` → `NewGameFactory` → `ChronicleGlobal.Initialize`／`LoadGame`、タイトルゲート）
- [x] Core 純粋層（Unit / JobMaster / PointsEconomy / RosterLifecycle / TimelineEngine / Naming / Persistence）
- [x] 戦闘コア（`BattleResolver` / `BattleSnapshot` / `EnemyState` / `EnemyScaler` / `AttackIntent(Roller)` / `BattleManager.ExecuteLastHit`）
- [x] V 字 3×3 編成盤面（`FormationBoard`）＋ 無人出撃封鎖（`DeploymentGate`）＋ ローテーション
- [x] フェーズ状態機械（一方通行）＋ 行動分岐（March/Rest・`ActionPhaseRouter`）＋ 構造的ゲート群
- [x] 「1 世代＝1 周」年送り（加齢→完全ロスト→収入→予言再生成）と戦果決算（`BattleSpoils` → 経済）
- [x] 手動婚姻（有償・自然婚姻 0pt）／外様スカウト／兵器廠（装備購入・強化）
- [x] ローカライズ結線（`MasterDataNameResolver` 等で job/item/prophecy/敵スキル/章名を辞書解決）
- [x] 動的 B 型 UI（現在フェーズ画面のみ new/QueueFree・リークフリー・各種オーバーレイ・JuiceDirector）
- [x] セーブ/ロード（アトミック書き込み・DTO・スキーマ Version 1）
- [x] xUnit 653 pass（Core 全域）＋ 手動トリガー CI

---

## 🧹 直近の整理（負債解消・低リスク）

### T-1. 並走 UI（`UserInterface/`）の整理 — ✅ 完了

- [x] `UserInterface/` の死蔵 6 View（`UserInterfaceRoot`/`TitleView`/`HubView`/`BattleView`/`SettlementView` ＋
      死蔵 `Hub/ProphecyTimelineOverlay`）を**削除**（→ [CLAUDE.md](CLAUDE.md) G-3）。
- [x] `ProphecyTimelineOverlay` の二重定義（`UI/` と `UserInterface/Hub/`）を解消（現役 `UI/` のただ 1 定義に）。
- [ ] 残る現役共有（`UserInterface/JobTextureLibrary.cs` ＋ `Hub/` の D&D 部品 3 種）の配置場所を見直す（`UI/` 直下 or `Assets` 系へ）。低優先。

### T-2. ドキュメント・警告の微修正

- [ ] `generated_csharp/README.md` / `docs/VISUAL_AND_JUICE_ROADMAP.md` のテスト数「630」を実数へ追従（現 653）。
- [ ] `Tests/Core/Battle/BattleSeatingContractTests.cs:82` の xUnit2013 警告（`Assert.Equal(1, …)` → `Assert.Single`）を解消。

---

## 🎮 ゲーム拡張（中核体験の作り込み）

### T-3. 予言生成の本実装

- [ ] `TimelineEngine.DefaultGenerator`（暫定の均等巡回）を `ProphecyMaster` 相当へ置換し、種別・SkipYears・Value をバランス設計。
- [ ] 予言の `DescriptionKey` を `localization_ja.json` に登録（説明文の辞書化）。

### T-4. ハクスラ拡張（ドロップ・Affix）

- [x] Affix（接尾効果）システム MVP — `AffixMaster`（Sharp=+ATK / Sturdy=+DEF / Swift=+SPD）の生成・解決・
      戦闘合流・ドロップ付与・localization・UnitDetailOverlay 表示まで実装（`CLAUDE.md` E-5）。
- [x] `EquipmentDrop` の 3 択ドロップ UI — `EquipmentDropService` で 3 候補生成 → `EquipmentDropOverlay` で 1 つ選び持ち物へ。
- [x] 持ち物（インベントリ）システム ＋ 付け外し UI — `InventoryService`／`BrigadeInventory`（SoT・永続化 v5）／拠点 `MarriageUI` の「🎒 持ち物」セクション（非破壊な付け替え）。
- [ ] Affix 拡張 — 経済軸 Affix（婚姻ポイント倍率）・パッシブ付与型・レアリティ別個数の調整。
- [x] 旧 `EquipItem`/`UnequipItem`（conjure/discard 型の Formation ドック）＋ `EquipmentService` を撤去し、編成ドックも持ち物（`InventoryService`）ベースへ一本化。
- [x] 敵撃破ポイント（`EarnFromKill`）は不採用に決定し削除（戦闘収入は戦果決算 `BattleSpoils` に一本化）。

### T-5. アセット ＆ 見栄え（`docs/VISUAL_AND_JUICE_ROADMAP.md`）

- [ ] 背景 4 枚（dawn/upheaval/decline/twilight）＋ 敵 5 枚（archetype 別）の追加と専用ローダ
      （`BackgroundTextureLibrary` / `EnemyTextureLibrary`）。詳細 [docs/ASSET_MANIFEST.md](docs/ASSET_MANIFEST.md)。
- [ ] ヒットエフェクト・効果音などの Juice 強化。

---

## 🧪 テスト拡充

- [ ] 戦闘ライフサイクル（`StartBattle`→`ResolveBattleTurn`→`EndBattle`→`FinalizeBattleSpoils`）の統合シナリオ追加。
- [ ] セーブ/ロード往復の網羅（血統・婚姻リンク・装備の復元）。

---

## 進行管理

- 各タスクのオーナーが決まったら `[ ]` の直後に `@担当者名` を追記。
- 完了時は `[x]` ＋ コミット。仕様変更を伴う場合は先に `instructions.md` を更新。
- ブロッカーは GitHub Issues で管理。
