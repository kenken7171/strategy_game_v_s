# CLAUDE.md — Chronicle Knights プロジェクト現状ドキュメント

> このドキュメントは **本リポジトリの「現在のありのままの設計図」** を記録する。
> 提案・将来構想は含まない。コードを読み取って実態のみを写す（将来構想は
> `docs/MIGRATION_GODOT_HACK_AND_SLASH.md` / `docs/VISUAL_AND_JUICE_ROADMAP.md` 側）。
>
> 一次仕様書（絶対ルール）は `instructions.md`。本書は「コードの実態」、instructions.md は
> 「守るべきルール」と役割が分かれている。
>
> 最終更新の根拠: バランス再検証（100 年シミュを実機の年送りループ＝予言 SkipYears・休息混在・章ボス強制出撃へ忠実化。旧「1 年 1 戦」前提を撤廃し絶滅は `ChronicleMetrics.Extinct` の明示フラグへ。新ループで一度 100% 絶滅が露見→模型を再調律し黄金均衡（絶滅率 0%・章ボス傾斜壁）を回復）＋ 戦場アセット基盤の通電（背景は `GameDirector` が全フェーズ共通で最背面・敵は `BattleUI`）＋ 半透明コンテンツカード／ 検収: `dotnet test` 756 pass / 0 fail。

---

## 0. 最重要 — このリポジトリは「2 つの時代」が同居している

| 区分 | 場所 | 実態 | 扱い |
|---|---|---|---|
| **現役の本体** | **`generated_csharp/`** | **Godot 4.3 (.NET/mono) ／ .NET 8 ターゲット ／ C# 12**。実機起動可能・756 テスト緑 | **すべての新規実装はここ** |
| 凍結された旧本体 | `apps/`・`packages/`・`scripts/`・`config/jobs.json`・`tools/` | Bun + Hono + React + Vite の TypeScript 版。もうゲームには一切繋がっていない | **参照専用（変更禁止・原則放置）**。アーキタイプ検証の歴史的レファレンス |

旧 TS 版は「先に TS で検証 → C# へ翻訳」というかつてのフローの名残で、今は完全に役目を終えている。
**本書の B 章以降はすべて `generated_csharp/`（C# 版）について記述する。** 旧 TS 版の構造を知りたい場合は
git 履歴（`abc249b` 以前）と各旧ファイルのコメントを参照する。

---

## A. システムアーキテクチャ概要

### A-1. プロジェクト構成（`generated_csharp/`）

```
generated_csharp/
├── project.godot                Godot プロジェクト定義（main_scene=Main.tscn / autoload=ChronicleGlobal）
├── Main.tscn                    起動シーン（ルート Control に UI/GameDirector を結ぶ唯一の .tscn）
├── ChronicleKnights.csproj      本体ビルド定義（Godot.NET.Sdk/4.3.0 / net8.0 / RollForward=LatestMajor）
├── ChronicleKnights.sln         本体 + Tests を束ねるソリューション
├── play.command                 Mac 用ワンクリック起動ランチャ（C# ビルド → godot 実機起動）
│
├── Core/        ★純粋ゲームロジック（Godot を一切知らない「脳」。xUnit で単体検証可）
├── Autoload/    ☆ChronicleGlobal.cs — 常駐 SoT（唯一の真実）。Godot 依存の中枢
├── UI/          ☆現役 UI（Godot Control をコードで動的構築。.tscn は Main 以外不使用）
├── UserInterface/  現役の共有部品のみ（JobTextureLibrary ＋ Hub の D&D 部品 3 種。死蔵 View は粛清済。後述 G-3）
├── Config/      localization_ja.json（全日本語テキストの唯一の辞書）
├── Assets/Textures/{Jobs|Backgrounds|Enemies}/  ジョブ立ち絵16枚＋背景4・敵5（背景/敵は原色プレースホルダ）
└── Tests/       xUnit 単体テスト（Core を対象。756 pass）
```

### A-2. 技術スタック

| レイヤ | 技術 | バージョン |
|---|---|---|
| ゲームエンジン | **Godot Engine**（.NET / mono 版必須） | 4.3 stable mono |
| ランタイム | **.NET** | ターゲット `net8.0`、`RollForward=LatestMajor`（手元の .NET 10 でも動作） |
| 言語 | **C#** | 12（nullable 有効・record 多用） |
| ビルド SDK | **Godot.NET.Sdk** | 4.3.0 |
| テスト | **xUnit** | Core 純粋層のみ対象（Godot 非依存） |
| UI | **Godot Control ノードをコードで動的構築**（`.tscn` は `Main.tscn` 1 枚のみ） | — |

### A-3. 三層アーキテクチャ（脳・SoT・身体の分離）

```
  ┌─────────────┐   ① API を呼ぶ    ┌──────────────────┐   ② 委譲   ┌─────────────┐
  │   UI 層      │ ───────────────▶ │   Autoload       │ ────────▶ │   Core 層    │
  │ (Godot依存)  │                  │ ChronicleGlobal   │           │ (純粋ロジック) │
  │ UI/*.cs      │ ◀─────────────── │ (唯一の真実 SoT)  │ ◀──────── │ Core/**.cs   │
  └─────────────┘  ④ シグナルで通知 └──────────────────┘ ③ 新レコード └─────────────┘
                                          ↑↓ lock で状態保護          Random は引数注入で再現可
```

- **Core/** は `Godot.*` を一切 import しない純粋 C#。`dotnet test` で画面なしに検証できる。
- **Autoload/ChronicleGlobal** だけが全状態を握り、UI からの API 呼び出しを受けて Core の純粋関数を叩き、
  結果を「丸ごと差し替え」てシグナルを発火する（単方向データフロー）。
- **UI/** は状態を持たず（無状態 UI）、シグナルを受けるたびに SoT を読み直して再描画する。

### A-4. コマンド実行規約（すべて `generated_csharp/` から）

| コマンド | 内容 |
|---|---|
| `dotnet build ChronicleKnights.csproj --configuration Debug` | 本体ビルド（Godot.NET.Sdk） |
| `dotnet test Tests/ChronicleKnights.Tests.csproj` | xUnit 全テスト（**756 pass / 0 fail**）。net8 ターゲットを RollForward で net10 上実行 |
| `./play.command` | C# 自動ビルド → `godot --path .` で実機起動 |
| `./play.command -e` | Godot エディタを開く |
| `godot --path .` | （ビルド済み前提で）直接起動 |

- `godot` は **.NET（mono）版** が必須（`godot --version` が `4.3.stable.mono.official` を返すこと）。
- macOS の `--headless` は Godot 4.3 既知の不具合（`recursive_mutex lock failed`）でクラッシュするため、
  画面確認は必ず windowed 起動で行う（CI でもヘッドレスは避ける）。

---

## B. Unit クラス / 型定義の現在の設計

実体: `generated_csharp/Core/Unit/Unit.cs`（namespace は型名衝突回避で `ChronicleKnights.Core.Units`）

### B-1. 設計の核心 — Unit は「数値ステータスも HP も持たない」

TS 版の `Unit` は HP・攻撃力・速度を自前で保持していたが、**C# 版は意図的にそれらを捨てた**。
`Unit` が持つのは「個体の履歴・属性」だけ。戦闘数値は `unit.Job` から `JobMaster.All[Job].Stats` で
**動的解決**する（数値の SoT は `JobMaster` ただ 1 つ）。

| プロパティ | 型 | 説明 |
|---|---|---|
| `Id` | `Guid` | 一意 ID。`BattleAffinity` のキー・盤面占有・突合の基準 |
| `Job` | `JobId` | ジョブ enum。HP/ATK/SPD/パッシブは本値から JobMaster で解決 |
| `Age` | `int` | 現在年齢。`WithAgeProgress(years)` で一気に加算（タイムスキップ） |
| `MaxAge` | `int` | 寿命。`Age >= MaxAge` で `HasReachedMaxAge`（完全ロスト対象） |
| `Level` | `int` | 1〜`MaxUnitLevel`(=3)。`WithLevelUp(out overflow)` で +1（上限時 overflow） |
| `Origin` | `Origin` | 命名文化圏（Japanese/European/Classical）。既定 European |
| `Gender` | `Gender` | Male/Female。婚姻は男女ペア限定。既定 Male |
| `FirstNameKey` | `string` | ASCII の名前引き当てキー（例 `name-japanese-male-007`） |
| `LastNameKey` | `string` | ASCII の姓引き当てキー |
| `MainEquipment` | `Equipment?` | 装備（5 大アイテム。null=未装備）。戦闘死で同時ロスト |
| `BattleAffinity` | `IReadOnlyDictionary<Guid,int>` | 自然婚姻ポイント（好感度）。**結婚条件には不使用**（後述 E-3） |
| `IsDead` | `bool` | 戦闘死フラグ。一度 true なら永久に旅団から失われる（完全ロスト） |
| `Parentage` | `Parentage?` | 父母 Id（婚姻で生まれた子のみ非 null）。家系図の縦軸の鍵 |
| `SpouseId` | `Guid?` | 配偶者 Id（婚姻成立で双方向リンク） |

### B-2. 派生プロパティ（純粋な読み取り）

| ゲッタ | 内容 |
|---|---|
| `IsAlive` | `!IsDead && !HasReachedMaxAge` |
| `HasReachedMaxAge` | `Age >= MaxAge`（タイムスキップを跨ぐため等号ではなく以上） |
| `IsAtMaxLevel` | `Level >= 3` |
| `IsRemovedFromRoster` | `IsDead || HasReachedMaxAge`（世代交代で外す判定） |
| `CanRetire` | `IsAlive && Level >= 3`（**Lv3 限定引退ルール**） |
| `HasEquipment` / `EquippedItemId` | 装備の有無 / 種別の射影（SoT は MainEquipment、ItemId は派生のみ） |
| `HasParentage` / `IsMarried` | 血統リンク / 婚姻の有無 |

### B-3. 不変更新メソッド（すべて新インスタンスを返す `record` + `with`）

`WithAgeProgress(years)` / `WithLevelUp(out overflow)` / `WithAddedAffinity(id, amount)` /
`WithEquipment(equip?)` / `MarkDeadInBattle()` / `WithParentage(fatherId, motherId)` / `WithSpouse(spouseId)`

### B-4. ハクスラ・ローグライト 4 大仕様（Unit に直結）

- **完全ロスト**: 戦闘死・寿命到達したユニットは旅団から永久に失われる。装備も同時ロスト。復活手段なし。
- **レベル上限 3**: `WithLevelUp` は Lv3 で overflow=true（余剰経験値は別系統へ）。
- **Lv3 限定引退**: 明示引退の対象になれるのは Lv3 の生存者のみ。
- **タイムスキップ加齢**: 予言の `SkipYears`（2〜4 年）ぶん `Age` が一気に進む（年 1 ずつではない）。

---

## C. ジョブシステム（数値の SoT）

実体: `Core/Job/JobMaster.cs`（数値 SoT）／ `Core/Job/JobData.cs`（enum・型）／ `Core/Job/JobCodex.cs`

### C-1. `JobId` enum（8 ジョブ）

`IronWallKnight / HeavyInfantry / StandardBearer / Tactician / Medic / Sniper / Sorcerer / Scout`

`Unit.Job` は **enum**（文字列 union ではない）。日本語ラベル・解説は `localization_ja.json` の `jobs` セクションが SoT。

### C-2. `JobStats`（JobMaster.All に格納された各ジョブの不変数値）

| ジョブ | MaxHp | Speed | FrontAttack | RearAttack | BattalionDefense | SquadDefense | InitiativeBuff | TurnEndSquadHeal | 特殊 |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---|
| 鉄壁騎士 IronWallKnight | 250 | 10 | 50 | 10 | 10 | 15 | 0 | 0 | — |
| 重装歩兵 HeavyInfantry | 300 | 15 | 70 | 20 | 0 | 10 | 0 | 0 | — |
| 旗手 StandardBearer | 150 | 20 | 30 | 30 | 0 | 5 | 40 | 0 | — |
| 戦術官 Tactician | 120 | 35 | 20 | 20 | 0 | 0 | 20 | 0 | — |
| 衛生兵 Medic | 100 | 25 | 10 | 10 | 0 | 0 | 0 | 30 | — |
| 狙撃兵 Sniper | 80 | 40 | 20 | 90 | 0 | 0 | 0 | 0 | ConsecutiveStrike |
| 呪術師 Sorcerer | 40 | 15 | 10 | 120 | 0 | 0 | 0 | 0 | — |
| 斥候 Scout | 90 | 60 | 40 | 40 | 0 | 0 | 0 | 0 | — |

### C-3. パッシブ種別 `PassiveKind`（略称完全廃止・正式名称化）

| enum | 旧略称 | 効果 | UI ラベル（localization） |
|---|---|---|---|
| `BattalionDefense` | BDF | FRONT 配置時、大隊全員の被ダメ軽減 | 🛡️ 大隊総守護力 |
| `SquadDefense` | SDF | 所属分隊の被ダメ軽減（配置不問） | 🛡️ 分隊守護力 |
| `InitiativeBuff` | AB | ターン頭に大隊全員（自分以外）の速度・攻撃を底上げ | ⚡ 突撃号令 |
| `TurnEndSquadHeal` | HL | ターン末に所属分隊の生存者を回復（HP 上限クランプ） | 💚 ターン末分隊治癒 |
| `ConsecutiveStrike` | 二の矢 | イニシアチブ 1 番手かつ分隊先頭時に通常攻撃 2 回 | 🎯 二の矢 |

- `JobMaster.HasPassive(id, kind)` が **データ駆動**で判定（`unit.Job == "..."` の文字列マッチを完全排除）。
  数値系は対応 `JobStats` 値 > 0、特殊系は `SpecialPassives` 包含で判定。
- `JobFormationGuide`（推奨 row / 効果範囲 / `EffectScope` / `EffectKind`）と `RoleBonus`・`TargetRating`
  （UI 比較用総合値 = `floor(MaxHp/5 + max(Front,Rear) + Speed) + RoleBonus`）も JobMaster が保持。
- 補助 enum: `SquadRow{Front,RearLeft,RearRight}` / `EffectScope{SelfOnly,OwnSquad,EntireBattalion,EntireBattalionWhenFront,OwnRow}` / `EffectKind{Defend(青),Buff(金),Heal(緑),Attack(朱)}`。

---

## D. 戦闘・編成システム

### D-1. 編成盤面 `FormationBoard`（`Core/Formation/FormationBoard.cs`）

- V 字 3×3 = **9 スロット**（`RowCount=3` × `ColumnsPerRow=3` = `SlotCount=9`）。
- 座標は `SlotCoordinate(SquadRow Row, int Column)`。row は `Front`(中央上)/`RearLeft`(左下)/`RearRight`(右下)。
- 盤面は **Unit 実体を持たず `OccupantId(Guid?)` だけを保持する薄い参照レイヤ**。正本はロスタ側（単一 SoT）。
- 不変メソッド: `WithUnitAt` / `ClearedAt` / `SwapSlots` / `Rotated(RotationDirection)` / `RetainingUnits(validIds)`。
- `RotationDirection{Clockwise, CounterClockwise}` で分隊（行）単位ローテーション（列順 0/1/2 は保持）。
- `DeploymentGate.CanMarch(board)`: 盤面に最低 1 名いないと編成→戦闘へ前進できない（無人出撃の絶対封鎖）。

### D-2. 戦闘の常駐統合（`ChronicleGlobal` の戦闘ライフサイクル）

純粋層 `BattleResolver`（1 ターン解決器）を SoT へ昇格させ、3 つの薄い API が統治する:

```
StartBattle(enemy, seed?)        → BattleResolver.CreateInitial で初期 BattleSnapshot 生成・CurrentBattle へ
ResolveBattleTurn(rotation?)     → 1 ターン解決し CurrentBattle を差し替え、ImmutableArray<BattleEvent> を返す
EndBattle()                      → 戦闘後の複製を正本ロスタへ書き戻し、CurrentBattle=null（非戦闘へ）
```

- 戦闘専用乱数 `_battleRng` を `StartBattle` で再シード（同一局面＋同一シードで全環境同一結果＝決定論）。
- `StartBattle` は `ActionPhaseRouter.MayGenerateEnemy(CurrentAction)`（=March のみ true）を最終結界とし、
  **戦闘以外の行動では敵・戦闘インスタンスを構造的に 1 ビットも生成しない**。
- `BattleProgressGate.CanLeaveBattlePhase(CurrentBattle)`: 戦闘が未決着の間は 戦闘→年代記 へ前進しない。

### D-3. スナップショットと型

| 型 | 内容 |
|---|---|
| `BattleSnapshot` | 戦闘の不変スナップショット（`Combatants: Guid→Unit` / `Enemy` / `TurnNumber` / `Outcome`） |
| `BattleOutcome` | `Ongoing` / `BattalionVictory` / `BattalionDefeat` |
| `EnemyState` | 敵 1 体（`Archetype` / `MaxHp` / `Attack` / `Speed` 等） |
| `EnemyArchetype` | `TrialGuardian`（通常敵・全時代共通）＋ 章ボス 4 種 `DawnWarden`/`UpheavalConqueror`/`DeclineTyrant`/`EternalSovereign` |
| `AttackIntent` | 攻撃予告（`AttackPatternKind{SingleStrike,Pincer,TotalAssault}` / `SkillNameKey` / 対象 row / ダメージ） |

### D-4. 敵生成・スケーリング

- `EnemyScaler`（`Core/Battle/`）: `BaseHp=150` / `BaseAttack=30` / `BaseSpeed=100`、年率 `HpGainPerYear=5.0` /
  `AttackGainPerYear=0.6` / `SpeedGainPerYear=0.6`、個体差ジッタ `0.85 + rng()*0.30`（**±15%**）、`PerLevelGain=0.5`。
- `ChronicleTimelineConfig`: 100 年 / 25 年で 1 章（`YearsPerEpoch=25`）。章ボス出現年は **25 / 50 / 75 / 100**、
  時代は `EpochId{Dawn, Upheaval, Decline, Twilight}`。`BattleArchetypeForYear(year)` がその年の原型を決定論選択。
- `ChronicleGlobal.CreateCurrentYearEnemy(seed?)` が「今年は誰と戦うか」を暦から決め、時代スケール＋個体差で敵 1 体を合成。
- `AttackIntentRoller.Forecast / ForecastWithOmens`: 現局面から決定論的に攻撃予告の帯を先読み（章ボス接近前兆を重畳）。

### D-5. ラストヒット（とどめ）解決 `BattleManager.ExecuteLastHit(unit, rng)`

「誰がトドメを刺したか」で報酬が変わるハクスラの脳汁ポイント:
1. ユニット成長（Lv<3 なら +1、Lv3 は overflow）
2. 装備強化（Lv<5 なら +1）
3. Lv5 装備の運命（50% 破壊 / 50% 生存）
4. 強欲の古銭(CoinGreed) Lv5: 100% 破壊だが引き換えに +1pt を強奪（`EarnDirect`）

`ChronicleGlobal.ResolveLastHit(unitId)` が結果をロスタ・経済へ反映。後段の `FinalizeBattleSpoils` が
「開戦時 → とどめ完了後」の Guid 突合で `BattleSpoils`（統合台帳）を確定する。

---

## E. 経済・婚姻・スカウト・装備（資源ループ）

### E-1. ポイント一元経済 `PointsEconomy`（`Core/Managers/`）

すべての消費（スカウト・結婚・装備購入/強化）が **単一通貨「ポイント」** を共有。状態は 3 値（現在残高 / 累計獲得 / 累計消費）。

| 収入 | メソッド | 式 |
|---|---|---|
| タイムスキップ年次収入（唯一の SoT 式） | `EarnFromTimeSkip(years)` | `years × 1`（`YearlyMinimumIncomePerYear=1`） |
| 特殊効果（戦果決算・強欲・予言報酬等） | `EarnDirect(delta)` | 任意の正数を直接加算（SoT 式の外側の共通入口） |

> 戦闘収入は「敵 1 体撃破ごとの報酬」ではなく、**戦果決算 `BattleSpoils`（婚姻ポイント）を `EarnDirect` で一括加算**する設計（個別の撃破報酬路 `EarnFromKill` は不採用・廃止済）。

`CanAfford(cost)` で純粋判定、`SpendPoints(cost)` は残高不足なら `InvalidOperationException`（マイナス残高は構造的に発生しない）。

### E-2. 戦果決算 `BattleSpoils`（戦闘 → 経済の資源ループの入口）

`AdvancePhase` の Battle→Chronicle 幕引きで `ApplyBattleSpoils` が婚姻ポイントを算出・経済へ非破壊加算する。
婚姻ポイント = （勝利時のみ）`VictoryBase 5` ＋ 昇級 `2/人` ＋ 装備進化 `1/件` − 完全ロスト `3/人`。敗北・戦果なしは 0。

### E-2b. 予言の報酬（カードの効果を「年末」に確定）

選択した予言（`_pendingProphecy`）の Kind/Value は、その年の決算で `RestService.Resolve` が現金化する
（非戦闘＝休息は `ExecuteRest`、章ボス年で強制出撃した非戦闘予言は `AdvanceGenerationLocked` 冒頭が拾う）:
- `RewardPoints` → `EarnDirect(Value)` でポイント加算。
- `ScoutReward` → `ScoutService.CreateOutsiderUnit` で無償の新人を `Value` 名加入（コスト 0）。
- `EquipmentDrop` → Lv `clamp(Value,1,5)` の 3 択候補を生成し、選んだ 1 つを持ち物へ（`EquipmentDropOverlay`・後述 E-5）。
- `Rest`/予言なし → 固定の休息ボーナス `RestPointsReward=2`。`Battle` → 報酬 0（戦果決算で報いる）。

成果は `RestOutcome`（休息頭数 / 獲得ポイント / 加入数 / ドロップ 3 択候補）として `RestResultOverlay`（＋ドロップは `EquipmentDropOverlay`）が提示する。

### E-3. 手動婚姻 `MarriageService`

- コスト = `ceil((父TargetRating×倍率 + 母TargetRating×倍率) / 20)`（`CostDivisor=20`）。
- **自然婚姻（コスト 0）**: 双方向 `BattleAffinity` がともに **150 以上**（`NaturalMarriageThreshold=150`）なら無償。
- 子: Level 1・ジョブは父母から 50/50 継承（`OverrideJob` 可）・Origin も 50/50 継承・名前は文化圏プールから自動払い出し・好感度/装備は空。
- 死亡同士は不可（例外）。`ChronicleGlobal.ExecuteMarriage` は例外を握り潰し null を返す（UI を落とさない）。

### E-4. 外様スカウト `ScoutService`

ポイントを払って血縁なしの傭兵を 1 名即採用。年齢 16〜28（`ScoutMinInitialAge`/`ScoutMaxInitialAge`）、
寿命 55〜75（`ScoutMinLifespan`/`ScoutMaxLifespan`）、ジョブ/文化圏/名前は乱数、Lv1・装備なし・親なし。残高不足・負コストは null。

### E-5. 装備 `Equipment` / 兵器廠 `ShopService`

- アイテム `ItemId`（5 種）: `SwordKnight` / `BowSniper` / `StaffMage` / `RingPurelove` / `CoinGreed`。
- レベル 1〜5（`MinEquipmentLevel`/`MaxEquipmentLevel`）。レベル倍率 `{1.2,1.3,1.4,1.5}`、`AffinityMultiplier = (1.0 + Level×0.1) × BaseAffinityMultiplier`。
- 兵器廠: 購入 `BuyCost=5`（固定）、強化 `UpgradeCostFor(lv) = 2 × lv`（`BaseUpgradeCost=2`）。共通サイフを消費。
- **持ち物（インベントリ）に一本化**: 旅団共有の未装着装備 `BrigadeInventory`（SoT）を `InventoryService`（`Core/Unit/InventoryService.cs`）が
  個体保持のまま非破壊に往復させる。`EquipFromInventory`（旧装備は持ち物へ戻る）/ `UnequipToInventory`（外しても消えない＝保存則）。
  付け外し UI は **拠点 `MarriageUI` の「🎒 持ち物」と編成 `FormationUI` の装備ドックの両方**が持ち物プルダウンで提供（休息年でも出撃年でも同一機構）。`InventoryChanged` で再描画。
  （旧・在庫なしの conjure/discard 型 `EquipItem`/`UnequipItem` ＋ `EquipmentService` は持ち物導入で廃止・削除済。）
- **3 択ドロップ**: `EquipmentDrop` 予言は自動装着をやめ、`EquipmentDropService.RollCandidates` が 3 候補（種別/Affix を散らす）を生成。
  `PendingDropCandidates`（SoT）→ `DropChoicePending` シグナル → `EquipmentDropOverlay` が提示 → `ChooseDroppedEquipment` で 1 つを持ち物へ（残りは破棄）。
- **Affix（接尾効果）**: `Equipment.AffixKeys`（個体ごとのランダム付加効果キー列）を `AffixMaster`（`Core/Unit/AffixMaster.cs`）が
  戦闘ステへ解決する。`AffixKind{Sharp=+ATK3 / Sturdy=+DEF2 / Swift=+SPD2}` のフラット加算（レベル乗算は通さない）で、
  `BattleManager.Equipment{Attack,Defense,Speed}Bonus` に合流して実戦に効く。ドロップ時に `RollAffixKeys` が
  レベル別個数（Lv1〜2→1 / Lv3+→2、相異なる種別を決定論抽選）を付与。表示名は `affixes.{key}.name`（`ResolveAffixName`）。

---

## F. ゲームフェーズ状態機械 ＆「1 世代 = 時間軸 1 周」

実体: `Core/GameFlow/`（GamePhase / PlannedAction / RestOutcome / ScreenVisibility）

### F-1. フェーズ循環（一方通行・不可逆）

```
  Chronicle ──▶ Guild ──▶ Formation ──▶ Battle ──┐
  (年代記/予言)  (拠点:婚姻  (大隊9名編成)  (ターン戦闘   │
                /スカウト                  →とどめ→決算) │
       ▲        /行動選択)                            │
       └────────────（年送り：数年が一気に流れる）◀──────┘
```

- `GamePhase{Chronicle, Guild, Formation, Battle}`。`GamePhaseFlow.Next` で「次はただ 1 つ」、
  後退・飛び越し・自己遷移は `CanTransition` がすべて false（絶対ガード）。
- `Slug()` が ASCII スラッグ（"chronicle" 等）を払い出し、ローカライズキー組み立てと画面解決に使う。

### F-2. 行動分岐 `PlannedAction`（出撃 March / 休息 Rest）

- **行動の既定値は年代記の予言で決まる**: `SelectProphecyAndAdvance` が選択予言の Kind から
  `ActionPhaseRouter.ActionForProphecyAtYear`（**Battle → March / それ以外 → Rest**）で `CurrentAction` を確定する。
  これにより「戦闘以外の予言を選んだのに編成・戦闘へ進む」事故を構造的に封じる。
- **章ボス年（25/50/75/100）は出撃必至**: 上記判定で、現在年が章ボス年なら予言が休息でも `March` へ強制上書き
  （`IsEpochBossYear`）。年送りのボススナップ（暦が必ずボス年へ着地）と合わせ、4 体の章ボスは休息で素通りできない。
- 拠点（Guild/MarriageUI）は確定済みの行動を**表示するだけ**（出撃/休息トグルは撤去・選び直し不可。
  矛盾する選択肢を一切出さない）。`SetPlannedAction` は SoT API としては残るが UI からは呼ばれない。
  離脱先は純粋ルータ `ActionPhaseRouter.PhaseAfterGuild`:
  - **March** → Formation（その後 Battle）。
  - **Rest** → Chronicle（**編成・戦闘の両フェーズを完全バイパス**する安全な年。`RestService` で休息決算）。
- 決定を編成より上流（年代記の予言）へ置くことで、休息時は編成画面・戦闘画面が一度も描かれない（亡霊残存の根絶）。

### F-3. 年送り（`AdvanceGenerationLocked`）

予言の `SkipYears` は選択時に消費せず `_pendingGenerationSkipYears` へ保留し、ループ幕引き（Battle→Chronicle、
または Rest の Guild→Chronicle）で一括適用する。適用年数 `years` は `SkipYears` を
`ChronicleTimelineConfig.ClampSkipToNextBossYear` で **章ボス年（25/50/75/100）を踏み越さないようクランプ**した値で、
加齢・収入・暦の前進すべてに同一の `years` を用いる（「○年経過」が暦にも効く・整合）:
1. 全旅団員を `years` ぶん加齢 → 寿命到達・戦闘死を完全ロストとして仕分け（`RosterLifecycle.AdvanceGeneration`）
2. 年代記ナレーション（損失・昇級）を `_chronicleLog` へ追記、去る者を英霊アーカイブ `_ancestralArchive` へ写し取り
3. 盤面から完全ロスト者を掃き出し（`ReconcileFormationWithRoster`）
4. 定期収入 `EarnFromTimeSkip(years)` を加算
5. **暦の年（`Turn`）を `years` ぶん進めて**次世代の予言 3 つを再生成（`TimelineEngine.AdvanceToNextTurn(…, years)`）。
   ボス接近周は暦がボス年へちょうど着地し、次の周回でその年の戦闘がボス戦になる（取りこぼし防止）。
   - 予言の中身は `ProphecyMaster.Generate(turn, rng)`（数値 SoT）が組む: **必ず相異なる 3 Kind**（「3 枚同じ」を構造排除）、
     **レア度 `ProphecyRarity{Bronze/Silver/Gold}`**（基本ブロンズ・たまに銀・稀に金で効果量が跳ねる。戦闘は暦が強さを決めるため常に Bronze）、
     **Kind×Rarity の効果量テーブル**（RewardPoints/ScoutReward/EquipmentDrop/Rest を単調に底上げ）、**暦連動**（章 Epoch とボス接近で Kind の出やすさを傾ける）。
     表示は `DescriptionKey="Kind.Rarity"`→`prophecies` 辞書のフレーバー文＋`prophecyRarities` のバッジで解決。`TimelineEngine.DefaultGenerator` はテスト用フォールバックに降格。

シグナル発火順は **Roster → Economy → Timeline →（必要時 Formation）→ Phase**（画面切替前にデータ確定を保証）。

---

## G. UI 層（`UI/` — 現役）

### G-1. 動的 B 型ライフサイクル（`UI/GameDirector.cs`）

- `Main.tscn` がルート Control に `GameDirector` をアタッチ。`_Ready` でローカライズ読込 → ヘッダー/画面コンテナ構築
  → タイトルゲート（`TitleScreen`）を最前面 overlay。新規/継続で `ChronicleGlobal.Initialize`/`LoadGame` を引く。
- 新規ゲームの初期状態は純粋ファクトリ `Core/Bootstrap/NewGameFactory`（ロスター＋財布）が組み、Initialize へ流す。
  初期 9 名は**完全一様ランダムではなく役割保証付き**: 前衛≥2・回復≥1・支援≥1 を確保し、同職は最大 2（`MaxSameJob`）。
  「前衛ゼロ／回復ゼロ／同職 3 ダブり」の詰み開幕＝リセマラを構造排除する（職の中身・性別・名前・年齢は乱数のまま）。
  役割判定は職名ハードコードではなく `JobMaster` のデータ（推奨 row／パッシブ `TurnEndSquadHeal`・`InitiativeBuff`）から導出。
- レイアウトは3層: **最背面＝全フェーズ共通の戦場背景**（`_backgroundRect`）→ **半透明コンテンツカード**（`ContentCard`：画面端から `ContentCardMarginPx` 余白を残した `MarginContainer`＋`PanelContainer`。地色は半透明暗色で背景が薄く透ける）→ その上に **ヘッダ＋現在フェーズ画面**。背景が額縁のように覗き、本番で「背景の上にカードを置く」見た目の土台になる。
- **常駐 A 型は廃止**。`PhaseChanged` を受け、現在フェーズの画面だけを 1 つ `new` してマウントし、旧画面は `QueueFree`
  （`MountScreenForCurrentPhase` / `FreeCurrentScreen`）。任意の瞬間に生きている画面はちょうど 1 つ（`ScreenVisibility` が形式仕様）。

| GamePhase | マウントされる現役画面 |
|---|---|
| Chronicle | `UI/TimelineUI.cs`（年代記・予言 3 択・歴史進行） |
| Guild | `UI/MarriageUI.cs`（拠点：婚姻・スカウト・今年の行動選択） |
| Formation | `UI/FormationUI.cs`（大隊編成・▲ウェッジ配置。出撃時のみ到達） |
| Battle | `UI/BattleUI.cs`（ターン戦闘 → とどめ → 決算。出撃時のみ） |

### G-2. オーバーレイ・演出（最前面に動的 overlay）

`UI/` 配下: `JobManualOverlay`（📖 ジョブ説明）/ `UnitDetailOverlay` / `PedigreeOverlay`（家系図）/
`ProphecyTimelineOverlay`（運命の帯）/ `RestResultOverlay`（休息報酬）/ `EquipmentDropOverlay`（3 択ドロップ）/ `LastHitCeremonyScreen`（とどめ演出）/
`BattleSpoilsScreen`（戦果決算）/ `JuiceDirector`（Flash/CountUp/Typewriter 等の演出）/ `JobDescriptionView`。

- testid は **`Node.SetMeta("data_testid", "...")`** で付与（kebab-case）。Godot の `Find` 系で参照可能。
- 表示文字列はすべて `ChronicleGlobal.Resolve*`（`ResolveJobName`/`ResolveItemName`/`ResolveProphecyKindName`/
  `ResolveDisplayName`/`ResolvePhaseName` 等）経由で localization から解決し、コード側に日本語・絵文字を持たない。

### G-3. `UserInterface/` — 死蔵 View 群を粛清し「現役共有部品」だけを残した

かつて `UserInterface/` には到達不能な並走 UI（`UserInterfaceRoot`/`Title/TitleView`/`Hub/HubView`/
`Battle/BattleView`/`Settlement/SettlementView` ＋ 死蔵側 `Hub/ProphecyTimelineOverlay`）が同居し、
`ProphecyTimelineOverlay` の二重定義の温床になっていた。コミット（本掃除）で **死蔵 6 View をすべて削除**し、
二重定義を解消した。残っているのは **すべて現役** の以下のみ:

| 残存ファイル | 利用元（現役） |
|---|---|
| `UserInterface/JobTextureLibrary.cs` | `UI/BattleUI` `UI/FormationUI` `UI/UnitDetailOverlay` `UI/JobDescriptionView`（ジョブ立ち絵の共有ライブラリ） |
| `UserInterface/BackgroundTextureLibrary.cs` | `UI/GameDirector`（章 Epoch ごとの戦場背景を**全フェーズ共通**で最背面へ。`Core/Assets/AssetSlugs` で slug 解決。`TimelineChanged`/`StateInitialized` で張替） |
| `UserInterface/EnemyTextureLibrary.cs` | `UI/BattleUI`（敵原型 Archetype ごとのイラストを敵カードへ。同上 slug 解決） |
| `UserInterface/Hub/FormationDragPayload.cs` | `UI/FormationUI`（D&D 編成のドラッグ運搬体） |
| `UserInterface/Hub/RosterDragCard.cs` | `UI/FormationUI`（ロスタ側ドラッグ元カード） |
| `UserInterface/Hub/FormationSlotControl.cs` | `UI/FormationUI`（盤面スロットのドロップ先） |

- これにより `ProphecyTimelineOverlay` は現役 `UI/ProphecyTimelineOverlay.cs` のただ 1 定義になった。
- 注意: `Tests/Core/Lifecycle/` の `*ViewContractTests`（`BattleViewContractTests` 等）は**削除した View の名前を冠するが中身は生きている契約テスト**。
  Core 純粋層のロジック（現役 `UI/` 画面が実装する契約）を固定するもので、削除済み View には一切コンパイル依存しない。名前は歴史的経緯。

---

## H. テスト規約と検証

実体: `generated_csharp/Tests/`（xUnit / **756 pass / 0 fail**）

### H-1. テスト方針

- **対象は Core 純粋層のみ**（Godot 非依存）。Autoload `ChronicleGlobal` もシグナル発火を `SafeEmit`（IsInsideTree ガード
  ＋ try/catch）で隔離しているため、`new ChronicleGlobal(); Initialize(...); 各 API; プロパティ assert` が Godot なしで動く。
- テストは `Core/**/*.cs` を `<Compile Include>` で取り込み、Godot 本体アセンブリを参照しない（CI も Godot 不要）。
- `Tests/ChronicleKnights.Tests.csproj` の `WarningsAsErrors` で 13 個の CS 警告コードをビルドエラーに昇格（構造的な品質ガード）。

### H-2. テスト群（抜粋）

`Core/Battle/`（AttackIntentRoller / BattleResolver / BattleProgressGate / BattleSeatingContract / BattleSpoils /
EnemyScaler / EnemyState / EpochBossForecast）、`Core/Chronicle/`（100 年シミュレーション / メトリクス / 多元宇宙ランナー /
UniverseEvaluator / EnemyScalingResolver）、`Core/GameFlow/`（GamePhaseFlow / ActionPhaseRouter / RestService /
ScreenVisibilityIntegration）、`Core/Lifecycle/`（各画面の契約テスト＝Battle/Chronicle/Formation/Hub/Phase/Prophecy/
RosterCard/Settlement）、`Core/Managers/`（BattlePassive / Marriage / PointsEconomy / RosterAdmin / RosterLifecycle /
Scout / EquipmentStatCorrection）、`Core/Naming` / `Core/Pedigree` / `Core/Persistence` / `Core/Shop` / `Core/Units`。

### H-3. CI

`.github/workflows/dotnet-test.yml` — **手動トリガー（workflow_dispatch）専用**。ubuntu + .NET 8 SDK で
Tests プロジェクトのみ restore/test（GitHub 課金分を抑えるため push/PR の自動実行は退役済み）。

---

## I. ローカライズ ＆ 永続化

### I-1. ローカライズ（`Config/localization_ja.json`）

- 全日本語テキストの唯一の辞書。トップレベルセクション: `phases / passives / squadRows / effectKinds /
  effectScopes / jobs / items / affixes / prophecyKinds / prophecyRarities / prophecies / enemySkills / epochs / enemyArchetypes / names / ui / marriage`。
  （`prophecyRarities`=銅/銀/金のバッジ name/icon、`prophecies`=予言フレーバー文を `"Kind.Rarity"` キーで引く flavor。）
- 純粋層 `NameResolver`（キー→氏名。`@` 連結の称号付き複合キーを「称号＋名＋姓」へ自動連結）/ `PhaseNameResolver` /
  `MasterDataNameResolver` が解決し、`ChronicleGlobal.LoadLocalization` が res:// から一度だけ読み込んで各リゾルバを構築。
- **未知キーは例外を投げず生キーを返す**（画面が落ちず、未登録キーが一目で分かる）。

### I-2. 命名 `NameGenerator`

- 3 文化圏 `Origin{Japanese, European, Classical}` × `Gender` のプール（`NameCatalog`/`NameTaxonomy`）。
- 歴史的重複を避けて未登場キーを払い出し、プール枯渇時は称号キーを `@` で前置した複合キーへフォールバック
  （`Jr.`/`II 世`/`(2)` 式の記号方式は仕様で不採用）。

### I-3. 永続化（`Core/Persistence/SaveSerializer`（純粋）＋ `SaveManager`（I/O））

- `user://save_data.json` に **未暗号化の整形 JSON** で保存（可読性・デバッグ性優先）。
- アトミック書き込み（`.tmp` 書き切り → 本ファイルを `.bak` へ退避 → リネーム）でクラッシュ耐性。
- 可変 DTO 経由でマッピング（enum は文字列、Guid キー辞書は文字列キー化）、`Version` でスキーマ管理（現 7。v5=持ち物 Inventory / v6=旅団史の Gender 追加 / v7=予言 Rarity 追加・旧版は既定値（Bronze 等）で後方互換）。
- **保存対象**: 経済 / タイムライン / ロスタ / `_chronicleLog` / 持ち物 `BrigadeInventory`（v5）。**非保存**: Random・盤面・戦闘・英霊アーカイブ・保留年数・選択待ちドロップ。
- ロード時は新しい Random を再注入し、`CurrentPhase` は **常に Chronicle から再開**。

---

## J. 主要な絶対ルール（要約・詳細は `instructions.md`）

- **開発憲法①（厳格 ASCII）**: `Core/` および UI 層の識別子・ノード名・testid・内部ログ・アセットパス・コメント記号は
  ASCII のみ。プレイヤー向け表示文字列のみ日本語（人間向けドキュメントは日本語可）。略称（BDF/SDF/AB/HL/FA/RA）は完全未使用。
- **開発憲法②（不変性）**: ドメインは `record` + `with` 式。リストは `ImmutableList`/`ImmutableArray`。破壊的変更禁止。
- **開発憲法③（単一 SoT ＋ 単方向データフロー）**: 状態は `ChronicleGlobal` のみが握る。UI は API を呼びシグナルで読み直すだけ。
  状態変更は `lock(_stateLock)` 内、`EmitSignal` はロック解放後（`SafeEmit`）。
- **開発憲法④（完全決定論シード）**: 新規ゲームは 1 シード注入（`StartNewGame(seed)`）。同一シードは同一の歴史。Random は引数注入。
- 大隊規模 **9 名（3×3）**、章ボス出現年 **25/50/75/100**、敵ステータス **±15%** 個体差。
- 新人スカウト・婚姻・解雇・引退はすべて手動選択。フェーズ遷移は不可逆・一方通行。
- 数値はハードコードせず Core の SoT 定数（`JobMaster` / `PointsEconomy` / `EnemyScaler` / `ChronicleTimelineConfig` 等）を参照。
- コミットメッセージは日本語、type prefix（`feat:`/`fix:`/`refactor:`/`docs:` 等）のみ英語。
- **修正のたびに毎回 commit & push**（main 直）。**C# 修正は `dotnet clean` → build → test を通してからコミット**（詳細は `instructions.md` §6）。

---

## K. 主要ファイルマップ（`generated_csharp/`）

### Core（純粋ロジック・48 ファイル）
- `Core/Unit/Unit.cs` `Equipment.cs` `InventoryService.cs` `EquipmentDropService.cs` `AffixMaster.cs` — 旅団員・装備・持ち物・ドロップ・Affix
- `Core/Job/JobMaster.cs` `JobData.cs` `JobCodex.cs` — ジョブ数値 SoT・enum
- `Core/GameFlow/GamePhase.cs` `PlannedAction.cs` `RestOutcome.cs` `ScreenVisibility.cs` — フェーズ・行動・休息・画面可視ルール
- `Core/Formation/FormationBoard.cs` `DeploymentGate.cs` — V 字盤面・無人出撃封鎖
- `Core/Battle/BattleResolver.cs` `BattleSnapshot.cs` `BattleManager.cs` `EnemyState.cs` `EnemyScaler.cs`
  `AttackIntent.cs` `AttackIntentRoller.cs` `BattleEvent.cs` `BattleProgressGate.cs` `BattleSpoils.cs` `EpochBossForecast.cs`
- `Core/Managers/PointsEconomy.cs` `TimelineEngine.cs` `RosterLifecycle.cs` `MarriageService.cs` `ScoutService.cs`
  `RosterAdminService.cs`
- `Core/Shop/ShopService.cs` / `Core/Timeline/Prophecy.cs` `ProphecyMaster.cs`（予言生成 SoT：3 枚キュレーション＋レア度＋効果量＋暦連動） / `Core/Chronicle/ChronicleTimelineConfig.cs`
  `EnemyScalingResolver.cs` `MetricsCollector.cs` `UniverseEvaluator.cs` `ChronicleLogEntry.cs`
- `Core/Naming/` `Core/Localization/` `Core/Pedigree/PedigreeGraph.cs` `Core/Persistence/` `Core/Bootstrap/NewGameFactory.cs`

### Autoload / UI / Config
- `Autoload/ChronicleGlobal.cs` — 常駐 SoT・全 API・シグナル・セーブ/ロード（約 2060 行）
- `UI/GameDirector.cs` `TimelineUI.cs` `MarriageUI.cs` `FormationUI.cs` `BattleUI.cs` ＋ 各オーバーレイ・`JuiceDirector.cs`
- `UserInterface/JobTextureLibrary.cs` `BackgroundTextureLibrary.cs` `EnemyTextureLibrary.cs`（現役・共有・画像ローダ）／ `UserInterface/Hub/` の D&D 部品 3 種（現役・FormationUI 利用。死蔵 View は粛清済・G-3）
- `Core/Assets/AssetSlugs.cs` — 画像ファイル名スラッグ（章/敵→snake_case）の純粋写像 SoT（ローダが引くパスの核）
- `Config/localization_ja.json` — 全日本語テキストの辞書
- `Assets/Textures/Jobs/{job}/{male|female}.png` — ジョブ立ち絵（16 枚）。`Backgrounds/{epoch}.png`（4）・`Enemies/{archetype}.png`（5）は**原色プレースホルダ配置済**（ローダ実装済・本番アート差し替え待ち。`docs/ASSET_MANIFEST.md`）

### ドキュメント
- `ONBOARDING.md` — **統合ガイド（ゲーム仕様＋設計＋ルール＋手順を 1 枚に集約）**。他 AI／新規参加者に最初に渡す
- `instructions.md` — 絶対ルール（C# 版憲法）
- `docs/MIGRATION_GODOT_HACK_AND_SLASH.md` — 移行戦略憲法（フェーズ 1〜3 はほぼ実現済み）
- `docs/system_architecture.md` / `design_blueprint.md` / `job_definitions.md` / `simulation_guide.md` — C# 版各論
- `docs/VISUAL_AND_JUICE_ROADMAP.md` / `ASSET_MANIFEST.md` — 見栄え強化ロードマップ・アセット必要物
- `PROGRESS_REPORT.md` — 実装進捗レポート
