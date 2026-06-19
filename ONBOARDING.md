# Chronicle Knights — 統合ガイド（オンボーディング ＆ ゲーム仕様 ＆ 設計）

> **このファイル 1 枚で、ゲーム内容・仕様・アーキテクチャ・ルール・ビルド手順・現状まで把握できる自己完結ドキュメント。**
> 他の AI／開発者に渡すならまずこれ。詳細は各専門ドキュメント（`CLAUDE.md` / `instructions.md` / `docs/*`）へリンクする。
>
> 数値はすべてコード（`generated_csharp/Core/`）から採取。検収: `dotnet test` = **653 pass / 0 fail**。

---

## ⚠ 0. 最重要の前提（最初に必ず読む）

このリポジトリには **「2 つの時代」が同居している**。

| 区分 | 場所 | 実態 | 扱い |
|---|---|---|---|
| **現役の本体** | **`generated_csharp/`** | **Godot 4.3 (.NET/mono) ／ .NET 8 ／ C# 12** | ★ 新規実装はすべてここ |
| 凍結された旧本体 | `apps/` `packages/` `scripts/` `config/jobs.json` `tools/` | Bun + Hono + React の旧 TypeScript 版 | 参照専用・**変更禁止** |

> **他の AI に渡すときの殺し文句:**
> 「現役は `generated_csharp/`（C#/Godot）。ルート直下の `apps/` `packages/` `scripts/` は凍結された旧 TS 版なので
> 絶対に編集しないこと。`packages/core` 等を現役と誤認しないこと。」

---

## 1. ゲーム概要

**Chronicle Knights** — 『ヴィーナス＆ブレイブス』にインスパイアされた **世代交代型シミュレーション RPG** を、
**ハクスラ（ハック＆スラッシュ）／ローグライト** と融合させた作品。

- プレイヤーは英雄個人ではなく **「旅団（大隊）」という組織と、そこに連なる血脈** を導く。
- 最強の剣士もやがて老いて旅団を去る（**完全ロスト**）。だから子を産み育て、外様（傭兵）を雇い、世代を繋ぐ。
- **「1 周（1 旅団の興亡）を約 3 時間で完結でき、ドロップ・育成・配合に一喜一憂する中毒性」** を志向する。
- **完全決定論**: 1 つのシードからは、まったく同じ歴史が再現される。

### コア思想：「1 世代 ＝ 時間軸 1 周」

```
   ┌──────── 1 世代（ゲームループ 1 周） ────────┐
 年代記 ──▶ 拠点 ──▶ 大隊編成 ──▶ 戦闘 ──┐
 (予言を選ぶ) (婚姻/スカウト) (9名配置)  (決着)  │
   ▲                                      │
   └──────── 年送り（数年が一気に流れる）◀┘
            ・全員が加齢　・寿命/戦死は永久離脱（完全ロスト）
            ・収入が入る　・次の予言が提示される
```

年代記で選ぶ **予言** には「この先 ◯ 年が流れる（タイムスキップ／2〜4 年）」が記されている。その年数ぶんの未来を
1 周のループで戦い抜き、ループ幕引き（戦闘 → 年代記）で **その年数が一気に経過**する。
つまり **「Chronicle で選んだ年数＝この世代の長さ」**。30 周回せば旅団の 30 年史になる。

---

## 2. ゲーム仕様（Game Spec）

### 2-1. フェーズ循環（一方通行・不可逆）

```
Chronicle ──▶ Guild ──▶ Formation ──▶ Battle ──▶（年送り）──▶ Chronicle
 年代記/予言   拠点       大隊編成      戦闘
```

- 各フェーズの「次」はただ 1 つ。**後退・飛び越し・自己遷移は全て禁止**（「戻る」ボタンなし＝後悔もゲームの一部）。

### 2-2. 行動分岐（出撃 March / 休息 Rest）

今年の行動は **年代記で選んだ予言の種別が唯一決める**（`Battle` 予言 → 出撃 / それ以外 → 休息）。拠点（Guild）では確定した行動を表示するだけ（選び直し不可）:
- **March（出撃）**: Guild → Formation → Battle（戦う年）。
- **Rest（休息）**: Guild → Chronicle（**編成・戦闘を完全バイパス**する安全な年。休息ボーナス +2pt、戦死者なし）。

休息時は編成画面も戦闘画面も一度も描かれず、敵データも 1 ビットも生成されない（構造的隔離）。

### 2-3. 大隊・編成（V 字 3×3 ＝ 9 名）

- 盤面は **9 スロット**（3 行 × 3 列）。行 = `Front`(中央上) / `RearLeft`(左下) / `RearRight`(右下) の **▲ウェッジ配置**。
- 分隊（行）単位でローテーション可能（時計回り / 反時計回り。列順 0/1/2 は保持）。
- **無人出撃は封鎖**: 盤面に最低 1 名いないと 編成→戦闘 へ進めない。

### 2-4. ジョブ仕様（8 種・数値は `Core/Job/JobMaster.cs`）

| ジョブ | MaxHp | Speed | 前衛攻撃 | 後衛攻撃 | 大隊守護 | 分隊守護 | 突撃号令 | ターン末治癒 | 特殊 |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---|
| 鉄壁騎士 IronWallKnight | 250 | 10 | 50 | 10 | 10 | 15 | — | — | — |
| 重装歩兵 HeavyInfantry | 300 | 15 | 70 | 20 | — | 10 | — | — | — |
| 旗手 StandardBearer | 150 | 20 | 30 | 30 | — | 5 | 40 | — | — |
| 戦術官 Tactician | 120 | 35 | 20 | 20 | — | — | 20 | — | — |
| 衛生兵 Medic | 100 | 25 | 10 | 10 | — | — | — | 30 | — |
| 狙撃兵 Sniper | 80 | 40 | 20 | 90 | — | — | — | — | 二の矢（連続攻撃） |
| 呪術師 Sorcerer | 40 | 15 | 10 | 120 | — | — | — | — | — |
| 斥候 Scout | 90 | 60 | 40 | 40 | — | — | — | — | — |

**パッシブ（正式名称。略称 BDF/SDF/AB/HL は廃止）**
| 能力 | UI ラベル | 効果 |
|---|---|---|
| BattalionDefense | 🛡️ 大隊総守護力 | **FRONT 配置時のみ**、大隊全員の被ダメを軽減 |
| SquadDefense | 🛡️ 分隊守護力 | 所属分隊の被ダメを軽減（配置不問） |
| InitiativeBuff | ⚡ 突撃号令 | ターン頭に大隊全員（自分以外）の速度・攻撃を底上げ |
| TurnEndSquadHeal | 💚 ターン末分隊治癒 | ターン末に所属分隊の生存者を回復（HP 上限クランプ） |
| ConsecutiveStrike | 🎯 二の矢 | イニシアチブ 1 番手かつ分隊先頭時に通常攻撃 2 回 |

> ★ 数値は `Unit` ではなく `JobMaster` が一元保持する（`Unit` は HP・ステータスを持たない経歴書）。
> パッシブ判定は `JobMaster.HasPassive(JobId, PassiveKind)` のデータ駆動（戦闘ロジックはジョブ非依存）。

### 2-5. 戦闘の流れ（1 ターン）

1. **行動順構築**: 実効速度（自身 Speed ＋ 号令ボーナス）の高い順。同速は味方優先。
2. **号令ブロードキャスト**: 突撃号令持ちが自分以外の全生存者へ「速度＝配り手の Speed / 攻撃＝配り手の号令値」を加算。
3. **連続攻撃**: 先頭かつ二の矢持ちのみ攻撃 2 回（例: `(後衛90 + 号令20) × 2 = 220`）。
4. **ダメージ軽減**: `最終被ダメ = max(1, baseDamage − 大隊守護 − 分隊守護)`（最低 1 は必ず通る）。
5. **継続回復**: ターン末に衛生兵が自分隊の生存者を回復（`min(maxHp, hp + heal)`）。

**攻撃予告（運命の帯）**: 敵が次ターン以降に放つ攻撃パターン（`SingleStrike` / `Pincer` / `TotalAssault`）を
現局面から決定論的に先読みし、狙われる行を赤枠で予告する。

### 2-6. とどめ（ラストヒット）— ハクスラの脳汁

「誰がトドメを刺したか」で報酬が変わる:
1. ユニット成長（Lv < 3 なら +1。Lv3 はオーバーフロー）
2. 装備強化（装備 Lv < 5 なら +1）
3. Lv5 装備の運命（50% 破壊 / 50% 生存）
4. 強欲の古銭(CoinGreed) Lv5: 100% 破壊だが引き換えに +1pt を強奪

### 2-7. 敵・章・章ボス

- 敵基準値: HP 150 / 攻撃 30 / 速度 100。年率上昇: HP +5.0 / 攻撃 +0.6 / 速度 +0.6 /年。**±15% の個体差**ジッタ。
- 100 年を 25 年ずつ 4 章に分割: **黎明(Dawn) → 動乱(Upheaval) → 衰退(Decline) → 黄昏(Twilight)**。
- **章ボス出現年 = 25 / 50 / 75 / 100**（DawnWarden / UpheavalConqueror / DeclineTyrant / EternalSovereign）。
  それ以外は通常敵「試練の門の守護者(TrialGuardian)」。
- **章ボス年は出撃必至（休息で回避不可）**。年送りはボス年へちょうど着地（スナップ）し、その年は予言が休息でも
  強制的に出撃して章ボスと決戦する。4 体の章ボスは取りこぼせない。

### 2-8. 世代交代・寿命・レベル（完全ロスト）

- **完全ロスト**: 戦闘死・寿命到達したユニットは旅団から**永久に失われる**（装備も同時喪失・復活なし）。
- **レベル上限 3**。**Lv3 限定引退**（明示引退できるのは Lv3 の生存者のみ）。
- **タイムスキップ加齢**: 年送りで予言の SkipYears ぶん一気に加齢し、寿命を超えた者を仕分けて外す。

### 2-9. 経済（ポイント一元・単一通貨）

すべての消費（スカウト・婚姻・装備）が単一通貨「ポイント」を共有する。

| 収入 | 式 |
|---|---|
| タイムスキップ年次収入 | 経過年数 × 1pt |
| 敵撃破報酬 | floor(敵レベル × 1.5) |
| 戦果決算（婚姻ポイント） | 勝利時のみ: 基本 5 ＋ 昇級 2/人 ＋ 装備進化 1/件 − 完全ロスト 3/人 |
| 休息ボーナス（Rest 予言） | +2pt |
| 予言報酬（RewardPoints） | +カードの Value pt |

残高不足の消費は拒否される（マイナス残高は構造的に発生しない）。

**予言カードの報酬は「年末（戦闘終了 or 休息後）」に確定する**（`RestService.Resolve`）:
`RewardPoints`→ポイント加算 / `ScoutReward`→無償の新人が加入 / `EquipmentDrop`→生存者へ装備ドロップ /
`Rest`→休息ボーナス +2 / `Battle`→報酬なし（戦果決算で報いる）。成果は休息結果画面に表示される。

### 2-10. 婚姻・スカウト・装備（すべて手動）

- **手動婚姻**: ポイントを払って男女 2 名を即結婚 → 即・子 1 名を生成。
  - コスト = `ceil((父Rating×倍率 + 母Rating×倍率) / 20)`。
  - **自然婚姻（0pt）**: 双方向の好感度（BattleAffinity）がともに **150 以上**なら無償。
  - 子は Lv1・ジョブ/文化圏を父母から 50:50 継承・名前自動生成・好感度/装備は空。男女ペア限定。
- **外様スカウト**: ポイントで血縁なしの傭兵を 1 名採用。年齢 16〜28・寿命 55〜75・乱数ジョブ・Lv1。
- **装備（5 種）**: 剣SwordKnight / 弓BowSniper / 杖StaffMage / 純愛の指輪RingPurelove / 強欲の古銭CoinGreed。Lv1〜5。
  - 兵器廠で購入（5pt 固定）・強化（2 × 現Lv）。編成段階の無償脱着もできる。

### 2-11. 命名・ローカライズ

- 3 文化圏（日本 Japanese / 欧州 European / 古典 Classical）× 性別の名前プール。歴史的重複を回避。
  枯渇時は称号を `@` で前置した複合キーへフォールバック（「鉄血のタケル」等。`Jr.`/`II世` 式は不採用）。
- 全日本語テキストは `Config/localization_ja.json` の辞書に分離。コードは ASCII のキーだけを扱う（憲法①）。

### 2-12. セーブ／ロード

- `user://save_data.json` に未暗号化の整形 JSON で保存。クラッシュ耐性のためアトミック書き込み。
- 保存: 経済 / タイムライン / ロスタ / 旅団史。非保存: 乱数・盤面・戦闘・英霊アーカイブ。
- ロード時は新しい乱数を再注入し、常に年代記フェーズから再開する。

---

## 3. アーキテクチャ（三層分離）

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
| **Core** | `generated_csharp/Core/` | ゲームのルール（不変ドメイン・純粋関数）。`dotnet test` で検証 | なし |
| **Autoload** | `Autoload/ChronicleGlobal.cs` | 全状態の保持・API・シグナル・セーブ/ロード（約 2060 行） | あり（Node） |
| **UI** | `UI/` | 無状態の描画（コードで動的構築）。シグナルで読み直して再描画 | あり（Control） |

- **SoT（唯一の真実）= `ChronicleGlobal`** が保持: 経済 / タイムライン / ロスタ / 盤面 / 戦闘 / 戦果 / 旅団史 / フェーズ / 行動。
- シグナル: `StateInitialized` / `EconomyChanged` / `TimelineChanged` / `RosterChanged` / `FormationChanged` / `BattleChanged` / `PhaseChanged`。
- **単方向データフロー**: UI は API を呼ぶ → SoT が Core の純粋関数を叩き状態を丸ごと差し替え → ロック解放後にシグナル発火 → UI が読み直して再描画。
- **UI ライフサイクル（動的 B 型）**: `GameDirector` が現在フェーズの画面だけを 1 つ生成し、旧画面は `QueueFree`。任意の瞬間に生きている画面はちょうど 1 つ（リークフリー）。

| GamePhase | 現役画面（`UI/`） |
|---|---|
| Chronicle | `TimelineUI`（年代記・予言 3 択） |
| Guild | `MarriageUI`（拠点：婚姻・スカウト・行動選択） |
| Formation | `FormationUI`（▲ウェッジ編成・出撃時のみ） |
| Battle | `BattleUI`（ターン戦闘 → とどめ → 決算・出撃時のみ） |

---

## 4. 開発の絶対ルール（四柱）

| 憲法 | 要旨 |
|---|---|
| **① 厳格 ASCII** | ロジックは日本語を知らない。識別子・ノード名・testid・ログ・コメント記号は ASCII。表示文字列のみ日本語で `localization_ja.json` に分離。略称（BDF/SDF/AB/HL/FA/RA）禁止 |
| **② 不変性** | ドメインは `record` + `with`。コレクションは `Immutable*`。破壊的代入禁止 |
| **③ 単一 SoT ＋ 単方向** | 状態は `ChronicleGlobal` のみ。UI は API を呼びシグナルで読み直す。状態変更は `lock` 内、`EmitSignal` はロック解放後 |
| **④ 完全決定論** | 新規ゲームは 1 シード注入（`StartNewGame(seed)`）。Random は引数注入・非保存 |

その他: 大隊 9 名固定 ／ 章ボス 25/50/75/100 ／ 敵 ±15% ／ 人事（スカウト・婚姻・解雇・引退）は全て手動 ／
フェーズは不可逆 ／ 数値はハードコードせず Core の SoT 定数を参照 ／ コミットメッセージは日本語（type prefix のみ英語）。

詳細は [instructions.md](instructions.md)。

---

## 5. ビルド・テスト・起動

前提: **.NET SDK 8 以上**（`net8.0` ターゲットを RollForward で 10.x でも実行）＋ **Godot 4.3 .NET/mono 版**。
すべて `generated_csharp/` から実行する。

```sh
dotnet build ChronicleKnights.csproj --configuration Debug   # 本体ビルド
dotnet test  Tests/ChronicleKnights.Tests.csproj             # xUnit（653 pass / 0 fail）
./play.command                                               # C# ビルド → 実機起動
./play.command -e                                            # Godot エディタを開く
```

- `godot --version` が `4.3.stable.mono.official` を返すこと（標準版では C# が動かない）。
- macOS の `--headless` は Godot 4.3 既知の不具合でクラッシュ。画面確認は windowed で。
- テストは Core 純粋層のみ対象（Godot 不要）。CI は `.github/workflows/dotnet-test.yml`（手動トリガー専用）。

---

## 6. ファイルマップ（`generated_csharp/`）

```
Core/Unit/Unit.cs                旅団員（不変・ステータス非保持）
Core/Unit/Equipment.cs           装備（5 種・Lv1〜5）
Core/Job/JobMaster.cs            8 ジョブ数値の SoT
Core/Job/JobData.cs              enum（JobId / PassiveKind / SquadRow / EffectScope / EffectKind）
Core/Formation/FormationBoard.cs V 字 3×3 盤面（占有 Id のみ保持）
Core/Formation/DeploymentGate.cs 無人出撃の封鎖
Core/Battle/BattleResolver.cs    1 ターン解決器
Core/Battle/BattleManager.cs     パッシブ解決・とどめ
Core/Battle/EnemyScaler.cs       敵スケーリング（±15% 個体差）
Core/Battle/AttackIntent*.cs     攻撃予告・先読み
Core/Battle/BattleSpoils.cs      戦果決算（統合台帳）
Core/Managers/PointsEconomy.cs   ポイント一元経済
Core/Managers/RosterLifecycle.cs 世代交代（加齢・完全ロスト仕分け）
Core/Managers/MarriageService.cs 手動婚姻・自然婚姻
Core/Managers/ScoutService.cs    外様スカウト
Core/Shop/ShopService.cs         兵器廠（購入・強化）
Core/Timeline/Prophecy.cs        予言（種別・SkipYears）
Core/Chronicle/ChronicleTimelineConfig.cs  章・年数・章ボス年
Core/Naming/ + Core/Localization/          命名・名前解決
Core/Persistence/SaveSerializer.cs         状態 ⇄ JSON（純粋）
Core/Bootstrap/NewGameFactory.cs           新規ゲーム初期状態
Autoload/ChronicleGlobal.cs      常駐 SoT・全 API・シグナル・セーブ/ロード
UI/GameDirector.cs               画面切替の司令塔（動的 B 型）。Main.tscn が起動
UI/{TimelineUI,MarriageUI,FormationUI,BattleUI}.cs   4 フェーズ画面
UI/*Overlay.cs / *Screen.cs / JuiceDirector.cs       オーバーレイ・演出
Config/localization_ja.json      全日本語テキストの辞書
Assets/Textures/Jobs/{job}/{male|female}.png         ジョブ立ち絵（16 枚）
Tests/                           xUnit（Core 対象・653 pass）
```

---

## 7. 現状・既知の問題・残課題

**現状**: コアロジック・Godot 外殻・本物の戦闘まで実装・通電済みで、世代交代の循環を端から端まで手で回せる。

**✅ 解消済み（旧・既知の問題）**
- **並走 UI のデッドコード**: かつて `UserInterface/` に到達不能な並走 UI（UserInterfaceRoot/TitleView/HubView/BattleView/
  SettlementView ＋ 死蔵 `Hub/ProphecyTimelineOverlay`）が同居していたが**削除済**。`ProphecyTimelineOverlay` の二重定義も解消。
  残るのは現役共有のみ（`JobTextureLibrary.cs` ＋ `Hub/` の D&D 部品 3 種＝FormationUI が利用）。詳細 [CLAUDE.md](CLAUDE.md) G-3。

**残課題（→ [TODO.md](TODO.md)）**
- 予言生成の本実装（現状 `TimelineEngine.DefaultGenerator` は暫定の均等巡回）
- Affix（接尾効果）／装備ドロップ予言の効果ハンドラ
- 背景 4 枚・敵 5 枚アセットと専用ローダ（→ [docs/ASSET_MANIFEST.md](docs/ASSET_MANIFEST.md)）

---

## 8. 専門ドキュメント索引

| ドキュメント | 内容 |
|---|---|
| [CLAUDE.md](CLAUDE.md) | コードの現状の実態（地図）。Claude Code が自動で読む |
| [instructions.md](instructions.md) | 絶対ルール（開発の四柱） |
| [generated_csharp/README.md](generated_csharp/README.md) | ビルド・起動の詳細手順（Mac の罠込み） |
| [docs/system_architecture.md](docs/system_architecture.md) | 三層・SoT・シグナル・年送りの詳細 |
| [docs/design_blueprint.md](docs/design_blueprint.md) | 設計思想 |
| [docs/job_definitions.md](docs/job_definitions.md) | ジョブ別の挙動詳細 |
| [docs/simulation_guide.md](docs/simulation_guide.md) | ビルド・テスト・シミュレーション検証 |
| [docs/MIGRATION_GODOT_HACK_AND_SLASH.md](docs/MIGRATION_GODOT_HACK_AND_SLASH.md) | 移行戦略憲法（なぜこの設計か） |
| [docs/VISUAL_AND_JUICE_ROADMAP.md](docs/VISUAL_AND_JUICE_ROADMAP.md) / [docs/ASSET_MANIFEST.md](docs/ASSET_MANIFEST.md) | 見栄え強化・必要アセット |
| [PROGRESS_REPORT.md](PROGRESS_REPORT.md) | 実装進捗レポート |

---

*— 本ガイドはコードの実態（`generated_csharp/`）から採取し、グリーンビルド（653 pass）で検収した。将来構想は移行憲法・ロードマップを参照。*
