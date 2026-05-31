/**
 * /api/guild/* — 人事フェーズ用エンドポイント
 *   GET  /decisions   採用候補 + 引退候補のリスト
 *   POST /accept      個別ユニットを採用
 *   POST /dismiss     個別ユニットを解雇
 *
 * 引退候補は「総合的な強さ（totalRating）」昇順でランク付け。
 * 仕様変更により、HumanDecisionService 由来の strengthRank（stats.strength
 * 基準）を上書きする形で、UI ユーザに分かりやすい totalRating 基準を採用。
 */
import { Hono } from "hono";
import { getOrCreateSession } from "../session";
import { getPendingDecisions } from "../../../../packages/core/src/services/HumanDecisionService";
import { computeBattleStats } from "../../../../packages/core/src/data/jobs";
import type { Unit } from "../../../../packages/core/src/models/Unit";

const MAX_BRIGADE_SIZE = 50;

export const guildRoute = new Hono();

guildRoute.get("/decisions", (c) => {
  const session = getOrCreateSession();
  const pending = getPendingDecisions(
    session.brigade,
    [...session.candidatePool],
    MAX_BRIGADE_SIZE
  );

  // 引退候補を totalRating 昇順で再ランク（弱い順 = 1位がリストラ最有力）
  const totalRatingMap = new Map<string, number>();
  for (const u of session.brigade.units) {
    totalRatingMap.set(u.id, computeBattleStats(u).totalRating);
  }
  const reordered = [...pending.retirementCandidates].sort(
    (a, b) => (totalRatingMap.get(a.unit.id)! - totalRatingMap.get(b.unit.id)!)
  );

  return c.json({
    year: session.year,
    currentSize: pending.currentSize,
    maxSize: pending.maxSize,
    overflowCount: pending.overflowCount,
    recruits: pending.recruits.map((r) => {
      const stats = computeBattleStats(r.unit);
      return {
        id: r.unit.id,
        name: r.unit.name,
        job: r.unit.job,
        gender: r.unit.gender,
        origin: r.unit.origin,
        age: r.unit.age,
        baseStrength: r.unit.baseStats.strength,
        source: r.source,
        hasLineage: r.relatedFamilyIds.length > 0,
        relatedFamilyIds: r.relatedFamilyIds,
        // 戦闘ステータス
        maxHp: stats.maxHp,
        attack: stats.attack,
        frontAttack: stats.frontAttack,
        rearAttack: stats.rearAttack,
        speed: stats.speed,
        totalRating: stats.totalRating,
      };
    }),
    retirementCandidates: reordered.map((rc, i) => {
      const stats = computeBattleStats(rc.unit);
      return {
        id: rc.unit.id,
        name: rc.unit.name,
        job: rc.unit.job,
        gender: rc.unit.gender,
        origin: rc.unit.origin,
        age: rc.unit.age,
        strength: rc.unit.stats.strength,
        // totalRating ベースの新ランク（1=最弱）
        strengthRank: i + 1,
        reasons: rc.reasons,
        hasLineage: rc.hasLineage,
        descendantCount: rc.descendantCount,
        isMarried: rc.unit.spouseId !== null,
        // 戦闘ステータス
        maxHp: stats.maxHp,
        attack: stats.attack,
        frontAttack: stats.frontAttack,
        rearAttack: stats.rearAttack,
        speed: stats.speed,
        totalRating: stats.totalRating,
      };
    }),
  });
});

guildRoute.post("/accept", async (c) => {
  const body = await c.req.json();
  const unitId: string = body.unitId;
  if (!unitId) return c.json({ ok: false, error: "unitId required" }, 400);
  const session = getOrCreateSession();
  const res = session.applyAccept(unitId);
  return c.json({
    ok: res.accepted !== null,
    brigadeSize: session.brigade.units.length,
    accepted: res.accepted ? { id: res.accepted.id, name: res.accepted.name } : null,
  });
});

guildRoute.post("/dismiss", async (c) => {
  const body = await c.req.json();
  const unitId: string = body.unitId;
  if (!unitId) return c.json({ ok: false, error: "unitId required" }, 400);
  const session = getOrCreateSession();
  const res = session.applyDismiss(unitId);
  return c.json({
    ok: res.dismissed !== null,
    brigadeSize: session.brigade.units.length,
    dismissed: res.dismissed ? { id: res.dismissed.id, name: res.dismissed.name } : null,
  });
});
