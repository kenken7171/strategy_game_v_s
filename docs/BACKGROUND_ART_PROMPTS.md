# 戦場背景 生成プロンプト集（Scenario 用）

> 4 章（黎明/激動/斜陽/終焉）の戦場背景**4 枚**を [Scenario](https://www.scenario.com/) で生成するための
> プロンプト集（英語・日本語併記）。出力先・形式・命名は `docs/ASSET_MANIFEST.md` §2 に従う。
> 同名ファイルを本番アートで上書きするだけで即反映（コード変更不要）。

## 0. 生成の前提（背景は敵と勝手が違う・必読）

| 項目 | 指定 |
|---|---|
| 配置先 | `generated_csharp/Assets/Textures/Backgrounds/{epoch}.png`（slug は下表） |
| 形式 | **不透明でOK・透過不要**（全画面背景）。PNG でも **JPEG中身でも可**（ローダが中身判定。JPEGは軽い） |
| アスペクト | **16:9 横長**（**1920×1080** 推奨。最低 1280×720） |
| 描画 | 全画面 `KeepAspectCovered`＝画面を埋めて**はみ出しは切る**。重要要素を端ギリギリに置かない |
| 画風 | 敵アートと色調をそろえる（厚塗り・シネマティック）。文字と喧嘩しない**中〜低キー** |

### ★ 最重要：背景は「シーン」ではなく「ムード」
ゲームでは背景の上に **不透明度 74% の暗いカード**が乗り（画面端から 24px の額縁だけ背景フル表示／中央は実質 ~26% しか透けない）。つまり：
- **中央の細かい描き込みはカードで隠れて無駄**。中央は静かに・シンプルに。
- **高コントラスト／ごちゃごちゃは UI 文字の可読性を壊す**。淡く・霞ませる。
- **画面端（額縁）に“その時代の象徴”を効かせる**と、覗いた縁で世界観が伝わる。
- 暗いカード越しでも沈み切らないよう、**ほどよい明度と各時代の色**を残す。

### 章別の配色・モチーフ（敵アートと連動）

| slug | 章 | 年 | 連動する敵 | 配色テーマ | ムード |
|---|---|---|---|---|---|
| `dawn` | 黎明 | 1–25 | ヘビ(翡翠金) | 朝の金・翡翠・淡い空 | 静謐・希望・夜明け |
| `upheaval` | 激動 | 26–50 | ワニ(深紅鉄) | 戦塵の赤錆・鉄灰・煙 | 戦乱・嵐・動乱 |
| `decline` | 斜陽 | 51–75 | ロボ(錆橙) | 夕焼け橙・灰・長い影 | 衰退・荒廃・落日 |
| `twilight` | 終焉 | 76–100 | 魔法騎士(紫黒金) | 虚無の紫黒・冷たい金 | 終焉・荘厳・不気味 |

### 共通スタイル（全プロンプト末尾・EN）
```
wide cinematic fantasy landscape, atmospheric matte painting, edge-to-edge full-frame background,
soft depth of field, low-to-mid key lighting, muted desaturated tones, simple calm center with
detail pushed to the edges, no characters, no text, 16:9 horizontal, cohesive game art
```

### 共通スタイル（日本語・対訳）
```
横長のシネマティックなファンタジー風景、空気感のあるマットペインティング、画面いっぱいの全面背景、
柔らかな被写界深度、中〜低キーの照明、彩度を抑えた色調、中央は静かでシンプル・ディテールは端へ寄せる、
人物なし、文字なし、16:9 横長、統一感のあるゲームアート
```

### 共通ネガティブ（全プロンプト共通・EN）
```
text, letters, watermark, signature, logo, ui, hud, frame border, characters, people, creatures,
close-up, busy clutter, high contrast, harsh highlights, oversaturated, blurry, lowres, jpeg artifacts
```

### 保存先パス（4 枚・このファイル名で上書き保存）

| 章 | slug | 保存先（リポジトリルートからの相対パス） |
|---|---|---|
| 黎明 | dawn | `generated_csharp/Assets/Textures/Backgrounds/dawn.png` |
| 激動 | upheaval | `generated_csharp/Assets/Textures/Backgrounds/upheaval.png` |
| 斜陽 | decline | `generated_csharp/Assets/Textures/Backgrounds/decline.png` |
| 終焉 | twilight | `generated_csharp/Assets/Textures/Backgrounds/twilight.png` |

- ファイル名は**小文字 ASCII**固定。Godot 内パスは `res://Assets/Textures/Backgrounds/{slug}.png`。
- 現状は原色プレースホルダ（dawn=黒/upheaval=白/decline=青/twilight=緑）。同名上書きで即反映。

---

## 1. 黎明 — `dawn.png`（年 1–25）

**English**
```
a tranquil dawn over a misty highland valley with faint ancient stone ruins, soft golden sunrise light
breaking through pale jade-green mist, distant gentle mountains, dewy calm, hopeful and serene, the
quiet morning of an age, gold and jade and pale sky palette,
wide cinematic fantasy landscape, atmospheric matte painting, edge-to-edge full-frame background,
soft depth of field, low-to-mid key lighting, muted desaturated tones, simple calm center with
detail pushed to the edges, no characters, no text, 16:9 horizontal, cohesive game art
```

**日本語（対訳）**
```
霧のかかった高原の谷に薄っすら残る古代の石の遺構、淡い翡翠色の霧を抜けるやわらかな黄金の日の出の光、
遠くの穏やかな山並み、露けき静けさ、希望に満ちて静謐、ある時代の静かな夜明け、金・翡翠・淡い空の配色、
横長のシネマティックなファンタジー風景、空気感のあるマットペインティング、画面いっぱいの全面背景、
柔らかな被写界深度、中〜低キーの照明、彩度を抑えた色調、中央は静かでシンプル・ディテールは端へ寄せる、
人物なし、文字なし、16:9 横長、統一感のあるゲームアート
```

## 2. 激動 — `upheaval.png`（年 26–50）

**English**
```
a war-torn battlefield plain under a dark roiling storm sky, distant burning ruins and rising black smoke,
broken banners and scorched earth at the edges, drifting embers, crimson glow on iron-grey clouds, turmoil
and unrest, the age of upheaval, dark crimson and iron-grey and smoke palette,
wide cinematic fantasy landscape, atmospheric matte painting, edge-to-edge full-frame background,
soft depth of field, low-to-mid key lighting, muted desaturated tones, simple calm center with
detail pushed to the edges, no characters, no text, 16:9 horizontal, cohesive game art
```

**日本語（対訳）**
```
渦巻く暗い嵐の空の下、戦で荒れた平原、遠くに燃える廃墟と立ち上る黒煙、
端には折れた軍旗と焼け焦げた大地、漂う火の粉、鉄灰色の雲に映る深紅の光、動乱と不穏、
激動の時代、深紅・鉄灰・煙の配色、
横長のシネマティックなファンタジー風景、空気感のあるマットペインティング、画面いっぱいの全面背景、
柔らかな被写界深度、中〜低キーの照明、彩度を抑えた色調、中央は静かでシンプル・ディテールは端へ寄せる、
人物なし、文字なし、16:9 横長、統一感のあるゲームアート
```

## 3. 斜陽 — `decline.png`（年 51–75）

**English**
```
a desolate decaying wasteland at sunset, rusted ruined structures and dead trees silhouetted at the edges,
a huge dying orange sun low on the horizon casting long shadows, drifting ash, cracked dry ground, melancholy
and decline, the fading age, rust-orange and ash-grey and ember palette,
wide cinematic fantasy landscape, atmospheric matte painting, edge-to-edge full-frame background,
soft depth of field, low-to-mid key lighting, muted desaturated tones, simple calm center with
detail pushed to the edges, no characters, no text, 16:9 horizontal, cohesive game art
```

**日本語（対訳）**
```
夕暮れの荒涼とした衰退の荒野、端には錆びた廃構造物と枯れ木のシルエット、
地平線低くに沈みかける巨大なオレンジの太陽が長い影を落とす、漂う灰、ひび割れた乾いた大地、
もの悲しさと衰退、翳りゆく時代、錆びた橙・灰・残り火の配色、
横長のシネマティックなファンタジー風景、空気感のあるマットペインティング、画面いっぱいの全面背景、
柔らかな被写界深度、中〜低キーの照明、彩度を抑えた色調、中央は静かでシンプル・ディテールは端へ寄せる、
人物なし、文字なし、16:9 横長、統一感のあるゲームアート
```

## 4. 終焉 — `twilight.png`（年 76–100）

**English**
```
a desolate end-of-the-world dusk, a vast dark throne-land under a deep violet-black void sky with eerie
cold-gold light on the horizon, distant shattered spires and faint arcane glow at the edges, utterly still,
ominous and solemn, the final age, deep void-violet and black and cold gold palette,
wide cinematic fantasy landscape, atmospheric matte painting, edge-to-edge full-frame background,
soft depth of field, low-to-mid key lighting, muted desaturated tones, simple calm center with
detail pushed to the edges, no characters, no text, 16:9 horizontal, cohesive game art
```

**日本語（対訳）**
```
荒涼とした世界の終わりの黄昏、深い紫黒の虚空の空の下に広がる暗い玉座の地、地平線に不気味な冷たい金の光、
端には遠く砕けた尖塔とかすかな魔法の燐光、完全な静寂、不吉で荘厳、最後の時代、
深い虚無の紫黒・黒・冷たい金の配色、
横長のシネマティックなファンタジー風景、空気感のあるマットペインティング、画面いっぱいの全面背景、
柔らかな被写界深度、中〜低キーの照明、彩度を抑えた色調、中央は静かでシンプル・ディテールは端へ寄せる、
人物なし、文字なし、16:9 横長、統一感のあるゲームアート
```

---

## 5. 目視確認（4 枚を本物のカード越しに）

- **`./preview_backgrounds.command`**（＝`res://BackgroundGallery.tscn`）を実行すると、4 章の背景を
  **16:9 のミニ画面＋本物のコンテンツカード（74%・額縁）＋サンプル文字**を重ねて一覧表示する。
  暗いカード越しの見え方と**文字の可読性**まで一度に確認できる（年 25/50/75/100 までプレイ不要）。
- 表示は**実ローダ `BackgroundTextureLibrary` 経由**なので、未配置／読込失敗は `MISSING` と出る。

## 6. Scenario 運用メモ

- **モデルは敵アートと同じ（または近い）厚塗り系**にそろえると、背景＋敵＋ジョブの世界観が一貫する。
- 16:9（1920×1080）で生成 → そのまま保存（縮小不要）。透過処理は**不要**（全画面背景）。
- 暗いカードに負けないよう、**真っ黒は避け**、各時代の色をほどよい明度で残す。生成後は必ず §5 のプレビューで
  「カード越しの見え方・文字可読性」を確認し、強すぎ/暗すぎなら再生成 or `ContentCardColor` の不透明度(0.74)で微調整。
