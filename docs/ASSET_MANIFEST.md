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

## 2. 戦場背景 🟡 原色プレースホルダ配置済（4枚・要差し替え）

`res://Assets/Textures/Backgrounds/{epoch}.png`。1時代（年代の章）につき1枚。
全画面に `KeepAspectCovered` で敷く想定なので、横長（例 1280x720 以上、16:9）推奨。
ローダ: `UserInterface/BackgroundTextureLibrary.cs`（**実装済**）。`BattleUI` が最背面へ全画面で敷く。
現状は動作確認用の**原色べた塗り**（dawn=黒 / upheaval=白 / decline=青 / twilight=緑）を配置済。本番アートで上書きするだけでよい。

| epoch (slug) | 時代 | 該当年 | 必要ファイル |
|---|---|---|---|
| dawn     | 黎明 | 1–25   | ⬜ `Backgrounds/dawn.png` |
| upheaval | 動乱 | 26–50  | ⬜ `Backgrounds/upheaval.png` |
| decline  | 衰退 | 51–75  | ⬜ `Backgrounds/decline.png` |
| twilight | 黄昏 | 76–100 | ⬜ `Backgrounds/twilight.png` |

→ **4枚**。

---

## 3. 敵イラスト 🟡 原色プレースホルダ配置済（5枚・要差し替え）

`res://Assets/Textures/Enemies/{archetype}.png`。敵カード上部に表示（`BattleUI` の `battle-enemy-portrait`）。縦長〜正方（例 256–512px）推奨。
ローダ: `UserInterface/EnemyTextureLibrary.cs`（**実装済**）。現状は動作確認用の原色べた塗りを配置済（本番アートで上書きするだけでよい）。

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
- 配置すれば即反映（コード変更不要）。背景・敵の**ローダ2クラス（`BackgroundTextureLibrary` / `EnemyTextureLibrary`）は実装済**で、
  `BattleUI` へ結線済み（背景=最背面 `KeepAspectCovered` / 敵=敵カード上部）。slug 写像は純粋層 `Core/Assets/AssetSlugs` が SoT で、
  `Tests/Core/Assets/AssetSlugsTests` が「全 Epoch / Archetype についてローダが引くパスに実ファイルがある」ことを固定する。

---

## 6. まとめ（基盤は通電済み・残りは本番アートの差し替えのみ）

| 種別 | 枚数 | 状態 |
|---|---|---|
| ジョブ | 16 | ✅ 本番配置済み |
| 背景   | 4  | 🟡 原色プレースホルダ配置済（ローダ実装済・要差し替え） |
| 敵     | 5  | 🟡 原色プレースホルダ配置済（ローダ実装済・要差し替え） |
| **本番アート差し替え待ち** | **9** | 🟡 |

→ ローダ（`BackgroundTextureLibrary` / `EnemyTextureLibrary`）と `BattleUI` 結線は実装済み。
   **同名ファイルを本番アートで上書きするだけで即反映**（コード変更不要・差し替え式）。

詳しい結線箇所（どの画面のどのノードへ載せるか）は `docs/VISUAL_AND_JUICE_ROADMAP.md` の
§1.4 / §7 を参照。
