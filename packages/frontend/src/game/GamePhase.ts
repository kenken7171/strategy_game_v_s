/**
 * ゲームフェーズ定義（4フェーズ厳格遷移）
 *
 * instructions.md F-1 ルール:
 *   CHRONICLE → GUILD_MANAGEMENT → BATTALION_FORMATION → BATTLE_SIMULATION
 *   → (次年の) CHRONICLE
 *
 * 不可逆・一方通行。`setPhase(任意)` のような自由遷移 API は禁止。
 */
export type GamePhase =
  | "CHRONICLE"
  | "GUILD_MANAGEMENT"
  | "BATTALION_FORMATION"
  | "BATTLE_SIMULATION";

/** 不変な遷移順序。順番を変えてはならない */
export const PHASE_ORDER: ReadonlyArray<GamePhase> = [
  "CHRONICLE",
  "GUILD_MANAGEMENT",
  "BATTALION_FORMATION",
  "BATTLE_SIMULATION",
];

/** 表示用ラベル（フェーズインジケータ等で利用） */
export const PHASE_LABEL: Record<GamePhase, string> = {
  CHRONICLE: "年代記",
  GUILD_MANAGEMENT: "人事",
  BATTALION_FORMATION: "編成",
  BATTLE_SIMULATION: "戦闘",
};

/**
 * 次フェーズを返す。BATTLE_SIMULATION の次は CHRONICLE（年を進める）。
 * 自由遷移は禁止しているため、本関数のみが正規の遷移手段。
 */
export function nextPhase(current: GamePhase): GamePhase {
  const idx = PHASE_ORDER.indexOf(current);
  // 最後のフェーズの次は最初に戻る（年が進む）
  return PHASE_ORDER[(idx + 1) % PHASE_ORDER.length];
}

/**
 * 戦闘完了 → CHRONICLE 遷移時は新しい年に進む合図でもある。
 * これを判定する純粋関数。
 */
export function isYearAdvancingTransition(
  current: GamePhase,
  next: GamePhase
): boolean {
  return current === "BATTLE_SIMULATION" && next === "CHRONICLE";
}
