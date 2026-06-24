# パラメータ一覧（ステータス関連 SoT 全集）

> Chronicle Knights（`generated_csharp/`）の**戦闘・成長・経済・敵・婚姻などステータスに関わる数値**を
> 1 枚に集約した参照ドキュメント。値はすべて Core の純粋層に名前付き定数／不変テーブルとして外出しされている
> （開発憲法: 数値はハードコードせず Core の SoT を参照）。本書はその実値を写したもの。
>
> 各節の見出しに **SoT ファイル** を併記。数値を変えるときは必ずそのファイルの定数／テーブルを編集する
> （UI・テスト・他ロジックは全て参照側）。最終確認: 実コード読取（2026-06-22）。

---

## 0. 実効戦闘ステの合成式（全体像）

```
実効ステ = ( 素のジョブ値[JobMaster] + 血統継承ボーナス[Unit.InheritedBonus] )
           × レベル係数[UnitStatProfile]
           × 加齢係数[UnitStatProfile]
         + 装備ボーナス( 基礎値×Lvスケール + Affix )[Equipment/AffixMaster]
```

- 解決器: `Core/Unit/UnitStatProfile.cs`（`EffectiveStats` / `Resolve`）。`BattleManager`・`BattleResolver` は
  必ず本解決器経由でステを読む（素の `JobStats` を直読しない）。
- 0 値の項（多くの BDEF/BUF/HEAL）は係数を掛けても 0 のまま（保護）。正値は最低 1 を保証。

---

## 1. ユニット成長（レベル＋三段階加齢） — `Core/Unit/UnitStatProfile.cs`

| 定数 | 値 | 意味 |
|---|---:|---|
| `MaturityAge` | 25 | 全盛期に到達する年齢（未満は修業期で線形成長） |
| `DeclineAge` | 45 | 衰退期に入る年齢（超で年あたり減衰） |
| `DeclineRetentionPerYear` | 0.88 | 衰退期の年あたり残存率＝**年 12% 減**（旧 0.97/3% から強化） |
| `LevelGrowthPerLevel` | 0.25 | レベル 1 段あたりの上昇率（+25%/Lv） |

- **レベル係数** `LevelMultiplier(lv)` = 1.0 + (clamp(lv,1,3)−1)×0.25 → Lv1=1.0 / Lv2=1.25 / Lv3=1.5
- **加齢係数** `AgeFactor(age)`:
  - age≤0 → 0.0／age<25 → age/25（修業期）／25≤age≤45 → 1.0（全盛期）／age>45 → 0.88^(age−45)（衰退期）
  - 参考カーブ（衰退）: age50≈0.53 / age55≈0.28 / age60≈0.15 / age65≈0.08

## 2. ユニット／レベル — `Core/Unit/Unit.cs`

| 定数 | 値 | 意味 |
|---|---:|---|
| `MaxUnitLevel` | 3 | レベル上限（超過は overflow） |
| `InitialLevel` | 1 | 加入直後のレベル |
| `RetirementEligibleLevel` | 3 | 明示引退が可能になる最小レベル |

---

## 3. ジョブ素ステ（数値 SoT） — `Core/Job/JobMaster.cs`

> 各ジョブの `JobStats`（Lv/加齢/装備を掛ける前の素値）。FA=前列攻撃 RA=後列攻撃 BDEF=大隊総守護力
> SDEF=分隊守護力 BUF=突撃号令 HEAL=ターン末分隊治癒。

| ジョブ | MaxHp | SPD | FA | RA | BDEF | SDEF | BUF | HEAL | RoleBonus | 特殊 |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---|
| IronWallKnight 鉄壁騎士 | 250 | 10 | 50 | 10 | 10 | 15 | 0 | 0 | 30 | — |
| HeavyInfantry 重装歩兵 | 300 | 15 | 70 | 20 | 0 | 10 | 0 | 0 | 0 | — |
| StandardBearer 旗手 | 150 | 20 | 30 | 30 | 0 | 5 | 40 | 0 | 65 | — |
| Tactician 戦術官 | 120 | 35 | 20 | 20 | 0 | 0 | 20 | 0 | 65 | — |
| Medic 衛生兵 | 100 | 25 | 10 | 10 | 0 | 0 | 0 | 30 | 90 | — |
| Sniper 狙撃兵 | 80 | 40 | 20 | 90 | 0 | 0 | 0 | 0 | 0 | ConsecutiveStrike |
| Sorcerer 呪術師 | 40 | 15 | 10 | 120 | 0 | 0 | 0 | 0 | 0 | — |
| Scout 斥候 | 90 | 60 | 40 | 40 | 0 | 0 | 0 | 0 | 30 | — |

- **TargetRating（UI 比較用総合値）** = `floor(MaxHp/5 + max(FA,RA) + SPD) + RoleBonus`（`CalculateTargetRating`）。
  除数は `JobMaster.HpRatingDivisor`=5.0（UI 比較用 Rating のみ・戦闘非関与）。

## 4. 血統継承ボーナス — `Core/Managers/MarriageService.cs`

| 定数 | 値 | 意味 |
|---|---:|---|
| `InheritedBonusShare` | 0.5 | 子が受ける「両親の高ステ差分」の割合（＝50%） |
| `NaturalMarriageThreshold` | 150 | 自然婚姻（コスト0）成立の双方向好感度しきい値 |
| `CostDivisor` | 20 | 婚姻コスト算出の除数 |

- **継承ボーナス** `CalculateInheritedBonus`: 各ステで `max(0, max(父,母値) − 子の継承ジョブ値) × 0.5`（四捨五入・全項目0ならnull）。
  各ジョブの最高値が天井で暴走しない。`Unit.InheritedBonus` に格納し `UnitStatProfile` が素値へ合算（Lv×加齢の前）。
- **婚姻コスト** `CalculateMarriageCost` = `ceil((父Rating×mult + 母Rating×mult) / 20)`（mult=`MarriageCostMultiplier` 既定1.0）。

---

## 5. 装備 — `Core/Unit/Equipment.cs` ／ 兵器廠 `Core/Shop/ShopService.cs`

| 定数 | 値 | 意味 |
|---|---:|---|
| `Equipment.MinEquipmentLevel` | 1 | 装備レベル下限 |
| `Equipment.MaxEquipmentLevel` | 5 | 装備レベル上限 |
| `LevelMultipliers` | {1.2, 1.3, 1.4, 1.5} | Lv3〜5 の累積乗算係数 |
| `ShopService.BuyCost` | 5 | 購入コスト（固定） |
| `ShopService.BaseUpgradeCost` | 2 | 強化コスト基数 |

- **5 大アイテム基礎ステ**（`BaseStatsRegistry`・Lv1 素値）:

| ItemId | AttackPower | SquadDefense | InitiativeBuff | BaseAffinityMultiplier | 特殊 |
|---|---:|---:|---:|---:|---|
| SwordKnight 誓いの聖剣 | 3 | 1 | 0 | 1.0 | — |
| BowSniper 必中の魔弓 | 4 | 0 | 0 | 1.0 | — |
| StaffMage 賢者の破滅杖 | 2 | 0 | 1 | 1.0 | — |
| RingPurelove 絆の誓輪 | 1 | 0 | 0 | 1.5 | 自然婚姻P×1.5 |
| CoinGreed 強欲の古銭 | 1 | 0 | 0 | 1.0 | 強欲(LH時P強奪) |

- **レベルスケール** `ComputeScaledStat(base)`: Lv1=base ／ Lv2=base+1 ／ Lv3〜5=(base+1)×(先頭 Lv−1 個の乗算)。
- **強化コスト** `UpgradeCostFor(lv)` = `2 × lv`（Lv1→2 は 2pt、Lv4→5 は 8pt と逓増）。
- **自然婚姻P倍率** `AffinityMultiplier` = `(1.0 + Level × AffinityBonusPerLevel) × BaseAffinityMultiplier`（`AffinityBonusPerLevel`=0.1）。
- レベル段差の `+1` は `Equipment.FlatBonusAboveLevel1`=1（ComputeScaledStat で参照）。
- **装備→戦闘ステ**（`BattleManager`）: ATK = `floor(CurrentAttackPower) + AffixAttackBonus`。DEF/SPD も同様に Affix を合流。

## 6. Affix（接尾効果） — `Core/Unit/AffixMaster.cs`

| AffixKind | キー | 効果（フラット加算） |
|---|---|---|
| Sharp | affix-sharp | ATK +3 |
| Sturdy | affix-sturdy | DEF +2 |
| Swift | affix-swift | SPD +2 |

| 定数 | 値 | 意味 |
|---|---:|---|
| `MinAffixCount` | 0 | 付与下限 |
| `MaxAffixCount` | 2 | 付与上限 |

- **付与個数** `AffixCountForLevel(lv)` = Lv≤2 → 1 ／ Lv≥3 → 2（相異なる種別を決定論抽選）。

---

## 7. 敵スケーリング（★現行は EnemyScalingResolver） — `Core/Chronicle/EnemyScalingResolver.cs`

> 実戦の敵は `ChronicleGlobal.CreateCurrentYearEnemy` → `EnemyScalingResolver.ComposeBattleEnemy` で合成される。
> （`Core/Battle/EnemyScaler.cs` は個体差プリミティブ専任へ整理済。`ApplyJitter` と `HpAggregationFactor` を本リゾルバが再利用する。）

**年成長率（1 年あたりの素増分・整数）:**

| 定数 | 値 |
|---|---:|
| `HpGainPerYear` | 5 |
| `AttackGainPerYear` | 1 |
| `DefenseGainPerYear` | 1 |
| `SpeedGainPerYear` | 1 |
| `MinimumStatValue` | 1 |
| `PercentDenominator` | 100 |

**敵テンプレート（スケール前の素値）:**

| 原型 Archetype | 出現 | BaseHp | BaseAttack | BaseDefense | BaseSpeed |
|---|---|---:|---:|---:|---:|
| TrialGuardian 試練の門の守護者 | 通常戦（全時代） | 150 | 30 | 10 | 100 |
| DawnWarden 黎明の番人 | 25 年（黎明ボス） | 200 | 40 | 15 | 90 |
| UpheavalConqueror 激動の征服者 | 50 年（激動ボス） | 260 | 55 | 20 | 110 |
| DeclineTyrant 斜陽の暴君 | 75 年（斜陽ボス） | 320 | 70 | 28 | 120 |
| EternalSovereign 終焉の君主 | 100 年（終焉ボス） | 420 | 90 | 36 | 140 |

**合成式:**
```
grown      = Base + clamp(year,1,100) × gainPerYear
afterDiff  = grown × 章.DifficultyScalePercent / 100
era        = afterDiff × 章.EnvironmentModifierPercent / 100   (Max(1,..))
hp戦闘値    = ApplyJitter( era.Hp × HpAggregationFactor )
attack/spd = ApplyJitter( era.Attack / era.Speed )
```

**個体差ジッタ（`Core/Battle/EnemyScaler.cs`・個体差プリミティブ専任）:**

| 定数 | 値 | 意味 |
|---|---:|---|
| `JitterFloor` | 0.85 | ジッタ下限 |
| `JitterSpan` | 0.30 | ジッタ幅 → 係数 0.85〜1.15＝**±15%** |
| `HpAggregationFactor` | 6 | HP 集約係数（旧 10 体合算の名残・章ボス壁緩和で 10→6） |
| `MinimumStatValue` | 1 | 下限 |

## 8. 暦・章（時代スケールの源） — `Core/Chronicle/ChronicleTimelineConfig.cs`

| 定数 | 値 |
|---|---:|
| `FirstYear` / `TotalYears` | 1 / 100 |
| `YearsPerEpoch` | 25（章ボス出現年 = 25 / 50 / 75 / 100） |
| `EpochBossForewarnLeadTurns` | 3（前兆の先読みターン） |

**章ごとの難易度・環境補正（`Epochs`）:**

| 章 Epoch | 年範囲 | DifficultyScale% | Environment% | ボス原型 |
|---|---|---:|---:|---|
| Dawn 黎明 | 1–25 | 80 | 100 | DawnWarden |
| Upheaval 激動 | 26–50 | 105 | 110 | UpheavalConqueror |
| Decline 斜陽 | 51–75 | 120 | 110 | DeclineTyrant |
| Twilight 終焉 | 76–100 | 135 | 110 | EternalSovereign |

---

## 9. 経済・戦果・休息

| 定数 | 値 | SoT |
|---|---:|---|
| `YearlyMinimumIncomePerYear` | 1 | `Core/Managers/PointsEconomy.cs`（年次収入 = years×1） |
| `VictoryBaseReward` | 5 | `Core/Battle/BattleSpoils.cs`（勝利基礎・婚姻P） |
| `LevelGainBounty` | 2 | 同上（昇級 1 件あたり） |
| `EquipmentEvolutionBounty` | 1 | 同上（装備進化 1 件あたり） |
| `PermanentLossPenalty` | 3 | 同上（完全ロスト 1 名あたり減算） |
| `RestPointsReward` | 2 | `Core/GameFlow/RestOutcome.cs`（休息固定ボーナス） |

## 10. スカウト・予言・とどめ・敵意・編成・新規ゲーム

**スカウト** `Core/Managers/ScoutService.cs`: 初期年齢 16〜28（`ScoutMinInitialAge`/`ScoutMaxInitialAge`）、寿命 55〜75（`ScoutMinLifespan`/`ScoutMaxLifespan`）。

**予言** `Core/Timeline/ProphecyMaster.cs`:

| 定数 / テーブル | 値 |
|---|---|
| `GoldChance` / `SilverChance` | 0.06 / 0.22（Bronze ≈ 0.72） |
| `OptionsPerTurn`（`TimelineEngine`） | 3 |
| SkipYears（`TimelineEngine` 既定） | 2〜4 |
| RewardPoints Value（Bronze/Silver/Gold） | 3 / 7 / 14 |
| ScoutReward Value | 1 / 2 / 3 |
| Rest Value | 2 / 5 / 10 |
| EquipmentDrop Lv | Bronze 1〜2 / Silver 3 / Gold 4〜5 |

**とどめ（ラストヒット）** `Core/Managers/BattleManager.cs`: `Lv5NormalItemDestructionProbability`=0.5、`Lv5GreedItemDestructionProbability`=1.0、`Lv5GreedPointsStolen`=1、`MinimumDamageAfterReduction`=1。

**敵意（攻撃予告）** `Core/Battle/AttackIntentRoller.cs`: SingleStrike ×2.0 / Pincer ×1.4 / TotalAssault ×0.9、`MinimumDamagePerUnit`=1、`MaxForecastTurns`=16。

**編成** `Core/Formation/FormationBoard.cs`: `RowCount`=3 × `ColumnsPerRow`=3 = `SlotCount`=9。出撃下限 `DeploymentGate.MinimumDeployedToMarch`=1。

**新規ゲーム** `Core/Bootstrap/NewGameFactory.cs`: `InitialRosterSize`=9、`InitialPointsBalance`=20、`GuaranteedFrontLine`=2、`GuaranteedHealer`=1、`GuaranteedSupport`=1、`MaxSameJob`=2。

**その他**: `EquipmentDropService.DropCandidateCount`=3、`PedigreeGraph.MaxDescendantGeneration`=2、`SaveSerializer.CurrentSaveVersion`=8。

---

## 11. 外出し監査（externalization audit）

**結論: ステータス関連の数値は全て Core の名前付き定数／不変テーブルに外出し済み**（約 80 個の `const` ＋
`JobMaster.All` / `Equipment.BaseStatsRegistry` / `AffixMaster.All` / `EnemyScalingResolver` テンプレ /
`ChronicleTimelineConfig.Epochs` 等のテーブル）。UI・テスト・ロジックは全て参照側で、マジックナンバー散乱はない。

**整理済み（2026-06-22）:**

1. 旧・未命名のインライン係数 3 件を命名定数化:
   `JobMaster.HpRatingDivisor`=5.0 ／ `Equipment.AffinityBonusPerLevel`=0.1 ／ `Equipment.FlatBonusAboveLevel1`=1。
2. 敵スケーラの新旧二重化を解消: 現行の実戦敵は `Core/Chronicle/EnemyScalingResolver.cs`（テンプレ＋章補正）が生成し、
   `Core/Battle/EnemyScaler.cs` は個体差プリミティブ（`ApplyJitter` / `HpAggregationFactor` / `JitterFloor` /
   `JitterSpan` / `MinimumStatValue`）専任へ縮約。旧 `ScaleTrialGuardian` メソッドと未使用の素値/年成長/レベル定数
   （`BaseHp` / `BaseAttack` / `BaseSpeed` / `HpGainPerYear` / `AttackGainPerYear` / `SpeedGainPerYear` /
   `PerLevelGain` / `BaseLevel`）は撤去。テストは `EnemyScalerTests`（ApplyJitter 直接検証）へ差し替え。CLAUDE.md D-4 も是正。

これにより、本書の各節の値はすべて単一の SoT 定数／テーブルが起点で、コード内に重複・浮きの数値は無い。

> 数値を調整するときは本書の各節の SoT ファイルを直接編集すること。挙動の単体検証は `dotnet test`
> （`UnitStatProfileTests` / `InheritedBonusTests` / `EnemyScalingResolverTests` / `*ContractTests` 等）。
