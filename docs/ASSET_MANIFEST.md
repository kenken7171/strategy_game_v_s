# アセット必要物 一覧（マニフェスト）

> 「結局どの画像を用意すればいいか」を 1 枚で示すチェックリスト。
> パス・ファイル名は ASCII（実装側の slug＝enum 名の snake_case）。本書は人間向け解説のため
> 説明文のみ日本語。出典: `docs/VISUAL_AND_JUICE_ROADMAP.md` §1、コード（`EpochId` / `EnemyArchetype`）。

統合配置ディレクトリは `res://Assets/Textures/`（= `generated_csharp/Assets/Textures/`）。
ローダは「ResourceLoader → `Image.LoadFromFile` 生ディスク復号」の2段なので、Godot の `.import`
生成前（ソース起動）でも表示される。**未配置でも null フォールバックで落ちない**ので、後から足してよい。

凡例: ✅ 配置済み / ⬜ 要追加

---

## 1. ジョブ（職）イラスト ✅ 配置済み（16枚）

`res://Assets/Textures/Jobs/{job}/{male|female}.png`（男女別）。実寸 512x512 / RGBA のドット絵。
ローダ: `UserInterface/JobTextureLibrary.cs`。

| job (slug) | 職 | 状態 |
|---|---|---|
| iron_wall_knight | 鉄壁騎士 | ✅ male/female |
| heavy_infantry   | 重装歩兵 | ✅ male/female |
| standard_bearer  | 旗手     | ✅ male/female |
| tactician        | 戦術官   | ✅ male/female |
| medic            | 衛生兵   | ✅ male/female |
| sniper           | 狙撃兵   | ✅ male/female |
| sorcerer         | 呪術師   | ✅ male/female |
| scout            | 斥候     | ✅ male/female |

→ 8職 × 2性別 = **16枚（配置済み）**。

---

## 2. 戦場背景 ⬜ 要追加（4枚）

`res://Assets/Textures/Backgrounds/{epoch}.png`。1時代（年代の章）につき1枚。
全画面に `KeepAspectCovered` で敷く想定なので、横長（例 1280x720 以上、16:9）推奨。
ローダ（新設予定）: `UserInterface/BackgroundTextureLibrary.cs`。

| epoch (slug) | 時代 | 該当年 | 必要ファイル |
|---|---|---|---|
| dawn     | 黎明 | 1–25   | ⬜ `Backgrounds/dawn.png` |
| upheaval | 動乱 | 26–50  | ⬜ `Backgrounds/upheaval.png` |
| decline  | 衰退 | 51–75  | ⬜ `Backgrounds/decline.png` |
| twilight | 黄昏 | 76–100 | ⬜ `Backgrounds/twilight.png` |

→ **4枚**。

---

## 3. 敵イラスト ⬜ 要追加（5枚）

`res://Assets/Textures/Enemies/{archetype}.png`。敵カードに表示。縦長〜正方（例 256–512px）推奨。
ローダ（新設予定）: `UserInterface/EnemyTextureLibrary.cs`。

`EnemyArchetype` は5種。`TrialGuardian` は全時代の通常敵、残り4種は各章ボス（出現年 25/50/75/100）。

| archetype (slug) | 役割 | 出現 | 必要ファイル |
|---|---|---|---|
| trial_guardian      | 通常敵（全時代共通） | 毎年（章ボス年以外） | ⬜ `Enemies/trial_guardian.png` |
| dawn_warden         | 黎明の章ボス         | 25年   | ⬜ `Enemies/dawn_warden.png` |
| upheaval_conqueror  | 動乱の章ボス         | 50年   | ⬜ `Enemies/upheaval_conqueror.png` |
| decline_tyrant      | 衰退の章ボス         | 75年   | ⬜ `Enemies/decline_tyrant.png` |
| eternal_sovereign   | 黄昏の最終章ボス     | 100年  | ⬜ `Enemies/eternal_sovereign.png` |

→ **5枚**。

---

## 4. 任意（無くても動く）

- **ヒットエフェクト用テクスチャ**: ロードマップ §2 の `HitEffectDirector` は `CpuParticles2D` で
  手続き的に出せるため、専用画像は**必須ではない**。凝るなら
  `res://Assets/Textures/Effects/{slash|heal|defeat}.png` を後から足す。
- **効果音 (SFX)**: 将来用に `res://Assets/Audio/` を兵站国家の次領土として確保予定（本マニフェストの対象外）。

---

## 5. 形式・命名の約束

- 形式: PNG / RGBA。ドット絵なら拡大時は `TextureFilter = Nearest`（呼び出し側で指定）。
- 命名: 小文字 ASCII の snake_case（= enum 名の snake_case）。日本語ファイル名は不可。
- 配置すれば即反映（コード変更不要）。ただし背景・敵の**ローダ2クラスはまだ未実装**（ロードマップ §1.3 で新設予定）。
  画像だけ先に置いても、ローダ実装までは画面には出ない点に注意。

---

## 6. まとめ（追加が必要なのは合計9枚）

| 種別 | 枚数 | 状態 |
|---|---|---|
| ジョブ | 16 | ✅ 配置済み |
| 背景   | 4  | ⬜ 要追加 |
| 敵     | 5  | ⬜ 要追加 |
| **合計（要追加）** | **9** | ⬜ |

詳しい結線箇所（どの画面のどのノードへ載せるか）は `docs/VISUAL_AND_JUICE_ROADMAP.md` の
§1.4 / §7 を参照。
