---
description: シミュレーション実行時の詳細レポート出力ルール（必須）
---

# Simulation Reporting

## 適用範囲（必須トリガー）

以下のいずれかを実行した場合、**必ず**詳細レポートを `reports/` 配下に Markdown で保存すること。

- `scripts/run-sim.ts` — 大隊間バトルプリセット対戦
- `scripts/run-grand-chronicle.ts` — 100年旅団変遷
- `scripts/age_progression_test.ts` — 経年変化動作確認
- `apps/cli/src/simulate_history.ts` — 100年旅団 JSON 出力
- `apps/cli/src/simulate_brigade_battle.ts` — 大隊間戦闘デモ
- `apps/cli/src/simulate_battle_*.ts` — 各バトルシナリオ
- `apps/cli/src/simulate_random_attack.ts`
- `apps/cli/src/simulate_unit.ts`
- その他「シミュレーションを実行して」とユーザに依頼された場合の任意スクリプト

ユーザに明示的に「レポート不要」と言われた場合のみ省略可。

## ファイル命名規則

```
reports/<sim_name>_<YYYY-MM-DD>_<HH-MM-SS>.md
```

- `<sim_name>`: スクリプトのファイル名から `.ts` を除いたもの（例: `run-grand-chronicle`, `simulate_history`）
- タイムスタンプはレポート作成時点のローカル時刻
- ハイフン区切り（コロンは macOS/Linux で問題ないが Windows 互換のため避ける）

**実行コマンド例:**

```bash
date +"%Y-%m-%d_%H-%M-%S"
# → 2026-05-24_04-12-37
```

このタイムスタンプを使い `reports/run-grand-chronicle_2026-05-24_04-12-37.md` のように保存する。

## レポートに含めるべきセクション

```markdown
# <シミュレーション名> 実行レポート

> 実行日時: YYYY-MM-DD HH:MM:SS
> 実行コマンド: `bun scripts/run-grand-chronicle.ts --seed 42`
> RNG seed: 42

## 実行条件

- 入力プリセット / 引数 / 初期状態
- 最大ターン数・年数などのパラメータ
- 使用した乱数シード

## サマリー

実行結果の要約を箇条書きまたはテーブルで。最重要指標を先頭に。

## 詳細結果

- ターン or 年ごとのイベントログ（必要に応じて折りたたみ可）
- 統計値（ダメージ・回復・キル数・生存者リストなど）
- グラフ的に追えるなら ASCII 表で

## 観察・考察

- 数値から読み取れる興味深い挙動（例: 「Year 70 で初敗北、衰退期ユニット増加と相関」）
- 既知の挙動から逸脱しているもの
- 次の調整候補（パラメータ・ジョブバランス等）

## 再現方法

```bash
bun scripts/run-grand-chronicle.ts --seed 42
```
```

## 実装フロー

1. スクリプトを実行（`bun ...` などで stdout を取得）
2. `date +"%Y-%m-%d_%H-%M-%S"` で現在時刻を取得
3. `reports/<sim_name>_<timestamp>.md` を Write ツールで作成
4. 上記セクション構成で、実行ログから要点を抽出してまとめる
5. ユーザへの返答では「レポートを `reports/...` に保存しました」と必ず明示

## 既存レポートとの整合

`reports/` には既存の手書きレポート（`battle_offense_integrated.md`, `brigade_snapshot.md` 等）がある。命名規則は新規分から本ルールに従い、既存ファイルはそのままにする。

## NG例

- レポートを作らず stdout の要約だけ返す → NG
- ファイル名にタイムスタンプが無く上書きされる → NG
- 統計値のみで観察・考察セクションが無い → NG（数値の意味づけが無いと履歴として価値が下がる）
