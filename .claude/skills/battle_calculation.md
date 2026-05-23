---
description: バトル計算の優先順位と各フェーズの詳細
---

# Battle Calculation Reference

## ターン処理の優先順位（processIntegratedTurn）

```
1. バフリセット         squad.resetBuffs() → speedBuff/attackBuff = 0
2. 戦術官バフ適用      applyTacticianBuffs() → 生存tactician が全体に ab を加算
3. イニシアチブ決定    buildInitiativeQueue() → finalSpeed 降順でソート
4. アクション実行      各エントリを順番に処理（enemy / squad）
   4a. 敵ターン        applyEnemyAction(action) → SDF/BDF 軽減後にダメージ
   4b. 味方ターン      processSquadOffense(squadId) → finalAttack でダメージ
5. 衛生兵回復          applyMedicHealing() → 生存medic が自分隊を hl 回復
```

## ダメージ計算の詳細

### 敵 → 味方

```
effectiveDamage = Math.max(1, baseDamage - BDF合計 - SDF合計)
  BDF合計 = FRONT スロットにいる生存 iron_wall_knight の bdf の総和
  SDF合計 = ターゲット分隊にいる生存 iron_wall_knight の sdf の総和
```

### 味方 → 敵

```
attackPower = FRONT スロット ? unit.finalFrontAttack : unit.finalRearAttack
finalFrontAttack = frontAttack + attackBuff
finalRearAttack  = rearAttack  + attackBuff
```

- 敵のHPはプール管理（`currentEnemyHp`）。0以下になった時点で戦闘終了。
- ダメージオーバーキルは `Math.min(attackPower, currentEnemyHp)` で実際与ダメを記録。

## イニシアチブ計算

```
Squad.averageSpeed = Σ(finalSpeed of alive units) / alive count
InitiativeEntry.speed:
  - enemy: enemy.speed
  - squad:  squad.averageSpeed
```

同速の場合は `sort` の安定性に依存（同点は enemy → ally の順を保証しない）。

## 狙撃兵 2連撃の発動条件

```typescript
const isFirstInInitiative = (i === 0);        // このsquadがイニシアチブ1番手
const isFirstUnit = (unitIdx === 0);           // squad内でfinalSpeed最大
const isSniper1st = unit.job === "sniper" && isFirstInInitiative && isFirstUnit;
const attackCount = isSniper1st ? 2 : 1;
```

**重要**: `isFirstUnit` はアクション実行前に `finalSpeed` でソートした配列インデックス 0 であること。

## finalSpeed の計算

```
finalSpeed = speed + speedBuff
```

`speedBuff` は戦術官が `applyTacticianBuffs` で加算したもの。  
毎ターン 1. でリセットされるため、イニシアチブ決定は **必ずバフ適用後** に行う（現在の実装もそう）。

## 経年変化とバトルステータスの関係

バトルで使用される `frontAttack`, `rearAttack`, `speed`, `maxHp` 等は Unit コンストラクタで固定される値。  
経年変化の影響を受けるのは `unit.stats`（`baseStats * growthFactor`）であり、旅団管理（`Brigade.advance`）や  
大隊選出（`Brigade.selectBattalion`）で参照されるが、バトル中の攻撃力には直接影響しない。  
将来的に `stats.strength` をバトルパラメータに連動させる場合は Unit 生成時に反映すること。
