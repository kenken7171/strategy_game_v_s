# Chronicle Knights — 開発の絶対ルール（Instructions）

> このドキュメントはプロジェクトの **永続的な指示書（憲法）** であり、すべての実装で必ず遵守する。
> ここに記された方針はメタ分析と過去の意思決定の集約であり、**個別タスクで無断で変えてはならない**。
> 仕様変更が必要な場合は本ドキュメントを先に更新し、その差分と理由を PR/コミットメッセージに明記すること。
>
> 役割分担: 本書＝「守るべきルール」／ `CLAUDE.md`＝「コードの現在の実態」／
> `docs/MIGRATION_GODOT_HACK_AND_SLASH.md`＝「将来の戦略（移行憲法）」。
>
> 対象は現役本体 **`generated_csharp/`（Godot 4.3 / .NET 8 / C# 12）**。
> 旧 TypeScript 版（`apps/` `packages/` `scripts/`）は凍結された参照専用で、本書の適用外（変更禁止）。

---

## 0. ゲームの最終目標

**Chronicle Knights は「世代交代型ハクスラ・ローグライト RPG」である。**

- プレイヤーは個々の英雄ではなく **「旅団（大隊）」という組織と、そこに連なる血脈** を導く。
- 個々の騎士は生まれ・育ち・全盛期を迎え・衰退し・寿命や戦死で旅団を去る（**完全ロスト**）。
- 婚姻・出産・継承で家系が紡がれ、外様（血縁なしの傭兵）を雇って世代を繋ぐ。
- **戦闘の勝敗は重要だが、それ以上に「誰を残し、誰を切り、誰を継ぐか」というプレイヤーの選択が物語を作る。**
- **「1 周（1 旅団の興亡）＝ 約 3 時間で完結し、ドロップ・育成・配合に一喜一憂する中毒性」** を志向する。

参考: Venus & Braves（時の流れと人の一生を中心に据えたゲームデザイン）。

---

## 1. 開発の四柱（設計憲法・最優先）

すべてのコードはこの 4 つを 1 ビットの隙もなく死守する。

### 憲法①：厳格 ASCII（言葉と仕組みの分離）

- `Core/` および UI 層の **識別子・クラス名・ノード名・testid・内部ログ・アセットパス・コメント中の記号は ASCII のみ**。
- **プレイヤー向け表示文字列だけが日本語**で、その実体は `Config/localization_ja.json` に完全分離する。
  コードは ASCII の「キー」だけを扱い、表示時に `NameResolver` / `MasterDataNameResolver` 等で辞書を引く。
- 例外: コード中の **日本語コメント** と **開発者向け診断メッセージ** は許容（プレイヤーの目に触れないため）。
- **略称（BDF / SDF / AB / HL / FA / RA）は完全禁止**。正式名称（`BattalionDefense` 等）を使う。
- 禁止: 日本語をロジックへ直書きすること。UI ラベルを localization を経由せず直書きすること。

### 憲法②：不変性（イミュータブル）の徹底

- ドメインは `record`（または init-only プロパティ）で定義し、**一度作ったら中身を書き換えない**。
- 変更は必ず `with` 式で **新インスタンスを返す**（`Unit.WithAgeProgress` 等）。
- コレクションは `ImmutableList<T>` / `ImmutableArray<T>` / `ImmutableDictionary<K,V>` を使い「丸ごと差し替える」。
- 禁止: `unit.Age = ...` のような破壊的代入。可変フィールドでドメイン状態を持つこと。

### 憲法③：単一 SoT ＋ 単方向データフロー

- ゲーム全状態は autoload **`/root/ChronicleGlobal`** ただ 1 人が握る（SoT）。
- UI は状態を **直接書き換えない**（すべて private setter）。必ず `ChronicleGlobal` の API を呼び、
  シグナル（`StateInitialized`/`EconomyChanged`/`TimelineChanged`/`RosterChanged`/`FormationChanged`/
  `BattleChanged`/`PhaseChanged`）を受けて状態を読み直して再描画する（無状態 UI）。
- スレッド安全規律: 状態変更はすべて `lock(_stateLock)` 内。**`EmitSignal` は必ずロック解放後**（`SafeEmit`、
  デッドロック防止）。年送り時の発火順は **Roster → Economy → Timeline →（必要時 Formation）→ Phase**
  （画面切替前にデータ確定を保証）。
- 禁止: UI 側に独自のゲームロジック／状態キャッシュを持つこと。`setState` 的な自由な状態書き換え。

### 憲法④：完全決定論シード

- 新規ゲームは 1 つのシードを注入（`ChronicleGlobal.StartNewGame(seed)` → `Initialize(rng: new Random(seed))`）。
  **同一シードからは同一の歴史**が再現される。
- 乱数は必ず引数注入（`Random` を DI）。Core の純粋関数は副作用なしで外部からシードされる。
- 戦闘は専用乱数 `_battleRng` を `StartBattle` で再シードし、世代用 `_rng` と独立した決定論ストリームを持つ。
- 禁止: グローバル `Random` の暗黙利用。Random をセーブデータへ含めること（ロード時に再注入する）。

---

## 2. バトル・敵の絶対ルール

### 2-1. 大隊規模: **9 名（3 × 3）**

- 編成盤面 `FormationBoard` は `RowCount=3` × `ColumnsPerRow=3` = **`SlotCount=9`** で固定。
- row は `Front`(中央上) / `RearLeft`(左下) / `RearRight`(右下) の **▲ウェッジ（V 字）配置**。
- 禁止: 9 名以外への変更。旧 12 名（4×3）は廃止済み。

### 2-2. 敵スケーリング（`Core/Battle/EnemyScaler.cs`）

- 基準値: `BaseHp=150` / `BaseAttack=30` / `BaseSpeed=100`。
- 年率上昇: `HpGainPerYear=5.0` / `AttackGainPerYear=0.6` / `SpeedGainPerYear=0.6`。
- 禁止: `SpeedGainPerYear` を過去の 1.5 へ戻すこと（過剰デフレを招くため緩和済み）。

### 2-3. 敵ステータスの **±15% 個体差**

- 個体差ジッタ = `0.85 + rng.NextDouble() * 0.30`（`JitterFloor=0.85` / `JitterSpan=0.30`）を HP/ATK/SPD に適用。
- 禁止: 固定値（振れ幅 0%）での敵生成。ローグライク「一度きりの賽の目」を戦闘へ導入するための必須仕様。

### 2-4. 章（Epoch）と章ボス

- 100 年 / 25 年で 1 章（`YearsPerEpoch=25`）。時代 = `Dawn / Upheaval / Decline / Twilight`。
- **章ボス出現年は 25 / 50 / 75 / 100**（`DawnWarden` / `UpheavalConqueror` / `DeclineTyrant` / `EternalSovereign`）。
  それ以外の年は通常敵 `TrialGuardian`。年→原型の決定は `ChronicleTimelineConfig.BattleArchetypeForYear`。
- **章ボス年は出撃必至（休息で素通り不可）**。年送りは `ClampSkipToNextBossYear` でボス年へちょうど着地（スナップ）し、
  その年は予言が休息でも `March` を強制する（`ActionForProphecyAtYear`）。4 体の章ボスは構造的に取りこぼせない。

---

## 3. 人事・世代交代の絶対ルール

### 3-1. 新陳代謝は「自動」と「手動」を厳密に分ける

- **自動（不可逆・プレイヤー操作なし）**: 年送りでの加齢 → 寿命到達・戦闘死の **完全ロスト**仕分け
  （`RosterLifecycle.AdvanceGeneration`）。去った者は二度と戻らず、装備も同時に失われる。
- **手動（プレイヤーの意思）**: 以下はすべてプレイヤー選択であり、自動化してはならない。
  1. **外様スカウト**（`ExecuteScout`）— ポイントを払って血縁なしの即戦力を採用。
  2. **手動婚姻**（`ExecuteMarriage`）— ポイントを払って男女 2 名を即結婚 → 即・子を生成。
  3. **戦力外通告（解雇）**（`ExecuteDismiss`）— 現役 1 名を任意に外す（払い戻しなし）。
  4. **Lv3 限定引退** — 明示引退できるのは Lv3 の生存者のみ。

### 3-2. 婚姻は手動・即時・有償（自然蓄積の自動婚姻は廃止）

- 結婚は **婚姻ポイント（共通サイフ）の支払い**で即時実行する能動アクション。
- コスト = `ceil((父TargetRating×倍率 + 母TargetRating×倍率) / 20)`（`CostDivisor=20`）。
- **自然婚姻（コスト 0）** は、双方向 `BattleAffinity` がともに **150 以上**（`NaturalMarriageThreshold=150`）のときだけの特権。
- `BattleAffinity`（好感度）は「戦闘履歴・相性ヒント・自然婚姻判定」用途のみ。**通常の結婚条件には使わない**。
- 婚姻は **男女ペア限定**（父=Male / 母=Female）。同性・性別逆転の組は SoT・UI 双方で拒絶する。

---

## 4. フェーズ・画面の絶対ルール

### 4-1. 4 フェーズの一方通行・不可逆循環

```
Chronicle ──▶ Guild ──▶ Formation ──▶ Battle ──▶（年送り）──▶ Chronicle
```

- 遷移可否の判断は純粋ロジック `GamePhaseFlow` に集約（`Next` / `CanTransition`）。
- **後退・飛び越し・自己遷移はすべて禁止**（コードレベルで no-op ／ false）。「戻る」ボタンを作らない（後悔はゲームの一部）。
- 拠点（Guild）離脱の行動分岐は純粋ルータ `ActionPhaseRouter` が担う:
  **March → Formation→Battle** ／ **Rest → Chronicle（編成・戦闘を完全バイパス）**。

### 4-2. 構造的ゲート（事故を型で封じる）

- **無人出撃の封鎖**（`DeploymentGate.CanMarch`）: 盤面に最低 1 名いないと 編成→戦闘 へ前進できない。
- **戦闘スキップの封鎖**（`BattleProgressGate.CanLeaveBattlePhase`）: 戦闘が未決着の間は 戦闘→年代記 へ前進しない。
- **敵生成の隔離結界**（`ActionPhaseRouter.MayGenerateEnemy` ＝ March のみ true）: 休息など非戦闘行動では
  敵・戦闘インスタンスを構造的に 1 ビットも生成しない。

### 4-3. UI ライフサイクル（動的 B 型・リークフリー）

- 任意の瞬間に生きているフェーズ画面は **ちょうど 1 つ**（`MountScreenForCurrentPhase` で new、旧画面は `QueueFree`）。
- 動的生成ノードはビュー毎の台帳に記録し、再描画冒頭と `_ExitTree` で `QueueFree` して更地化する。
- すべての演出 Tween は対象ノードへ束縛（解放で自動失効、コールバックは `IsInstanceValid` ガード）。
- シグナル購読は `_Ready` で張り、`_ExitTree`／ビュー切替で完全解除する。

### 4-4. testid（自動検証のための識別子）

- 全コンポーネント・ボタン・カード・主要セルに **`Node.SetMeta("data_testid", "...")`** を例外なく付与。
- 命名規則: kebab-case の `{phase|section}-{element}-{id}`。動的要素は ID/row/col を必ず埋め込む。

---

## 5. 数値・コンフィグの SoT（ハードコード禁止）

数値は直書きせず、Core 層の SoT 定数を参照する:

| 領域 | SoT |
|---|---|
| ジョブ能力値・パッシブ・Rating | `Core/Job/JobMaster.cs` |
| 経済（年次収入・撃破報酬） | `Core/Managers/PointsEconomy.cs` |
| 婚姻コスト・自然婚姻閾値 | `Core/Managers/MarriageService.cs` |
| スカウト年齢・寿命 | `Core/Managers/ScoutService.cs` |
| 兵器廠コスト | `Core/Shop/ShopService.cs` |
| 装備レベル・倍率 | `Core/Unit/Equipment.cs` |
| 敵基準値・年率・ジッタ | `Core/Battle/EnemyScaler.cs` |
| 章・年数・章ボス年 | `Core/Chronicle/ChronicleTimelineConfig.cs` |
| 戦果決算（婚姻ポイント） | `Core/Battle/BattleSpoils.cs` |

UI 表示テキスト（ラベル・職名・アイテム名・予言名・絵文字）の SoT は `Config/localization_ja.json` ただ 1 つ。

---

## 6. ワークフロー規約

- **コミットメッセージは日本語**、type prefix（`feat:` / `fix:` / `refactor:` / `docs:` / `test:` 等）のみ英語。
- 仕様変更時は **本ファイルを先に更新**し、その差分と理由をコミット/PR に明記する。
- 変更後は `dotnet test Tests/ChronicleKnights.Tests.csproj`（**653 pass を維持**）を必ず通す。
  実機確認が要る変更は `./play.command` で windowed 起動して確認する（`--headless` は不可）。
- 新ジョブ/新アイテム/新予言の追加は「Core の SoT（enum + 定義）追加 ＋ `localization_ja.json` にキー追加」で完結させる
  （データ駆動を崩さない）。

---

## 7. ドキュメント更新履歴

| 日付 | 変更内容 |
|---|---|
| 2026-05-30 | 初版（TS 版）。大隊 9 名・敵スピード緩和・乱数化・人事権委譲を固定 |
| 2026-05-30 | フロントエンド絶対ルール（4 フェーズ厳格遷移・data-testid 強制）を追加 |
| 2026-06-18 | **C# / Godot 版へ全面改訂**。開発の四柱（厳格 ASCII・不変性・単一 SoT・決定論シード）を一次規範に据え、
ハクスラ・ローグライト仕様（完全ロスト・Lv 上限 3・手動婚姻＝有償・章ボス）・構造的ゲート・動的 B 型 UI を反映 |
