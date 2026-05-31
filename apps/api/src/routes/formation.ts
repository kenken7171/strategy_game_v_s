/**
 * /api/formation/* — 編成フェーズ用エンドポイント
 *   GET  /roster     編成可能ユニット一覧（旅団全員）+ 好感度マトリクス
 */
import { Hono } from "hono";
import { getOrCreateSession } from "../session";

export const formationRoute = new Hono();

/**
 * 全旅団員 + 好感度ペア情報を返す。
 * フロントは「同分隊に配置時のハートマーク」判定に好感度マップを使う。
 */
formationRoute.get("/roster", (c) => {
  const session = getOrCreateSession();
  const units = session.brigade.units;

  // 好感度マップ: { unitA: { unitB: 100, ... }, ... }
  const affinityMap: Record<string, Record<string, number>> = {};
  for (const u of units) {
    const row: Record<string, number> = {};
    for (const [otherId, v] of u.affinity) {
      row[otherId] = v;
    }
    affinityMap[u.id] = row;
  }

  return c.json({
    year: session.year,
    units: units.map((u) => ({
      id: u.id,
      name: u.name,
      job: u.job,
      gender: u.gender,
      origin: u.origin,
      age: u.age,
      strength: u.stats.strength,
      baseStrength: u.baseStats.strength,
      growthFactor: u.growthFactor,
      isMarried: u.spouseId !== null,
      spouseId: u.spouseId,
      parents: u.parents,
      isAlive: u.isAlive,
      isRetired: u.isRetired,
    })),
    affinityMap,
  });
});
