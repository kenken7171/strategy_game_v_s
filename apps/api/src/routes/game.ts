/**
 * /api/game/* — ゲームセッション管理
 *   POST /new       新規ゲーム開始（seed指定）
 *   GET  /state     現在のゲーム状態
 */
import { Hono } from "hono";
import { getOrCreateSession, resetSession } from "../session";

export const gameRoute = new Hono();

gameRoute.post("/new", async (c) => {
  const body = await c.req.json().catch(() => ({}));
  const seed = typeof body.seed === "number" ? body.seed : 42;
  const session = resetSession(seed);
  return c.json({
    year: session.year,
    seed: session.seed,
    brigadeSize: session.brigade.units.length,
    maxBrigadeSize: 50,
  });
});

gameRoute.get("/state", (c) => {
  const session = getOrCreateSession();
  return c.json({
    year: session.year,
    seed: session.seed,
    brigadeSize: session.brigade.units.length,
    maxBrigadeSize: 50,
    candidatePoolSize: session.candidatePool.length,
  });
});
