# verify-individuality 実行レポート（統合Config + 全盛期年齢の個体差検証）

> 実行日時: 2026-05-27 23:18:13
> 実行コマンド: `bun scripts/verify-individuality.ts`
> RNG seed: 42

## 実行条件

| 項目 | 値 |
|---|---|
| 統合Config | `CHRONICLE_CONFIG.TIME.BASE_PEAK_START_AGE = 24` / `BASE_PEAK_END_AGE = 28` |
| 新人生成数 | 100名 |
| 新人ロール仕様 | BASE値 ±3 で独立ロール、`start < end` ガード |
| 子供生成数 | 50名 |
| 子供親条件 | 父=標準型(24/28)、母=晩成型(30/34) → 平均(27/31) |
| 子供ロール仕様 | 両親平均 ±1（成長タイプの遺伝性） |

## サマリー

- ✅ **全5項目 PASS**
- 100名新人の peakStartAge は 21〜27 の **7値すべて** 出現（個体ごとに異なる修業期終了タイミング）
- 仕様レンジ違反 0/100、`peakStart < peakEnd` ガード違反 0/100
- 子50名は全員が両親平均±1 のレンジ（peakStart 26〜28 / peakEnd 30〜32）に収まり、晩成型の親から晩成型の子が生まれる遺伝性を実証

## 詳細結果

### Test 1: 新人100名の peakStartAge 分布

```
peakStartAge 分布（期待: 21〜27）:
   21 :   9 █████████
   22 :  19 ███████████████████
   23 :  18 ██████████████████
   24 :  20 ████████████████████   ← BASE_PEAK_START_AGE
   25 :  19 ███████████████████
   26 :   9 █████████
   27 :   6 ██████
```

- BASE 値 24 を中心としたほぼ正規分布
- 全7値出現 → 個体ごとに修業期終了が異なるタイミングであることが視覚的に明らか

### Test 1: 修業期の終わる年齢分布（peakStartAge - 1）

```
   20歳 :   9 █████████
   21歳 :  19 ███████████████████
   22歳 :  18 ██████████████████
   23歳 :  20 ████████████████████
   24歳 :  19 ███████████████████
   25歳 :   9 █████████
   26歳 :   6 ██████
```

→ **「いつ全盛期に入るか」が騎士ごとに6年もの幅でバラつく**。早熟型(20歳)も晩成型(26歳)も自然に共存できる旅団になる。

### Test 1: 入団から全盛期入りまでの年数

```
    6年 :   9   ← 入団15歳 → 21歳全盛期入り（早熟）
    7年 :  19
    8年 :  18
    9年 :  20
   10年 :  19
   11年 :   9
   12年 :   6   ← 入団15歳 → 27歳全盛期入り（晩成）
```

| 指標 | 値 | 判定 |
|---|---|---|
| peakStartAge 仕様レンジ外（21〜27） | 0/100 | ✓ |
| peakEndAge 仕様レンジ外（25〜31） | 0/100 | ✓ |
| `peakStart >= peakEnd` 違反 | 0/100 | ✓ |
| peakStartAge ユニーク値数 | 7/7 | ✓ バラつき十分 |
| peakEndAge ユニーク値数 | 7/7 | ✓ バラつき十分 |

### Test 2: 子供の遺伝性（両親平均±1）

**親設定:**
- 父: peakStartAge=24, peakEndAge=28（標準型）
- 母: peakStartAge=30, peakEndAge=34（晩成型）
- 期待される平均: peakStart=**27**, peakEnd=**31**
- 期待レンジ: peakStart [26〜28] / peakEnd [30〜32]

**子50名の分布:**

```
peakStartAge:
   26 : 15 ███████████████
   27 : 17 █████████████████   ← 親平均
   28 : 18 ██████████████████

peakEndAge:
   30 : 17 █████████████████
   31 : 13 █████████████       ← 親平均
   32 : 20 ████████████████████
```

| 指標 | 値 | 判定 |
|---|---|---|
| peakStartAge レンジ外（26〜28） | 0/50 | ✓ |
| peakEndAge レンジ外（30〜32） | 0/50 | ✓ |
| `peakStart >= peakEnd` 違反 | 0/50 | ✓ |

→ **晩成型の母 (30/34) と標準型の父 (24/28) の子は、確実に両親の中間（27±1 / 31±1）になる**。完全ランダム ±3 ではなく、親の特徴が引き継がれている。

## 観察・考察

### 設計上の良かった点

1. **`rollChildPeakAges` を BirthRegistry に組み込んだのが正解** — 出産予約時点で子の peakStart/End を確定するため、15年後に親が死んでいても親の能力ピークが子に正しく反映される。`u.peakStartAge` を入団時にもう一度親から取り直す必要がなく、シンプル。

2. **`as const` で CHRONICLE_CONFIG が型レベル不変** — `CHRONICLE_CONFIG.TIME.BASE_PEAK_START_AGE = 25` のような誤書き換えは TypeScript がコンパイル時に弾く。バランス調整は ChronicleConfig.ts 編集 → 再ビルドのみの一本道。

3. **Unit のコンストラクタは純粋を維持** — 個体差ロールは呼び出し側 (`rollPeakAges` ヘルパー) の責任にしたため、Unit 自体は副作用なくテスト容易な状態を保てた。既存テスト41件は無改修で全 PASS。

4. **「成長タイプ遺伝」の物語性** — 親平均±1 という狭めレンジは設計上の意図が出やすい。例えば「鉄壁騎士の若き巨匠（早熟父）と熟練呪術師の母（晩成母）の子」は確実に中庸型になり、世代をまたいだ多様性の混合が物語として読める。

### 注意点・調整余地

1. **`childMaxAge` は CHRONICLE_CONFIG 未収録** — `Brigade.AdvanceOptions.childMaxAge` のデフォルトを 55 のまま残置している。仕様の CHRONICLE_CONFIG にキーがなかったため。将来 `TIME.CHILD_MAX_AGE_BASE` を追加すれば統合可能。

2. **個体差レンジの拡張余地** — 現状 ±3 / ±1 はそれぞれの仕様通り。将来「天才型」「凡人型」「奇人型」のような3層モデルにしたければ `rollPeakAges` に `talent: "genius" | "normal" | "late_bloomer"` 引数を追加して分布を切り替える設計も考えられる。

3. **マジックナンバー 0.97 → DECAY_RATE 化済み** — `Math.pow(1 - 0.03, ...)` を `Math.pow(1 - CHRONICLE_CONFIG.TIME.DECAY_RATE, ...)` に置換完了。同様に `Math.max(1, ...)` も `Math.max(CHRONICLE_CONFIG.TIME.MIN_STAT_VALUE, ...)` に統一。

### 副次効果

- `simulate_history` の TOTAL_YEARS が `CHRONICLE_CONFIG.SCHEDULE.CHRONICLE_YEARS` 経由
- `run-grand-chronicle` の年次パラメータ（100年・2年ごと・5年ごと・9名大隊・30ターン）すべて CHRONICLE_CONFIG 参照に置換済み
- `run-sim` の `--turns` デフォルトも CHRONICLE_CONFIG 経由

これで CHRONICLE_CONFIG を 1 箇所書き換えるだけで全システムの挙動が連動して変わる。

## 再現方法

```bash
# 個体差検証
bun scripts/verify-individuality.ts          # seed=42
bun scripts/verify-individuality.ts --seed 7 # 別シード

# 関連検証（CHRONICLE_CONFIG リファクタの回帰確認）
bun test packages/core/test/                 # 41 pass
bun scripts/verify-bloodline.ts              # 血統継承 PASS
bun scripts/verify-naming.ts                 # 命名重複回避 PASS
bun scripts/run-grand-chronicle.ts           # 100年シミュ動作
```
