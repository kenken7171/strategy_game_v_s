export { Unit } from "./models/Unit";
export type { Stats, UnitProps, JobType, Gender, Parents, Origin } from "./models/Unit";
export { NameGenerator, NAMES, ALL_ORIGINS, TITLES, pickRandomOrigin } from "./data/names";
export type { NamePool, NameGenerationResult } from "./data/names";
// ジョブ定義（単一 SoT）と総合強さヘルパー
export {
  JOB_DEFAULTS, JOB_JP, JOB_ABILITY, ROLE_BONUS, JOB_TARGET_RATING, formatJob,
  computeBattleStats, totalRating,
} from "./data/jobs";
export type { JobDefaults, BattleStats, JobAbility } from "./data/jobs";
export { Brigade } from "./models/Brigade";
export type {
  YearEvent,
  AdvanceResult,
  AdvanceOptions,
  BirthRegistry,
} from "./models/Brigade";
export { Squad } from "./models/Squad";
export { Enemy } from "./models/Enemy";
export type { EnemyAction } from "./models/Enemy";
export { BattleManager } from "./BattleManager";
export type {
  AttackForecast,
  ActionResult,
  SlotResult,
  UnitAttackLog,
  SquadOffenseResult,
  InitiativeEntry,
  HealLog,
  IntegratedTurnResult,
} from "./BattleManager";
export { BattleSimulator, printBattleReport } from "./BattleSimulator";
export type {
  BattleStatistics,
  SurvivorRecord,
  SimulationResult,
  TurnLog,
  RotationStrategy,
  GridPlacement,
  TimelineEntry,
} from "./BattleSimulator";
export { MAX_UNITS_PER_SQUAD, MAX_ENEMY_ACTION_LOOP } from "./config";
export { CHRONICLE_CONFIG } from "./config/ChronicleConfig";
export type { ChronicleConfigType } from "./config/ChronicleConfig";
export { CHRONICLE_CONFIG_EXTREME } from "./config/ChronicleConfig.extreme";
export { rollPeakAges, rollChildPeakAges } from "./utils/age";
export type { PeakAges } from "./utils/age";
export { enforceMaxBrigadeSize } from "./utils/brigade";
export type { RetirementResult } from "./utils/brigade";
// 人事フェーズ API（手動介入用）
export {
  getPendingDecisions,
  acceptRecruit,
  dismissUnit,
  applyDecisions,
} from "./services/HumanDecisionService";
export type {
  RetirementReason,
  RetirementCandidate,
  RecruitCandidate,
  PendingDecisions,
  DismissResult,
  HumanDecisions,
  DecisionsApplied,
} from "./services/HumanDecisionService";
