/**
 * /api/formation/* — 編成フェーズ用エンドポイント
 *   GET  /roster     編成可能ユニット一覧（旅団全員）+ 好感度マトリクス
 *                    各ユニットに HP/ATK/SPD/totalRating を含む
 */
import { Hono } from "hono";
import { getOrCreateSession } from "../session";
import { computeBattleStats } from "../../../../packages/core/src/data/jobs";

export const formationRoute = new Hono();

formationRoute.get("/roster", (c) => {
  const session = getOrCreateSession();
  const units = session.brigade.units;

  // 好感度マップ
  const affinityMap: Record<string, Record<string, number>> = {};
  for (const u of units) {
    const row: Record<string, number> = {};
    for (const [otherId, v] of u.affinity) row[otherId] = v;
    affinityMap[u.id] = row;
  }

  return c.json({
    year: session.year,
    units: units.map((u) => {
      const stats = computeBattleStats(u);
      return {
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
        // 戦闘ステータス（job 由来 × growthFactor）
        maxHp: stats.maxHp,
        attack: stats.attack,
        frontAttack: stats.frontAttack,
        rearAttack: stats.rearAttack,
        speed: stats.speed,
        totalRating: stats.totalRating,
      };
    }),
    affinityMap,
  });
});
