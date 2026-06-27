# 予言（選択）カード アート生成プロンプト集（Scenario 用）

> Chronicle（年代記）で毎ターン引く **1 of 3 の予言カード**の上部イラスト**5 種**（種別ごと）を
> [Scenario](https://www.scenario.com/) で生成するためのプロンプト集（英語・日本語併記）。
> レア度（銅/銀/金）はコード側の色味（`TimelineUI.RarityColor`）で表現するので、**絵は種別ごとに 1 枚**でよい。

## 0. 生成の前提（共通仕様）

| 項目 | 指定 |
|---|---|
| 配置先 | `generated_csharp/Assets/Textures/Prophecies/{slug}.png`（slug は下表） |
| 形式 | PNG（透過は任意。カード上に帯状に出るので不透明でも可。中身 JPEG でもローダは読む） |
| アスペクト | **縦長カード 3:4 推奨**（カードの絵placeは 190×250≒3:4。3枚を横に並べる TCG 風）。**960×1280** 程度で生成→そのまま |
| 構図 | **縦構図で中央に象徴1つ**を大きく・小さく(190px幅)でも一目で分かる・読みやすい・上下に余白を持たせる |
| 画風 | 敵/背景アートと色調をそろえる（厚塗り・シネマティック）。レア度色と喧嘩しない中庸トーン |

- 5 枚は**同一モデル・近い構図**でそろえると「同じデッキのカード」感が出る。
- レア度（銅/銀/金）は**コードが色味で表現**するので絵は種別1枚でOK（金だけ豪華版…等にしたければ後で追加可）。

### 共通スタイル（全プロンプト末尾・EN）
```
single iconic centered symbol, vertical portrait composition, fantasy tarot/omen card illustration,
painterly, clean readable shape, soft glow, shallow depth of field, cohesive game art,
3:4 vertical portrait, no text, no letters, no border
```

### 共通スタイル（日本語・対訳）
```
中央に1つの象徴アイコン、縦構図、ファンタジーのタロット/予兆カード風イラスト、厚塗り、読みやすい明快な形、
柔らかな発光、浅い被写界深度、統一感のあるゲームアート、3:4 縦長、文字なし、枠なし
```

### 共通ネガティブ（全プロンプト共通・EN）
```
text, letters, numbers, watermark, signature, logo, ui, card frame, border, multiple separate icons,
busy clutter, photo, realistic photo, blurry, lowres, jpeg artifacts
```

### 対象一覧 / 保存先パス

| slug（ファイル名） | 種別(ProphecyKind) | 表示 | モチーフ | 配色 | 保存先 |
|---|---|---|---|---|---|
| `reward_points` | RewardPoints | 💰 報酬獲得 | 金貨・宝の輝き | 金 | `generated_csharp/Assets/Textures/Prophecies/reward_points.png` |
| `battle` | Battle | ⚔ 戦闘発生 | 交差する剣・戦の予兆 | 朱/鉄 | `generated_csharp/Assets/Textures/Prophecies/battle.png` |
| `scout_reward` | ScoutReward | 👥 新人加入 | 加わる仲間・募兵の旗 | 青 | `generated_csharp/Assets/Textures/Prophecies/scout_reward.png` |
| `equipment_drop` | EquipmentDrop | 📦 装備入手 | 光る武具・戦利品箱 | 緑 | `generated_csharp/Assets/Textures/Prophecies/equipment_drop.png` |
| `rest` | Rest | 💤 休息 | 夜営のたき火・安息 | 藍 | `generated_csharp/Assets/Textures/Prophecies/rest.png` |

- ファイル名は**小文字 ASCII**固定。Godot 内パスは `res://Assets/Textures/Prophecies/{slug}.png`。
- 現状は原色プレースホルダ配置済（種別ごとに色違いの単色）。同名上書きで即反映。

---

## 1. 報酬獲得 — `reward_points.png`

**English**
```
a radiant pile of gold coins and gemstones with a glowing golden aura, an omen of wealth and reward,
warm golden light, treasure motif,
single iconic centered symbol, fantasy tarot/omen card illustration, painterly, clean readable shape,
soft glow, shallow depth of field, cohesive game art, 3:4 vertical portrait, no text, no letters, no border
```

**日本語（対訳）**
```
黄金のオーラを放つ金貨と宝石の山、富と報酬の予兆、暖かな金色の光、宝物モチーフ、
中央に1つの象徴アイコン、ファンタジーのタロット/予兆カード風イラスト、厚塗り、読みやすい明快な形、
柔らかな発光、浅い被写界深度、統一感のあるゲームアート、3:4 縦長、文字なし、枠なし
```

## 2. 戦闘発生 — `battle.png`

**English**
```
two crossed swords clashing with sparks, a grim omen of imminent battle, dark crimson and iron tones,
dramatic backlight, war motif,
single iconic centered symbol, fantasy tarot/omen card illustration, painterly, clean readable shape,
soft glow, shallow depth of field, cohesive game art, 3:4 vertical portrait, no text, no letters, no border
```

**日本語（対訳）**
```
火花を散らして交差する二振りの剣、迫る戦闘の不吉な予兆、暗い深紅と鉄の色調、ドラマチックな逆光、戦のモチーフ、
中央に1つの象徴アイコン、ファンタジーのタロット/予兆カード風イラスト、厚塗り、読みやすい明快な形、
柔らかな発光、浅い被写界深度、統一感のあるゲームアート、3:4 縦長、文字なし、枠なし
```

## 3. 新人加入 — `scout_reward.png`

**English**
```
a raised recruiting banner with a glowing silhouette of a new comrade stepping forward to join,
an omen of fellowship and reinforcement, cool blue tones, hopeful light, recruitment motif,
single iconic centered symbol, fantasy tarot/omen card illustration, painterly, clean readable shape,
soft glow, shallow depth of field, cohesive game art, 3:4 vertical portrait, no text, no letters, no border
```

**日本語（対訳）**
```
掲げられた募兵の旗と、加わろうと歩み出る新たな仲間の光るシルエット、結束と増援の予兆、
冷たい青の色調、希望の光、募兵のモチーフ、
中央に1つの象徴アイコン、ファンタジーのタロット/予兆カード風イラスト、厚塗り、読みやすい明快な形、
柔らかな発光、浅い被写界深度、統一感のあるゲームアート、3:4 縦長、文字なし、枠なし
```

## 4. 装備入手 — `equipment_drop.png`

**English**
```
an open loot chest radiating green light with a glowing weapon and armor floating above it,
an omen of a powerful equipment drop, emerald-green glow, treasure-loot motif,
single iconic centered symbol, fantasy tarot/omen card illustration, painterly, clean readable shape,
soft glow, shallow depth of field, cohesive game art, 3:4 vertical portrait, no text, no letters, no border
```

**日本語（対訳）**
```
緑の光を放つ開いた戦利品の箱と、その上に浮かぶ光る武器と防具、強力な装備入手の予兆、
エメラルドグリーンの輝き、戦利品モチーフ、
中央に1つの象徴アイコン、ファンタジーのタロット/予兆カード風イラスト、厚塗り、読みやすい明快な形、
柔らかな発光、浅い被写界深度、統一感のあるゲームアート、3:4 縦長、文字なし、枠なし
```

## 5. 休息 — `rest.png`

**English**
```
a peaceful campfire at a night camp under calm stars, an omen of rest and recovery, deep indigo-blue
night tones with warm fire glow, restful and serene, respite motif,
single iconic centered symbol, fantasy tarot/omen card illustration, painterly, clean readable shape,
soft glow, shallow depth of field, cohesive game art, 3:4 vertical portrait, no text, no letters, no border
```

**日本語（対訳）**
```
穏やかな星空の下の夜営の安らかなたき火、休息と回復の予兆、深い藍色の夜の色調に暖かな火の光、
安らかで静謐、安息のモチーフ、
中央に1つの象徴アイコン、ファンタジーのタロット/予兆カード風イラスト、厚塗り、読みやすい明快な形、
柔らかな発光、浅い被写界深度、統一感のあるゲームアート、3:4 縦長、文字なし、枠なし
```

---

## 6. 目視確認 / 運用メモ

- **`./preview_prophecies.command`**（＝`res://ProphecyGallery.tscn`）で 5 種のカード絵を実ローダ
  `ProphecyTextureLibrary` 経由で一覧表示（種別名・銅銀金の色見本・`OK`画素サイズ/`MISSING`）。
- カードの絵 place は **190×250≒3:4（縦長）**。3:4 で作れば letterbox の黒帯が出ない。3 枚を横に並べた TCG 風レイアウト。
- レア度は絵ではなく**色味（`TimelineUI.RarityColor`）**で差が付く。金だけ豪華版にしたい等あれば、
  `{slug}_gold.png` 方式へ拡張も可能（要相談）。
- 形式の注意は敵と同じ（`.png` 名でも中身 JPEG はローダが中身判定で読む。透過が要るなら PNG/RGBA）。
