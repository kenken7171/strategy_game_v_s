/**
 * Chronicle Knights — システム全体の統合 Config
 *
 * すべてのチューニングパラメータをここに集約する。各モジュールは数値を
 * ハードコードせず、必ず `CHRONICLE_CONFIG.<SECTION>.<KEY>` を参照する。
 *
 * バランス調整や仕様変更は本ファイル1箇所で完結することを目標とする。
 */
export const CHRONICLE_CONFIG = {
  // 1. 時間と年齢の基準値 (Time & Aging Baselines)
  TIME: {
    /** 全盛期開始の基準年齢（個体差は ±3） */
    BASE_PEAK_START_AGE: 24,
    /** 全盛期終了の基準年齢（個体差は ±3） */
    BASE_PEAK_END_AGE: 28,
    /** 子供（継承者）の入団年齢 */
    INDUCTION_AGE: 15,
    /** 衰退期の複利減少率（毎年 3% ずつ係数が下がる） */
    DECAY_RATE: 0.03,
    /** ステータスの最低保証値（修業期初期や衰退期後期で適用） */
    MIN_STAT_VALUE: 1,
  },

  // 2. 旅団運営とイベント周期 (Brigade & Schedule)
  SCHEDULE: {
    /** 総シミュレーション年数 */
    CHRONICLE_YEARS: 100,
    /** 新人加入の間隔（年） */
    RECRUIT_INTERVAL: 2,
    /** 1回あたりの新人加入人数 */
    RECRUIT_COUNT: 2,
    /** 定例戦の間隔（年） */
    BATTLE_INTERVAL: 5,
    /** 旅団結成時の初期人数 */
    INITIAL_MEMBER_COUNT: 5,
    /** 大隊に選出される最大人数 */
    BATTALION_SIZE: 9,
  },

  // 3. 好感度・結婚・血統 (Lineage & Marriage)
  LINEAGE: {
    /** 同じ分隊で1戦した際の上昇好感度 */
    AFFINITY_PER_BATTLE: 10,
    /** 結婚可能になる最低好感度 */
    MARRIAGE_THRESHOLD: 100,
    /** 条件を満たした男女が毎年結婚する確率 */
    MARRIAGE_PROBABILITY: 0.3,
    /** 結婚したペアが毎年子供を授かる確率 */
    BIRTH_PROBABILITY: 0.2,
    /** 子供が親の文化圏（Origin）を継承する確率（残りは反対側親） */
    CULTURE_INHERIT_PROB: 0.5,
  },

  // 4. バトルロジック係数 (Battle Mechanics)
  BATTLE: {
    /** 最大ターン数（超過時はドロー） */
    MAX_TURNS: 30,
    /** 1分隊の最大人数 */
    SQUAD_SIZE: 3,
    /** FRONT（前衛）の最大配置枠数 */
    FRONT_ROW_COUNT: 3,
  },

  // 5. 命名ルール (Naming)
  NAMING: {
    /** 各名前プール（Origin × Gender）の最低保証件数 */
    POOL_MIN_SIZE: 150,
  },

  // 6. 上限・物理制約 (Limits)
  LIMITS: {
    /**
     * 旅団全体の最大定員。超過した場合は自動的に弱者・老兵から除名される
     * （`utils/brigade.ts` の `enforceMaxBrigadeSize` 参照）。
     * デフォルトモードでは事実上無制限（10000）。極端モードでは50など。
     */
    MAX_BRIGADE_SIZE: 10000,
  },
} as const;

// ─── 型ヘルパー ──────────────────────────────────────────────────────────────

/** CHRONICLE_CONFIG の型（`as const` により全プロパティが readonly） */
export type ChronicleConfigType = typeof CHRONICLE_CONFIG;
