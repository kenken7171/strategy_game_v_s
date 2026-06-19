# 見栄え・Juice 強化ロードマップ

> ステータス: **設計図のみ**（本書はコード変更を伴いません）。
> 対象: `generated_csharp/` で実稼働する Godot 4 / .NET 8 の C# ビルド。
> 目的: 既存ロジックを壊さず、**アセットと演出を「追加」する**ことで、ゲームの見栄え（Juice）と
> 戦術フィードバックを商業レベルへ引き上げる。653件の緑の xUnit、▲ウェッジのドラッグ＆ドロップ、
> 日本語テキスト層、婚姻の性別分離ガードには 1ビットも触れない。

> ※ 本書は人間向けの解説書のため日本語で記述します。ただし**クラス名・メソッド名・アセットパス・
> 識別子は ASCII のまま**引用します（開発憲法①: ロジック側は ASCII 限定。実装時もこれを死守）。

---

## 0. 不可侵の前提（最初に読むこと）

以下はすべて**追加（add-on）**であり、既存アーキテクチャを尊重して何も退行させない。

- **単一 SoT。** ゲーム状態は autoload `ChronicleGlobal`（`/root/ChronicleGlobal`）だけが持つ。
  ビューは状態をキャッシュせず、シグナル（`StateInitialized` / `EconomyChanged` / `TimelineChanged` /
  `RosterChanged` / `BattleChanged` / `FormationChanged` / `PhaseChanged`）で読み直して再描画する。
- **無状態 UI。** 提示ノードは一過性の操作ラッチのみを持ち、ゲームデータは決して持たない。
- **リークフリーなライフサイクル。** 動的生成ノードはビュー毎の台帳（例 `_battleNodes` / `_treeNodes`）へ
  入れ、再描画の冒頭と `_ExitTree` で `QueueFree` する。Tween は**ノード束縛**（`node.CreateTween()` か
  `JuiceDirector.*` 経由）でノード解放と同時に失効させ、コールバックは `IsInstanceValid` で必ずガードする。
- **開発憲法①（ASCII）。** 識別子・ノード名・`data_testid`・アセットパス・コア内部ログは ASCII。
  プレイヤー向け表示文字列のみ日本語可（UI 層に既に与えた言語特例）。
- **WarningsAsErrors。** 13 の CS コードがエラー昇格済み
  （CS1998;CS4014;CS8618;CS8602;CS8603;CS8604;CS8509;CS8524;CS0162;CS0169;CS0414;CS0649;CS0067）。
  新規コードも静的件数ゼロを維持する。
- **net8.0 + RollForward。** `dotnet build ChronicleKnights.csproj` でビルド、
  `dotnet test Tests/ChronicleKnights.Tests.csproj` でテスト（RollForward 焼き込み済み・環境変数不要）。
- **純粋ロジックは純粋なまま。** レイアウト計算・色選択・パスの組み立て等、Godot 非依存にできるものは
  `Core/` へ置いて単体テスト可能にし、テスト総数を増やす。

アセット注記: `JobTextureLibrary.TryLoad` が安全ロードの型を実証済み ──
「① `ResourceLoader.Exists` → ② `ProjectSettings.GlobalizePath` + `Image.LoadFromFile` の生ディスク復号
フォールバック」の2段。Godot が `.import` を生成する前のソース起動でも画像が出る。
**以下の新規ローダはこの2段パターンを必ず踏襲すること。**

---

## 1. 戦場背景と敵デザイン ── アセット兵站

### 1.1 現状
- アセット根は `res://Assets/Textures/` に集約済み（ジョブは
  `res://Assets/Textures/Jobs/{slug}/{male|female}.png`）。
- 敵はデータのみ: `Core/Battle/EnemyState.cs` が `EnemyState.Archetype`（型 `EnemyArchetype`:
  `TrialGuardian` / `DawnWarden` / `UpheavalConqueror` / `DeclineTyrant` / `EternalSovereign`）を持つ。
- ステージもデータのみ: `Core/Chronicle/ChronicleTimelineConfig.cs` が `EpochId`
  （`Dawn` / `Upheaval` / `Decline` / `Twilight`）と各時代の `RegularArchetype` / `BossArchetype` を持つ。
  章ボスの出現年は 25/50/75/100。

### 1.2 次なる領土（ディレクトリ）
```
res://Assets/Textures/
  Jobs/{slug}/{male|female}.png        (既存)
  Backgrounds/{epoch_slug}.png         (新規 -- EpochId ごとに1枚)
  Enemies/{archetype_slug}.png         (新規 -- EnemyArchetype ごとに1枚)
```
slug は enum 名由来の ASCII snake_case（例 `EpochId.Dawn -> "dawn"`、
`EnemyArchetype.UpheavalConqueror -> "upheaval_conqueror"`）。

### 1.3 新規ローダ（JobTextureLibrary をミラー）
- `UserInterface/BackgroundTextureLibrary.cs`
  - `static Texture2D? TryLoad(EpochId epoch)` -> `res://Assets/Textures/Backgrounds/{slug}.png`。
- `UserInterface/EnemyTextureLibrary.cs`
  - `static Texture2D? TryLoad(EnemyArchetype archetype)` -> `res://Assets/Textures/Enemies/{slug}.png`。
- いずれも `ResourceLoader` -> `Image.LoadFromFile` の2段解決を踏襲し、欠落時は `null` を返す
  （呼び出し側は空表示・絶対に落とさない）。
- enum->slug 変換は純粋な `switch` 式（網羅・CS8509 を出さない）。slug マップは小さな純粋ヘルパ
  （例 `Core/Assets/AssetSlugs.cs`）へ置き、**単体テスト**する（全 enum 値が非空 ASCII slug へ写ることを保証）。

### 1.4 結線（どこにノードを足すか）
- **背景**: `UI/BattleUI.cs` に全画面 `TextureRect` を画面の**最背面**（`_rootShakeTarget` や
  `_popupLayer` より下）へ追加。`StretchMode = KeepAspectCovered`、`MouseFilter = Ignore`。
  現在年からタイムライン設定で epoch を選び、`BattleChanged` で選び直す。`TimelineUI`（年代記画面）にも
  同じ背景を出すと統一感が増す。
- **敵グラフィック**: `UI/BattleUI.cs` の敵カード（`_enemyCard`）は現在 名前 + HP バーのみ。ここへ
  `EnemyTextureLibrary.TryLoad(CurrentBattle.Enemy.Archetype)` を流す `TextureRect` を追加。
- どちらも既存 `_battleNodes` 台帳へ載せ、再描画 / `_ExitTree` で解放（リークフリー）。

### 1.5 不破の保証
背景・敵グラは純粋な提示。`EnemyScaler` / `EnemyState` / `ChronicleTimelineConfig` / 戦闘解決は不変。
黄金均衡に影響なし。

---

## 2. Juice ── カメラシェイクとヒットエフェクト

### 2.1 現状（再利用せよ・作り直すな）
- `UI/JuiceDirector.cs` が無状態の演出ツール箱:
  `Flash` / `Shake` / `SlideTo` / `Punch` / `FadeToDeath` / `DrainBar` / `CountUp` /
  `Typewriter` / `GrowLine` / `RisingPopup`。すべてノード束縛 `Tween` を返す。
- `UI/BattleUI.cs` は既に**カメラシェイク**を結線済み: `_rootShakeTarget`（盤面 VBox）、
  `ShakeCamera()` / `KillCameraShake()`、定数 `CameraShakeAmplitude = 14` /
  `CameraShakeStepSeconds = 0.05`。対象行のフラッシュ（`FlashRow`）と `AllyOffenseEvent` での
  敵カード明滅も実装済み。
- 演出は純粋イベント列（`Core/Battle/BattleEvent.cs`）から駆動: `AllyOffenseEvent` /
  `EnemyOffenseEvent` / `UnitDamagedEvent` / `UnitDefeatedEvent` / `UnitHealedEvent` /
  `LastHitResolvedEvent` / `BattleConcludedEvent` / `RotationPerformedEvent`。

### 2.2 追加: シェイク強度をイベント量に連動
- 現状は固定振幅。純粋ヘルパ `Core/Juice/ShakeProfile.cs` ->
  `float AmplitudeFor(int damage, bool isCrit, bool isFrontGuard)` を新設し、手応えをデータ駆動化して
  **単体テスト**（小ダメージ=控えめ / 会心・鉄壁ガード=強め）。`BattleUI.ShakeCamera` が描画中イベントから
  読む。ロジック非干渉・純加点。
- 前衛の盾軽減（`BattalionDefense` / `SquadDefense` の有意な軽減）が決まった瞬間にも一発揺らし、
  「盾が耐えた」に重みを与える。

### 2.3 追加: パーティクルのヒットエフェクト（Particle2D 層）
- 新レイヤ: `UI/BattleUI.cs` に `_effectLayer`（全画面 `Control`、`MouseFilter = Ignore`）を、盤面より
  **上**・`_popupLayer` より**下**へ置く（数値は常に最前面で読めるように）。`_battleNodes` へ台帳化。
- 新ヘルパ `UI/HitEffectDirector.cs`（無状態・`JuiceDirector` をミラー）:
  - `static void Slash(Control layer, Vector2 globalPos)` -> 白/鋼の短命 `CpuParticles2D`
    （`OneShot = true` / `Emitting = true`、ノード束縛タイマか Tween で自動 `QueueFree`）。
  - `static void Heal(Control layer, Vector2 globalPos)` -> 緑の輝き。
  - `static void Defeat(Control layer, Vector2 globalPos)` -> 暗色の爆ぜ。
- 発生位置 = 対象ユニットのライブ盤面セル（`FlashRow` 用に `unitId -> cell` の索引が既にある・再利用）。
  `UnitDamagedEvent`（斬撃）/ `UnitHealedEvent`（回復）/ `UnitDefeatedEvent`（撃破）/
  `AllyOffenseEvent`（敵カードへ斬撃）で発火。
- **ドット絵のシャープさ**: スプライトアトラスを使うなら `TextureFilter = Nearest`。インポート不要で
  ソース起動でも動くよう `CpuParticles2D` を優先（アセットの安全ロード思想と一致）。

### 2.4 不破の保証
イベント列は純粋 `Core/Battle` が生成済み。パーティクル/シェイクはイベントを**読むだけ**で、解決へは
還流しない。Tween/パーティクルはすべてノード束縛・台帳化を厳守。

---

## 3. UI フィードバック ── 配置のスナップとダメージポップアップ

### 3.1 ▲配置成立時の「スナップ＆バウンド」
- 現状: `UI/FormationUI.cs` が `ChronicleGlobal.CurrentFormation` からウェッジを描き、
  `UserInterface/Hub/FormationSlotControl`（ドロップ先/ドラッグ元）+ `RosterDragCard`（ドラッグ元）+
  `FormationDragPayload`（ASCII コーデック）で D&D を行う。配置は
  `ChronicleGlobal.PlaceUnitOnFormation` / `SwapFormationSlots` を呼び、`FormationChanged` で再描画。
  **配置アニメはまだ無い。**
- 追加（提示のみ）: ドロップ成功後、心地よいスナップを再生:
  - 埋まったスロットの内側ノードへ `JuiceDirector.Punch(node, 1.18f, 0.18)`（スケールのオーバーシュート
    → 収束）で「ガシャン」と吸着。
  - 任意で `JuiceDirector.SlideTo` を使い、放した位置からスロット中心へ磁石のように寄せる。
- 結線箇所: `FormationUI` は `FormationChanged` で盤面を丸ごと再描画するので、「直近に配置した座標」
  （状態ではなく一過性の UI ラッチ）を `RenderBoard` へ渡し、そのスロットだけ Punch。消費後にラッチを破棄。
- D&D の意味論（`PlaceRequested` / `SwapRequested` デリゲート）は不変。演出は描画に重ねるだけで
  データ経路には触れない。

### 3.2 ダメージ / 回復 / 会心ポップアップ
- 現状: `UI/BattleUI.cs` は既に `_popupLayer`（`battle-damage-popup-layer`・全画面・`MouseFilter = Ignore`）
  と色定数 `DamagePopupColor`（赤）/ `HealPopupColor`（緑）/ `LastHitPopupColor`（金）、および
  `JuiceDirector.RisingPopup` を所有。ダメージ/回復の数値は既に飛び出す。
- 追加: 会心・回復をもっと「映え」させる:
  - 純粋ヘルパ `Core/Juice/PopupStyle.cs` -> `(Color color, float fontScale, string prefix) For(BattleEvent e)`:
    高ダメージ/とどめは 大きい赤・大フォント、`UnitHealedEvent` は緑、`LastHitResolvedEvent` は金の星。
    マッピングを**単体テスト**（会心 > 通常のスケール / 回復は緑 / 撃破マークの有無）。
  - `RisingPopup` に `fontScale` を持たせ（数値サイズで重みを表現）、`PopupStyle.For(event)` から駆動。
- **数値そのもの**（630件でテスト済みのコアが算出）は権威のまま。飛び出し方だけを様式化する。

### 3.3 不破の保証
`FormationBoard` / `DeploymentGate` / D&D ペイロード / 戦闘イベント列は不変。スナップ/ポップアップは
描画時のみ。

---

## 4. 100年の血統「家系図」の視覚化

### 4.1 現状（土台にせよ・作り直すな）
- `Core/Pedigree/PedigreeGraph.cs`（純粋・不変）: `PedigreeNode`（`Generation` は [-2, +2]）、
  `PedigreeEdge`、そして祖父母(-2) / 父母(-1) / 本人・配偶者・兄弟(0) / 子(+1) / 孫(+2) を解決する
  `PedigreeGraph`。既に単体テスト済み。
- `UI/PedigreeOverlay.cs` は既に世代帯ごとにカードを描き、親→子を `Godot.Line2D` で繋ぎ
  `JuiceDirector.GrowLine` で伸長させる。カード+コネクタ（`_treeNodes`）と伸長 Tween（`_growTweens`）を
  台帳化し、`GameDirector` が最前面へマウント（`CloseRequested` / `_ExitTree` の自己崩壊型・ジョブ図鑑や
  予言オーバーレイと同型）。

### 4.2 追加の視覚磨き（提示のみ）
- **顔グラ**: テキストのみのノードを、`JobTextureLibrary.TryLoad(job, gender)` のジョブイラストに置換
  （性別読み分けは全コードベースで既に維持）。
- **世代帯**: -2..+2 の各帯を別色で塗り、日本語の帯ラベル（「祖父母 / 父母 / 本人世代 / 子 / 孫」）を付けて
  一目で読めるように。
- **婚姻リンク**: 本人↔配偶者の水平 `Line2D` を縦の親子コネクタとは別の暖色（ハートの線）で描く。
- **段差 + 演出**: `GrowLine` の段差（`ConnectorGrowStaggerSeconds`）を活かして線を順に開通させ、
  入線が完了したカードへ `JuiceDirector.Punch` を軽く当てる（連鎖リビール）。
- **入口**: オーバーレイは既に婚姻画面から到達可能。加えてロスター/ユニット詳細カードに「血」ボタンを置き、
  そのユニットを根とする家系図を開く（既存マウントと同様、`AddChild` 前に `TargetUnitId` を注入）。

### 4.3 任意の純粋拡張（テスト可能）
より広い樹形が欲しければ、純粋 `Core/Pedigree/PedigreeLayout.cs` を追加し、`PedigreeGraph` を入力に
ノード毎の正規化座標 (x, y)（列=兄弟インデックス / 行=世代）を返す。レイアウトを**単体テスト**
（決定論的位置・重なり無し・本人と配偶者が隣接）。オーバーレイは座標をピクセルへ写すだけになり、
計算はテスト済みコアに留まる。

### 4.4 不破の保証
`PedigreeGraph` と `MarriageService`（性別ガード含む）は不変。家系図は `ChronicleGlobal` の血統データの
読み取り専用ビュー。

---

## 5. 推奨実装順

1. **アセット兵站（第1章）** ── ディレクトリ + 2ローダ + 純粋 slug マップとそのテスト。最小リスクで
   以降の視覚要素を解放する。
2. **ポップアップ＆配置スナップ（第3章）** ── 1行あたりの手応えが最大。既存 `RisingPopup` / `Punch` を
   再利用。2つの純粋様式ヘルパ + テストを追加。
3. **パーティクル＆連動シェイク（第2章）** ── `_effectLayer` + `HitEffectDirector` を新設、
   `ShakeProfile` + テストを追加。
4. **家系図の磨き（第4章）** ── 顔グラ・帯配色・任意の `PedigreeLayout` + テスト。

各ステップは独立コミット: build 0/0、xUnit 全緑（かつ純増）、それから push。

---

## 6. テストへの影響

本書が提案する純粋ヘルパ（`AssetSlugs` / `ShakeProfile` / `PopupStyle` / `PedigreeLayout`）は
すべて Godot 非依存で xUnit を**純増**させる。630件から減ることはない。Godot のビュー結線
（TextureRect / パーティクル / Tween）はビルドと実機起動で検証し、コアロジックは決して書き換えない。

---

## 7. ファイル一覧（新規 vs 改修）

新規（追加）:
```
res://Assets/Textures/Backgrounds/{epoch}.png      (アート)
res://Assets/Textures/Enemies/{archetype}.png      (アート)
Core/Assets/AssetSlugs.cs                           (+ テスト)
Core/Juice/ShakeProfile.cs                          (+ テスト)
Core/Juice/PopupStyle.cs                            (+ テスト)
Core/Pedigree/PedigreeLayout.cs                     (任意・+ テスト)
UserInterface/BackgroundTextureLibrary.cs
UserInterface/EnemyTextureLibrary.cs
UI/HitEffectDirector.cs
```
改修（描画のみ・ロジック不変）:
```
UI/BattleUI.cs            (背景 TextureRect・敵グラ・_effectLayer・ポップアップ様式)
UI/FormationUI.cs         (直近配置スロットへの Punch スナップ)
UI/PedigreeOverlay.cs     (顔グラ・帯配色・婚姻リンク)
UI/JuiceDirector.cs       (RisingPopup が fontScale を受ける -- 後方互換)
```

`Core/Battle` の解決、`FormationBoard`、`DeploymentGate`、`MarriageService`、日本語テキスト層、
D&D ペイロードは何も変わらない。
