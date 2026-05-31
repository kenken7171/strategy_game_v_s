/**
 * /api/guild/* — 人事フェーズ用エンドポイント
 *   GET  /decisions   採用候補 + 引退候補のリスト
 *   POST /accept      個別ユニットを採用
 *   POST /dismiss     個別ユニットを解雇
 */
import { Hono } from "hono";
import { getOrCreateSession } from "../session";
import { getPendingDecisions } from "../../../../packages/core/src/services/HumanDecisionService";

const MAX_BRIGADE_SIZE = 50;

export const guildRoute = new Hono();

/** 採用候補・引退候補を構造化して返す */
guildRoute.get("/decisions", (c) => {
  const session = getOrCreateSession();
  const pending = getPendingDecisions(
    session.brigade,
    [...session.candidatePool],
    MAX_BRIGADE_SIZE
  );

  // フロント向けに serialization 可能な形にマッピング
  return c.json({
    year: session.year,
    currentSize: pending.currentSize,
    maxSize: pending.maxSize,
    overflowCount: pending.overflowCount,
    recruits: pending.recruits.map((r) => ({
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
    })),
    retirementCandidates: pending.retirementCandidates.map((rc) => ({
      id: rc.unit.id,
      name: rc.unit.name,
      job: rc.unit.job,
      gender: rc.unit.gender,
      origin: rc.unit.origin,
      age: rc.unit.age,
      strength: rc.unit.stats.strength,
      strengthRank: rc.strengthRank,
      reasons: rc.reasons,
      hasLineage: rc.hasLineage,
      descendantCount: rc.descendantCount,
      isMarried: rc.unit.spouseId !== null,
    })),
  });
});

/** 採用 */
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

/** 解雇 */
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
