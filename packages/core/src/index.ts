export { Unit } from "./models/Unit";
export type { Stats, UnitProps, JobType, Gender, Parents, Origin } from "./models/Unit";
export { NameGenerator, NAMES, ALL_ORIGINS, TITLES, pickRandomOrigin } from "./data/names";
export type { NamePool, NameGenerationResult } from "./data/names";
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
} from "./BattleSimulator";
export { MAX_UNITS_PER_SQUAD, MAX_ENEMY_ACTION_LOOP } from "./config";
