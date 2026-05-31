/**
 * API クライアント — fetch ラッパー
 *
 * Vite の proxy 設定で /api 以下がバックエンドに転送される（開発時）。
 * 本番では同一ホスト想定。
 */
import type {
  GameStateResponse,
  ChronicleResponse,
  EnemyPreviewResponse,
  GuildDecisionsResponse,
  FormationRosterResponse,
  BattleRunResponse,
  BattleFinishResponse,
  BattlePlacement,
} from "./types";

async function getJSON<T>(path: string): Promise<T> {
  const res = await fetch(path);
  if (!res.ok) throw new Error(`GET ${path} failed: ${res.status}`);
  return (await res.json()) as T;
}

async function postJSON<T>(path: string, body: unknown = {}): Promise<T> {
  const res = await fetch(path, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(body),
  });
  if (!res.ok) throw new Error(`POST ${path} failed: ${res.status}`);
  return (await res.json()) as T;
}

export const api = {
  // Game
  newGame: (seed: number = 42) =>
    postJSON<GameStateResponse>("/api/game/new", { seed }),
  getState: () => getJSON<GameStateResponse>("/api/game/state"),

  // Chronicle
  getChronicle: () => getJSON<ChronicleResponse>("/api/chronicle"),
  getEnemyPreview: () => getJSON<EnemyPreviewResponse>("/api/chronicle/preview"),

  // Guild
  getDecisions: () => getJSON<GuildDecisionsResponse>("/api/guild/decisions"),
  acceptRecruit: (unitId: string) =>
    postJSON<{ ok: boolean; brigadeSize: number }>("/api/guild/accept", { unitId }),
  dismissUnit: (unitId: string) =>
    postJSON<{ ok: boolean; brigadeSize: number }>("/api/guild/dismiss", { unitId }),

  // Formation
  getRoster: () => getJSON<FormationRosterResponse>("/api/formation/roster"),

  // Battle
  runBattle: (placements: BattlePlacement[]) =>
    postJSON<BattleRunResponse>("/api/battle/run", { placements }),
  finishBattle: () => postJSON<BattleFinishResponse>("/api/battle/finish"),
};
