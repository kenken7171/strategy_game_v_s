# 敵イラスト生成プロンプト集（Scenario 用）

> 通常敵 1 種＋各章ボス 4 種＝**5 枚**の敵イラストを [Scenario](https://www.scenario.com/) で生成するための
> プロンプト集（英語・日本語併記）。出力先・形式・命名は `docs/ASSET_MANIFEST.md` §3 に従う。
> 同名 PNG を本番アートで上書きするだけで即反映（コード変更不要）。

## 0. 生成の前提（共通仕様）

| 項目 | 指定 |
|---|---|
| 配置先 | `generated_csharp/Assets/Textures/Enemies/{slug}.png`（slug は下表） |
| 形式 | **PNG（中身も PNG であること）/ RGBA・背景透過**（Scenario の Remove Background / transparent 出力を使う） |
| サイズ | 正方推奨（**1024×1024 で生成**で可）。敵カードは**画面横幅の約 60%**まで自動拡大表示（`BattleUI.EnemyPortraitWidthRatio=0.6`）。 |
| 画風 | **既存ジョブ立ち絵（512² ドット絵）と統一**。＝高精細ピクセルアート／JRPG モンスタースプライト |
| 構図 | 単体・中央・全身・こちらを向く・カード映えする威圧ポーズ・余白（頭上少し空け） |

- **英語プロンプト推奨**（多くの Scenario ベースモデルは英語学習）。日本語は対訳＝微調整・確認用。
- 5 枚は**同一モデル・同一スタイルトークン・近いシード**で揃えると画風が統一される（同じ世界の敵に見える）。
- 章が進むほど**威圧度を上げる**（実ステも HP150→420・ATK30→90 と逓増）。下の各節「威圧度」を参照。

### 共通スタイル（全プロンプト末尾に付ける・EN）

```
single creature, centered composition, full body, facing the viewer, menacing dynamic pose,
high-resolution pixel art, detailed JRPG monster sprite, crisp clean pixels, cohesive game asset,
dramatic rim lighting, strong silhouette, isolated on transparent background
```

### 共通スタイル（日本語・対訳）

```
単体の敵、中央配置、全身、こちらを向く、威圧的で動きのあるポーズ、
高精細ピクセルアート、作り込まれた JRPG モンスタースプライト、くっきり清潔なドット、統一感のあるゲームアセット、
ドラマチックなリムライト、強いシルエット、背景は透過で単体を切り抜き
```

### 共通ネガティブプロンプト（全プロンプト共通・EN）

```
text, letters, watermark, signature, logo, ui, frame, border, multiple characters,
human bystanders, cropped, out of frame, blurry, lowres, jpeg artifacts, extra limbs,
deformed, mutated, messy background, scenery, ground shadow
```

### 対象一覧

| slug（ファイル名） | 表示名 | 役割／出現 | モチーフ | 威圧度 |
|---|---|---|---|---|
| `trial_guardian` | 試練の門の守護者 | 通常敵・全時代（章ボス年以外の毎年） | **ネズミ** | 小（雑魚・反復） |
| `dawn_warden` | 黎明の守り手 | 黎明の章ボス・25 年 | **ヘビ** | 中 |
| `upheaval_conqueror` | 激動の征服者 | 激動の章ボス・50 年 | **ワニ** | 大 |
| `decline_tyrant` | 斜陽の暴君 | 斜陽の章ボス・75 年 | **ロボット** | 特大 |
| `eternal_sovereign` | 永劫の覇王 | 終焉の最終章ボス・100 年 | **人型の魔法騎士** | 最大（ラスボス） |

### 保存先パス（5 枚・このファイル名で上書き保存）

> ファイル名は固定（slug＝enum 名の snake_case）。**すべて既に原色プレースホルダが置いてあるので、同名で上書きするだけ**。
> リポジトリルート（`/Users/ken/work/strategy_game_v_s/`）からの相対パスは下表のとおり。Godot 内のパスは `res://Assets/Textures/Enemies/{slug}.png`。

| モチーフ | 表示名 | 保存先（リポジトリルートからの相対パス） |
|---|---|---|
| ネズミ（通常敵） | 試練の門の守護者 | `generated_csharp/Assets/Textures/Enemies/trial_guardian.png` |
| ヘビ（黎明ボス） | 黎明の守り手 | `generated_csharp/Assets/Textures/Enemies/dawn_warden.png` |
| ワニ（激動ボス） | 激動の征服者 | `generated_csharp/Assets/Textures/Enemies/upheaval_conqueror.png` |
| ロボット（斜陽ボス） | 斜陽の暴君 | `generated_csharp/Assets/Textures/Enemies/decline_tyrant.png` |
| 魔法騎士（終焉ボス） | 永劫の覇王 | `generated_csharp/Assets/Textures/Enemies/eternal_sovereign.png` |

- ファイル名は**大文字・スペース・日本語不可**（小文字 ASCII の snake_case のみ）。`.png`（RGBA・背景透過）。
- 置けば即反映（ローダ `UserInterface/EnemyTextureLibrary.cs` 実装済・コード変更不要）。

---

## 1. 通常敵 — 試練の門の守護者（ネズミ） `trial_guardian.png`

- 役割: 全時代の通常敵。毎年（章ボス年以外）出現する反復ザコ。雑兵感・門番感を出す。
- 威圧度: 小。巨大すぎず、しかしモンスターとして読める。やや薄汚れた門番。

**English**
```
a giant battle rat gatekeeper monster, mangy gray-brown fur, twitching whiskers, sharp yellow teeth,
beady red eyes, long scaly tail, wearing crude rusty scrap-iron armor on the shoulders,
gripping a chipped makeshift spear, scrappy lowly minion, snarling guard of a stone gate,
slightly hunched feral stance, earthy gray and rust palette,
single creature, centered composition, full body, facing the viewer, menacing dynamic pose,
high-resolution pixel art, detailed JRPG monster sprite, crisp clean pixels, cohesive game asset,
dramatic rim lighting, strong silhouette, isolated on transparent background
```

**日本語（対訳）**
```
巨大な戦闘ネズミの門番モンスター、薄汚れた灰褐色の毛皮、ぴくつくヒゲ、鋭い黄ばんだ歯、
小さく赤い目、長く鱗状の尾、肩に粗末で錆びたスクラップ鉄の防具、
欠けた間に合わせの槍を構える、雑兵じみた下級の手下、石の門を守り唸る、
やや前かがみの野性的な構え、土気色の灰色と錆色のパレット、
単体の敵・中央配置・全身・こちらを向く・威圧的で動きのあるポーズ、
高精細ピクセルアート、作り込まれた JRPG モンスタースプライト、くっきり清潔なドット、統一感のあるゲームアセット、
ドラマチックなリムライト、強いシルエット、背景は透過で単体を切り抜き
```

---

## 2. 黎明の章ボス — 黎明の守り手（ヘビ） `dawn_warden.png`

- 役割: 黎明（年 1–25）の章ボス。最初の壁。「守り手」＝古き門を守る原初の番獣。
- 威圧度: 中。とぐろを巻く大蛇。夜明けの光・翡翠と黄金。神聖な番獣感。

**English**
```
a colossal ancient guardian serpent, coiled and rearing up, emerald and jade scales with golden trim,
glowing amber eyes, flared hood with sacred dawn-rune markings, dripping fangs, primordial temple warden,
soft golden dawn light and pale morning mist, jade-green and gold palette, sacred yet threatening,
single creature, centered composition, full body, facing the viewer, menacing dynamic pose,
high-resolution pixel art, detailed JRPG monster sprite, crisp clean pixels, cohesive game asset,
dramatic rim lighting, strong silhouette, isolated on transparent background
```

**日本語（対訳）**
```
巨大な古の守護大蛇、とぐろを巻き鎌首をもたげる、翡翠と碧玉の鱗に黄金の縁取り、
琥珀色に光る目、聖なる暁のルーンを刻んだ広がる頭巾、滴る牙、原初の神殿の番獣、
やわらかな黄金の夜明けの光と淡い朝霧、翡翠の緑と金のパレット、神聖だが脅威的、
単体の敵・中央配置・全身・こちらを向く・威圧的で動きのあるポーズ、
高精細ピクセルアート、作り込まれた JRPG モンスタースプライト、くっきり清潔なドット、統一感のあるゲームアセット、
ドラマチックなリムライト、強いシルエット、背景は透過で単体を切り抜き
```

---

## 3. 激動の章ボス — 激動の征服者（ワニ） `upheaval_conqueror.png`

- 役割: 激動（年 26–50）の章ボス。戦乱・侵略の時代。「征服者」＝武装した軍閥のワニ。
- 威圧度: 大。重装甲のワニの軍閥。鉄板・鋲・破れた軍旗・戦傷。泥と深紅。

**English**
```
a massive armored warlord crocodile, standing upright on hind legs, thick olive-green hide,
battle-scarred snout full of jagged teeth, heavy riveted iron plate armor and spiked pauldrons,
torn war banners on its back, wielding a huge notched war-cleaver, brutal conqueror of the age of upheaval,
muddy river battlefield grime, dark crimson and iron-gray palette, aggressive and brutal,
single creature, centered composition, full body, facing the viewer, menacing dynamic pose,
high-resolution pixel art, detailed JRPG monster sprite, crisp clean pixels, cohesive game asset,
dramatic rim lighting, strong silhouette, isolated on transparent background
```

**日本語（対訳）**
```
巨躯の重装甲の軍閥ワニ、後ろ脚で直立、ぶ厚いオリーブ色の体皮、
ギザギザの歯が並ぶ戦傷だらけの顎、鋲を打った重い鉄板の鎧と棘付き肩当て、
背に破れた軍旗、巨大で刃こぼれした戦斧を振るう、激動の時代の残虐な征服者、
泥にまみれた河の戦場の汚れ、暗い深紅と鉄灰色のパレット、攻撃的で残忍、
単体の敵・中央配置・全身・こちらを向く・威圧的で動きのあるポーズ、
高精細ピクセルアート、作り込まれた JRPG モンスタースプライト、くっきり清潔なドット、統一感のあるゲームアセット、
ドラマチックなリムライト、強いシルエット、背景は透過で単体を切り抜き
```

---

## 4. 斜陽の章ボス — 斜陽の暴君（ロボット） `decline_tyrant.png`

- 役割: 斜陽（年 51–75）の章ボス。衰退・崩落の時代。「暴君」＝朽ちゆく巨大戦争機械。
- 威圧度: 特大。錆びた古代の兵器。亀裂から漏れる残光。落日のオレンジ。圧政的。

**English**
```
a towering decaying war automaton tyrant, hulking rusted mechanical body, cracked iron plating,
exposed broken gears and frayed cables, a single glowing dying-ember core in its chest,
heavy piston arms ending in crushing claws, flickering malfunctioning eye lights, ancient oppressive machine,
backlit by a dim decaying sunset glow, rust-orange ash-gray and ember-red palette, ominous and oppressive,
single creature, centered composition, full body, facing the viewer, menacing dynamic pose,
high-resolution pixel art, detailed JRPG monster sprite, crisp clean pixels, cohesive game asset,
dramatic rim lighting, strong silhouette, isolated on transparent background
```

**日本語（対訳）**
```
そびえ立つ朽ちゆく戦争機械の暴君、ごつい錆びた機械の体躯、亀裂の入った鉄装甲、
むき出しの壊れた歯車とほつれたケーブル、胸に消えかけの残り火のような単一の動力炉、
押し潰す爪を備えた重いピストン腕、明滅し誤作動する眼光、古く圧政的な機械、
かすかに沈む夕陽に逆光で照らされる、錆びたオレンジ・灰色・残り火の赤のパレット、不吉で圧政的、
単体の敵・中央配置・全身・こちらを向く・威圧的で動きのあるポーズ、
高精細ピクセルアート、作り込まれた JRPG モンスタースプライト、くっきり清潔なドット、統一感のあるゲームアセット、
ドラマチックなリムライト、強いシルエット、背景は透過で単体を切り抜き
```

---

## 5. 終焉の最終章ボス — 永劫の覇王（人型の魔法騎士） `eternal_sovereign.png`

- 役割: 終焉（年 76–100）の最終章ボス＝ラスボス。100 年史の終着点。「覇王」＝永劫を統べる人型の魔法騎士。
- 威圧度: 最大。荘厳で華美な漆黒の鎧、燐光のアルカナ・ルーン、宇宙的・不死の威圧。

**English**
```
a humanoid arcane knight sovereign, the final boss, tall imposing figure in ornate obsidian-black full plate armor,
flowing tattered royal cape, glowing violet arcane runes etched across the armor, a horned regal helm with a cold
glowing visor, wielding a massive enchanted greatsword wreathed in violet flame, levitating arcane sigils around him,
aura of an immortal eternal ruler, deep void-violet black and cold gold palette, regal cosmic and overwhelming,
single creature, centered composition, full body, facing the viewer, menacing dynamic pose,
high-resolution pixel art, detailed JRPG monster sprite, crisp clean pixels, cohesive game asset,
dramatic rim lighting, strong silhouette, isolated on transparent background
```

**日本語（対訳）**
```
人型の魔法騎士の覇王、ラストボス、華美な漆黒（黒曜石色）のフルプレート鎧を纏う長身で威圧的な姿、
たなびくぼろぼろの王のマント、鎧一面に刻まれ紫に輝くアルカナ・ルーン、冷たく光るバイザーの角付き王冠兜、
紫の炎をまとう巨大な魔剣を振るう、周囲に浮遊する魔法陣、
不死永劫の支配者のオーラ、深い虚無の紫黒と冷たい金のパレット、荘厳で宇宙的、圧倒的、
単体の敵・中央配置・全身・こちらを向く・威圧的で動きのあるポーズ、
高精細ピクセルアート、作り込まれた JRPG モンスタースプライト、くっきり清潔なドット、統一感のあるゲームアセット、
ドラマチックなリムライト、強いシルエット、背景は透過で単体を切り抜き
```

---

## ⚠ 形式の注意（敵が表示されない時はまずここ）

- **拡張子 `.png` でも中身が JPEG だと、標準の Godot は読み込めない**（Godot は拡張子で画像形式を判定するため、
  PNG デコーダが JPEG バイト列を解釈できず空画像になる）。Scenario は **JPEG で書き出すことがある**ので注意。
- 対策（実装済み）: 本リポジトリの画像ローダ（`UserInterface/TextureDiskLoader.cs`）は**ファイルの中身（マジックバイト）で
  形式を判定**し、`.png` 名でも中身が JPEG / WebP なら正しくデコードする。よって**多少形式がずれても表示はされる**。
- ただし**透過（背景の切り抜き）は PNG/RGBA でしか出せない**。JPEG は透過を持てないため、JPEG を置くと
  敵の背景が四角く残る。透明な切り抜きが欲しい場合は Scenario の **Remove Background → PNG（RGBA）で書き出す**こと。
- 推奨運用: **PNG（RGBA・透過）で書き出して** §0 のパスへ保存。中身が PNG なら Godot のインポートも素直に通る。
- 現状の5枚: Scenario 出力が JPEG（白〜色付き背景・不透明）だったため、**縁から色連続でフラッドする自動背景除去**
  （`scripts/`相当の一時処理・許容差 8）で透過を焼き込み済み（本体は保持・背景 ~43–62% を除去）。ただし
  **自動除去は完璧ではない**（特に robot のように背景が本体と同系色のシーンだと縁がやや甘い）。
  きれいな切り抜きが要るなら **Scenario の Remove Background → PNG(RGBA)** で書き出して同名上書きするのが確実。

## 目視確認（全5体を一画面で）

- **`./preview_enemies.command`**（＝`res://EnemyGallery.tscn`）を実行すると、5 体すべての敵イラストを
  **市松模様（透過が見える）**の上に一覧表示する QA 画面が開く。100 年プレイせずに全敵を一度に確認できる。
- 表示は**実ローダ `EnemyTextureLibrary` 経由**なので、未配置／読込失敗は各セルに `MISSING` と出る（描画パイプラインのテストになる）。
  正常時は `OK 1024x1024` のように画素サイズが出る。透過部分は市松が透けて見える。

## 6. Scenario 運用メモ

- **モデル**: ジョブ立ち絵と同じ（または近い）ピクセルアート系モデルを選ぶと 5 枚＋既存 16 枚の画風が揃う。
  別画風（厚塗り等）にしたい場合は共通スタイル末尾の `high-resolution pixel art, detailed JRPG monster sprite...`
  を差し替える（5 枚すべて同じ差し替えで統一する）。
- **アスペクト比 / 解像度**: 1:1（1024×1024）で生成 → 透過で背景除去 → 512×512 へ縮小。敵カードは縦長気味でも可。
- **背景透過**: Scenario の「Remove Background」または transparent 出力を使い、PNG/RGBA で書き出す（地面影は出さない）。
- **画風の統一**: 1 体決め打ちで気に入った seed を出したら、その seed/設定を基準に他 4 体も近いパラメータで生成。
  Image-to-Image や同一モデルのリファレンスでテイストを合わせると「同じ世界の敵」感が出る。
- **威圧度の段階**: ザコ（ネズミ）＜黎明（ヘビ）＜激動（ワニ）＜斜陽（ロボット）＜終焉（魔法騎士）。
  サイズ・装飾・発光量・ポーズの迫力を段階的に上げる（実ステ HP150→420・ATK30→90 の逓増と一致）。
- **配置 → 即反映**: 生成物を `generated_csharp/Assets/Textures/Enemies/{slug}.png` に**同名で上書き**保存するだけ
  （ローダ `EnemyTextureLibrary` 実装済・コード変更不要）。slug は §0 の表のとおり。
