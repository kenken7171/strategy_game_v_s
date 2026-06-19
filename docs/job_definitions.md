# Job Definitions（C# 版）

> ジョブ定義のカタログ。**数値の正規ソースは `generated_csharp/Core/Job/JobMaster.cs`**（`JobMaster.All`）、
> 日本語ラベル・解説の SoT は `generated_csharp/Config/localization_ja.json` の `jobs` セクション。
> 本書は能力の挙動まで含めた人間向けリファレンス。略称（SDF/BDF/AB/HL）は廃止し正式名称を用いる。

---

## 0. パッシブ用語（`PassiveKind`）

| 正式名称 | 旧略称 | UI ラベル | 効果 |
|---|---|---|---|
| `BattalionDefense` | BDF | 🛡️ 大隊総守護力 | **FRONT 配置時のみ**、大隊全員の被ダメを軽減 |
| `SquadDefense` | SDF | 🛡️ 分隊守護力 | 所属分隊の被ダメを軽減（配置不問） |
| `InitiativeBuff` | AB | ⚡ 突撃号令 | ターン頭に大隊全員（自分以外）の速度・攻撃を底上げ |
| `TurnEndSquadHeal` | HL | 💚 ターン末分隊治癒 | ターン末に所属分隊の生存者を回復（HP 上限クランプ） |
| `ConsecutiveStrike` | — | 🎯 二の矢 | イニシアチブ 1 番手かつ分隊先頭時に通常攻撃 2 回 |

判定は `JobMaster.HasPassive(JobId, PassiveKind)` のデータ駆動（数値系は `JobStats` 値 > 0、特殊系は `SpecialPassives` 包含）。

---

## 1. 一覧（`JobId`）

| Job ID | 日本語名 | 役割 | EffectKind（UI 配色） |
|---|---|---|---|
| `IronWallKnight` | 鉄壁騎士 | 前衛防御・大隊防護 | Defend（青） |
| `HeavyInfantry` | 重装歩兵 | 単騎完結型前衛 | Attack（朱） |
| `StandardBearer` | 旗手 | 全体支援（最大規模） | Buff（金） |
| `Tactician` | 戦術官 | 軽量全体支援 | Buff（金） |
| `Medic` | 衛生兵 | 継続回復 | Heal（緑） |
| `Sniper` | 狙撃兵 | 後衛高火力 | Attack（朱） |
| `Sorcerer` | 呪術師 | 後衛超火力（要護衛） | Attack（朱） |
| `Scout` | 斥候 | 高速削り役 | Attack（朱） |

`JobMaster.DisplayOrder` は上表の順（防御→攻撃前衛→支援→回復→後衛火力→速度）。

---

## 2. 各ジョブ（数値は `JobMaster.All`）

### IronWallKnight（鉄壁騎士）
MaxHp 250 / Speed 10 / FrontAttack 50 / RearAttack 10 / BattalionDefense **10** / SquadDefense **15** ／ RoleBonus 30

- **BattalionDefense**: FRONT 配置時、大隊全体の被ダメを軽減（複数いれば加算）。
- **SquadDefense**: 所属分隊の被ダメを軽減（配置不問・加算）。最終被ダメ = `max(1, baseDamage − 大隊守護 − 分隊守護)`。
- 推奨配置: `Front`（FRONT で BDF が発動し全体防護を担う）。

### HeavyInfantry（重装歩兵）
MaxHp **300** / Speed 15 / FrontAttack **70** / RearAttack 20 / SquadDefense 10 ／ RoleBonus 0

- 全ジョブ最高 HP と高 FA を持つ単騎完結の前衛。BDF は持たない（大隊全体防護なし）。
- 推奨配置: `Front`。鉄壁騎士の隣で攻撃役を担うと強力。

### StandardBearer（旗手）
MaxHp 150 / Speed 20 / FrontAttack 30 / RearAttack 30 / SquadDefense 5 / InitiativeBuff **40** ／ RoleBonus 65

- **InitiativeBuff 40**: 大隊全員（自分以外）の速度・攻撃を底上げ（最大規模・戦術官と併用可・加算）。
- 推奨配置: `Front` / `RearLeft` / `RearRight`（どこでも全体に効く）。後衛火力役の底上げと好相性。

### Tactician（戦術官）
MaxHp 120 / Speed 35 / FrontAttack 20 / RearAttack 20 / InitiativeBuff **20** ／ RoleBonus 65

- 軽量 InitiativeBuff ＋ 自身も中速。旗手と重ねて号令を積み上げられる。
- 推奨配置: どこでも可。複数編成でバフを積む構成が有効。

### Medic（衛生兵）
MaxHp 100 / Speed 25 / FrontAttack 10 / RearAttack 10 / TurnEndSquadHeal **30** ／ RoleBonus 90

- **TurnEndSquadHeal 30**: ターン末に所属分隊の生存者を回復（`min(maxHp, hp + heal)` でクランプ・同分隊複数で加算）。
  異なる分隊の衛生兵はそれぞれ自分の分隊のみ回復（クロス回復なし）。
- 推奨配置: `RearLeft` / `RearRight`。唯一の継続支援役（RoleBonus 最大）。

### Sniper（狙撃兵）
MaxHp 80 / Speed 40 / FrontAttack 20 / RearAttack **90** / SpecialPassives: **ConsecutiveStrike** ／ RoleBonus 0

- **二の矢**: イニシアチブ 1 番手かつ分隊先頭スロット時、通常攻撃が 2 回（例: `(後衛90 + 号令20) × 2 = 220`）。
- 推奨配置: `RearLeft` / `RearRight`。号令で速度を上げて先頭を取り、二連撃を安定発動。

### Sorcerer（呪術師）
MaxHp **40** / Speed 15 / FrontAttack 10 / RearAttack **120** ／ RoleBonus 0

- 全職最高の後衛火力。一方で MaxHp 40 は最低で即死しやすい「砲台」。
- 推奨配置: `RearLeft` / `RearRight`（FRONT は厳禁）。前衛を厚くし号令を乗せて運用。

### Scout（斥候）
MaxHp 90 / Speed **60** / FrontAttack 40 / RearAttack 40 ／ RoleBonus 30

- 全職最速。配置に依存せずダメージを出せ、先制で敵を削って後衛火力役の安全を確保する。
- 推奨配置: `RearLeft` / `RearRight` / `Front`（推奨 row が広い）。

---

## 3. 戦闘パッシブの解決順（`BattleManager`）

1. **行動順構築**: 実効速度（自身 Speed ＋ 号令ボーナス）の高い順。同速は味方優先のタイブレーク。
2. **号令ブロードキャスト**: InitiativeBuff 持ちが自分以外の全生存者へ「速度＝配り手の Speed / 攻撃＝配り手の InitiativeBuff」を加算。
3. **連続攻撃**: 先頭かつ ConsecutiveStrike 保持時のみ攻撃 2 回。
4. **ダメージ軽減**: `max(1, baseDamage − 大隊守護 − 分隊守護)`（最低 1 は必ず通る）。
5. **継続回復**: ターン末に衛生兵が自分隊を回復（上限クランプ）。

数値例は `PROGRESS_REPORT.md` §3-4（テストで固定済みの実例）と `Tests/Core/Managers/BattlePassiveTests.cs` を参照。

---

## 4. 新ジョブ追加手順（データ駆動）

1. `Core/Job/JobData.cs` の `JobId` enum に ID を追加。
2. `Core/Job/JobMaster.cs` の `BuildAll()` に `JobDefinition`（`JobStats` / `FormationGuide` / `RoleBonus` /
   必要なら `SpecialPassives`）を追加。
3. `Config/localization_ja.json` の `jobs` セクションに表示名・解説キーを追加。
4. 立ち絵 `Assets/Textures/Jobs/{job}/{male|female}.png` を配置（任意・未配置でも null フォールバック）。
5. 数値で表せない特殊パッシブが必要なら `PassiveKind` に種別を足し、`BattleManager` に発動ロジックを実装。
6. 本書（`docs/job_definitions.md`）に仕様を追記。
