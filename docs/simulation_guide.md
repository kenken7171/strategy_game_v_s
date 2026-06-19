# Build / Test / Simulation Guide（C# 版）

> 対象は現役本体 `generated_csharp/`。旧 TypeScript 版の Bun CLI（`bun scripts/run-sim.ts` 等）は
> 凍結された参照専用で、現行ゲームのビルド・検証経路ではない。
> 現行の検証は **`dotnet test`** と **Godot 実機起動** の 2 経路に集約されている。

---

## 0. 前提ツール

| ツール | 用途 | 確認 |
|---|---|---|
| **.NET SDK**（8 以上） | ビルド・テスト | `dotnet --version`（`net8.0` ターゲットを `RollForward=LatestMajor` で実行） |
| **Godot 4.3（.NET/mono 版）** | 実機起動 | `godot --version` → `4.3.stable.mono.official` |

すべてのコマンドは `generated_csharp/` ディレクトリから実行する。

---

## 1. ビルド

```sh
dotnet build ChronicleKnights.csproj --configuration Debug
```

`Godot.NET.Sdk/4.3.0` を採用。`net8.0` ターゲットだが `RollForward=LatestMajor` を焼き込んであるため、
8.0 ランタイムが無く 10.x のみの環境でも環境変数なしで動く。

---

## 2. テスト（xUnit / Core 純粋層）

```sh
dotnet test Tests/ChronicleKnights.Tests.csproj
```

- 現況 **653 pass / 0 fail**。
- Godot 非依存。`Tests` プロジェクトは `Core/**/*.cs` を `<Compile Include>` で取り込み、Godot 本体を参照しない。
- `WarningsAsErrors` に 13 個の CS 警告コードを列挙しており、禁止警告が混入するとビルドが赤くなる（構造的品質ガード）。

### 個別テストの絞り込み

```sh
# クラス名・メソッド名で絞る
dotnet test Tests/ChronicleKnights.Tests.csproj --filter "FullyQualifiedName~BattlePassive"
dotnet test Tests/ChronicleKnights.Tests.csproj --filter "FullyQualifiedName~ChronicleHundredYear"
```

---

## 3. 100 年シミュレーション・バランス検証（テストとして実装）

旧 TS の `run-grand-chronicle` / メタ分析に相当する検証は、xUnit テストへ移植されている
（`Tests/Core/Chronicle/`）:

| テスト/ランナー | 役割 |
|---|---|
| `ChronicleHundredYearSimulationTests` | 100 年（1 旅団の興亡）を決定論シードで通し、不変条件を検証 |
| `MultiverseSimulationRunner` | 複数シード（多元宇宙）でモンテカルロ実行 |
| `MetricsCollector` / `MetricsReporter` / `MetricsLogFormatter` | 年次メトリクスの収集・整形・ASCII 構造化ログ出力 |
| `UniverseEvaluator` | 1 周の結果（絶滅率・勝率等）を評価し黄金均衡を判定 |
| `EnemyScalingResolver` / `EpochBossForecast` | 時代難易度曲線・章ボス前兆スケジュールの検証 |

実走時の機械可読ログは `ChronicleGlobal` が `GD.Print(MetricsLogFormatter.Format...)` で標準出力へ流す
（戦闘開始・戦果決算の年・内訳・残高など）。実機プレイ中にこれをコンソールで観測できる。

---

## 4. 実機起動（手で 1 周回す）

```sh
./play.command          # C# 自動ビルド → godot --path . で起動
./play.command -e       # Godot エディタを開く
```

- 起動の流れ: Godot が `/root/ChronicleGlobal`（autoload）を生成 → `Main.tscn` が `GameDirector` を起動 →
  タイトル → 新規/継続で `Initialize`/`LoadGame` → 年代記（予言 3 択）→ 拠点（婚姻/スカウト/行動選択）→
  編成（▲ウェッジ配置）→ 戦闘（ターン → とどめ → 戦果決算）→ 年送りで年代記へ。
- macOS の `--headless` は Godot 4.3 既知の不具合でクラッシュする。画面確認は必ず windowed で行う。

---

## 5. 決定論の確認

同一シードからは同一の歴史が再現される（憲法④）。テストでは `Initialize(rng: new Random(seed))` や
`StartBattle(enemy, battleSeed)` に固定シードを渡して再現性を担保している。実機では
`StartNewGame(seed)` が 1 本の乱数ストリームでその 100 年史を決定づける。
