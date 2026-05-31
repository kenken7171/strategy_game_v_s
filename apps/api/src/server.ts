/**
 * Chronicle Knights — API サーバエントリポイント
 *
 * Hono + Bun。`bun run dev` で http://localhost:8787 にホット起動。
 *
 * エンドポイント:
 *   POST /api/game/new          新規ゲーム
 *   GET  /api/game/state        現在状態
 *   GET  /api/chronicle         年代記サマリー
 *   GET  /api/chronicle/preview 敵プレビュー
 *   GET  /api/guild/decisions   採用・引退候補
 *   POST /api/guild/accept      採用
 *   POST /api/guild/dismiss     解雇
 *   GET  /api/formation/roster  編成可能ユニット + 好感度マップ
 *   POST /api/battle/run        戦闘実行 + ターンログ
 *   POST /api/battle/finish     年送り
 */
import { Hono } from "hono";
import { cors } from "hono/cors";
import { gameRoute } from "./routes/game";
import { chronicleRoute } from "./routes/chronicle";
import { guildRoute } from "./routes/guild";
import { formationRoute } from "./routes/formation";
import { battleRoute } from "./routes/battle";

const app = new Hono();

app.use("*", cors({
  origin: ["http://localhost:5173", "http://localhost:4173"],
  credentials: true,
}));

app.get("/", (c) => c.json({ name: "Chronicle Knights API", version: "0.1.0" }));
app.get("/health", (c) => c.json({ ok: true }));

app.route("/api/game", gameRoute);
app.route("/api/chronicle", chronicleRoute);
app.route("/api/guild", guildRoute);
app.route("/api/formation", formationRoute);
app.route("/api/battle", battleRoute);

const port = Number(process.env.PORT ?? 8787);
console.log(`Chronicle Knights API → http://localhost:${port}`);

export default {
  port,
  fetch: app.fetch,
};
