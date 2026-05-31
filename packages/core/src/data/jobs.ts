/**
 * ジョブ定義の単一 SoT
 *
 * これまで scripts/run-grand-chronicle.ts / scripts/meta-analyze-guild.ts /
 * apps/api/src/routes/battle.ts に重複定義されていた JOB_DEFAULTS を集約。
 * 加えて、フロント/バック共通の日本語ラベル（JOB_JP）と、UI 比較用の
 * 総合強さ（Total Rating）計算ヘルパーも提供する。
 */
import type { JobType, Unit } from "../models/Unit";

export interface JobDefaults {
  readonly maxHp: number;
  readonly speed: number;
  readonly frontAttack: number;
  readonly rearAttack: number;
  readonly bdf: number;
  readonly sdf: number;
  readonly ab: number;
  readonly hl: number;
}

/**
 * 8ジョブのデフォルトステータス。`config/jobs.json` の値を TypeScript 側に
 * 反映したもの。本ファイルが SoT。
 */
export const JOB_DEFAULTS: Record<JobType, JobDefaults> = {
  iron_wall_knight: { maxHp: 250, speed: 10, frontAttack: 50, rearAttack:  10, bdf: 10, sdf: 15, ab:  0, hl:  0 },
  tactician:        { maxHp: 120, speed: 35, frontAttack: 20, rearAttack:  20, bdf:  0, sdf:  0, ab: 20, hl:  0 },
  medic:            { maxHp: 100, speed: 25, frontAttack: 10, rearAttack:  10, bdf:  0, sdf:  0, ab:  0, hl: 30 },
  sniper:           { maxHp:  80, speed: 40, frontAttack: 20, rearAttack:  90, bdf:  0, sdf:  0, ab:  0, hl:  0 },
  sorcerer:         { maxHp:  40, speed: 15, frontAttack: 10, rearAttack: 120, bdf:  0, sdf:  0, ab:  0, hl:  0 },
  standard_bearer:  { maxHp: 150, speed: 20, frontAttack: 30, rearAttack:  30, bdf:  0, sdf:  5, ab: 40, hl:  0 },
  heavy_infantry:   { maxHp: 300, speed: 15, frontAttack: 70, rearAttack:  20, bdf:  0, sdf: 10, ab:  0, hl:  0 },
  scout:            { maxHp:  90, speed: 60, frontAttack: 40, rearAttack:  40, bdf:  0, sdf:  0, ab:  0, hl:  0 },
};

/** 日本語ラベル（フロント全画面で必須参照） */
export const JOB_JP: Record<JobType, string> = {
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
  return (JOB_JP as Record<string, string>)[job] ?? job;
}

// ─── 戦闘ステータス計算 ──────────────────────────────────────────────────────

/** ユニットの「現年齢時点」の実効戦闘ステータス */
export interface BattleStats {
  readonly maxHp: number;
  /** 攻撃力（max(frontAttack, rearAttack)）。配置最適時の最大火力を示す */
  readonly attack: number;
  /** 内訳: 前衛配置時の攻撃力 */
  readonly frontAttack: number;
  /** 内訳: 後衛配置時の攻撃力 */
  readonly rearAttack: number;
  readonly speed: number;
  /** UI 用の総合強さ（Total Rating） */
  readonly totalRating: number;
}

/**
 * ジョブのデフォルト値を age の growthFactor でスケールして
 * 「現年齢時点の戦闘ステータス」を返す。
 *
 * HP は容量なので growthFactor を掛けない（怪我の概念は別途）。
 * ATK/SPD は能力なので growthFactor を掛ける。
 */
export function computeBattleStats(unit: Unit): BattleStats {
  if (!unit.job) {
    // ジョブなしユニット（敵などのプレースホルダ）はそのまま
    const atk = Math.max(unit.frontAttack, unit.rearAttack);
    return {
      maxHp: unit.maxHp,
      attack: atk,
      frontAttack: unit.frontAttack,
      rearAttack: unit.rearAttack,
      speed: unit.speed,
      totalRating: totalRating(unit.maxHp, atk, unit.speed),
    };
  }
  const d = JOB_DEFAULTS[unit.job];
  const f = unit.growthFactor;
  const scale = (v: number) => Math.max(1, Math.round(v * f));
  const front = scale(d.frontAttack);
  const rear = scale(d.rearAttack);
  const attack = Math.max(front, rear);
  const speed = scale(d.speed);
  return {
    maxHp: d.maxHp,
    attack,
    frontAttack: front,
    rearAttack: rear,
    speed,
    totalRating: totalRating(d.maxHp, attack, speed),
  };
}

/**
 * UI 比較用の総合強さ指標。
 *
 * 式: floor(maxHp / 5 + attack + speed)
 *   - HP は数百単位なので /5 して他指標とスケールを揃える
 *   - attack は配置最適時の max(FA, RA) を採用（脆い砲台 sorcerer も正当評価される）
 *   - speed は先制取れるかの命綱なのでそのまま
 *
 * 参考値:
 *   iron_wall_knight (HP250 / FA50 / SPD10) = 50+50+10 = 110
 *   heavy_infantry   (HP300 / FA70 / SPD15) = 60+70+15 = 145  ← 単体最強
 *   sorcerer         (HP40  / RA120 / SPD15) = 8+120+15 = 143
 *   sniper           (HP80  / RA90 / SPD40)  = 16+90+40 = 146
 *   scout            (HP90  / FA40 / SPD60)  = 18+40+60 = 118
 */
export function totalRating(maxHp: number, attack: number, speed: number): number {
  return Math.floor(maxHp / 5 + attack + speed);
}
