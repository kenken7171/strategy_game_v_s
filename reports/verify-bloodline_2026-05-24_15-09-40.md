# verify-bloodline 実行レポート

> 実行日時: 2026-05-24 15:09:40
> 実行コマンド: `bun scripts/verify-bloodline.ts`
> RNG seed: 7（戦闘）/ 106（advance）

## 実行条件

| 項目 | 値 |
|---|---|
| 主人公 | Arthur (Male, iron_wall_knight, baseStrength=120) / Elise (Female, medic, baseStrength=100) |
| 補助ユニット | filler1〜3（同分隊・後衛配置で同分隊カウントを稼ぐ要員） |
| 同分隊戦闘回数 | 10戦 |
| 結婚条件 | 互いの好感度 ≥ 100 |
| `marriageProb` | 1.0（決定的成立） |
| `birthProb` | 1.0（決定的予約） |
| `affinityPerBattle` | 10（仕様） |
| `affinityThreshold` | 100（仕様） |
| 子の `peakStartAge` | 25（advance デフォルト） |

## サマリー

- ✅ 全4項目 PASS
- 同分隊10戦で好感度が正確に **10→100** に上昇（線形）
- 1年経過 → marriage イベント発火、双方向 `spouseId` 設定
- さらに1年経過 → `birth_planned` イベント発火、`pendingBirths.length = 1`
- 15年経過（Year 18） → `birth` イベント発火、継承者 Unit 生成
- 継承者の `baseStats` = 両親平均、`stats` = `baseStats × 0.6`（=15/25）が完全一致

## 詳細結果

### Step 1: 同分隊10戦

| Battle | 結果 | ターン | Arthur→Elise 好感度 |
|---:|---|---:|---:|
| 1 | Allies | 1 | 10 |
| 2 | Allies | 1 | 20 |
| 3 | Allies | 1 | 30 |
| 4 | Allies | 1 | 40 |
| 5 | Allies | 1 | 50 |
| 6 | Allies | 1 | 60 |
| 7 | Allies | 1 | 70 |
| 8 | Allies | 1 | 80 |
| 9 | Allies | 1 | 90 |
| 10 | Allies | 1 | **100** |

- Elise→Arthur も対称的に 100。`applyBattleAffinity` が双方向更新を正しく行っている。

### Step 2: 結婚成立（Year 2）

```
イベント: marriage
  husband: Arthur (p-arthur)
  wife   : Elise  (p-elise)
  ✓ Arthur.spouseId = p-elise
  ✓ Elise.spouseId  = p-arthur
```

### Step 3: 出産予約（Year 3）

```
イベント: birth_planned
  fatherId       : p-arthur
  motherId       : p-elise
  birthYear      : 3
  plannedJoinYear: 18
  potentialStats : {strength:110, agility:60, intelligence:60, endurance:60}
  job (継承)     : medic    ← 母から継承
pendingBirths.length = 1
```

### Step 4: 15歳入団（Year 18）

```
イベント: birth
  name    : 継承者child-18-0
  id      : child-18-0
  age     : 15
  gender  : Female
  job     : medic
  parents : { fatherId: p-arthur, motherId: p-elise }
  baseStats: {strength:110, agility:60, intelligence:60, endurance:60}  ← = potentialStats
  growthFactor: 0.600
  stats   : {strength:66, agility:36, intelligence:36, endurance:36}
```

### Step 5: 継承計算検証

| 項目 | 期待値 | 実値 | 判定 |
|---|---|---|---|
| potentialStats (=baseStats) | (120+100)/2=110 ほか | strength:110, others:60 | ✓ |
| 実 stats (× 15/25 = 0.6) | strength:66, others:36 | strength:66, others:36 | ✓ |
| job 継承 | iron_wall_knight or medic | medic | ✓ |
| parents 記録 | fatherId=p-arthur, motherId=p-elise | 一致 | ✓ |

## 観察・考察

### 設計面で良かった点

1. **`baseStats = potentialStats` で stats ゲッターを再利用できる** — 仕様の「`potentialStats × (15 / peakStartAge)`」は三段階モデルの修業期式 `growthFactor = age / peakStartAge` に age=15 を代入したものと一致する。継承者の実ステータス算出に専用ロジックを書かず、既存の getter がそのまま機能する。

2. **イミュータビリティが破綻していない** — `withIncreasedAffinity` と `withSpouse` のヘルパーで新 Map・新 Unit を返す設計のため、複数年の advance チェーンでも整合性が崩れない。10戦 → 18年 advance まで一貫して動作。

3. **`applyBattleAffinity` を独立メソッドにしたのは正解** — `BattleSimulator.run()` 直後に呼べて、`advance({ battlePairs })` でもまとめて呼べる二段構え。検証スクリプトでは前者を採用しテンポよく好感度を積めた。

### 注意点・調整候補

1. **`marriageProb=0.3 / birthProb=0.2` のデフォルト** は仕様外の数値（仕様は「一定確率」とのみ）。実運用での結婚率・出生率は GrandChronicle 統合時に調整余地あり。

2. **継承者の `peakStartAge` 固定値（25）** は将来「親から継承」も検討可能。現状は `AdvanceOptions.childPeakStartAge` で外部から差し替え可能にしてある。

3. **`pendingBirths` がカップル単位で複数年連続予約される** — 結婚後ずっと毎年20%抽選なので、長寿カップルからは複数の継承者が生まれうる。これは仕様通り（家系継承）だが、同年内の同一カップルからは重複しないよう `handledCouples` Set でガードしている。

4. **絵文字（💍👶🎉）** はターミナル表示のためだけに使った。CIや非対話環境での実行は問題ない。

### 次の調整候補

- `GrandChronicle` に血統継承を統合し、100年間で何世代続くかを観察する
- 継承者の `peakStartAge` を親から継承する派生版を試す
- 同性ペアの好感度上昇は今も発生しているが結婚はしない（仕様通り）。将来「親友」関係として別効果を持たせる余地あり

## 再現方法

```bash
bun scripts/verify-bloodline.ts          # seed=7（既定）
bun scripts/verify-bloodline.ts 42       # seed=42 で再実行
```

確率は内部的に 1.0 固定（marriage/birth）なので、シードを変えても 100% 同じイベントが順序通り発生する。
