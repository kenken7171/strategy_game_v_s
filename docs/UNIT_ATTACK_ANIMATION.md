# ユニット攻撃アニメーション（ドット絵・攻撃モーション）

> 各ジョブの立ち絵を「右向き 3/4・攻撃モーション」のドット絵アニメにするための制作ガイド。
> 既存の正面立ち絵（`Assets/Textures/Jobs/{slug}/{male|female}.png`・512² 高精細ドット絵）を
> **ベースにして**、**Scenario で右向き土台 → Pixel Engine で攻撃アニメ**の2ツール分業で攻撃コマを作る。
> **同名規則の PNG を1枚置くだけでコード変更なしに即反映**（ローダ・再生機構は実装済み）。
>
> 分業の理由: Pixel Engine は「渡した絵の向きを保ったままモーションを付ける」ため**向きは回せない**。
> 向き（正面→右向き3/4）は Scenario（静止画・画風モデル）で作り、その土台を Pixel Engine で動かす。

---

## 0. 全体像（何を作り、どう繋がるか）

```
既存の正面立ち絵 ──(Scenarioで右向き化)──▶ 右向き3/4ベース ──(Pixel Engineでattackアニメ)──▶ 攻撃コマ列
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

## 2. 制作フロー（Scenario → Pixel Engine の2ツール分業）

各ツールの得意が違うため**向きは Scenario／動きは Pixel Engine**と分業する:

- **Scenario**＝狙った構図・**向き**の“静止画”生成（自分の画風モデル/参照で画風を保てる）。回転（正面→右向き）はこちら。
- **Pixel Engine**＝1枚のスプライトを**なめらかなアニメ**にする（`Give it your sprite, describe the motion`）。ただし**渡した絵の向きは変えられない**ので、必ず「右向きの土台」を先に用意してから渡す。

### 手順
1. **Scenario で右向き3/4の“土台1枚”を作る**（下のプロンプト）。既存 `{slug}/{gender}.png` を
   **参照画像（img2img / image reference / IP-Adapter）**に入れて画風・装備を保つ。透過・単体・右向きで生成し、
   赤マント・盾・十字紋が保たれた1枚を選ぶ（細部は Pixel Engine 内蔵の Piskel エディタで後修正可）。
2. **Pixel Engine でその土台をアニメ化**。土台をアップロードし、**動きだけ**を英語で説明（見た目は書かない）。
   向きは土台のまま右向きを保つ。
3. **書き出し**: Pixel Engine 内蔵 Piskel で「Export → Spritesheet → PNG／**rows = 1（横一列）**」。
   キャンバスは**正方**（例 128²）にしておくと、こちらの「コマ数＝幅÷高さ」自動判定がそのまま効く。**GIF は不可**（Godot がテクスチャ化できない）。
4. 置く: `generated_csharp/Assets/Textures/Jobs/{slug}/{gender}_attack.png` → 起動して戦闘で確認。
   （frame 0＝右向き待機ポーズが、そのまま戦闘中の静止立ち絵にもなる。別途 idle は不要。）

### Scenario 用プロンプト（鉄壁騎士・右向き土台・英語推奨）

**Positive**
```
detailed 16-bit SNES JRPG pixel art, full-body knight in silver plate armor,
flowing red cape, heater shield on the left arm, sword in the right hand,
thick dark outline, soft cel shading, limited palette, single character,
side view, the ENTIRE BODY turned to the RIGHT: head, shoulders, torso, hips,
legs and feet all pointing right, right foot stepping forward, striding to the right,
body in profile, transparent background, centered, no shadow
```
**Negative**
```
front view, facing camera, torso facing forward, shoulders square to camera,
only the head turned while body faces front, back view, multiple characters,
text, watermark, blurry, extra limbs, cropped, background scenery,
shadow, ground shadow, drop shadow, cast shadow, shadow under the feet
```
- 既存立ち絵を**参照画像**に入れる。**「首だけ右」になるのは参照の向きに引っ張られているサイン**なので、
  **画像の影響（Image influence / reference strength）を弱め（＝creativity を上げ）**て体ごと回るようにする。
  それでも足りなければ `side view` / `body in profile` を強め、`3/4` 寄せに戻すのは体が回ってから。
- 影が出る場合は Negative の shadow 系を効かせるほか、Scenario の **Remove Background / transparent 出力**で床影ごと除去する。
  Scenario 側に学習済みピクセルモデルがあれば base に使う。
- 他ジョブは被写体だけ差し替え（例 狙撃兵＝`archer holding a bow, light leather armor` / 呪術師＝`sorcerer with a long staff, dark hooded robe`）。①③④相当（画風・透過・`3/4 view facing RIGHT`）は全職で固定して16体を揃える。

### Pixel Engine 用プロンプト（動きだけ／鉄壁騎士・剣）
```
sword slash attack: raise the sword overhead, swing it down diagonally to the right,
then return to a guard stance
```
- 見た目は土台画像が担うので**書かない**。射撃職は `draw the bow and release an arrow` / 呪術師は `channel energy and cast a spell forward` 等の動き記述へ差し替え。

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
