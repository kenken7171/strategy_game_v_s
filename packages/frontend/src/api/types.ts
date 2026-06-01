/**
 * API レスポンスの型定義（バックエンドと合わせる）。
 */

// 共通
export type Gender = "Male" | "Female";
export type Origin = "Japanese" | "European" | "Classical";
export type SquadRow = "FRONT" | "REAR-L" | "REAR-R";
export type Winner = "Allies" | "Enemies" | "Draw";

// /api/game/new, /state
export interface GameStateResponse {
  year: number;
  seed: number;
  brigadeSize: number;
  maxBrigadeSize: number;
  candidatePoolSize?: number;
}

// /api/chronicle
export interface ChronicleHistoryEntry {
  year: number;
  type: "join" | "retire" | "marriage" | "birth_planned" | "birth" | "battle";
  text: string;
}
export interface ChronicleResponse {
  year: number;
  brigadeSize: number;
  marriedCouples: number;
  descendants: number;
  history: ChronicleHistoryEntry[];
}

// /api/chronicle/preview
export interface EnemyPreviewStat {
  base: number;
  min: number;
  max: number;
}
export interface EnemyPreviewResponse {
  year: number;
  enemyCount: number;
  hp: EnemyPreviewStat;
  attack: EnemyPreviewStat;
  speed: EnemyPreviewStat;
}

// /api/guild/decisions
export interface BattleStatsFields {
  maxHp: number;
  attack: number;        // max(frontAttack, rearAttack)
  frontAttack: number;
  rearAttack: number;
  speed: number;
  totalRating: number;   // floor(maxHp/5 + attack + speed)
}
export interface RecruitRow extends BattleStatsFields {
  id: string;
  name: string;
  job: string | null;
  gender: Gender;
  origin: Origin;
  age: number;
  baseStrength: number;
  source: "application" | "heir";
  hasLineage: boolean;
  relatedFamilyIds: string[];
}
export interface RetireeRow extends BattleStatsFields {
  id: string;
  name: string;
  job: string | null;
  gender: Gender;
  origin: Origin;
  age: number;
  strength: number;
  strengthRank: number;
  reasons: string[];
  hasLineage: boolean;
  descendantCount: number;
  isMarried: boolean;
}
export interface GuildDecisionsResponse {
  year: number;
  currentSize: number;
  maxSize: number;
  overflowCount: number;
  recruits: RecruitRow[];
  retirementCandidates: RetireeRow[];
}

// /api/formation/roster
export interface RosterUnit extends BattleStatsFields {
  id: string;
  name: string;
  job: string | null;
  gender: Gender;
  origin: Origin;
  age: number;
  strength: number;
  baseStrength: number;
  growthFactor: number;
  isMarried: boolean;
  spouseId: string | null;
  parents: { fatherId: string; motherId: string } | null;
  isAlive: boolean;
  isRetired: boolean;
  descendantCount: number;
}
export interface FormationRosterResponse {
  year: number;
  units: RosterUnit[];
  affinityMap: Record<string, Record<string, number>>;
}

// /api/battle/run

export type RotationStrategy = "NONE" | "CW" | "CCW";

/** ターン開始時の各ユニットの配置スナップショット */
export interface GridPlacement {
  unitId: string;
  unitName: string;
  job: string | null;
  row: SquadRow;
  col: number;
  hp: number;
  maxHp: number;
}

export interface TurnLog {
  turn: number;
  headerText: string;
  initiativeText: string;
  enemyActionText: string;
  allyAttackLines: string[];
  healLines: string[];
  victory: boolean;
  /** このターン開始時のローテーション通知（NONE なら null） */
  rotationNotice: string | null;
  /** このターン開始時点の配置 */
  placements: GridPlacement[];
}

// /api/battle/preview
export interface PreviewTimelineEntry {
  kind: "ally" | "enemy";
  id: string;
  label: string;
  speed: number;
  jobs?: string[];
  members?: string[];
}
export interface BattlePreviewResponse {
  year: number;
  timeline: PreviewTimelineEntry[];
  enemyPreview: {
    count: number;
    maxSpeed: number;
    maxAttack: number;
  };
}
export interface SurvivorRow {
  name: string;
  job: string | null;
  hp: number;
  maxHp: number;
}
export interface BattleRunResponse {
  ok: boolean;
  year: number;
  winner: Winner;
  turns: number;
  statistics: {
    totalDamageDealt: Record<string, number>;
    totalDamageMitigated: number;
    totalHealing: Record<string, number>;
    killCount: Record<string, number>;
  };
  allySurvivors: SurvivorRow[];
  enemySurvivors: SurvivorRow[];
  turnLogs: TurnLog[];
  rotationStrategy: RotationStrategy;
}

// /api/battle/finish
export interface BattleFinishResponse {
  ok: boolean;
  nextYear: number;
  eventsCount: number;
  brigadeSize: number;
}

// 編成データ送信用
export interface BattlePlacement {
  row: SquadRow;
  col: number;
  unitId: string;
}
