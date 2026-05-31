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

/**
 * 各ジョブの「戦術貢献ボーナス」。
 *
 * 純戦闘ステータス（HP/ATK/SPD）だけだとバフ・回復・防御役が不当に低く
 * 評価される。例: medic は HP100/ATK10/SPD25 で基礎値55、heavy_infantry は
 * HP300/ATK70/SPD15 で基礎値145。実戦価値は同等以上なのに数値差が大きい。
 *
 * 各ジョブの「戦闘での戦術的価値」を反映するボーナスを足すことで、
 * 全ジョブの平均総合値が 140〜148 程度に揃うよう調整。
 *
 * 設計指針:
 *   - 純アタッカー (heavy/sniper/sorcerer) は ATK 自体が高いので +0
 *   - 鉄壁騎士は BDF/SDF があり防御役として加点 +30
 *   - 斥候は速度先制取りで +30
 *   - 旗手・戦術官はバフ役として大幅加点 +65
 *   - 衛生兵は回復役として最大加点 +90
 */
export const ROLE_BONUS: Record<JobType, number> = {
  iron_wall_knight:  30, // BDF=10 + SDF=15（前衛・大隊防護）
  heavy_infantry:     0, // 純戦闘力で既に高い（HP300, FA70）
  sniper:             0, // 純戦闘力で既に高い（RA90 + 2連撃）
  sorcerer:           0, // 純戦闘力で既に高い（RA120）
  scout:             30, // 速度差先制（SPD=60の戦術価値）
  standard_bearer:   65, // AB=40（大隊全員を底上げ）
  tactician:         65, // AB=20 + SPD補正
  medic:             90, // HL=30（毎ターン回復、唯一の継続支援）
};

/**
 * 全ジョブのベースライン総合値（ROLE_BONUS 込み）。
 * 同レベルの優秀さなら全ジョブが 140〜148 で並ぶよう調整済み。
 */
function baseJobRating(job: JobType): number {
  const d = JOB_DEFAULTS[job];
  const baseStat = Math.floor(d.maxHp / 5 + Math.max(d.frontAttack, d.rearAttack) + d.speed);
  return baseStat + ROLE_BONUS[job];
}

/** 各ジョブの基準総合値（参考表示用） */
export const JOB_TARGET_RATING: Record<JobType, number> = {
  iron_wall_knight: baseJobRating("iron_wall_knight"),
  heavy_infantry:   baseJobRating("heavy_infantry"),
  sniper:           baseJobRating("sniper"),
  sorcerer:         baseJobRating("sorcerer"),
  scout:            baseJobRating("scout"),
  standard_bearer:  baseJobRating("standard_bearer"),
  tactician:        baseJobRating("tactician"),
  medic:            baseJobRating("medic"),
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
 * HP は容量なので growthFactor を掛けない。ATK/SPD は能力なのでスケール。
 * totalRating は「ジョブ基準値 × 年齢補正 × 遺伝係数」の正規化指標。
 */
export function computeBattleStats(unit: Unit): BattleStats {
  if (!unit.job) {
    const atk = Math.max(unit.frontAttack, unit.rearAttack);
    const base = Math.floor(unit.maxHp / 5 + atk + unit.speed);
    return {
      maxHp: unit.maxHp,
      attack: atk,
      frontAttack: unit.frontAttack,
      rearAttack: unit.rearAttack,
      speed: unit.speed,
      totalRating: base,
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
    totalRating: totalRating(unit),
  };
}

/**
 * 個体の遺伝係数（0.85〜1.15）。
 * baseStats.strength は 70〜130 の範囲でランダム生成されるため、
 * 70 → 0.85（最弱）、100 → 1.00（平均）、130 → 1.15（最強）にマップする。
 */
function geneticMultiplier(baseStrength: number): number {
  const clamped = Math.max(70, Math.min(130, baseStrength));
  return 0.85 + ((clamped - 70) / 60) * 0.30;
}

/**
 * UI 比較用の総合強さ指標（再設計版）。
 *
 * 式: round(JOB_TARGET_RATING[job] × growthFactor × geneticMultiplier)
 *
 * 設計意図:
 *   - JOB_TARGET_RATING はジョブごとに 140〜148 に揃えてあり、
 *     バフ・回復職と純アタッカーが「平均的個体ならほぼ同等の総合強さ」と
 *     して扱われる（不当評価の解消）
 *   - growthFactor で年齢補正（修業期は線形上昇、衰退期は 3%/年複利減衰）
 *   - geneticMultiplier で個体ごとの遺伝の優秀さ（baseStrength 70-130 → ±15%）
 *
 * 例（全盛期・平均遺伝）:
 *   medic   全盛期 strength=100 → 145 × 1.0 × 1.0 = 145
 *   medic   全盛期 strength=130 → 145 × 1.0 × 1.15 ≈ 167  ← 遺伝最強
 *   medic   修業中(20歳) g=0.83 → 145 × 0.83 × 1.0 ≈ 120
 *   heavy   全盛期 平均         → 145 × 1.0 × 1.0 = 145
 *   tactician 全盛期 strength=70 → 144 × 1.0 × 0.85 ≈ 122  ← 遺伝最弱
 *
 * ジョブなしユニット（敵）はジョブ基準値が定義できないため、純戦闘ステ
 * から floor(HP/5 + ATK + SPD) を返す（旧式と同じ）。
 */
export function totalRating(unit: Unit): number {
  if (!unit.job) {
    const atk = Math.max(unit.frontAttack, unit.rearAttack);
    return Math.floor(unit.maxHp / 5 + atk + unit.speed);
  }
  const target = JOB_TARGET_RATING[unit.job];
  return Math.round(target * unit.growthFactor * geneticMultiplier(unit.baseStats.strength));
}
