/**
 * ジョブ表示ヘルパー（フロント側）。
 *
 * バックエンド core/data/jobs.ts の JOB_JP をフロントにミラーリング。
 * 全画面（年代記・人事・編成・戦闘ログ・生存者リスト）で必ずこのヘルパー
 * を経由して英語ジョブ識別子を日本語に変換する。
 */

const JOB_JP: Record<string, string> = {
  iron_wall_knight: "鉄壁騎士",
  tactician: "戦術官",
  medic: "衛生兵",
  sniper: "狙撃兵",
  sorcerer: "呪術師",
  standard_bearer: "旗手",
  heavy_infantry: "重装歩兵",
  scout: "斥候",
};

/** ジョブID を日本語ラベルに変換。null/未知ジョブは "—" にフォールバック */
export function formatJob(job: string | null | undefined): string {
  if (!job) return "—";
  return JOB_JP[job] ?? job;
}
