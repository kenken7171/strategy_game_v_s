# 設計憲法 — ハクスラ・ローグライト化と Godot 4 (.NET/C#) への移行

> 本書は **Chronicle Knights の今後の開発方針を定める戦略ドキュメント** である。
> 個別タスクの実装手順ではなく、「どこへ向かい、なぜそこへ向かうか」「何を継承し、何を変えるか」を宣言する。
> 本書に反する設計判断は、本書を先に更新してから行うこと。
>
> 関連ドキュメント:
> - `instructions.md` — 絶対ルール集（永続指示書）
> - `CLAUDE.md` — 現在のコード実態の設計図
> - `docs/system_architecture.md` — 既存システムのアーキテクチャ詳細

---

## 1. 新たなゲームデザインの目標（ビジョン）

### 1-1. ハクスラ・ローグライトへの転換

これまで Chronicle Knights は「100 年・世代交代型のじっくりプレイ」を志向してきたが、
今後は以下のコアコンセプトに完全シフトする：

> **「1 周（1 旅団の結成から終焉まで）を約 3 時間でサクッと完結でき、
> ドロップや育成に一喜一憂する中毒性の高いハクスラ・ローグライト」**

#### 設計指針

| 軸 | 旧方針（廃止） | 新方針（採用） |
|---|---|---|
| 1 周の長さ | 100 年・無期限 | **約 3 時間で完結する 1 旅団** |
| 中心体験 | 長期的な家系運営 | **戦闘後のドロップ選択と育成の脳汁** |
| プレイヤーの動機 | 物語・歴史の達成感 | **「もう 1 周回したい」中毒性** |
| 失敗時の挙動 | 引退・年送りで継続 | **旅団全滅で 1 周終了、新規旅団で再挑戦** |
| 報酬の質 | 年代記の積み上げ | **戦闘ごとにランダムドロップ・パッシブ強化** |
| 結婚・出産 | 好感度の自然蓄積を待つ | **婚姻ポイントを支払って即時実行** |

旧方針の「世代交代・血統継承」要素は **完全に廃棄するわけではない** が、
1 周 3 時間に圧縮された時間軸の中で「親の能力を子に変異継承する」高速配合システムへと再構成する（セクション 4 参照）。

### 1-2. 公開戦略

```
[Phase A] itch.io アルファ版 (無料 / 投げ銭)
            ↓ コミュニティの熱心なフィードバックを集約
            ↓ ハクスラ要素の調整・遺伝バリエーション拡張
[Phase B] Steam 正式リリース
```

- **itch.io 先行公開**: ハクスラ・ローグライト系の熱心な早期ユーザーが集まるプラットフォームでアルファ版を公開し、バランス調整・パッシブ重複の組合せ評価・遺伝モデルの面白さを実プレイヤーから得る
- **Steam 正式リリース**: itch.io でコアプレイループが成立した段階で、Steam にて正式版として公開

---

## 2. ゲームエンジンおよび技術スタックの刷新

### 2-1. Godot Engine 4.x (.NET/C# 版) への移行

現行スタック (Bun + Hono + React + Vite) は Web アプリとしての検証には極めて優秀だったが、
ハクスラ・ローグライトのプレゼンテーション層・配布・Steam 統合を考えると以下が理想：

| 要件 | Godot 4 (.NET) の優位性 |
|---|---|
| 2D 表現（ドット絵 16-bit） | Tilemap / Sprite2D で軽快、`image-rendering: pixelated` 相当が標準 |
| UI システム | Control ノード階層が宣言的、現行 React コンポーネント構造と素直に対応 |
| マルチプラットフォーム | Windows / macOS / Linux にワンクリックエクスポート |
| Steam 統合 | GodotSteam プラグインで実績・クラウドセーブが容易 |
| 配布サイズ | Web ビルド (現状 200KB) より大きくなるが、ネイティブ実行で起動が速い |
| 静的型 | C# のレコード型・null 許容で TS と同等以上の型安全 |

#### 採用バージョン

- **Godot 4.x（最新安定版）**
- **.NET 8 LTS** （C# 12 機能を活用）
- 出力ターゲット: Windows / macOS / Linux （まずは PC 3 OS）

### 2-2. C# による不変（Immutable）モデルの継承

現行 TypeScript で磨き上げた以下の設計思想を **C# にそのまま美しく翻訳する**：

#### 翻訳マッピング

| TypeScript（現行） | C#（移行先） | 備考 |
|---|---|---|
| `class Unit { readonly ... }` | `record Unit` または `sealed class Unit` (with init-only props) | `record` の `with` 式で `grow()` `takeDamage()` を簡潔表現 |
| `readonly Map<string, number>` | `ImmutableDictionary<string, int>` (`System.Collections.Immutable`) | affinity の不変保証 |
| `type JobType = "iron_wall_knight" \| ...` | `enum JobType { IronWallKnight, ... }` | 文字列マッチを enum + switch expression に集約 |
| `JOB_PASSIVES` predicate object | `static class JobPassives` with `bool` predicates | 同じ宣言的呼び出しスタイル |
| `processIntegratedTurn() { Phase A → B → C → B }` | `IntegratedTurnResult ProcessIntegratedTurn()` | Phase 分離をそのまま継承（メソッド名同一） |

#### 不変性の徹底

- C# 側でも **`Unit` インスタンスは一切ミューテートしない**
- `with` 式で新インスタンスを返す関数型スタイルを継続
- 副作用は `BattleManager` / `BattleSimulator` のオーケストレータに局所化

```csharp
// TypeScript: return new Unit({ ...this, hp: Math.max(0, this.hp - amount) });
public Unit TakeDamage(int amount) => this with { Hp = Math.Max(0, Hp - amount) };
```

### 2-3. プロジェクト構成（移行後イメージ）

```
chronicle_knights_godot/
├── project.godot              # Godot プロジェクトファイル
├── ChronicleKnights.csproj    # .NET プロジェクト
├── src/
│   ├── core/                  # ゲームロジック層（純粋 C#、Godot 依存なし）
│   │   ├── Models/            # Unit, Squad, Brigade, Enemy
│   │   ├── Battle/            # BattleManager, BattleSimulator
│   │   ├── Data/              # Jobs (JOB_DEFAULTS, JOB_PASSIVES, JOB_FORMATION_GUIDE)
│   │   ├── Marriage/          # MarriagePoint, MarriageCost (本書 セクション 4)
│   │   └── Services/          # HumanDecisionService
│   ├── ui/                    # Godot Control ノード継承の UI 層
│   │   ├── Formation/         # V 字フォーメーション画面
│   │   ├── Battle/            # 戦闘画面 (EnemyStatusCard, IntentBanner)
│   │   ├── Guild/             # 人事画面 + 婚姻画面（NEW）
│   │   ├── Chronicle/         # 年代記画面
│   │   └── Common/            # UnitIcon, JobManualOverlay
│   └── tests/                 # NUnit / xUnit による回帰テスト
└── assets/
    └── image/{jobId}/{gender}.png  # 16-bit ドット絵原本
```

`src/core` は **Godot ノードを継承しない純粋な C# プロジェクト** とする。
これにより、CLI でのシミュレーション・テスト実行が現行と同様に高速で回せる。

---

## 3. フロントエンド（UI/UX）の思想継承

### 3-1. 既存仕様の 100% 移植

現行で磨き上げた以下の UI/UX 仕様は **デザイン的にも実装パターン的にも完成形** に近い。
Godot の Control ノードで完全に再現する：

| 既存仕様 | Godot 実装方針 |
|---|---|
| **V 字型フォーメーション**（FRONT 中央上 / REAR-L 左下 / REAR-R 右下） | `GridContainer` をベースに、`anchor` と `offset` で V 字配置を実現 |
| **分隊単位のローテーション (squad swap)**（CW: REAR-L→FRONT、CCW: REAR-R→FRONT） | C# 側 `BattleSimulator.RotateGrid` で既存ロジックそのまま、UI 側は `Tween` で squad ノード位置をアニメーション swap |
| **ターゲット分隊の赤枠脈動エフェクト** | `Control` ノードに `Shader` または `AnimationPlayer` で `box-shadow + pulse` 相当を再現 |
| **グローバルヘッダー常設「📖 ジョブ説明」ボタン** | ルートシーンの `MarginContainer` に固定配置、`PopupPanel` で全画面オーバーレイ |
| **4 色配色のミニ V 字図付きジョブマニュアル** | サイドバー: `ItemList` で 8 ジョブ、メイン: `VBoxContainer` + ミニ V 字図、配色は **defend=青 / buff=金 / heal=緑 / attack=朱** をそのまま継承 |
| **EnemyStatusCard（最上部の敵カード）** | `PanelContainer` + `ProgressBar` で HP バー、`HBoxContainer` で chip 表示 |
| **AttackIntent バナー**（次ターン攻撃予告） | `RichTextLabel` + `Tween` で SINGLE_STRIKE/PINCER/TOTAL_ASSAULT を色分け |
| **次鋒予告**（CW/CCW 押下時に FRONT へ来る squad のメンバー予告） | `Button` 子ノードに次鋒メンバー名を表示 |
| **UnitIcon の親要素フィット型**（width: 100%; height: 100%; object-fit: contain） | `TextureRect` の `Stretch Mode = "Keep Aspect"` で同等再現、`image-rendering: pixelated` は `texture_filter = Nearest` |

### 3-2. data-testid 規約の C# 版継承

現行 frontend で徹底している「全コンポーネントに `data-testid` 必須」のルールは、
Godot 版でも **`Node.Name` を testid と等価に扱う規約** として継承：

| 現行 | Godot 版 |
|---|---|
| `<div data-testid="formation-v-shape-root">` | `Control Name="FormationVShapeRoot"` |
| `<div data-testid="formation-target-slot-FRONT-0">` | `Button Name="FormationTargetSlotFRONT0"` |
| `<div data-testid="battle-live-v-squad-FRONT" data-targeted="true">` | `Control Name="BattleLiveVSquadFRONT"` with `meta["targeted"] = true` |

E2E テストツール（Godot 用には [GdUnit4](https://github.com/MikeSchulze/gdUnit4) など）から
`Find_node()` で同等に参照可能。命名規則は kebab-case → PascalCase に機械的変換。

### 3-3. 直感的な日本語ラベルの維持（SoT として引き継ぎ）

今回のリファクタで整備した **「略称 → 直感的な日本語」のマッピング** は、
Godot 版でも **そのまま SoT として引き継ぐ**：

| 内部キー (不変) | UI 表示ラベル (継承) | 説明 |
|---|---|---|
| `bdf` | **🛡️ 大隊総守護力** | 大隊全員の受けるダメージ軽減 |
| `sdf` | **🛡️ 分隊守護力** | 自分隊の受けるダメージ軽減 |
| `ab`  | **⚡ 突撃号令** | ターン開始時の速度・攻撃バフ |
| `hl`  | **💚 ターン末分隊治癒** | ターン終了時の分隊回復量 |
| `special-double-strike` | **🎯 二の矢** | sniper の 1 番手 + 先頭時 2 連撃 |

C# 側では `JobPassives.cs` に `LabelJp` プロパティとして格納し、UI ラベルは必ずこれを経由する。

```csharp
public static class PassiveLabelJp
{
    public const string Bdf = "🛡️ 大隊総守護力";
    public const string Sdf = "🛡️ 分隊守護力";
    public const string Ab  = "⚡ 突撃号令";
    public const string Hl  = "💚 ターン末分隊治癒";
}
```

HP / SPD / FA / RA など広く認知されている略称は **そのまま英略のまま維持**。

### 3-4. アセットパイプライン

- ドット絵原本 (`/image/{jobId}/{gender}.png`) は **そのまま Godot プロジェクトの `assets/image/` 配下へコピー**
- パス規約 `image/{jobId}/{gender}.png` は不変
- `texture_filter = Nearest` で `pixelated` を強制適用
- `formatJob()` / `getJobIconPath()` 相当を C# 側 `JobUtils.cs` に翻訳

---

## 4. 高速世代交代：戦闘報酬ポイントによる「手動婚姻システム」

ハクスラ・ローグライトの 3 時間テンポを実現するために、世代交代の中核である
「結婚・出産」を、**プレイヤーの直接選択による即時実行制** へ全面再設計する。

### 4-1. 廃止する旧仕様

| 旧仕様（完全廃止） | 廃止理由 |
|---|---|
| **同分隊配置による好感度の自然蓄積** | 同じ男女ペアを何度も戦わせる「作業」が発生し、3 時間テンポを阻害する |
| **`applyBattleAffinity`（戦闘ごとの affinity 加算）** | プレイヤー意思の介在余地が薄く、最適解が機械的に固定化される |
| **好感度しきい値で自動結婚 → 翌年自動出産** | プレイヤーが「今ここで配合する」という選択を持てない |
| **`Brigade.advance` 内の自然婚姻ルール** | 1 年単位の自然進行モデル全体を「戦闘単位の即時実行モデル」へ置き換えるため |

`Unit.affinity` プロパティ自体は C# 翻訳時にも残すが、その用途は
「血統履歴の参照値（誰と組んで戦ったか）」のみに縮退し、**結婚条件には一切使われない**。

### 4-2. 新仕様：婚姻ポイント（Marriage Point）の導入

結婚は無料の自然発生ではなく、**戦闘等で得られるリソース（婚姻ポイント）を支払う能動的アクション** に変わる：

#### ポイントの収入源

| 収入源 | 獲得タイミング | 量 |
|---|---|---|
| **定時収入** | 毎年 1 ポイント | **1 pt / 年**（最低保証） |
| **撃破ボーナス（ハクスラ的ドロップ）** | 戦闘で敵を撃破した時 | **敵の強さ・レアリティに応じて加算** |
| **戦闘勝利ボーナス** | 戦闘終了時（勝利時のみ） | **基本 +N pt + 残存兵力ボーナス**（要バランス調整） |

撃破ボーナスの計算式（暫定設計、要 itch.io アルファでの調整）：

```
撃破ポイント = floor(敵の totalRating / 100) × レアリティ倍率
              + クリティカル撃破ボーナス（先制 1 番手で撃破した時の上乗せ）
```

#### ポイントの消費先（結婚コスト）

結婚は **新規アクション「💞 婚姻」** として人事フェーズに追加され、
ポイントを消費して「2 名の旅団員を即時結婚 → 即時に子供 1 名を生成 → ベンチに追加」する：

```
[人事フェーズで「💞 婚姻」を選択]
   ↓
[結婚させる男女 2 名を旅団員リストから直接選択]
   ↓
[システムが両者の totalRating・パッシブ・血統から必要ポイントを算出して表示]
   ↓
[プレイヤーが「結婚させる（X pt 消費）」を確定]
   ↓
[子供ユニットが即時に生成され、ベンチに追加（次の編成で出撃可能）]
```

これにより、好感度の蓄積を待つ必要がなくなり、**戦闘 1 回 → 即婚姻 → 即配合 → 次戦闘** の高速ループが成立する。

### 4-3. 戦略的トレードオフ（必要ポイントの設計）

結婚コストは「両親の戦闘的価値の総和」に比例して上がる。
これにより、プレイヤーに **「今すぐ中堅同士で配合するか、強敵を倒してポイントを貯めてエリート配合するか」** の選択を迫る：

| 配合パターン | コスト感 | リターン |
|---|---|---|
| 中堅 × 中堅 | 低コスト（数 pt） | 平均的な子供。次戦闘の即戦力には届くが大化けは期待薄 |
| エリート × 中堅 | 中コスト | 親 A の強パッシブを子が継承する可能性 |
| **エリート × エリート** | **高コスト（数十 pt）** | 両親のパッシブが交差する高遺伝の子（変異込み） |
| 血統濃縮（継承者同士） | 最高コスト | パッシブ Lv の連鎖、ただし近親リスクのデバフ要素を別途検討 |

コスト式の暫定設計（要 itch.io 調整）：

```csharp
public static int CalculateMarriageCost(Unit a, Unit b)
{
    var ratingCost      = (a.TotalRating + b.TotalRating) / 20;
    var passiveCost     = (a.UniquePassiveCount + b.UniquePassiveCount) * 3;
    var lineageMultiplier = (a.HasLineage && b.HasLineage) ? 2.0 : 1.0;
    return (int)Math.Ceiling((ratingCost + passiveCost) * lineageMultiplier);
}
```

### 4-4. C# データモデルの骨子（フェーズ 3 で実装）

```csharp
namespace ChronicleKnights.Core.Marriage;

/// <summary>
/// プレイヤーが保有する婚姻ポイント。Brigade の不変状態の一部として保持。
/// </summary>
public record MarriagePoints
{
    public required int Current { get; init; }
    /// <summary>累計取得（プレイヤー実績用） </summary>
    public required int TotalEarned { get; init; }

    public MarriagePoints Earn(int delta)
        => this with { Current = Current + delta, TotalEarned = TotalEarned + delta };

    public MarriagePoints Spend(int cost)
        => Current >= cost
            ? this with { Current = Current - cost }
            : throw new InvalidOperationException($"Not enough points: need {cost}, have {Current}");
}

/// <summary>
/// 結婚コスト算出と即時婚姻・出産処理（純粋関数）。
/// </summary>
public static class ManualMarriageService
{
    public record MarriageQuote(int Cost, string Breakdown);

    public static MarriageQuote Quote(Unit father, Unit mother) { /* 4-3 の式 */ }

    public record MarriageResult(
        Brigade NewBrigade,
        MarriagePoints NewPoints,
        Unit NewChild  // 即時生成された子供
    );

    public static MarriageResult ExecuteMarriage(
        Brigade brigade,
        MarriagePoints points,
        Unit father,
        Unit mother,
        Func<double> rng
    ) { /* GeneticInheritance を使って子を生成 */ }
}
```

### 4-5. 既存システムへの影響範囲

| 既存システム | 影響 |
|---|---|
| `Brigade.applyBattleAffinity` | **TS 版から削除**（C# 版では翻訳しない） |
| `Brigade.advance` 内の自動婚姻ロジック | **削除**。年送りでは年齢 +1 と引退判定のみを行う |
| `Unit.affinity` プロパティ | **保持**（戦闘履歴の参照値として）。結婚条件としては不使用 |
| `HumanDecisionService` | **拡張**: 新アクション `proposeMarriage(fatherId, motherId)` を追加 |
| 人事画面 UI | **拡張**: 「💞 婚姻」セクション追加、保有 pt 表示、相手選択 UI |
| 戦闘終了処理 | **拡張**: 撃破ポイントを集計して `MarriagePoints.Earn(delta)` を呼ぶ |

### 4-6. data-testid 設計（C# 版 Node.Name）

新規追加（命名規約は既存通り kebab-case → PascalCase）：

| 用途 | testid / Node.Name |
|---|---|
| 婚姻ポイント残高表示 | `guild-marriage-points-balance` |
| 婚姻アクションボタン | `guild-marriage-action-button` |
| 結婚相手選択（父） | `guild-marriage-father-select` |
| 結婚相手選択（母） | `guild-marriage-mother-select` |
| 必要ポイント見積もり表示 | `guild-marriage-cost-quote` |
| 婚姻確定ボタン | `guild-marriage-confirm-button` |
| 撃破ボーナス通知（戦闘後） | `battle-result-marriage-point-gain` |

---

## 5. 今後の開発ロードマップ（3 つのフェーズ）

### フェーズ 1: コアロジックの C# 翻訳

**目的**: 現行 TypeScript で 41 pass を達成している実績あるコアロジックを、C# へ意味を変えずに 1 対 1 翻訳する。

#### 翻訳対象（優先度順）

| # | TS ファイル | C# 翻訳先 | テスト |
|---|---|---|---|
| 1 | `packages/core/src/models/Unit.ts` | `src/core/Models/Unit.cs` (record) | Unit プロパティ・grow/takeDamage |
| 2 | `packages/core/src/models/Squad.ts` | `src/core/Models/Squad.cs` | replaceUnits / averageSpeed |
| 3 | `packages/core/src/models/Enemy.ts` | `src/core/Models/Enemy.cs` | getActionForTurn |
| 4 | `packages/core/src/data/jobs.ts` | `src/core/Data/Jobs.cs` | JOB_DEFAULTS / JOB_PASSIVES / JOB_FORMATION_GUIDE / JOB_ABILITY |
| 5 | `packages/core/src/BattleManager.ts` | `src/core/Battle/BattleManager.cs` | Phase A/B/C 分離継承、`JobPassives` 述語で文字列マッチ排除 |
| 6 | `packages/core/src/BattleSimulator.ts` | `src/core/Battle/BattleSimulator.cs` | runOneTurn / rotateGrid (squad swap) / getNextActionIntent / getEnemyState |
| 7 | `packages/core/src/services/HumanDecisionService.ts` | `src/core/Services/HumanDecisionService.cs` | 純粋関数群 |
| 8 | `packages/core/src/data/names.ts` | `src/core/Data/Names.cs` | NameGenerator (3 文化圏) |
| 9 | `packages/core/src/config/ChronicleConfig.ts` | `src/core/Config/ChronicleConfig.cs` | settings 一元管理 |

#### 翻訳の流儀

- **メソッド名・型名はそのまま PascalCase 化**（`processIntegratedTurn` → `ProcessIntegratedTurn`）
- **コメントは日本語のまま完全保持**（仕様書としての価値）
- **既存テスト `packages/core/test/*.test.ts` を NUnit/xUnit に 1 対 1 翻訳** し、**全 41 ケースが C# 側でも pass する** ことを完了条件とする
- Claude Code を活用し、ファイル単位でレビューしながら翻訳を進める

#### フェーズ 1 完了条件

- [ ] core 全 9 ファイルの C# 翻訳完了
- [ ] 41 ケース相当のテストが C# 側でも全 pass
- [ ] CLI からシミュレーションを 100 年回せる（現行と同じシード・同じ結果）

### フェーズ 2: ハクスラ要素（ランダムドロップ・能力重複）のドメイン拡張

**目的**: 中毒性の中核となる「戦闘後のドロップ選択」と「パッシブの組合せ爆発」を導入する。

#### 拡張ドメインモデル（C# 設計）

```csharp
// 装備（ハクスラ風のランダム生成）
public record Equipment
{
    public required string Id { get; init; }
    public required EquipmentSlot Slot { get; init; }   // Weapon / Armor / Trinket
    public required EquipmentRarity Rarity { get; init; } // Common / Rare / Epic / Legendary
    public required int Level { get; init; }              // 1 〜 3
    public required ImmutableArray<Affix> Affixes { get; init; }
}

// パッシブの強化レベル
public record PassiveInstance
{
    public required string Key { get; init; }    // "bdf" | "sdf" | "ab" | "hl" | ...
    public required int Level { get; init; }      // Lv.1 〜 Lv.3
    public required int BonusValue { get; init; } // Lv 増加で値も増える
}

// ランダムな付加価値（Affix）
public record Affix
{
    public required AffixKind Kind { get; init; }
    public required int Value { get; init; }
    public required string DisplayLabel { get; init; } // 「⚡ 突撃号令 +5」等
}
```

#### Affix の例（既存パッシブ命名を活用）

| Affix キー | 表示ラベル | 効果 |
|---|---|---|
| `affix-bdf-plus` | 🛡️ 大隊総守護力 +N | BDF 値が +N |
| `affix-double-strike-chance` | 🎯 二の矢 確率 +N% | sniper 以外でも 2 連撃確率を付与 |
| `affix-spd-plus` | SPD +N | 素のスピード加算 |
| `affix-ab-radius` | ⚡ 突撃号令 +N | AB 値追加 |
| `affix-marriage-point-bonus` | 💞 婚姻ポイント獲得 +N% | 撃破時の婚姻ポイント獲得量を増加（4 章と連携） |

#### ドロップシステム

- **戦闘勝利ごとに 3 つの選択肢** から 1 つを選ぶ（Roguelite の定番パターン）
- 選択肢は `Equipment` または `PassiveInstance` のランダム生成
- レア度（Common ~ Legendary）が高いほど Affix 数・値が増える
- **「現在装備中の Affix と組み合わさったら強い選択肢」をハイライト表示**（プレイヤーへの戦略ヒント）

#### フェーズ 2 完了条件

- [ ] `Equipment` / `PassiveInstance` / `Affix` データモデルを C# で実装
- [ ] 戦闘後の選択 UI を Godot で実装
- [ ] BattleManager が Affix 効果を Phase C で参照する形に拡張（既存 JobPassives 述語と同じパターン）
- [ ] バランス検証: 10 周自動シミュレーションで「3 時間相当 = 30 戦闘」をプレイし、Affix の組合せが面白く効くことを確認

### フェーズ 3: 時間軸圧縮と高速世代交代（配合システム）の構築

**目的**: 3 時間で脳汁が出るテンポを実現するため、時間軸を圧縮し、世代交代を「手動婚姻システム」と「ハクスラの楽しみ」に統合する。

#### 時間軸の圧縮

| 軸 | 現行 | フェーズ 3 後 |
|---|---|---|
| 1 年あたりのアクション | 4 フェーズ（Chronicle/Guild/Formation/Battle） | **戦闘 1 回 + 高速報酬選択 + 任意で 1 回の婚姻** |
| 全盛期の長さ | 約 8 年 | **約 3 戦闘** |
| 1 周あたりの年数 | 100 年 | **約 30 戦闘 ≈ 30 年（圧縮）** |
| 加齢ロジック | 1 年ずつ | **戦闘ごとに 1 年加齢**（戦闘 = 1 年） |
| 結婚契機 | 好感度しきい値の自動発火 | **手動婚姻：プレイヤーがポイントを支払って明示実行**（セクション 4） |

`ChronicleConfig.TIME.PEAK_*` 等の数値を圧縮スケールに調整した
`ChronicleConfig.hackandslash.cs` を別ファイルとして用意する（既存の extreme 設定と同じパターン）。

#### 配合の流れ（手動婚姻 + 遺伝・変異）

```
[戦闘 1 周終了] → [撃破ポイント獲得 → 婚姻ポイント残高に加算]
         ↓
[人事フェーズで「💞 婚姻」を選択（任意）]
         ↓
[男女 2 名を選択 → 必要ポイント表示]
         ↓
[ポイントを支払って即時婚姻実行]
         ↓
[子の Affix 生成:
   ・継承   (80%)  → 親と同じ Affix 値
   ・変異   (15%)  → 親より +1 Lv の Affix 値
   ・新規   ( 5%)  → 完全に新しい Affix が出現]
         ↓
[子はベンチに即時追加され、次の編成で出撃可能]
         ↓
[1 周終了時、生き残った旅団員と婚姻で生まれた子供たちが
  「次の旅団に持ち越せる遺伝プール」として保存される]
```

これにより、**1 周は約 3 時間で終わるが、「次の旅団に何を継承させるか」がメタ進行として残り続ける** ループが成立する。

#### 遺伝モデル（C# データ骨子）

```csharp
public record GeneticInheritance
{
    /** 親 2 名のパッシブ Affix セットから子のパッシブを派生 */
    public required ImmutableArray<PassiveInstance> InheritedPassives { get; init; }
    /** 変異率（指定確率で親より +1Lv の能力に変異） */
    public required double MutationRate { get; init; }
    /** 変異した能力のリスト */
    public required ImmutableArray<PassiveInstance> MutatedPassives { get; init; }
}
```

#### フェーズ 3 完了条件

- [ ] 時間軸圧縮スケール (`ChronicleConfig.hackandslash`) を定義
- [ ] `MarriagePoints` / `ManualMarriageService` を実装（セクション 4-4 参照）
- [ ] 戦闘後の撃破ポイント加算を `BattleSimulator` に組み込み
- [ ] 「💞 婚姻」UI（pt 残高表示・相手選択・コスト見積もり・確定）を Godot で実装
- [ ] `GeneticInheritance` モデルで子の Affix 生成を実装
- [ ] 「次の旅団に継承可能な子のプール」をセーブデータに永続化
- [ ] 10 周連続プレイで「初代旅団 → 5 代目」までメタ進行が機能することを確認

---

## 6. 設計憲法（不変ルール）

本書を実行する上で、以下のルールは **絶対に破棄しない**：

| ルール | 理由 |
|---|---|
| **Unit / Squad / Brigade はイミュータブル** | C# 翻訳後も `record` + `with` 式で維持。並列処理・テスト容易性のため |
| **ジョブ文字列マッチは `JobPassives` 述語に集約** | TS 版で確立した「BattleManager をジョブ非依存に保つ」原則を C# でも継承 |
| **JOB_DEFAULTS / JOB_PASSIVES / JOB_FORMATION_GUIDE が SoT** | データ駆動の設計を維持。新ジョブ追加はデータ追加のみで完結すること |
| **V 字フォーメーションを全画面で 100% 統一** | 編成・戦闘・モーダル・マニュアルすべてで同じ視覚表現 |
| **UI ラベルは 略称ではなく日本語**（HP/SPD 等の汎用略語は例外） | 「🛡️ 大隊総守護力」等の和訳辞書を SoT として継承 |
| **結婚は手動・即時・有償**（セクション 4） | 好感度蓄積による自然婚姻は完全廃止。プレイヤーの意思決定リソースとして設計 |
| **全 UI ノードに識別子 (testid 相当の Name)** | E2E テスト・自動検証を継続可能にする |
| **コミットメッセージは日本語**（英語 type prefix 付き） | プロジェクト全体の慣習を継承 |
| **設定値はハードコードせず `ChronicleConfig.*.cs` 経由** | 既存の extreme / hackandslash 設定切替パターンを継承 |

---

## 7. 移行を判断する境界条件

以下の条件が満たされた段階で、TypeScript 版から Godot 版へ主軸を完全に移す：

- [ ] フェーズ 1 完了（C# core が TS core と機能等価、テスト 41 pass）
- [ ] Godot 版で V 字配置・戦闘ターン処理・ジョブマニュアルが動作
- [ ] フェーズ 2 完了（戦闘後ドロップ・Affix 組合せが機能）
- [ ] フェーズ 3 完了（手動婚姻・撃破ポイント・遺伝継承が機能）
- [ ] バランス検証で「1 周 3 時間」のテンポが成立
- [ ] itch.io 公開準備（実績システム・セーブシステム・タイトル画面）

TypeScript 版は **アーキタイプ検証用のレファレンス実装** として保持し、
将来のロジック仕様変更時には「先に TS で検証 → C# に翻訳」のフローを継続する選択肢も残す。

---

## 付録 A: 翻訳例（TS → C#）

参考として、現行 `Unit.ts` を C# に翻訳した場合のスケッチを示す（実装ではない、流儀の例示）：

```csharp
namespace ChronicleKnights.Core.Models;

public enum JobType
{
    IronWallKnight, Tactician, Medic, Sniper,
    Sorcerer, StandardBearer, HeavyInfantry, Scout,
}

public enum Gender { Male, Female }

public enum Origin { Japanese, European, Classical }

public record Stats(int Strength, int Agility, int Intelligence, int Endurance);

public record Parents(string FatherId, string MotherId);

public record Unit
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required int Age { get; init; }
    public int? BirthYear { get; init; }
    public required int PeakStartAge { get; init; }
    public required int PeakEndAge { get; init; }
    public required int MaxAge { get; init; }
    public required Stats BaseStats { get; init; }
    public int MaxHp { get; init; } = 100;
    public int Hp { get; init; } = 100;
    public int Speed { get; init; } = 0;
    public int FrontAttack { get; init; } = 0;
    public int RearAttack { get; init; } = 0;
    public JobType? Job { get; init; }
    public int Sdf { get; init; } = 0;
    public int Bdf { get; init; } = 0;
    public int Ab { get; init; } = 0;
    public int Hl { get; init; } = 0;
    public int SpeedBuff { get; init; } = 0;
    public int AttackBuff { get; init; } = 0;
    public Gender Gender { get; init; } = Gender.Male;
    public ImmutableDictionary<string, int> Affinity { get; init; }
        = ImmutableDictionary<string, int>.Empty;
    public Parents? Parents { get; init; }
    public string? SpouseId { get; init; }
    public Origin Origin { get; init; } = Origin.European;

    // 派生プロパティ（TS 版の getter と同じ計算）
    public double GrowthFactor => /* 三段階モデル */ ...;
    public Stats Stats => /* baseStats × growthFactor */ ...;
    public bool IsRetired => Age >= MaxAge;
    public bool IsAlive  => Hp > 0;
    public bool IsMarried => SpouseId is not null;
    public int FinalSpeed       => Speed + SpeedBuff;
    public int FinalFrontAttack => FrontAttack + AttackBuff;
    public int FinalRearAttack  => RearAttack + AttackBuff;

    // イミュータブル更新メソッド（TS 版と完全に同じ API）
    public Unit Grow()                 => this with { Age = Age + 1 };
    public Unit TakeDamage(int amount) => this with { Hp = Math.Max(0, Hp - amount) };
    public Unit WithHeal(int amount)   => this with { Hp = Math.Min(MaxHp, Hp + amount) };
    public Unit WithBuffs(int spd, int atk)
        => this with { SpeedBuff = SpeedBuff + spd, AttackBuff = AttackBuff + atk };
    public Unit ResetBuffs()           => this with { SpeedBuff = 0, AttackBuff = 0 };
    public Unit WithSpouse(string id)  => this with { SpouseId = id };
    public Unit WithIncreasedAffinity(string otherId, int delta)
        => this with { Affinity = Affinity.SetItem(otherId, Affinity.GetValueOrDefault(otherId) + delta) };
}
```

C# の `record` + `with` 式により、TS 版の不変パターンが **より少ない記述で同じ意味** で表現できる。

---

## 付録 B: 関連既存ドキュメント一覧

| ファイル | 役割 | 移行時の扱い |
|---|---|---|
| `instructions.md` | 絶対ルール集 | C# 版でも憲法として継承（数値ルール = config に集約） |
| `CLAUDE.md` | 現在のコード実態の設計図 | TS 版の最終スナップショットとして凍結 |
| `docs/design_blueprint.md` | 三段階モデル他の設計図 | C# 版でも参照する仕様書として継承 |
| `docs/system_architecture.md` | アーキテクチャ詳細 | C# 版で新たに書き起こす |
| `docs/job_definitions.md` | ジョブ定義リファレンス | JOB_DEFAULTS の SoT として C# 版でも参照 |
| `docs/simulation_guide.md` | シミュレーション実行手順 | CLI 部分は C# 版でも継承 |
| `docs/MIGRATION_GODOT_HACK_AND_SLASH.md` (本書) | 移行戦略憲法 | フェーズ 3 完了まで継続更新 |

---

> **本書は「未来の設計図」である。**
> 実装はこの憲法に沿って進め、憲法に反する判断が必要になったときは、
> まず本書を更新し、その差分と理由をコミットメッセージに明記すること。
