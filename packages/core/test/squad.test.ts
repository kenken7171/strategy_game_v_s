import { describe, it, expect, beforeEach } from "bun:test";
import { Squad } from "../src/models/Squad";
import { Unit } from "../src/models/Unit";
import { Brigade } from "../src/models/Brigade";
import { MAX_UNITS_PER_SQUAD } from "../src/config";

const makeUnit = (id: string, speed: number, hp = 100, maxHp = 100) =>
  new Unit({
    id,
    name: `Unit-${id}`,
    age: 20,
    peakAge: 30,
    maxAge: 60,
    baseStats: { strength: 50, agility: speed, intelligence: 0, endurance: 0 },
    maxHp,
    hp,
    speed,
  });

describe("Config", () => {
  it("MAX_UNITS_PER_SQUAD は config から正しく読み込まれる", () => {
    expect(MAX_UNITS_PER_SQUAD).toBe(3);
  });
});

describe("Squad", () => {
  let unitA: Unit;
  let unitB: Unit;
  let unitC: Unit;

  beforeEach(() => {
    unitA = makeUnit("a", 10);
    unitB = makeUnit("b", 20);
    unitC = makeUnit("c", 30);
  });

  describe("averageSpeed", () => {
    it("ユニットなしのとき 0 を返す", () => {
      const squad = new Squad("s1");
      expect(squad.averageSpeed).toBe(0);
    });

    it("配属されたユニットの speed 平均を正しく計算する", () => {
      const squad = new Squad("s1", [unitA, unitB, unitC]);
      expect(squad.averageSpeed).toBe((10 + 20 + 30) / 3);
    });

    it("addUnit 後に averageSpeed が更新される", () => {
      const squad = new Squad("s1", [unitA]);
      squad.addUnit(unitB);
      expect(squad.averageSpeed).toBe((10 + 20) / 2);
    });
  });

  describe("最大人数バリデーション", () => {
    it("MAX_UNITS_PER_SQUAD を超える初期ユニットはエラー", () => {
      const extra = makeUnit("d", 5);
      expect(
        () => new Squad("s1", [unitA, unitB, unitC, extra])
      ).toThrow();
    });

    it("満員の分隊に addUnit するとエラー", () => {
      const squad = new Squad("s1", [unitA, unitB, unitC]);
      const extra = makeUnit("d", 5);
      expect(() => squad.addUnit(extra)).toThrow();
    });
  });

  describe("applyDamage", () => {
    it("全生存ユニットが同じダメージ値をそのまま受ける", () => {
      const squad = new Squad("s1", [unitA, unitB]);
      squad.applyDamage(40);
      const hps = squad.units.map((u) => u.hp);
      expect(hps).toEqual([60, 60]);
    });

    it("HP が 0 になったユニットにはそれ以上ダメージが入らない", () => {
      const dead = makeUnit("dead", 0, 0);
      const alive = makeUnit("alive", 10, 50);
      const squad = new Squad("s1", [dead, alive]);
      squad.applyDamage(20);
      expect(squad.units[0].hp).toBe(0);
      expect(squad.units[1].hp).toBe(30);
    });

    it("全ユニット死亡後に applyDamage しても例外が出ない", () => {
      const dead1 = makeUnit("d1", 0, 0);
      const dead2 = makeUnit("d2", 0, 0);
      const squad = new Squad("s1", [dead1, dead2]);
      expect(() => squad.applyDamage(999)).not.toThrow();
    });
  });

  describe("isDefeated", () => {
    it("全ユニットが hp=0 のとき true", () => {
      const dead = makeUnit("d", 0, 100, 100);
      const squad = new Squad("s1", [new Unit({ ...dead, hp: 0 })]);
      squad.applyDamage(0);
      expect(squad.isDefeated).toBe(true);
    });

    it("生存ユニットが 1 体でもいれば false", () => {
      const squad = new Squad("s1", [makeUnit("a", 10, 1), makeUnit("b", 10, 0)]);
      expect(squad.isDefeated).toBe(false);
    });

    it("ユニットがいない分隊は false", () => {
      expect(new Squad("empty").isDefeated).toBe(false);
    });
  });
});

describe("Brigade.assignUnitToSquad", () => {
  it("指定ユニットを指定分隊に配属できる", () => {
    const unit = makeUnit("u1", 15);
    const brigade = new Brigade([unit]);
    const squad = new Squad("sq1");
    brigade.addSquad(squad);

    brigade.assignUnitToSquad("u1", "sq1");

    expect(brigade.squads[0].units).toHaveLength(1);
    expect(brigade.squads[0].units[0].id).toBe("u1");
  });

  it("配属後、分隊の averageSpeed がユニットの speed と一致する", () => {
    const unit = makeUnit("u1", 25);
    const brigade = new Brigade([unit]);
    const squad = new Squad("sq1");
    brigade.addSquad(squad);

    brigade.assignUnitToSquad("u1", "sq1");

    expect(brigade.squads[0].averageSpeed).toBe(25);
  });

  it("存在しない unitId はエラー", () => {
    const brigade = new Brigade([]);
    brigade.addSquad(new Squad("sq1"));
    expect(() => brigade.assignUnitToSquad("ghost", "sq1")).toThrow();
  });

  it("存在しない squadId はエラー", () => {
    const unit = makeUnit("u1", 10);
    const brigade = new Brigade([unit]);
    expect(() => brigade.assignUnitToSquad("u1", "ghost")).toThrow();
  });
});
