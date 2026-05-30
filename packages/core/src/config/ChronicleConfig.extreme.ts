/**
 * Chronicle Knights — 極端モード（ギルドモード）専用 Config
 *
 * 仕様: 50人定員・超高回転スパルタモード
 *   - 毎年戦闘（100年で100戦）
 *   - 毎年新人3名加入
 *   - 衰退率 10%/年で世代交代を強制
 *   - 結婚閾値70・出産確率60% で血統サイクル爆発
 *   - 定員50名超過時は自動リストラ
 *
 * 使い方:
 *   bun scripts/run-grand-chronicle.ts --config extreme
 *   bun scripts/meta-analyze-guild.ts
 */
import type { ChronicleConfigType } from "./ChronicleConfig";

export const CHRONICLE_CONFIG_EXTREME = {
  TIME: {
    /** デフォルトの 0.06 を超える、毎年10%の超高速デフレ */
    DECAY_RATE: 0.10,
    BASE_PEAK_START_AGE: 24,
    BASE_PEAK_END_AGE: 28,
    INDUCTION_AGE: 15,
    MIN_STAT_VALUE: 1,
  },
  SCHEDULE: {
    CHRONICLE_YEARS: 100,
    /** 毎年新人加入 */
    RECRUIT_INTERVAL: 1,
    /** 1回あたり3名 = 年間3名流入 */
    RECRUIT_COUNT: 3,
    /** 毎年戦闘（100年で100戦） */
    BATTLE_INTERVAL: 1,
    /** 初期メンバー25名 */
    INITIAL_MEMBER_COUNT: 25,
    /** 大隊枠12名（4×3分隊） */
    BATTALION_SIZE: 12,
  },
  LINEAGE: {
    /** 2戦同分隊で結婚圏内（35×2=70） */
    AFFINITY_PER_BATTLE: 35,
    /** 結婚条件閾値 */
    MARRIAGE_THRESHOLD: 70,
    /** 条件成立年の80%で結婚 */
    MARRIAGE_PROBABILITY: 0.8,
    /** 結婚カップル毎年60%で出産 */
    BIRTH_PROBABILITY: 0.6,
    CULTURE_INHERIT_PROB: 0.5,
  },
  BATTLE: {
    MAX_TURNS: 30,
    /** 1分隊4名 */
    SQUAD_SIZE: 4,
    /** 前衛枠4名 */
    FRONT_ROW_COUNT: 4,
  },
  NAMING: {
    POOL_MIN_SIZE: 150,
  },
  LIMITS: {
    /** 旅団全体の最大定員50名。超過時は自動リストラ */
    MAX_BRIGADE_SIZE: 50,
  },
} as const satisfies ChronicleConfigType;
