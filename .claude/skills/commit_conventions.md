---
description: Git コミットメッセージの記述ルール
---

# Commit Message Conventions

## 言語

**コミットメッセージは日本語で書くこと**（タイトル・本文ともに）。

英語混じりの技術用語（API名・クラス名・ファイルパス等）はそのままで可。Conventional Commits の type プレフィックス（`feat`, `fix`, `docs`, `chore`, `refactor`, `test` 等）は英語のまま使う。

## フォーマット

```
<type>(<scope>): <一行サマリー（日本語）>

<本文：何を・なぜ変更したかを日本語で説明>

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
```

- **タイトル**: 50〜70字以内、句点なし
- **本文**: 1行空けてから記述。何を変えたかだけでなく、なぜ・どんな効果があるかを書く
- **Co-Authored-By**: Claude が生成・支援した場合は付ける

## type 一覧

| type | 用途 |
|---|---|
| `feat` | 新機能追加 |
| `fix` | バグ修正 |
| `docs` | ドキュメントのみ変更 |
| `chore` | ビルド・補助ツール・設定など |
| `refactor` | 機能変更を伴わないリファクタ |
| `test` | テスト追加・修正 |
| `style` | フォーマットのみ |

## scope

主要なコンポーネント名を入れる: `core`, `cli`, `scripts`, `skills`, `docs` 等。

## 例

```
feat(scripts): GrandChronicle 100年旅団変遷シミュレーターを追加

各ジョブ1名＋鉄壁騎士1名の計5名（20歳）で開始し、100年にわたる旅団の
変遷を追う。2年ごとに新人2名加入、毎年衰退期最高齢を除名、5年ごとに
上位9名で大隊編成して試練戦（敵10体・攻撃力30）を行う。

戦闘時のユニットステータスは年齢由来の growthFactor で速度・攻撃力を
スケールする。各戦闘で年・勝敗・ターン数・平均年齢・全盛期比率・MVP・
新人/退役の増減を出力し、最終的に通算戦績・最強期・歴代最多キルジョブ
を集計する。

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
```

```
fix(cli): simulate_brigade_battle.ts の旧 peakAge フィールドを修正

三段階モデル移行時の漏れ。peakStartAge / peakEndAge に置換し、
新 Unit インタフェースに整合させる。

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
```

## NG例

- タイトルが英語で書かれている → NG（type プレフィックスは除く）
- 「Update file」「Fix bug」のように内容が分からない → NG
- 本文に「何を」だけで「なぜ」が無い → NG
