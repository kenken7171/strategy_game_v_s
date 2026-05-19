import { describe, it, expect } from "bun:test";
import { Enemy, EnemyAction } from "../src/models/Enemy";
import { BattleManager, ActionResult } from "../src/BattleManager";
import { Squad } from "../src/models/Squad";
import { Unit } from "../src/models/Unit";
import { MAX_ENEMY_ACTION_LOOP } from "../src/config";

const makeUnit = (id: string, hp = 100) =>
  new Unit({
    id,
    name: `Unit-${id}`,
    age: 20,
    peakAge: 30,
    maxAge: 60,
    baseStats: { strength: 50, agility: 10, intelligence: 0, endurance: 0 },
    maxHp: hp,
    hp,
    speed: 10,
  });

const makeAction = (
  name: string,
  targetSlotIds: "RANDOM" | "ALL" | "NONE" | string[],
  damage: number,
  hitCount = 1,
  multiTargetMode?: "spread" | "random"
): EnemyAction => ({ name, targetSlotIds, damage, hitCount, multiTargetMode });

describe("Enemy.getActionForTurn", () => {
  it("3つの行動リストで4ターン目には1つ目の行動に戻る", () => {
    const actions: EnemyAction[] = [
      makeAction("Strike", ["FRONT"], 10),
      makeAction("Sweep", ["REAR-L"], 15),
      makeAction("Blast", ["REAR-R"], 20),
    ];
    const enemy = new Enemy({ hp: 100, maxHp: 100, speed: 5, actions });

    expect(enemy.getActionForTurn(0).name).toBe("Strike");
    expect(enemy.getActionForTurn(1).name).toBe("Sweep");
    expect(enemy.getActionForTurn(2).name).toBe("Blast");
    expect(enemy.getActionForTurn(3).name).toBe("Strike");
  });

  it("ターンインデックスが行動数の倍数でも正しくループする", () => {
    const actions: EnemyAction[] = [
      makeAction("A", ["FRONT"], 5),
      makeAction("B", ["REAR-L"], 5),
    ];
    const enemy = new Enemy({ hp: 100, maxHp: 100, speed: 5, actions });

    expect(enemy.getActionForTurn(0).name).toBe("A");
    expect(enemy.getActionForTurn(2).name).toBe("A");
    expect(enemy.getActionForTurn(6).name).toBe("A");
    expect(enemy.getActionForTurn(1).name).toBe("B");
    expect(enemy.getActionForTurn(5).name).toBe("B");
  });
});

describe("Enemy バリデーション", () => {
  it(`行動が ${MAX_ENEMY_ACTION_LOOP} 個以内なら正常に生成できる`, () => {
    const actions = Array.from({ length: MAX_ENEMY_ACTION_LOOP }, (_, i) =>
      makeAction(`Action${i}`, ["FRONT"], 5)
    );
    expect(() => new Enemy({ hp: 100, maxHp: 100, speed: 5, actions })).not.toThrow();
  });

  it("11個以上の行動を登録するとバリデーションエラーになる", () => {
    const actions = Array.from({ length: MAX_ENEMY_ACTION_LOOP + 1 }, (_, i) =>
      makeAction(`Action${i}`, ["FRONT"], 5)
    );
    expect(() => new Enemy({ hp: 100, maxHp: 100, speed: 5, actions })).toThrow();
  });
});

describe("BattleManager ダメージ処理", () => {
  it("範囲攻撃（FRONT と REAR-L）が指定された際、両方の分隊の HP が減少する", () => {
    const frontSquad = new Squad("FRONT", [makeUnit("f1"), makeUnit("f2")]);
    const rearLSquad = new Squad("REAR-L", [makeUnit("r1")]);
    const rearRSquad = new Squad("REAR-R", [makeUnit("r2")]);

    const enemy = new Enemy({
      hp: 200,
      maxHp: 200,
      speed: 3,
      // hitCount=2: rng=0→FRONT, rng=0.5→REAR-L で両方に確実に命中
      actions: [makeAction("WideSlash", ["FRONT", "REAR-L"], 20, 2)],
    });

    let call = 0;
    const rng = () => (call++ % 2 === 0 ? 0 : 0.5);
    const manager = new BattleManager([frontSquad, rearLSquad, rearRSquad], enemy, rng);
    manager.processTurn();

    expect(frontSquad.units.every((u) => u.hp < 100)).toBe(true);
    expect(rearLSquad.units[0].hp).toBeLessThan(100);
    expect(rearRSquad.units[0].hp).toBe(100);
  });

  it("対象外のスロットの分隊には HP 変化がない", () => {
    const frontSquad = new Squad("FRONT", [makeUnit("f1")]);
    const rearSquad = new Squad("REAR-R", [makeUnit("r1")]);

    const enemy = new Enemy({
      hp: 100,
      maxHp: 100,
      speed: 3,
      actions: [makeAction("Focus", ["FRONT"], 30)],
    });

    const manager = new BattleManager([frontSquad, rearSquad], enemy);
    manager.processTurn();

    expect(frontSquad.units[0].hp).toBeLessThan(100);
    expect(rearSquad.units[0].hp).toBe(100);
  });
});

describe("BattleManager 攻撃予報", () => {
  it("getAttackForecast が次のターンの行動名と対象スロットを返す", () => {
    const squad = new Squad("FRONT", [makeUnit("u1")]);
    const actions: EnemyAction[] = [
      makeAction("Slash", ["FRONT"], 10),
      makeAction("Roar", ["REAR-L"], 5),
    ];
    const enemy = new Enemy({ hp: 100, maxHp: 100, speed: 5, actions });
    const manager = new BattleManager([squad], enemy);

    const forecast = manager.getAttackForecast();
    expect(forecast.actionName).toBe("Slash");
    expect(forecast.targetSlotIds).toEqual(["FRONT"]);

    manager.processTurn();

    const next = manager.getAttackForecast();
    expect(next.actionName).toBe("Roar");
    expect(next.targetSlotIds).toEqual(["REAR-L"]);
  });

  it("RANDOM ターゲットのアクション予報は 'RANDOM' を返す", () => {
    const squad = new Squad("FRONT", [makeUnit("u1")]);
    const enemy = new Enemy({
      hp: 100, maxHp: 100, speed: 5,
      actions: [makeAction("Scatter", "RANDOM", 10, 3)],
    });
    const manager = new BattleManager([squad], enemy);

    const forecast = manager.getAttackForecast();
    expect(forecast.actionName).toBe("Scatter");
    expect(forecast.targetSlotIds).toBe("RANDOM");
  });
});

describe("BattleManager hitCount と ActionResult", () => {
  it("hitCount=4 で rng=0 のとき常に最初の候補に集中し ActionResult が正確", () => {
    const front = new Squad("FRONT", [makeUnit("f1")]);
    const rearL = new Squad("REAR-L", [makeUnit("r1")]);

    const enemy = new Enemy({
      hp: 100, maxHp: 100, speed: 3,
      actions: [makeAction("Barrage", ["FRONT", "REAR-L"], 10, 4)],
    });

    // rng=0 → Math.floor(0 * 2) = 0 → 常に FRONT
    const manager = new BattleManager([front, rearL], enemy, () => 0);
    const result: ActionResult = manager.applyEnemyAction(enemy.actions[0]);

    expect(result.perSlot["FRONT"].hits).toBe(4);
    expect(result.perSlot["REAR-L"].hits).toBe(0);
    expect(result.perSlot["FRONT"].damageTaken).toBe(40);
    expect(result.perSlot["REAR-L"].damageTaken).toBe(0);
    expect(front.units[0].hp).toBe(60);
  });

  it("RANDOM ターゲットで rng=0.999 のとき常に最後の候補（REAR-R）に命中", () => {
    const squads = ["FRONT", "REAR-L", "REAR-R"].map(
      (id) => new Squad(id, [makeUnit(id)])
    );

    const enemy = new Enemy({
      hp: 100, maxHp: 100, speed: 3,
      actions: [makeAction("Scatter", "RANDOM", 15, 3)],
    });

    // rng=0.999 → Math.floor(0.999 * 3) = 2 → REAR-R
    const manager = new BattleManager(squads, enemy, () => 0.999);
    const result = manager.applyEnemyAction(enemy.actions[0]);

    expect(result.perSlot["REAR-R"].hits).toBe(3);
    expect(result.perSlot["FRONT"].hits).toBe(0);
    expect(result.perSlot["REAR-L"].hits).toBe(0);
  });

  it("集中攻撃で分隊が壊滅すると defeated=true かつ damageTaken が HP 上限でキャップされる", () => {
    const squad = new Squad("FRONT", [makeUnit("f1", 80)]);

    const enemy = new Enemy({
      hp: 100, maxHp: 100, speed: 3,
      actions: [makeAction("Devastate", ["FRONT"], 15, 6)],
    });

    const manager = new BattleManager([squad], enemy);
    const result = manager.applyEnemyAction(enemy.actions[0]);

    expect(result.perSlot["FRONT"].hits).toBe(6);
    expect(result.perSlot["FRONT"].defeated).toBe(true);
    expect(result.perSlot["FRONT"].damageTaken).toBe(80); // HP は 0 で止まる
  });

  it("processTurn が ActionResult を返す", () => {
    const front = new Squad("FRONT", [makeUnit("u1")]);
    const enemy = new Enemy({
      hp: 100, maxHp: 100, speed: 3,
      actions: [makeAction("Strike", ["FRONT"], 20)],
    });
    const manager = new BattleManager([front], enemy);

    const result = manager.processTurn();

    expect(result.perSlot["FRONT"]).toBeDefined();
    expect(result.perSlot["FRONT"].hits).toBe(1);
    expect(result.perSlot["FRONT"].damageTaken).toBe(20);
    expect(manager.turn).toBe(1);
  });

  it("ALL + spread で全スロットが同量ダメージを受ける", () => {
    const front = new Squad("FRONT", [makeUnit("f1")]);
    const rearL = new Squad("REAR-L", [makeUnit("r1")]);
    const rearR = new Squad("REAR-R", [makeUnit("r2")]);

    const enemy = new Enemy({
      hp: 100, maxHp: 100, speed: 3,
      actions: [makeAction("Quake", "ALL", 30, 1)], // ALL defaults to spread
    });

    const manager = new BattleManager([front, rearL, rearR], enemy);
    manager.processTurn();

    expect(front.units[0].hp).toBe(70);
    expect(rearL.units[0].hp).toBe(70);
    expect(rearR.units[0].hp).toBe(70);
  });

  it("NONE ターゲットでは全スロットの HP が変化しない", () => {
    const front = new Squad("FRONT", [makeUnit("f1")]);
    const rearL = new Squad("REAR-L", [makeUnit("r1")]);

    const enemy = new Enemy({
      hp: 100, maxHp: 100, speed: 3,
      actions: [makeAction("Charge", "NONE", 0, 0)],
    });

    const manager = new BattleManager([front, rearL], enemy);
    const result = manager.processTurn();

    expect(front.units[0].hp).toBe(100);
    expect(rearL.units[0].hp).toBe(100);
    expect(Object.keys(result.perSlot)).toHaveLength(0);
  });

  it("spread モードで string[] の全ターゲットにダメージが入る", () => {
    const rearL = new Squad("REAR-L", [makeUnit("r1")]);
    const rearR = new Squad("REAR-R", [makeUnit("r2")]);
    const front = new Squad("FRONT", [makeUnit("f1")]);

    const enemy = new Enemy({
      hp: 100, maxHp: 100, speed: 3,
      actions: [makeAction("BackStrike", ["REAR-L", "REAR-R"], 40, 1, "spread")],
    });

    const manager = new BattleManager([front, rearL, rearR], enemy);
    manager.processTurn();

    expect(front.units[0].hp).toBe(100);   // 対象外
    expect(rearL.units[0].hp).toBe(60);    // 100 - 40
    expect(rearR.units[0].hp).toBe(60);    // 100 - 40
  });
});
