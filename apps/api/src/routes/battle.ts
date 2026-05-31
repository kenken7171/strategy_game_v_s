/**
 * /api/battle/* — 戦闘フェーズ用エンドポイント
 *   POST /run        編成データを受け取り、戦闘実行 + 詳細ログ返却
 *   POST /finish     戦闘後の年送り（advance）+ チロニクル更新
 */
import { Hono } from "hono";
import { getOrCreateSession } from "../session";
import {
  Unit, Squad, BattleSimulator,
  CHRONICLE_CONFIG_EXTREME as CHRONICLE_CONFIG,
  type JobType,
} from "../../../../packages/core/src/index";

export const battleRoute = new Hono();

interface JobDefaults {
  maxHp: number; speed: number;
  frontAttack: number; rearAttack: number;
  bdf: number; sdf: number; ab: number; hl: number;
}
const JOB_DEFAULTS: Record<JobType, JobDefaults> = {
  iron_wall_knight: { maxHp: 250, speed: 10, frontAttack: 50, rearAttack:  10, bdf: 10, sdf: 15, ab:  0, hl:  0 },
  tactician:        { maxHp: 120, speed: 35, frontAttack: 20, rearAttack:  20, bdf:  0, sdf:  0, ab: 20, hl:  0 },
  medic:            { maxHp: 100, speed: 25, frontAttack: 10, rearAttack:  10, bdf:  0, sdf:  0, ab:  0, hl: 30 },
  sniper:           { maxHp:  80, speed: 40, frontAttack: 20, rearAttack:  90, bdf:  0, sdf:  0, ab:  0, hl:  0 },
  sorcerer:         { maxHp:  40, speed: 15, frontAttack: 10, rearAttack: 120, bdf:  0, sdf:  0, ab:  0, hl:  0 },
  standard_bearer:  { maxHp: 150, speed: 20, frontAttack: 30, rearAttack:  30, bdf:  0, sdf:  5, ab: 40, hl:  0 },
  heavy_infantry:   { maxHp: 300, speed: 15, frontAttack: 70, rearAttack:  20, bdf:  0, sdf: 10, ab:  0, hl:  0 },
  scout:            { maxHp:  90, speed: 60, frontAttack: 40, rearAttack:  40, bdf:  0, sdf:  0, ab:  0, hl:  0 },
};

function mulberry32(seed: number): () => number {
  let s = seed;
  return () => {
    s = (s + 0x6d2b79f5) | 0;
    let t = Math.imul(s ^ (s >>> 15), 1 | s);
    t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}

function buildBattleUnit(u: Unit): Unit {
  if (!u.job) return u;
  const d = JOB_DEFAULTS[u.job];
  const f = u.growthFactor;
  const scale = (v: number) => Math.max(1, Math.round(v * f));
  return new Unit({
    ...u,
    maxHp: d.maxHp, hp: d.maxHp,
    speed: scale(d.speed),
    frontAttack: scale(d.frontAttack),
    rearAttack: scale(d.rearAttack),
    bdf: d.bdf, sdf: d.sdf, ab: d.ab, hl: d.hl,
  });
}

/** 試練の敵（±15% 乱数化）。instructions.md B-3 準拠 */
function makeTrialEnemy(year: number, rng: () => number): Squad[] {
  const sc = CHRONICLE_CONFIG.ENEMY_SCALING;
  const baseHp    = sc.BASE_HP     + year * sc.HP_GAIN_PER_YEAR;
  const baseAtk   = sc.BASE_ATTACK + year * sc.ATTACK_GAIN_PER_YEAR;
  const baseSpeed = sc.BASE_SPEED  + year * sc.SPEED_GAIN_PER_YEAR;
  const jitter = () => 0.85 + rng() * 0.30;
  const enemyUnit = (i: number): Unit => {
    const hp    = Math.max(1, Math.round(baseHp    * jitter()));
    const atk   = Math.max(1, Math.round(baseAtk   * jitter()));
    const speed = Math.max(1, Math.round(baseSpeed * jitter()));
    return new Unit({
      id: `enemy-${i}`, name: `試練の兵${i + 1}`, job: null,
      age: 25, peakStartAge: 25, peakEndAge: 35, maxAge: 60,
      baseStats: { strength: 60, agility: 0, intelligence: 0, endurance: 0 },
      maxHp: hp, hp, speed, frontAttack: atk, rearAttack: atk,
    });
  };
  const units = Array.from({ length: 10 }, (_, i) => enemyUnit(i));
  return [
    new Squad("E1", units.slice(0, 3)),
    new Squad("E2", units.slice(3, 6)),
    new Squad("E3", units.slice(6, 9)),
    new Squad("E4", units.slice(9, 10)),
  ];
}

interface BattlePlacement {
  row: "FRONT" | "REAR-L" | "REAR-R";
  col: number; // 0,1,2
  unitId: string;
}

/**
 * 編成済みユニット9名で戦闘実行 + 詳細ログ返却
 * リクエスト: { placements: BattlePlacement[] }
 */
battleRoute.post("/run", async (c) => {
  const body = await c.req.json();
  const placements: BattlePlacement[] = body.placements ?? [];
  const session = getOrCreateSession();

  if (placements.length !== 9) {
    return c.json({ ok: false, error: "placements must be 9" }, 400);
  }

  // ユニットを取得
  const idMap = new Map(session.brigade.units.map((u) => [u.id, u]));
  const grouped: Record<string, Unit[]> = { FRONT: [], "REAR-L": [], "REAR-R": [] };
  for (const p of placements) {
    const u = idMap.get(p.unitId);
    if (!u) return c.json({ ok: false, error: `unit ${p.unitId} not found` }, 400);
    grouped[p.row].push(buildBattleUnit(u));
  }

  const squads = [
    new Squad("FRONT",  grouped.FRONT),
    new Squad("REAR-L", grouped["REAR-L"]),
    new Squad("REAR-R", grouped["REAR-R"]),
  ];

  // 戦闘用 RNG（year + seed で再現性確保）
  const battleRng = mulberry32(session.seed * 1000 + session.year);
  const enemy = makeTrialEnemy(session.year, battleRng);

  const sim = new BattleSimulator(squads, enemy, {
    maxTurns: CHRONICLE_CONFIG.BATTLE.MAX_TURNS,
    rng: battleRng,
    verbose: false,
  });
  const result = sim.run();

  // 戦闘後: 好感度蓄積（同分隊ペアに加算）
  session.setBrigade(
    session.brigade.applyBattleAffinity(
      result.squadmatePairs,
      CHRONICLE_CONFIG.LINEAGE.AFFINITY_PER_BATTLE
    )
  );

  // 戦闘ログを年代記へ
  session.pushChronicle({
    year: session.year,
    type: "battle",
    text: `⚔️ Year ${session.year}: ${result.winner} (${result.turns}ターン)`,
  });

  // 結果をセッションに保持
  session.setLastBattleResult(result);

  return c.json({
    ok: true,
    year: session.year,
    winner: result.winner,
    turns: result.turns,
    statistics: result.statistics,
    allySurvivors: result.allySurvivors,
    enemySurvivors: result.enemySurvivors,
    turnLogs: result.turnLogs,
  });
});

/**
 * 戦闘終了後、次年への進行を確定する。
 * advance() で加齢・引退・結婚・出産・15歳入団を一括処理。
 */
battleRoute.post("/finish", async (c) => {
  const session = getOrCreateSession();
  const advRng = mulberry32(session.seed * 1000 + session.year + 500);
  const events = session.advanceYear(advRng);
  return c.json({
    ok: true,
    nextYear: session.year,
    eventsCount: events.length,
    brigadeSize: session.brigade.units.length,
  });
});
