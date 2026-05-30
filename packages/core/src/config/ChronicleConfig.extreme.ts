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
    /**
     * 大隊枠9名（3×3分隊）。instructions.md の絶対ルールにより、
     * 編成のジレンマを強化するため12→9に削減。
     */
    BATTALION_SIZE: 9,
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
    /** 1分隊3名（instructions.md 絶対ルール: 3×3 構成） */
    SQUAD_SIZE: 3,
    /** 前衛枠3名（instructions.md 絶対ルール: 3×3 構成） */
    FRONT_ROW_COUNT: 3,
  },
  NAMING: {
    POOL_MIN_SIZE: 150,
  },
  LIMITS: {
    /** 旅団全体の最大定員50名。超過時は自動リストラ */
    MAX_BRIGADE_SIZE: 50,
  },
  /**
   * 試練の敵の年代スケーリング。
   * 計算式: BASE + year × GAIN_PER_YEAR を基準にし、
   * makeTrialEnemy で個体ごとに ±15% の乱数ぶれを掛ける（instructions.md B-3 ルール）。
   *
   * 例: Y100 の基準値 HP=650, ATK=90, SPD=160
   *     → 個体差で 552〜748 / 76〜103 / 136〜184 にランダム化される
   */
  ENEMY_SCALING: {
    BASE_HP: 150,
    BASE_ATTACK: 30,
    BASE_SPEED: 100,
    HP_GAIN_PER_YEAR: 5,        // Y100: +500 → 基準HP650
    ATTACK_GAIN_PER_YEAR: 0.6,  // Y100: +60  → 基準ATK90
    /**
     * instructions.md 絶対ルールにより 1.5 → 0.6 に緩和。
     * Y100 で基準 SPD=160。味方 scout(60) × 全盛期 × 旗手(+40)
     * × 戦術官(+20) = 120 → 血統素体値ボーナス・編成シナジー次第で
     * 先制を狙える「熱い速度調整」へデチューン。
     */
    SPEED_GAIN_PER_YEAR: 0.6,
  },
} as const satisfies ChronicleConfigType;
