# Job Definitions

ジョブ定義のカタログ。`config/jobs.json` が正規ソースだが、ここでは能力の詳細な挙動まで記述する。

---

## 一覧

| Job ID | 日本語名 | 役割 |
|---|---|---|
| `iron_wall_knight` | 鉄壁騎士 | 前衛防御・大隊防護 |
| `tactician` | 戦術官 | 全体バフ支援 |
| `medic` | 衛生兵 | 回復後方支援 |
| `sniper` | 狙撃兵 | 後衛高火力 |
| `sorcerer` | 呪術師 | 後衛超火力（要護衛） |
| `standard_bearer` | 旗手 | 火力特化バフ |
| `heavy_infantry` | 重装歩兵 | 単体特化型前衛 |
| `scout` | 斥候 | 高速削り役 |

---

## iron_wall_knight（鉄壁騎士）

**デフォルトステータス**

| 項目 | 値 |
|---|---|
| maxHp | 250 |
| speed | 10 |
| frontAttack | 50 |
| rearAttack | 10 |
| sdf | 15 |
| bdf | 10 |

**能力詳細**

- **SDF（Squad Defense）**: 自分が属する分隊が受けるダメージを `sdf` だけ軽減する。同じ分隊に複数いれば加算。
- **BDF（Brigade Defense）**: `FRONT` スロットに配置されている場合、大隊全体（全スロット）が受けるダメージを `bdf` だけ軽減。複数いれば加算。
- 最低ダメージ保証: `Math.max(1, damage - reduction)` のため0にはならない。

**推奨配置**: `FRONT` スロット。`FRONT` 配置時に BDF が発動し、全体防護を担う。

---

## tactician（戦術官）

**デフォルトステータス**

| 項目 | 値 |
|---|---|
| maxHp | 120 |
| speed | 35 |
| frontAttack | 20 |
| rearAttack | 20 |
| ab | 20 |

**能力詳細**

- **AB（Attack Buff）**: ターン開始時（バフリセット後）に、大隊全体の全ユニット（自分以外）の `speedBuff` と `attackBuff` を `ab` 加算する。
- 複数いれば加算。自分自身へはバフが乗らない。
- バフは毎ターンリセットされるため永続しない。

**推奨配置**: `FRONT` または `REAR` スロット。複数編成でバフを積み上げる構成が有効。

---

## medic（衛生兵）

**デフォルトステータス**

| 項目 | 値 |
|---|---|
| maxHp | 100 |
| speed | 25 |
| frontAttack | 10 |
| rearAttack | 10 |
| hl | 30 |

**能力詳細**

- **HL（Heal）**: ターン終了時に、自分が属する分隊の生存全ユニットを `hl` だけ回復。`Math.min(maxHp, hp + hl)` で上限あり。
- 同じ分隊に複数いれば回復量が加算される。
- 異なる分隊の衛生兵は、それぞれ自分の分隊のみを回復する（クロス回復なし）。

**推奨配置**: `REAR` スロット。生存率を高めて毎ターン回復を継続させる。

---

## sniper（狙撃兵）

**デフォルトステータス**

| 項目 | 値 |
|---|---|
| maxHp | 80 |
| speed | 40 |
| frontAttack | 20 |
| rearAttack | 90 |

**能力詳細**

- **2連撃条件**: イニシアチブが大隊内で1番手かつ、分隊内で最速の場合、同ターン2回攻撃。
- rearAttack が高いため `REAR` スロット配置で真価を発揮。
- 速度が高く tactician バフが乗ると2連撃発動率が向上する。

**推奨配置**: `REAR-L` または `REAR-R`。`REAR-L` の1番手に置き、tactician のバフで速度をさらに上げると2連撃を安定発動できる。

---

## sorcerer（呪術師）

**デフォルトステータス**

| 項目 | 値 |
|---|---|
| maxHp | 40 |
| speed | 15 |
| frontAttack | 10 |
| rearAttack | **120** |
| sdf | 0 |
| bdf | 0 |

**能力詳細**

- 大隊最高クラスの rearAttack（120）。狙撃兵（90）を上回る後衛火力。
- 一方で maxHp 40 は全ジョブ最低。敵の標準的な一撃で即死しやすい。
- 守られて初めて真価を発揮する「砲台」型。前衛で受け、後方から焼き払う運用が前提。

**推奨配置**: `REAR-L` または `REAR-R`。前衛に鉄壁騎士・重装歩兵を厚く配置し、tactician/standard_bearer のバフを乗せて運用する。`FRONT` は厳禁（即死）。

---

## standard_bearer（旗手）

**デフォルトステータス**

| 項目 | 値 |
|---|---|
| maxHp | 150 |
| speed | 20 |
| frontAttack | 30 |
| rearAttack | 30 |
| sdf | 5 |
| ab | **40** |

**能力詳細**

- **AB（Attack Buff）**: ターン開始時に、大隊全体の全ユニット（自分以外）の `speedBuff` と `attackBuff` を `ab` 加算する。
- 戦術官の倍となる ab=40 で大隊火力を底上げする。複数いれば加算（戦術官と併用可）。
- 自身も sdf=5 を持ち、分隊にわずかな防護も提供する。
- 戦術官より HP が高く（150 vs 120）、前線でも耐えやすい精神的支柱。

**推奨配置**: `FRONT` または `REAR`。呪術師・狙撃兵・重装歩兵の火力を引き上げる構成と相性が良い。

---

## heavy_infantry（重装歩兵）

**デフォルトステータス**

| 項目 | 値 |
|---|---|
| maxHp | **300** |
| speed | 15 |
| frontAttack | **70** |
| rearAttack | 20 |
| sdf | 10 |
| bdf | 0 |

**能力詳細**

- 全ジョブ最高の HP（300）と高水準の frontAttack（70）を持つ単騎完結型の前衛。
- 鉄壁騎士と異なり BDF を持たないため、大隊全体への防護効果はない。
- sdf=10 で自分隊にわずかな防護を提供する。
- 「単体で粘り強く戦う」ことが役割。

**推奨配置**: `FRONT`。鉄壁騎士と併置すると、BDF を提供する鉄壁の隣で攻撃役を担う形になり強力。

---

## scout（斥候）

**デフォルトステータス**

| 項目 | 値 |
|---|---|
| maxHp | 90 |
| speed | **60** |
| frontAttack | 40 |
| rearAttack | 40 |
| sdf | 0 |
| bdf | 0 |

**能力詳細**

- 全ジョブ最速（speed=60）。狙撃兵（40）を上回る。
- frontAttack=rearAttack=40 で配置に依存せずダメージを出せる。
- 単発火力は控えめだが、確実に先制してターン頭から敵HPを削れる。
- イニシアチブ1番手を取れば、後続の狙撃兵・呪術師の生存率を上げる効果もある。

**推奨配置**: `REAR-L` または `REAR-R`。先制で敵を削り、後衛火力役（sniper/sorcerer）の安全を確保する役回り。

---

## 新ジョブ追加手順

1. `config/jobs.json` に新エントリを追加（`id`, `name`, `description`, `defaults`）
2. `packages/core/src/models/Unit.ts` の `JobType` に ID を追加
3. `BattleManager.ts` に必要であれば能力発動ロジックを実装（`applyTacticianBuffs` 等を参考）
4. `docs/job_definitions.md`（本ファイル）に仕様を追記
5. `scripts/run-sim.ts` の `JOB_DEFAULTS` に値を追加
