# System Architecture（C# / Godot 版）

> 対象は現役本体 `generated_csharp/`（Godot 4.3 mono / .NET 8 / C# 12）。
> 旧 TypeScript 版（`packages/core` 等）は凍結された参照専用で本書の対象外。
> 数値の根拠は各 SoT クラスの定数。実装の俯瞰は `CLAUDE.md`、設計理念は `docs/design_blueprint.md`。

---

## 1. 三層アーキテクチャ — 「純粋な脳」と「Godot の身体」の分離

```
  ┌─────────────┐   ① API     ┌──────────────────┐   ② 委譲    ┌─────────────┐
  │   UI 層      │ ──────────▶ │   Autoload        │ ──────────▶ │   Core 層    │
  │ UI/*.cs      │             │ ChronicleGlobal    │            │ Core/**.cs   │
  │ (Godot依存)  │ ◀────────── │ (唯一の真実 SoT)   │ ◀────────── │ (純粋ロジック) │
  └─────────────┘ ④ シグナル   └──────────────────┘ ③ 新レコード └─────────────┘
                                      ↑↓ lock 保護          Random は引数注入で再現可能
```

| 層 | 場所 | 責務 | Godot 依存 |
|---|---|---|---|
| Core | `generated_csharp/Core/` | ゲームのルール（不変ドメイン・純粋関数）。xUnit で単体検証 | なし |
| Autoload | `generated_csharp/Autoload/ChronicleGlobal.cs` | 全状態の保持・API・シグナル・セーブ/ロード | あり（Node） |
| UI | `generated_csharp/UI/` | 無状態の描画（コードで動的構築）。シグナルで読み直して再描画 | あり（Control） |

このご利益: 戦闘計算・経済・世代交代といった「心臓部」を、Godot を起動せず `dotnet test` で検証できる
（例: 狙撃兵の二連撃ダメージが 220 か、を画面なしで機械検証）。

---

## 2. 常駐 SoT（`ChronicleGlobal`）

Godot の autoload として `/root/ChronicleGlobal` に常駐し、ゲーム全体の唯一の真実を保持する。

### 2-1. 保持する状態（外部からは読み取り専用）

| 状態 | 型 | 内容 |
|---|---|---|
| `CurrentEconomy` | `PointsEconomy` | ポイント一元経済の財布 |
| `CurrentTimeline` | `TimelineEngine?` | 予言タイムラインの現在状態 |
| `BattalionRoster` | `ImmutableList<Unit>` | 大隊の全旅団員 |
| `CurrentFormation` | `FormationBoard` | V 字 3×3 編成盤面（占有 Id のみ保持） |
| `CurrentBattle` | `BattleSnapshot?` | 進行中戦闘の不変スナップショット（非戦闘時 null） |
| `LastBattleSpoils` | `BattleSpoils` | 直近戦闘の統合台帳（戦果決算） |
| `LastRestOutcome` | `RestOutcome` | 直近の休息年の決算 |
| `CurrentPhase` | `GamePhase` | フェーズ状態マシンの現在地 |
| `CurrentAction` | `PlannedAction` | 今年の行動（March / Rest） |
| `_chronicleLog`（private） | `ImmutableArray<ChronicleLogEntry>` | 旅団史ナレーション（セーブ対象） |
| `_ancestralArchive`（private） | `ImmutableDictionary<Guid,Unit>` | 英霊アーカイブ（去った祖先の遺影。非セーブ） |

### 2-2. シグナル（観測側 UI への通知）

`StateInitialized` / `EconomyChanged` / `TimelineChanged` / `RosterChanged` / `FormationChanged` /
`BattleChanged` / `PhaseChanged`。すべて const string で宣言され、`SafeEmit`（`IsInsideTree()` ガード ＋ try/catch）で
発火するため、Godot 非依存の xUnit 環境でも例外なく動く。

### 2-3. 単方向データフローの規律

1. UI が `ChronicleGlobal` の API を呼ぶ（例: `ResolveLastHit`）。
2. `ChronicleGlobal` が `lock(_stateLock)` 内で Core の純粋関数を叩き、不変レコードを「丸ごと差し替える」。
3. **ロックを解放してから** 該当シグナルを `SafeEmit`（ロック内発火はデッドロックの危険があるため厳禁）。
4. UI がシグナルを受け、SoT を読み直して再描画する。

---

## 3. ゲームループ ＆「1 世代 = 時間軸 1 周」

### 3-1. フェーズ循環（`Core/GameFlow/GamePhaseFlow`）

```
Chronicle ──▶ Guild ──▶ Formation ──▶ Battle ──┐
(年代記/予言) (拠点)     (大隊9名編成)  (戦闘)    │
     ▲                                        │
     └──────────（年送り：数年が一気に流れる）◀┘
```

`Next(current)` は「次はただ 1 つ」、`CanTransition(from,to)` は後退・飛び越し・自己遷移をすべて false。

### 3-2. 行動分岐（`Core/GameFlow/ActionPhaseRouter`）

拠点（Guild）で確定した `PlannedAction` で離脱先が変わる:
- **March**: Guild → Formation → Battle（戦う年）。
- **Rest**: Guild → Chronicle（編成・戦闘の両画面を完全バイパスする安全な年。`RestService` で休息決算）。

### 3-3. 年送り（`ChronicleGlobal.AdvanceGenerationLocked`）

予言の `SkipYears` は選択時に消費せず保留（`_pendingGenerationSkipYears`）。ループ幕引き（Battle→Chronicle、
または Rest の Guild→Chronicle）で一括適用する:

適用年数は `SkipYears` を `ChronicleTimelineConfig.ClampSkipToNextBossYear` で章ボス年（25/50/75/100）を踏み越さないよう
クランプした `years` で、加齢・収入・暦の前進すべてに同一値を用いる（「○年経過」が暦・加齢・収入で整合）:

1. 全旅団員を `years` ぶん加齢 → 寿命到達・戦闘死を完全ロストとして仕分け（`RosterLifecycle.AdvanceGeneration`）。
2. 年代記ナレーション（損失・昇級）を `_chronicleLog` へ追記、去る者を英霊アーカイブへ写し取り。
3. 盤面から完全ロスト者を掃き出し（`ReconcileFormationWithRoster`）。
4. 定期収入 `EarnFromTimeSkip(years)` を加算。
5. 暦の年 `Turn` を `years` ぶん進めて次世代の予言 3 つを再生成（`TimelineEngine.AdvanceToNextTurn(…, years)`）。
   ボス接近周は暦がボス年へちょうど着地し、次周がボス戦になる（取りこぼし防止）。

発火順: **Roster → Economy → Timeline →（必要時 Formation）→ Phase**（画面切替前にデータ確定を保証）。
出撃の幕引きでは加えて `ApplyBattleSpoils(LastBattleSpoils)` が戦果から婚姻ポイントを算出し経済へ加算する。

---

## 4. 戦闘の常駐統合

純粋層 `BattleResolver`（1 ターン解決器）を SoT へ昇格させ、3 つの薄い API が統治する:

| API | 役割 |
|---|---|
| `StartBattle(enemy, seed?)` | `BattleResolver.CreateInitial` で初期 `BattleSnapshot` を生成し `CurrentBattle` へ。`_battleRng` を再シード |
| `ResolveBattleTurn(rotation?)` | 1 ターン解決し `CurrentBattle` を差し替え、`ImmutableArray<BattleEvent>` を返す |
| `EndBattle()` | 戦闘後の複製を正本ロスタへ書き戻し、`CurrentBattle=null`（非戦闘へ） |

- とどめ: `ResolveLastHit(unitId)` → `BattleManager.ExecuteLastHit`。その後 `FinalizeBattleSpoils` が
  「開戦時 → とどめ完了後」の Guid 突合で統合台帳 `BattleSpoils` を確定する。
- 敵: `CreateCurrentYearEnemy(seed?)` が暦から原型を選び、時代スケール（`EnemyScaler`）＋ ±15% 個体差で 1 体合成。
- 攻撃予告: `ForecastEnemyIntents / ...WithOmens` が現局面から決定論シードで「運命の帯」を先読み。

詳細な戦闘・パッシブ仕様は `PROGRESS_REPORT.md` §3-4 と `docs/job_definitions.md` を参照。

---

## 5. 永続化（`Core/Persistence/`）

- `SaveSerializer`（純粋・DTO マッピング）＋ `SaveManager`（Godot I/O）に層分離。
- `user://save_data.json` に **未暗号化の整形 JSON** で保存。クラッシュ耐性のため
  アトミック書き込み（`.tmp` 書き切り → 本ファイルを `.bak` へ退避 → リネーム）。
- enum は文字列で保存（定義順変更に強い）、Guid キー辞書は文字列キー化。`Version`（現 1）でスキーマ管理。
- **保存対象**: 経済 / タイムライン / ロスタ / `_chronicleLog`。
  **非保存**: Random（ロード時に新規再注入）・盤面・戦闘・英霊アーカイブ・保留年数。
- ロード後 `CurrentPhase` は **常に Chronicle から再開**。

---

## 6. ローカライズ ＆ 命名（`Core/Naming/` `Core/Localization/`）

- 全日本語テキストの SoT は `Config/localization_ja.json`（セクション: phases / passives / squadRows /
  effectKinds / effectScopes / jobs / items / prophecyKinds / enemySkills / epochs / enemyArchetypes / names / ui / marriage）。
- `NameResolver`（キー→氏名、`@` 連結の称号付き複合キーを「称号＋名＋姓」へ連結）／ `PhaseNameResolver` ／
  `MasterDataNameResolver`（ジョブ/アイテム/予言/敵スキル/章名）が解決。`ChronicleGlobal.LoadLocalization` が
  res:// から一度だけ読み込んで各リゾルバを構築する。
- `NameGenerator` は 3 文化圏 × 性別のプールから歴史的重複を避けてキーを払い出し、枯渇時は称号複合キーへフォールバック。
- **未知キーは例外を投げず生キーを返す**（画面が落ちず、未登録キーが一目で分かる）。

---

## 7. テスト ＆ CI

- xUnit。対象は **Core 純粋層のみ**（Godot 非依存）。`ChronicleGlobal` も `SafeEmit` 隔離により Godot なしで API を検証可能。
- 現況 **681 pass / 0 fail**（`dotnet test Tests/ChronicleKnights.Tests.csproj`）。
- `Tests/ChronicleKnights.Tests.csproj` は `Core/**/*.cs` を `<Compile Include>` で取り込み、Godot 本体アセンブリを参照しない。
  `WarningsAsErrors` で 13 個の CS 警告コードをビルドエラー化（構造的品質ガード）。
- CI: `.github/workflows/dotnet-test.yml`（**手動トリガー専用** / ubuntu / .NET 8 SDK / Tests のみ）。

---

## 8. 主要ファイル早見

```
Core/Unit/Unit.cs                旅団員（不変・ステータス非保持）
Core/Job/JobMaster.cs            8 ジョブ数値 SoT
Core/Formation/FormationBoard.cs V 字 3×3 盤面
Core/Battle/BattleResolver.cs    1 ターン解決器
Core/Battle/EnemyScaler.cs       敵スケーリング（±15% 個体差）
Core/Managers/PointsEconomy.cs   ポイント一元経済
Core/Managers/RosterLifecycle.cs 世代交代（加齢・完全ロスト仕分け）
Core/Timeline/Prophecy.cs        予言レコード + 種別
Core/Chronicle/ChronicleTimelineConfig.cs  章・年数・章ボス年
Core/Persistence/SaveSerializer.cs         状態 ⇄ JSON（純粋）
Autoload/ChronicleGlobal.cs      常駐 SoT・全 API・シグナル
UI/GameDirector.cs               画面切替の司令塔（動的 B 型）
Config/localization_ja.json      全日本語テキストの辞書
```
