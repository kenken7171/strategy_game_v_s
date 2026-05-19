import { describe, it, expect, beforeEach } from "bun:test";
import { Unit } from "../src/models/Unit";
import { Squad } from "../src/models/Squad";
import { Enemy } from "../src/models/Enemy";
import { BattleManager } from "../src/BattleManager";

// ---- ファクトリ ----

const makeSniper = (speed: number, rearAttack = 80) =>
  new Unit({
    id: "sniper_1",
    name: "狙撃兵",
    age: 25,
    peakAge: 30,
    maxAge: 60,
    baseStats: { strength: 60, agility: speed, intelligence: 50, endurance: 30 },
    maxHp: 100,
    hp: 100,
    speed,
    frontAttack: 20,
    rearAttack,
    job: "sniper",
  });

const makeTactician = (speed: number, ab = 0) =>
  new Unit({
    id: "tactician_1",
    name: "戦術官",
    age: 25,
    peakAge: 30,
    maxAge: 60,
    baseStats: { strength: 30, agility: speed, intelligence: 80, endurance: 50 },
    maxHp: 100,
    hp: 100,
    speed,
    frontAttack: 15,
    rearAttack: 15,
    job: "tactician",
    ab,
  });

const makeIronWall = (sdf: number, bdf: number) =>
  new Unit({
    id: "iron_wall_1",
    name: "鉄壁騎士",
    age: 25,
    peakAge: 30,
    maxAge: 60,
    baseStats: { strength: 80, agility: 10, intelligence: 10, endurance: 100 },
    maxHp: 300,
    hp: 300,
    speed: 10,
    frontAttack: 40,
    rearAttack: 10,
    job: "iron_wall_knight",
    sdf,
    bdf,
  });

const makeMedic = (hl: number) =>
  new Unit({
    id: "medic_1",
    name: "衛生兵",
    age: 25,
    peakAge: 30,
    maxAge: 60,
    baseStats: { strength: 20, agility: 30, intelligence: 90, endurance: 60 },
    maxHp: 100,
    hp: 80,
    speed: 30,
    frontAttack: 5,
    rearAttack: 5,
    job: "medic",
    hl,
  });

// 1ターン行動しない敵（NONE アクション）
const makeIdleEnemy = (speed: number) =>
  new Enemy({
    hp: 2000,
    maxHp: 2000,
    speed,
    actions: [{ name: "待機", targetSlotIds: "NONE", damage: 0, hitCount: 0 }],
  });

// ====================================================================
// 1. 戦術官バフ → 狙撃兵が1番手になり2回攻撃が発動するシナリオ
// ====================================================================

describe("【狙撃兵 × 戦術官】SPDバフによる2回攻撃発動テスト", () => {
  it("戦術官がいない場合：敵が先行し、狙撃兵は1番手でないため1回攻撃", () => {
    // 敵speed=25 > 狙撃兵speed=20 → 敵が先行 → 狙撃兵は1番手でない
    const sniperSquad = new Squad("REAR-L", [makeSniper(20)]);
    const enemy = makeIdleEnemy(25);
    const manager = new BattleManager([sniperSquad], enemy);

    const result = manager.processIntegratedTurn();

    // 行動順確認：enemy が先
    expect(result.initiativeOrder[0].type).toBe("enemy");
    expect(result.initiativeOrder[0].speed).toBe(25);

    const sniperResult = result.squadOffenseResults.find(
      (r) => r.squadId === "REAR-L"
    );
    expect(sniperResult?.attackLogs).toHaveLength(1);
    expect(sniperResult?.attackLogs[0].isDoubleAttack).toBe(false);
  });

  it("戦術官がいる場合：SPDバフで狙撃兵が1番手になり2回攻撃が発動する", () => {
    // 戦術官 speed=30 → 狙撃兵に+30 SPD付与 → 狙撃兵 finalSpeed=50
    // 行動順: 狙撃兵(50) → 戦術官(30) → 敵(25)
    const sniper = makeSniper(20, 80);
    const tactician = makeTactician(30, 0);

    const sniperSquad = new Squad("REAR-L", [sniper]);
    const tacticianSquad = new Squad("REAR-R", [tactician]);
    const enemy = makeIdleEnemy(25);

    const manager = new BattleManager([sniperSquad, tacticianSquad], enemy);
    const result = manager.processIntegratedTurn();

    // 行動順：REAR-L が先頭（speed=50 に昇格）
    expect(result.initiativeOrder[0].type).toBe("squad");
    expect(result.initiativeOrder[0].id).toBe("REAR-L");
    expect(result.initiativeOrder[0].speed).toBe(50);

    // 狙撃兵が2回攻撃
    const sniperResult = result.squadOffenseResults.find(
      (r) => r.squadId === "REAR-L"
    );
    expect(sniperResult?.attackLogs).toHaveLength(2);
    expect(sniperResult?.attackLogs[0].isDoubleAttack).toBe(false);
    expect(sniperResult?.attackLogs[1].isDoubleAttack).toBe(true);

    // 合計ダメージ = rearAttack(80) × 2 = 160
    expect(sniperResult?.totalDamage).toBe(160);
  });

  it("戦術官のABバフがFA/RAに加算されfinalAttackに反映される", () => {
    // 戦術官 ab=20 → 狙撃兵の rearAttack(80) + 20 = finalRearAttack(100)
    const sniper = makeSniper(20, 80);
    const tactician = makeTactician(30, 20);

    const sniperSquad = new Squad("REAR-L", [sniper]);
    const tacticianSquad = new Squad("REAR-R", [tactician]);
    const enemy = makeIdleEnemy(25);

    const manager = new BattleManager([sniperSquad, tacticianSquad], enemy);
    const result = manager.processIntegratedTurn();

    const sniperResult = result.squadOffenseResults.find(
      (r) => r.squadId === "REAR-L"
    );

    // 2回攻撃、各 finalRearAttack = 80 + 20 = 100
    expect(sniperResult?.attackLogs).toHaveLength(2);
    expect(sniperResult?.attackLogs[0].attackPower).toBe(100);
    expect(sniperResult?.totalDamage).toBe(200);
  });
});

// ====================================================================
// 2. 鉄壁騎士のSDF/BDF軽減テスト
// ====================================================================

describe("【鉄壁騎士】ダメージ軽減テスト", () => {
  it("鉄壁騎士がFRONTにいる場合、BDFが大隊全体のダメージを軽減する", () => {
    const ironWall = makeIronWall(0, 15); // BDF=15
    const frontSquad = new Squad("FRONT", [ironWall]);
    const rearSquad = new Squad("REAR-L", [
      new Unit({
        id: "u1",
        name: "兵士",
        age: 25,
        peakAge: 30,
        maxAge: 60,
        baseStats: { strength: 50, agility: 20, intelligence: 0, endurance: 50 },
        maxHp: 100,
        hp: 100,
        speed: 20,
        frontAttack: 30,
        rearAttack: 30,
      }),
    ]);

    const enemy = new Enemy({
      hp: 500,
      maxHp: 500,
      speed: 5,
      actions: [
        { name: "全体攻撃", targetSlotIds: "ALL", damage: 50, hitCount: 1 },
      ],
    });

    const manager = new BattleManager([frontSquad, rearSquad], enemy);
    manager.processTurn();

    // BDF=15 軽減 → 実ダメージ = Math.max(1, 50 - 15) = 35
    expect(frontSquad.units[0].hp).toBe(300 - 35); // 265
    expect(rearSquad.units[0].hp).toBe(100 - 35);  // 65
  });

  it("鉄壁騎士が所属分隊への攻撃に対してSDFで軽減する", () => {
    const ironWall = makeIronWall(20, 0); // SDF=20
    const targetSquad = new Squad("FRONT", [ironWall]);

    const enemy = new Enemy({
      hp: 500,
      maxHp: 500,
      speed: 5,
      actions: [
        { name: "前衛攻撃", targetSlotIds: ["FRONT"], damage: 50, hitCount: 1 },
      ],
    });

    const manager = new BattleManager([targetSquad], enemy);
    manager.processTurn();

    // SDF=20 軽減 → 実ダメージ = Math.max(1, 50 - 20) = 30
    expect(targetSquad.units[0].hp).toBe(300 - 30); // 270
  });
});

// ====================================================================
// 3. 衛生兵の回復テスト
// ====================================================================

describe("【衛生兵】ターン終了時回復テスト", () => {
  it("ターン終了時に分隊の全生存ユニットがHL分回復する", () => {
    const medic = makeMedic(25); // hp=80, hl=25
    const ally = new Unit({
      id: "ally_1",
      name: "兵士",
      age: 25,
      peakAge: 30,
      maxAge: 60,
      baseStats: { strength: 50, agility: 20, intelligence: 0, endurance: 50 },
      maxHp: 100,
      hp: 60,
      speed: 20,
      frontAttack: 30,
      rearAttack: 30,
    });

    const squad = new Squad("REAR-L", [medic, ally]);
    const enemy = makeIdleEnemy(10);
    const manager = new BattleManager([squad], enemy);

    manager.processIntegratedTurn();

    // 衛生兵 hp=80 → min(100, 80+25) = 100
    // 兵士 hp=60 → min(100, 60+25) = 85
    expect(squad.units[0].hp).toBe(100);
    expect(squad.units[1].hp).toBe(85);
  });

  it("HPが最大値を超えて回復しない", () => {
    const medic = makeMedic(50); // hp=80, hl=50
    const squad = new Squad("REAR-L", [medic]);
    const enemy = makeIdleEnemy(10);
    const manager = new BattleManager([squad], enemy);

    manager.processIntegratedTurn();

    // 80 + 50 = 130 だが maxHp=100 でキャップ
    expect(squad.units[0].hp).toBe(100);
  });
});

// ====================================================================
// 4. Unit.finalSpeed / finalAttack のユニットテスト
// ====================================================================

describe("Unit バフ適用テスト", () => {
  it("withBuffs でspeedBuff/attackBuffが加算されfinalSpeed/finalAttackに反映される", () => {
    const unit = new Unit({
      id: "u1",
      name: "テスト",
      age: 25,
      peakAge: 30,
      maxAge: 60,
      baseStats: { strength: 50, agility: 30, intelligence: 0, endurance: 50 },
      speed: 30,
      frontAttack: 50,
      rearAttack: 70,
    });

    expect(unit.finalSpeed).toBe(30);
    expect(unit.finalFrontAttack).toBe(50);
    expect(unit.finalRearAttack).toBe(70);

    const buffed = unit.withBuffs(20, 15);
    expect(buffed.finalSpeed).toBe(50);
    expect(buffed.finalFrontAttack).toBe(65);
    expect(buffed.finalRearAttack).toBe(85);
  });

  it("resetBuffs でspeedBuff/attackBuffが0にリセットされる", () => {
    const buffed = new Unit({
      id: "u1",
      name: "テスト",
      age: 25,
      peakAge: 30,
      maxAge: 60,
      baseStats: { strength: 50, agility: 30, intelligence: 0, endurance: 50 },
      speed: 30,
      frontAttack: 50,
      rearAttack: 70,
      speedBuff: 25,
      attackBuff: 10,
    });

    expect(buffed.finalSpeed).toBe(55);

    const reset = buffed.resetBuffs();
    expect(reset.finalSpeed).toBe(30);
    expect(reset.finalFrontAttack).toBe(50);
  });

  it("戦術官が自身をバフしないことを確認する", () => {
    const tactician = makeTactician(30, 10);
    const squad = new Squad("FRONT", [tactician]);

    // applyBuff で自分自身(excludeUnitId)を除外
    squad.applyBuff(30, 10, tactician.id);

    // 戦術官自身の finalSpeed は変化しない
    expect(squad.units[0].finalSpeed).toBe(30);
    expect(squad.units[0].finalFrontAttack).toBe(15);
  });
});
