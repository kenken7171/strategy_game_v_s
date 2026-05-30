# Chronicle Knights — 開発タスクリスト

> 本リストは [instructions.md](instructions.md) で定義されたゲーム方針に基づく実装作業の一覧。
> チェックボックス完了時は PR/コミットで本ファイルも更新すること。
> 仕様の根拠が必要なら必ず [instructions.md](instructions.md) を参照する。

---

## 🎯 マイルストーン M1: 仕様の反映（フロント実装着手前）

`instructions.md` の絶対ルールをコードベースに反映する。フロントエンド実装着手の **前提条件**。

### バックエンド TODO

#### B-1. 大隊サイズを 12 → 9（3×3）へ統一

- [ ] `packages/core/src/config/ChronicleConfig.extreme.ts`
  - `BATTLE.SQUAD_SIZE`: 4 → **3**
  - `BATTLE.FRONT_ROW_COUNT`: 4 → **3**
  - `SCHEDULE.BATTALION_SIZE`: 12 → **9**
- [ ] `packages/core/src/config/ChronicleConfig.ts`
  - 既に 3/3/9 になっていることを再確認
- [ ] `scripts/meta-analyze-guild.ts`
  - `formBattalion` のスライス計算 (`rear.slice(0, squadMax)` 等) が 3 で正しく動くか確認
- [ ] 動作確認: `bun scripts/meta-analyze-guild.ts` でエラーなく10連実行できる
- [ ] テスト: `bun test packages/core/test/` 41件 PASS

#### B-2. 敵スピード成長率の引き下げ（1.5 → 0.6）

- [ ] `packages/core/src/config/ChronicleConfig.extreme.ts`
  - `ENEMY_SCALING.SPEED_GAIN_PER_YEAR`: 1.5 → **0.6**
- [ ] 動作確認: Y100 で敵 SPD=160 と算出される
- [ ] 効果検証: メタ分析で勝率が 21% → **40〜60%** 程度に回復することを確認
- [ ] 結果レポート保存（`reports/_speed-detune_*.md`）

#### B-3. 敵生成のランダムバリエーション導入

- [ ] `scripts/run-grand-chronicle.ts` の `makeTrialEnemy(year)` を `makeTrialEnemy(year, rng)` に
  - 各個体の HP/ATK/SPD を `BASE × (0.85 + rng() × 0.30)` で **±15% 振れ幅**
  - 10体それぞれ独立にロール（同戦闘内でも個体差を出す）
- [ ] `scripts/meta-analyze-guild.ts` 同様の修正
- [ ] 効果検証: 同一シードで複数回ロールするとバトル結果に揺らぎが出るか
- [ ] レポートでは「乱数化前後の勝率分散」を比較

#### B-4. 自動リストラの凍結 + 手動人事インターフェースの用意

- [ ] `packages/core/src/utils/brigade.ts` の `enforceMaxBrigadeSize` を**自動運用しない**:
  - `scripts/run-grand-chronicle.ts` から呼び出し削除
  - メタ分析スクリプト（測定用）はオプション `--auto-cull` で残す
- [ ] 新規 API: `packages/core/src/services/HumanDecisionService.ts`（仮）
  - `getPendingDecisions(brigade, options?)`: `{ recruits, retirees, heirs, overflowCandidates }` を返す
  - `applyDecisions(brigade, decisions)`: 採用/解雇/継承承認を一括適用、新 Brigade を返す
- [ ] Type 定義: `PendingDecision`, `DecisionResult`
- [ ] 単体テスト: 各 API が期待通り動く
- [ ] イミュータビリティ維持: Brigade は新インスタンスを返す

#### B-5. ゲームループ用 API の整備（フロント連携）

- [ ] `packages/core/src/services/GameLoop.ts`（仮）
  - `startNewGame(seed)`: 初期ステート返却
  - `stepYear(state, decisions?)`: 1年進める。`decisions` 未指定なら年初の判断待ち
  - `runBattle(state, battalion)`: 戦闘実行、結果と更新後 state を返す
  - `serialize(state)` / `deserialize(json)`: セーブデータ用
- [ ] イベントタイプの整理: `RecruitOffered`, `MarriageProposal`, `RetirementSuggestion`, `BattleResult`
- [ ] フロントから安全に呼べる**純粋関数 API**として設計（DOM 非依存）

#### B-6. ドキュメント・スキル更新

- [ ] `docs/system_architecture.md` に「人事フェーズ」セクション追加
- [ ] 新スキル `.claude/skills/game_constitution.md` を作成（本 instructions.md の要点をスキル化）
- [ ] `docs/job_definitions.md` の推奨配置を「9名（3×3）」前提に書き直し

---

## 🎨 マイルストーン M2: フロントエンド実装（ローグライク画面）

ローグライク UI として、毎年プレイヤーが判断を下す画面群を実装する。
フロントエンドのフレームワーク・スタックは別途決定（候補: React + Vite, Bun + HTMX 等）。

### フロントエンド TODO

#### F-1. ギルド人事画面 — 「採用」「引退勧告」を委ねる場

- [ ] 志願者リスト UI（名前 / 文化圏 / ジョブ / 推定ステータス）
- [ ] 採用ボタン / 不採用ボタン
- [ ] 引退候補者リスト（衰退期ユニットを赤系で強調）
- [ ] 「血統DNAを持つ者」を別カラム or バッジで強調（親情報・子孫情報）
- [ ] 定員管理 UI: 旅団残り枠 vs 採用希望者の数
- [ ] 苦渋の決断を演出する確認モーダル（「本当にこの英雄を解雇しますか？」）

#### F-2. 戦術編成画面 — 3×3 のドラッグ&ドロップ

- [ ] 旅団メンバー一覧（左サイドバー）
- [ ] **3×3 マスのグリッド**（FRONT / REAR-L / REAR-R × 3スロット）
- [ ] ドラッグ&ドロップでマスへ配置
- [ ] **同列・同分隊での好感度ハート演出**:
  - 男女ペアの好感度が閾値近くで ❤️
  - 結婚済みカップルは ❤️‍🔥
  - 親子・兄弟ペアは家紋アイコン
- [ ] 推定ステータス（HP合計・速度合計・職分布）の即時表示
- [ ] 出撃ボタン（編成確定）

#### F-3. 年次クロニクル・タイムライン — 歴史のログ

- [ ] 100年タイムラインの横スクロール UI
- [ ] 年ごとのイベント表示（結婚 💍 / 出産 👶 / 戦闘 ⚔️ / 引退 🕊️ / 入団 ✨）
- [ ] **家系図ビュー**（クリックで先祖・子孫をたどれる）
- [ ] 戦闘結果のサマリー（勝敗 / MVPジョブ / ターン数）
- [ ] 名前枯渇時の称号付き「英雄」を金色強調で表示
- [ ] エクスポート機能（JSON / Markdown チャレンジレポート）

#### F-4. 敵ステータス予測・出撃確認画面 — 「次の試練」を覚悟する場

- [ ] 敵スケーリング情報の事前表示（今年の敵想定 HP/ATK/SPD 範囲）
- [ ] **「乱数による振れ幅」を可視化**（HP: 460〜540 のような範囲表記）
- [ ] 大隊の予想初速 vs 敵速度の比較バー
- [ ] 「先制を取れる/取れない」の予測アイコン
- [ ] 敗北リスクの言語化（「7割で前衛壊滅」等）
- [ ] 「撤退」オプション（戦闘を回避して家系継承に専念）

---

## 🛠️ マイルストーン M3: 追加コンテンツ・ポリッシュ

M1/M2 完了後の改善・拡張。

### バックエンド拡張

- [ ] 称号システムを 100年プレイで稼働させる（プール削減 or 長期化）
- [ ] 新ジョブ追加: hero（血統限定の上位職）/ champion 等
- [ ] 戦闘イベント: 罠 / 増援 / 天候による速度補正
- [ ] 装備システム（武器・防具で stats 補正）
- [ ] セーブ/ロード（SQLite or LocalStorage）

### フロント拡張

- [ ] BGM/SE（V&B風オーケストラル）
- [ ] アニメーション（戦闘・継承・引退の演出）
- [ ] チュートリアルモード
- [ ] 多言語対応（日本語/英語）

---

## ✅ 完了済み（参考）

これらは過去マイルストーンで完了。詳細は [docs/system_architecture.md](docs/system_architecture.md) と reports/ 参照。

- [x] コア戦闘エンジン（Unit / Brigade / BattleManager / BattleSimulator）
- [x] 8ジョブ実装（iron_wall_knight / tactician / medic / sniper / sorcerer / standard_bearer / heavy_infantry / scout）
- [x] 三段階経年変化モデル（修業期 / 全盛期 / 衰退期）
- [x] 個体差システム（rollPeakAges ±3）
- [x] 血統継承（gender / affinity / marriage / birth / 15歳入団）
- [x] 多文化命名（910名 × 3 Origin + 称号フォールバック）
- [x] CHRONICLE_CONFIG 統合（5+2 セクション、`as const` 不変）
- [x] マルチConfig（default + extreme、`--config` 切替）
- [x] 敵スケーリング（ENEMY_SCALING、HP/ATK/SPD 年率上昇）
- [x] 10連メタ分析スクリプト（grand-chronicle / guild）
- [x] 検証スクリプト群（verify-bloodline / verify-naming / verify-individuality）

---

## 進行管理

- 各タスクのオーナーが決まったら `[ ]` の直後に `@担当者名` を追記
- 完了時は `[x]` にチェック + コミット
- ブロッカーは別途 GitHub Issues で管理
