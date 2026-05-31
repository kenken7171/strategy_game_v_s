/**
 * /api/chronicle/* — 年代記フェーズ用エンドポイント
 *   GET  /            年代記サマリー + 履歴
 *   GET  /preview     今年の敵プレビュー（±15%予測レンジ）
 *   POST /advance     CHRONICLE→GUILD 直前に呼ぶ年送り（B→Cの遷移時に呼ぶ）
 *                     ※ 実際の年送りは BATTLE 後に行う設計のためここでは状態取得のみ
 */
import { Hono } from "hono";
import { getOrCreateSession } from "../session";
import { CHRONICLE_CONFIG_EXTREME as CHRONICLE_CONFIG } from "../../../../packages/core/src/config/ChronicleConfig.extreme";

export const chronicleRoute = new Hono();

/** 年代記表示用データ */
chronicleRoute.get("/", (c) => {
  const session = getOrCreateSession();
  const brigade = session.brigade;

  // 既婚カップル数（双方向重複除去）
  const married = new Set<string>();
  for (const u of brigade.units) {
    if (u.spouseId) {
      const key = [u.id, u.spouseId].sort().join("|");
      married.add(key);
    }
  }
  const descendants = brigade.units.filter((u) => u.parents !== null).length;

  return c.json({
    year: session.year,
    brigadeSize: brigade.units.length,
    marriedCouples: married.size,
    descendants,
    // 最新50件の履歴を返す（多すぎると重い）
    history: session.chronicle.slice(-50),
  });
});

/** 今年の敵プレビュー（±15%予測レンジ） */
chronicleRoute.get("/preview", (c) => {
  const session = getOrCreateSession();
  const sc = CHRONICLE_CONFIG.ENEMY_SCALING;
  const year = session.year;
  const baseHp    = sc.BASE_HP     + year * sc.HP_GAIN_PER_YEAR;
  const baseAtk   = sc.BASE_ATTACK + year * sc.ATTACK_GAIN_PER_YEAR;
  const baseSpeed = sc.BASE_SPEED  + year * sc.SPEED_GAIN_PER_YEAR;
  return c.json({
    year,
    enemyCount: 10,
    hp:    { base: baseHp,    min: Math.round(baseHp    * 0.85), max: Math.round(baseHp    * 1.15) },
    attack:{ base: baseAtk,   min: Math.round(baseAtk   * 0.85), max: Math.round(baseAtk   * 1.15) },
    speed: { base: baseSpeed, min: Math.round(baseSpeed * 0.85), max: Math.round(baseSpeed * 1.15) },
  });
});
