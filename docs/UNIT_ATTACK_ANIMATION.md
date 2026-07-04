# ユニット攻撃アニメーション（ドット絵・攻撃モーション）

> 各ジョブの立ち絵を「右向き 3/4・攻撃モーション」のドット絵アニメにするための制作ガイド。
> 既存の正面立ち絵（`Assets/Textures/Jobs/{slug}/{male|female}.png`・512² 高精細ドット絵）を
> **ベースにして**、アニメ機能付きのドット絵ツール（pixel engine 系）で攻撃コマを作る。
> **同名規則の PNG を1枚置くだけでコード変更なしに即反映**（ローダ・再生機構は実装済み）。

---

## 0. 全体像（何を作り、どう繋がるか）

```
既存の正面立ち絵 ──(ツールで右向き化)──▶ 右向き3/4ベース ──(attackアニメ適用)──▶ 攻撃コマ列
        │                                                                            │
        │                                                     横一列スプライトシートで書き出し
        ▼                                                                            ▼
  static TextureRect（現状）                       Assets/Textures/Jobs/{slug}/{gender}_attack.png
                                                                                     │
                                        JobTextureLibrary.TryLoadAttack が拾う（存在時のみ）
                                                                                     ▼
                                        SpriteSheetAnimator（frame 0＝右向き待機／攻撃で再生）
                                                                                     ▼
                                        BattleUI: 味方の攻撃（AllyOffenseEvent）で当人が振る
```

- **シートが無いジョブは今までどおり正面立ち絵のまま**（グレースフルフォールバック）。1体ずつ順次投入できる。
- 攻撃の発火は戦闘の 1 ターン解決イベント `AllyOffenseEvent`（攻撃者 Id 付き）に紐付け済み。

---

## 1. シート仕様（この形で書き出せば即動く）

| 項目 | 指定 |
|---|---|
| 配置先 | `generated_csharp/Assets/Textures/Jobs/{slug}/{male\|female}_attack.png` |
| レイアウト | **横一列のスプライトシート**（コマを左→右に等間隔で並べる） |
| コマ形状 | **正方（各コマ = 高さ×高さ）**。→ コマ数は `width / height` で**自己記述**（メタ不要） |
| コマ数 | 攻撃は **4〜6コマ**推奨（①構え ②振りかぶり ③斬撃/インパクト ④戻り） |
| 例 | 4コマ・128²/コマ → **512×128 の PNG**。6コマ・128² → 768×128 |
| 形式 | **PNG／RGBA・背景透過** |
| 整列 | **全コマ同一キャンバス・足元（ピボット）固定**（コマ間でガタつかせない） |
| 向き | **右向き3/4**（フル横顔でなく、正面感を残した斜め右）。frame 0 は「右向き待機」＝戦闘中の静止ポーズにもなる |
| 画風 | 既存の正面立ち絵と統一（高精細ピクセルアート・太い輪郭・限定パレット・JRPG） |
| FPS | コード側 `SpriteSheetAnimator.DefaultFps = 10`（必要なら変更可） |

- **slug 一覧**: `iron_wall_knight` / `heavy_infantry` / `standard_bearer` / `tactician` / `medic` / `sniper` / `sorcerer` / `scout`。
- 解像度は 512² でも動くが、コマ数ぶん重くなる。まずは **128²/コマ** 推奨（表示は `TextureFilter=Nearest` で integer-scale 相当に綺麗）。

---

## 2. 制作フロー（アニメ機能付きツール前提）

1. **右向きベースを作る**: ツールに既存の `{slug}/{gender}.png` を **参照（base/reference）**として読み込み、
   「方向回転（direction / rotate）」で **右向き3/4** のベースを1枚生成。
   - デザイン（装備・色・シルエット）が保たれているか確認。崩れたら短いスタイル指定で寄せる。
2. **attack アニメを適用**: そのベースに「attack / melee slash（近接）」「shoot（射撃）」等のスケルトンを適用して 4〜6コマ生成。
   - 武器を持つ手がリードするように。**インパクトのコマ**は踏み込み＋武器を右前方へ大きく。
3. **書き出し**: 上記「1. シート仕様」に従い、**横一列・正方コマ・透過**の 1 枚 PNG にする。
4. 置く: `generated_csharp/Assets/Textures/Jobs/{slug}/{gender}_attack.png` へ保存 → 起動して戦闘で確認。

### プロンプト雛形（鉄壁騎士・剣／英語推奨・スタイル寄せ）

```
detailed 16-bit SNES JRPG pixel art, full-body knight in silver plate armor,
flowing red cape, heater shield with red cross crest, thick dark outline, soft shading,
limited palette, transparent background, centered, feet on a fixed baseline,
3/4 view facing RIGHT, identical character across all frames.

Frame 1 (idle):    standing guard, sword lowered, facing right
Frame 2 (windup):  sword raised back over the shoulder, weight shifted back
Frame 3 (strike):  sword slashing forward-right, arm extended, motion emphasis
Frame 4 (recover): returning to the guard pose
```

- 毎コマに `same character across frames` / 参照画像固定 / seed 固定 / 透過・中心・足元基準を効かせるのがブレ防止のコツ。
- 射撃職（狙撃兵・呪術師）は `draw and release the bow` / `channel and cast a spell` 等へ差し替え。

---

## 3. プロトタイプ（まず1体でパイプラインを通す）

- **対象: `iron_wall_knight` / `male`**（剣＝手の動きが分かりやすい）。
- 4コマの `iron_wall_knight/male_attack.png` を1枚作って置く → 戦闘で味方の鉄壁騎士が攻撃した瞬間に振れば成功。
- OK なら他ジョブ・女性版・射撃モーションへ横展開（同じ規則で `_attack.png` を足すだけ）。

---

## 4. コード側の対応状況（実装済み・変更不要）

| 部品 | 役割 |
|---|---|
| `Core/Assets/SpriteSheetSlicer.cs` | 横一列シート → コマ矩形の純粋計算（`SquareFrameCount` / `HorizontalStrip`）。xUnit 済み |
| `UserInterface/JobTextureLibrary.TryLoadAttack` | `{slug}/{gender}_attack.png` を拾う（無ければ null＝静止立ち絵にフォールバック） |
| `UI/SpriteSheetAnimator.cs` | シートをコマに切って `PlayAttack()` で1回再生（frame 0 待機・`Nearest` 表示） |
| `UI/BattleUI.cs` | 攻撃シートがあれば立ち絵をアニメータ化し索引登録。`AllyOffenseEvent` で当人の `PlayUnitAttack` |

- 攻撃シートが無い間は**現状の見た目のまま**（何も壊れない）。1 枚置くたびにそのジョブだけ動き出す。
- FPS を変えたい場合は `SpriteSheetAnimator.DefaultFps`、攻撃発火点を変えたい場合は `BattleUI.PlayEventJuice` の `AllyOffenseEvent` 分岐。

---

## 5. 目視 QA

現状は攻撃シート未導入のため、`./play.command` で戦闘に入り「攻撃で立ち絵が静止のまま＝フォールバック健全」を確認できる。
`iron_wall_knight/male_attack.png` を置いた後は、鉄壁騎士を編成して出撃 →「次のターン」で味方攻撃 → 立ち絵が振れることを確認する。
（`--headless` は macOS で既知の不具合のため必ず windowed 起動で確認する。）
